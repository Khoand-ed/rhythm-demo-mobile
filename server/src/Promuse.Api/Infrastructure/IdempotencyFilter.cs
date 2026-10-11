using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Infrastructure;

/// <summary>
/// Makes one endpoint safe to retry.
///
/// 为什么不是中间件 / An endpoint filter rather than middleware because it runs
/// after model binding, so the request is already a typed object. Hashing that
/// instead of the raw bytes makes the comparison canonical: a client that
/// reformats its JSON between retries is still sending the same request, and
/// should get the stored answer rather than a 409.
///
/// 三种结果 / Three outcomes:
///   - No record: run the handler, store what it returned, return it.
///   - Record with the same request hash: replay it. The handler does not run.
///   - Record with a different hash: 409. The same key for a different request
///     is not a retry, and answering it with the first response would be a lie.
///
/// 同一个键排队 / Requests carrying the same key take turns. Without that, retries
/// that overlap - a client firing again before the first answer arrived - all read
/// "no record" and all run the handler: a pull is rolled and charged once per copy,
/// and whichever copy stores its answer first becomes the key's answer, even when it
/// is a loser's 409 and the account it lost to was created by the very same key. CI
/// caught exactly that on register. The queue is a Postgres advisory lock held on a
/// connection of its own, so it costs no table and is released by the database
/// itself if this process dies holding it.
///
/// 记录按账号分开 / Records are scoped to the caller. A replay hands back a stored
/// response in full, and those responses carry player state, so two accounts
/// presenting the same key must never meet in the same record. Keys are chosen by
/// the client and some of them are values that appear elsewhere - a run closes
/// under its own run id - so "unguessable" is not something this can rely on.
/// </summary>
public sealed class IdempotencyFilter<TRequest> : IEndpointFilter
    where TRequest : notnull
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Long enough for any retry a client will really make.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        CancellationToken ct = http.RequestAborted;

        // 从请求作用域取 / Resolved per invocation rather than injected into the
        // constructor. A filter registered with AddEndpointFilter is not
        // guaranteed to be built once per request, and capturing a scoped
        // DbContext in something longer-lived is the classic way to get an
        // object disposed underneath you - or worse, shared between requests.
        var db = http.RequestServices.GetRequiredService<PromuseDbContext>();
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();

        // 必须和响应用同一套 / The application's own serializer settings, not a
        // private copy. A stored response written with different settings
        // deserialises differently on replay - with enums configured as strings
        // on the way out, a local copy would have replayed `"tab": 0` where the
        // first answer said `"tab": "Daily"`.
        JsonSerializerOptions json = http.RequestServices
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        if (!http.Request.Headers.TryGetValue(HeaderName, out var header) ||
            string.IsNullOrWhiteSpace(header.ToString()))
        {
            return ApiProblems.Create(400, "IDEMPOTENCY_KEY_REQUIRED",
                "Idempotency-Key is required",
                $"This endpoint creates or consumes something, so it needs an {HeaderName} header.")
                .ToResult();
        }

        string key = header.ToString();
        string endpoint = $"{Subject(http)} {http.Request.Method} {http.Request.Path}";
        string requestHash = HashRequest(context.Arguments.OfType<TRequest>().FirstOrDefault(), json);

        // 拿到锁才往下 / Held until the response is stored. The second copy of a request
        // waits here, then finds the first one's record below and replays it.
        await using Gate gate = await Gate.EnterAsync(db, key, endpoint, ct);

        IdempotencyRecord? existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint, ct);

        if (existing is not null) return Replay(existing, requestHash);

        object? result = await next(context);

        // 只存最终答案 / 5xx is not stored. A transient fault must not be cached
        // for 24 hours as this key's permanent answer - the whole point of a
        // retry is that the next one might work.
        if (TryDescribe(result, json, out int status, out string body) && status < 500)
        {
            var record = new IdempotencyRecord
            {
                Id = Guid.NewGuid(),
                Key = key,
                Endpoint = endpoint,
                RequestHash = requestHash,
                StatusCode = status,
                ResponseBody = body,
                CreatedAt = clock.GetUtcNow(),
                ExpiresAt = clock.GetUtcNow().Add(Retention),
            };

            db.IdempotencyRecords.Add(record);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // 兜底 / The gate makes this unreachable for two requests through this
                // filter; the unique index stays as the last word if anything else ever
                // writes a record for the key. The record already there is the
                // authoritative answer, so replay it.
                db.ChangeTracker.Clear();

                IdempotencyRecord? winner = await db.IdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint, ct);

                if (winner is not null) return Replay(winner, requestHash);

                throw;
            }
        }

        return result;
    }

    /// <summary>
    /// 谁在发这个请求 / Who the record belongs to.
    ///
    /// 匿名也要有个名字 / Register and guest sign-in run this filter with nobody
    /// signed in yet, and those are exactly the calls a client retries after a
    /// dropped response. They share one bucket, which is what they did before -
    /// their responses are the caller's own new session and hold no one else's
    /// state.
    /// </summary>
    private static string Subject(HttpContext http) =>
        http.User.TryGetAccountId(out Guid accountId) ? accountId.ToString() : "anon";

    private static IResult Replay(IdempotencyRecord record, string requestHash)
    {
        if (record.RequestHash != requestHash) return ApiProblems.IdempotencyKeyConflict().ToResult();

        return Results.Text(record.ResponseBody, "application/json", Encoding.UTF8, record.StatusCode);
    }

    /// <summary>
    /// Pulls the status and body out of whatever the handler returned. Every
    /// result this API produces implements both interfaces; anything that does
    /// not is simply not stored, which fails safe - the request is answered, it
    /// is just not replayable.
    /// </summary>
    private static bool TryDescribe(object? result, JsonSerializerOptions json, out int status, out string body)
    {
        status = 0;
        body = string.Empty;

        if (result is not IStatusCodeHttpResult { StatusCode: not null } coded) return false;
        if (result is not IValueHttpResult valued) return false;

        status = coded.StatusCode.Value;
        body = JsonSerializer.Serialize(valued.Value, json);

        return true;
    }

    private static string HashRequest(TRequest? request, JsonSerializerOptions json) =>
        Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, json))));

    /// <summary>
    /// A transaction-scoped advisory lock on a connection of its own, named by the key
    /// and endpoint. Leaving the scope rolls the empty transaction back, which releases
    /// the lock; a dropped connection releases it too.
    ///
    /// 不用请求自己的连接 / Not the request's own DbContext connection: handlers open
    /// their own transactions on that one, and a transaction already open there would
    /// make every one of them throw.
    /// </summary>
    private sealed class Gate : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;

        private Gate(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public static async Task<Gate> EnterAsync(
            PromuseDbContext db, string key, string endpoint, CancellationToken ct)
        {
            var connection = new NpgsqlConnection(db.Database.GetConnectionString());

            try
            {
                await connection.OpenAsync(ct);
                NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

                await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@id)", connection, transaction);
                command.Parameters.AddWithValue("id", LockId(key, endpoint));
                await command.ExecuteNonQueryAsync(ct);

                return new Gate(connection, transaction);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        // 64 位就够 / The first 64 bits of a SHA-256. Two different keys landing on one
        // lock is astronomically unlikely, and if it happened they would only queue
        // behind each other - correctness never depends on the id being unique.
        private static long LockId(string key, string endpoint) =>
            BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint + "\n" + key)), 0);

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

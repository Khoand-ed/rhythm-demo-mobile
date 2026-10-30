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
///   - No record, or only one past its 24 hours: run the handler, store what it
///     returned, return it.
///   - Record with the same request hash: replay it. The handler does not run.
///   - Record with a different hash: 409. The same key for a different request
///     is not a retry, and answering it with the first response would be a lie.
///
/// 二十四小时 / A record answers for its key for the contract's 24 hours and never
/// after. A replay hands back a stored response in full - a sign-up's tokens, a
/// pull's operators, a balance - and once those 24 hours are up nobody was promised
/// it any more, so a request under an expired key runs as a new one. The expired
/// row is deleted under the gate before the handler runs: it no longer answers, but
/// it still holds the key's place under the unique index, and the answer stored
/// afterwards must be able to take it. IdempotencySweep deletes the rest, the ones
/// no request comes back for.
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

    /// <summary>
    /// 列宽 / The widths of the two columns a record is filed under, its key and its endpoint
    /// (PromuseDbContext). Counted in UTF-16 units, which are never fewer than the characters
    /// Postgres counts, so whatever passes here fits there.
    /// </summary>
    public const int MaxKeyLength = 128;

    /// <inheritdoc cref="MaxKeyLength"/>
    public const int MaxEndpointLength = 128;

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
            return ApiProblems.IdempotencyKeyRequired().ToResult();
        }

        string key = header.ToString();

        // 存不下就别开始 / Refused before the gate and the handler. A record cannot hold a longer key,
        // and finding that out at the end is too late: the handler has committed - the purchase, the
        // pull - when the record's insert fails, the request answers 500 with nothing stored, and the
        // retry a 500 invites does the work a second time.
        if (key.Length > MaxKeyLength) return ApiProblems.IdempotencyKeyInvalid(MaxKeyLength).ToResult();

        string endpoint = EndpointName(http);
        string requestHash = HashRequest(context.Arguments.OfType<TRequest>().FirstOrDefault(), json);

        // 拿到锁才往下 / Held until the response is stored. The second copy of a request
        // waits here, then finds the first one's record below and replays it.
        await using Gate gate = await Gate.EnterAsync(db, key, endpoint, ct);

        // 进门后再看钟 / Read once the gate is ours: a copy that queued behind another is
        // judged by when it got to look, not by when it arrived. Both lookups below go by it.
        DateTimeOffset now = clock.GetUtcNow();

        IdempotencyRecord? existing = await FindAsync(db, key, endpoint, ct);

        if (existing is not null)
        {
            if (existing.IsLive(now)) return Replay(http, existing, requestHash);

            // 过期的让位 / Past its 24 hours: no answer, but still in the key's place under the
            // unique index. Deleted here, under the gate, where no other request through this
            // filter can be storing for the key - the sweep may not reach it for an hour.
            Guid expired = existing.Id;
            await db.IdempotencyRecords.Where(r => r.Id == expired).ExecuteDeleteAsync(ct);
        }

        object? result = await next(context);

        // 只存最终答案 / 5xx is not stored. A transient fault must not be cached
        // for 24 hours as this key's permanent answer - the whole point of a
        // retry is that the next one might work.
        if (TryDescribe(result, json, out int status, out string body) && status < 500)
        {
            DateTimeOffset storedAt = clock.GetUtcNow();
            var record = new IdempotencyRecord
            {
                Id = Guid.NewGuid(),
                Key = key,
                Endpoint = endpoint,
                RequestHash = requestHash,
                StatusCode = status,
                ResponseBody = body,
                CreatedAt = storedAt,
                ExpiresAt = storedAt.Add(Retention),
            };

            // 只写这一条 / The record and nothing else. This is the handler's DbContext, and
            // SaveChanges writes everything tracked on it - so a row a service changed before it
            // refused would be written here, outside the transaction its refusal rolled back. A
            // Manufacture upgrade short of LMD once moved its station's anchor that way. By now the
            // handler has saved what it meant to keep; anything still tracked is left over.
            db.ChangeTracker.Clear();
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
                // authoritative answer while it is live, so replay it. An expired one
                // answers nothing here either: it fails the request as finding no record
                // does. It cannot be one this filter left behind - the one met above was
                // deleted before the handler ran.
                db.ChangeTracker.Clear();

                IdempotencyRecord? winner = await FindAsync(db, key, endpoint, ct);

                if (winner is not null && winner.IsLive(now)) return Replay(http, winner, requestHash);

                throw;
            }
        }

        return result;
    }

    /// <summary>
    /// The key's record on this endpoint - the unique index allows one - live or not. Whether
    /// it may answer is <see cref="IdempotencyRecord.IsLive"/>'s to say, at both places that ask.
    /// </summary>
    private static Task<IdempotencyRecord?> FindAsync(
        PromuseDbContext db, string key, string endpoint, CancellationToken ct) =>
        db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint, ct);

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

    /// <summary>
    /// 记录归在谁名下 / What a record is filed under, and half its lock's name: whose it is, the
    /// method and the path.
    ///
    /// 太长才换成摘要 / Unless that is longer than the endpoint column. Paths carry values the client
    /// chose - an event id, an offer id, an operator id - and the ids an administrator may really
    /// give an event and its shop offer, 64 characters each, make a purchase's name 196. A name like
    /// that made the record's insert fail after the handler had committed: a 500 in place of the
    /// service's own answer, and a retry that did the work again. It is filed under its owner and a
    /// SHA-256 of the whole name instead - at most 108 characters, and the same on every retry.
    ///
    /// 平常的名字不动 / Only when too long, so every ordinary path keeps the name it has always been
    /// filed under, and records written before this still replay. Not a wider column instead: the
    /// client decides how long a path is, so any width would only move the 500 further out. The
    /// owner stays in clear, so a record still opens with the account it belongs to; and as a plain
    /// name has a space before its path while the digest form has none after its owner, the two
    /// forms can never produce the same name.
    /// </summary>
    private static string EndpointName(HttpContext http)
    {
        string subject = Subject(http);
        string name = $"{subject} {http.Request.Method} {http.Request.Path}";

        if (name.Length <= MaxEndpointLength) return name;

        return $"{subject} sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))}";
    }

    private static IResult Replay(HttpContext http, IdempotencyRecord record, string requestHash)
    {
        bool conflict = record.RequestHash != requestHash;

        // 按路由模板计 / By the route's pattern, never its path: a path carries a run id, and every
        // distinct tag value is a series of its own.
        string route = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";
        http.RequestServices.GetRequiredService<PromuseMetrics>().Replayed(route, conflict);

        if (conflict) return ApiProblems.IdempotencyKeyConflict().ToResult();

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

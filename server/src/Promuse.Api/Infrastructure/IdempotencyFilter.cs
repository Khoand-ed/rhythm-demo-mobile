using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

        if (!http.Request.Headers.TryGetValue(HeaderName, out var header) ||
            string.IsNullOrWhiteSpace(header.ToString()))
        {
            return ApiProblems.Create(400, "IDEMPOTENCY_KEY_REQUIRED",
                "Idempotency-Key is required",
                $"This endpoint creates or consumes something, so it needs an {HeaderName} header.")
                .ToResult();
        }

        string key = header.ToString();
        string endpoint = $"{http.Request.Method} {http.Request.Path}";
        string requestHash = HashRequest(context.Arguments.OfType<TRequest>().FirstOrDefault());

        IdempotencyRecord? existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint, ct);

        if (existing is not null) return Replay(existing, requestHash);

        object? result = await next(context);

        // 只存最终答案 / 5xx is not stored. A transient fault must not be cached
        // for 24 hours as this key's permanent answer - the whole point of a
        // retry is that the next one might work.
        if (TryDescribe(result, out int status, out string body) && status < 500)
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
                // 并发重试 / Two retries raced: both read nothing, both ran, and
                // the unique index refused this insert. The other one's response
                // is the authoritative answer for this key, so replay that
                // rather than returning a second, differently-shaped success.
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
    private static bool TryDescribe(object? result, out int status, out string body)
    {
        status = 0;
        body = string.Empty;

        if (result is not IStatusCodeHttpResult { StatusCode: not null } coded) return false;
        if (result is not IValueHttpResult valued) return false;

        status = coded.StatusCode.Value;
        body = JsonSerializer.Serialize(valued.Value, JsonOptions.Web);

        return true;
    }

    private static string HashRequest(TRequest? request) =>
        Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions.Web))));
}

/// <summary>
/// One serializer configuration, used for hashing a request and for storing a
/// response. They must agree: a body serialized one way and hashed another would
/// make a replay compare unequal to itself.
/// </summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}

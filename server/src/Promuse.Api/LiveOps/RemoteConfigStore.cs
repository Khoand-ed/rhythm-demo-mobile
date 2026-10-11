using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Promuse.Contracts.Config;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.LiveOps;

/// <summary>
/// The live remote config, read once and kept for a few seconds.
///
/// 每个请求都要看它 / Every game request passes the maintenance and version gate, and those read
/// the config. Going to the database for each one would double the query count of the whole API
/// for a value that changes a few times a week, so it is cached here - and dropped the moment an
/// admin saves through this instance, so a change made here is live on the next request. Another
/// instance behind the same database picks it up within <see cref="Lifetime"/>.
/// </summary>
public sealed class RemoteConfigStore(IServiceScopeFactory scopes, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _refresh = new(1, 1);
    private volatile Snapshot? _snapshot;

    public JsonSerializerOptions Json => json.Value.SerializerOptions;

    /// <param name="Version">The live version - also the ETag.</param>
    public sealed record Snapshot(int Version, ConfigDocument Document, DateTimeOffset UpdatedAt, DateTimeOffset ReadAt);

    public async Task<Snapshot> GetAsync(CancellationToken ct)
    {
        Snapshot? held = _snapshot;
        if (held is not null && DateTimeOffset.UtcNow - held.ReadAt < Lifetime) return held;

        // 一个人去读 / One reader at a time, so a burst of requests after expiry is one query.
        await _refresh.WaitAsync(ct);
        try
        {
            held = _snapshot;
            if (held is not null && DateTimeOffset.UtcNow - held.ReadAt < Lifetime) return held;

            using IServiceScope scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

            RemoteConfigVersion row = await db.RemoteConfigVersions
                .AsNoTracking()
                .OrderByDescending(v => v.Version)
                .FirstAsync(ct);

            held = new Snapshot(row.Version, Parse(row.Document), row.CreatedAt, DateTimeOffset.UtcNow);
            _snapshot = held;
            return held;
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>Forget the cached copy; the next read goes to the database.</summary>
    public void Invalidate() => _snapshot = null;

    public ConfigDocument Parse(string document) =>
        JsonSerializer.Deserialize<ConfigDocument>(document, Json)
        ?? throw new InvalidOperationException("A remote config version holds an empty document.");

    public string Serialize(ConfigDocument document) => JsonSerializer.Serialize(document, Json);
}

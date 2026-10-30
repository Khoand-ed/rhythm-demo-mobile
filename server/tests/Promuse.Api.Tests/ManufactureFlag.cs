using Microsoft.Extensions.DependencyInjection;
using Promuse.Api.LiveOps;
using Promuse.Contracts.Config;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Tests;

/// <summary>
/// 开关 / Switches Manufacture on or off for the test host, the way an admin's save would: a new
/// config version, then the server's cached copy dropped, so the very next request sees it.
///
/// 用完要关 / Off is the default, and every test class in the collection shares the config, so a
/// test that switches it on switches it off again afterwards.
/// </summary>
internal static class ManufactureFlag
{
    public static async Task SwitchAsync(PromuseApiFactory factory, bool on, string by)
    {
        var store = factory.Services.GetRequiredService<RemoteConfigStore>();
        store.Invalidate();
        RemoteConfigStore.Snapshot live = await store.GetAsync(CancellationToken.None);
        if (live.Document.Features.Manufacture == on) return;

        ConfigDocument document = live.Document with { Features = live.Document.Features with { Manufacture = on } };

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();
            db.RemoteConfigVersions.Add(new RemoteConfigVersion
            {
                Version = live.Version + 1,
                Document = store.Serialize(document),
                CreatedAt = DateTimeOffset.UtcNow,
                Note = $"{by}: manufacture {(on ? "on" : "off")}",
            });
            await db.SaveChangesAsync();
        }

        store.Invalidate();
    }
}

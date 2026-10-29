using Microsoft.EntityFrameworkCore;
using Promuse.Api.Campaign;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Contracts.Items;
using Promuse.Contracts.Players;
using Promuse.Persistence;

namespace Promuse.Api.Manufacture;

/// <summary>
/// 蓝图 / Hands out Manufacture's blueprints, two ways that come to the same thing:
///
///   - on a stage's first clear, as one of the campaign's <see cref="ICampaignClearListener"/>s,
///     so the results screen shows it;
///   - when a player opens Manufacture, for every stage they cleared without being paid - before
///     Manufacture existed, while it was switched off, or in a window where a rolled-back server
///     ran on the new schema without paying them.
///
/// 只给一个 / Both give one, and only if none is held (<see cref="Inventory.GiveOnceAsync"/>), so no
/// path, nor both together, ever leaves a player with two. A blueprint is never spent, so holding it
/// is all that is ever asked.
///
/// 不依赖 ManufactureService / Its own class rather than part of ManufactureService: that one reads
/// stage codes from CampaignService, and the campaign calls this - one class doing both would be a
/// dependency cycle the container refuses.
/// </summary>
public sealed class BlueprintGrants(
    PromuseDbContext db,
    Inventory inventory,
    TimeProvider clock,
    RemoteConfigStore config,
    PromuseMetrics metrics) : ICampaignClearListener
{
    private static readonly string[] Numerals = ["I", "II", "III"];

    public IReadOnlyDictionary<string, string> StagesPaidOn { get; } =
        ManufactureRules.Blueprints.ToDictionary(b => b.StageId, b => $"its first clear pays {NameOf(b)}");

    /// <summary>
    /// The blueprint <paramref name="stageId"/>'s first clear pays, if any.
    ///
    /// 关着就不发 / Nothing while Manufacture is switched off: the results screen would otherwise show
    /// an item for something players cannot open yet. They lose nothing by it - opening Manufacture
    /// once it is on hands over what their clears earned (<see cref="BackfillAsync"/>).
    /// </summary>
    public async Task<FirstClearPayout> OnFirstClearAsync(
        Guid accountId, string stageId, DateTimeOffset now, CancellationToken ct)
    {
        if (ManufactureRules.BlueprintForStage(stageId) is not { } blueprint) return FirstClearPayout.Nothing;
        if (!(await config.GetAsync(ct)).Document.Features.Manufacture) return FirstClearPayout.Nothing;
        if (!await inventory.GiveOnceAsync(accountId, blueprint.ItemId, ct)) return FirstClearPayout.Nothing;

        return new FirstClearPayout(
            [new ItemStack(blueprint.ItemId, 1)],
            () => metrics.ManufactureBlueprintGranted(blueprint.ItemId, "first_clear"));
    }

    /// <summary>
    /// 补发 / Gives every blueprint the player has earned - its stage cleared - and does not hold.
    /// Its own transaction, which moves the state version when it gives anything, since the bag
    /// changed. Returns what it gave: nothing, almost always.
    /// </summary>
    public async Task<IReadOnlyList<ItemStack>> BackfillAsync(Guid accountId, CancellationToken ct)
    {
        string[] stages = [.. ManufactureRules.Blueprints.Select(b => b.StageId)];
        int[] keyItems = [.. KeyItems.All];

        List<string> cleared = await db.CampaignClears.AsNoTracking()
            .Where(c => c.AccountId == accountId && stages.Contains(c.StageId))
            .Select(c => c.StageId)
            .ToListAsync(ct);
        if (cleared.Count == 0) return [];

        List<int> held = await db.PlayerItems.AsNoTracking()
            .Where(i => i.AccountId == accountId && keyItems.Contains(i.ItemId))
            .Select(i => i.ItemId)
            .ToListAsync(ct);

        ManufactureRules.Blueprint[] owed =
            [.. ManufactureRules.Blueprints.Where(b => cleared.Contains(b.StageId) && !held.Contains(b.ItemId))];
        if (owed.Length == 0) return [];

        var given = new List<ItemStack>();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (ManufactureRules.Blueprint blueprint in owed)
            {
                if (await inventory.GiveOnceAsync(accountId, blueprint.ItemId, ct)) given.Add(new ItemStack(blueprint.ItemId, 1));
            }

            if (given.Count > 0) await inventory.BumpStateVersionAsync(accountId, clock.GetUtcNow(), ct);
            await transaction.CommitAsync(ct);
        }

        foreach (ItemStack blueprint in given) metrics.ManufactureBlueprintGranted(blueprint.ItemId, "backfill");
        return given;
    }

    private static string NameOf(ManufactureRules.Blueprint blueprint) =>
        $"Manufacture Blueprint {Numerals[blueprint.Level - 1]}";
}

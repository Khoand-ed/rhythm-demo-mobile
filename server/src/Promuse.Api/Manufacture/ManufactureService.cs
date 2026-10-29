using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Promuse.Api.Campaign;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts;
using Promuse.Contracts.Items;
using Promuse.Contracts.Manufacture;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Manufacture;

/// <summary>
/// 制造站 / Manufacture: what a player's two stations hold, collecting it, and raising a station.
///
/// 服务端算 / Every number comes from here. The device shows the view and names what it wants -
/// collect this station, raise that one from the level it saw - and never says how much was made.
///
/// 开放条件 / Open to a player who holds Manufacture Blueprint I. Their stations are created the
/// first time they are needed, counting from that player's first clear of the stage that pays the
/// blueprint: someone who unlocked Manufacture days before opening it finds a full store rather
/// than an empty one, and the capacity is what keeps that from being more than a store's worth.
/// </summary>
public sealed class ManufactureService(
    PromuseDbContext db,
    TimeProvider clock,
    Inventory inventory,
    PlayerService players,
    CampaignService campaign,
    PromuseMetrics metrics,
    IOptions<ManufactureOptions> options,
    ILogger<ManufactureService> logger)
{
    /// <summary>How the screen lays them out, LMD first. Rows are locked in ManufactureStations.All order instead.</summary>
    private static readonly string[] DisplayOrder = [ManufactureStations.Lmd, ManufactureStations.Exp];

    // ------------------------------------------------------------------ view

    public async Task<ManufactureView> ViewAsync(Guid accountId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlyDictionary<string, string> codes = await campaign.StageCodesAsync(ct);

        if (!await HoldsAsync(accountId, KeyItems.ManufactureBlueprint1, ct))
        {
            return new ManufactureView(false, CodeOf(codes, ManufactureRules.UnlockStageId), [], now);
        }

        await EnsureStationsAsync(accountId, now, ct);

        List<ManufactureStation> rows = await db.ManufactureStations.AsNoTracking()
            .Where(s => s.AccountId == accountId)
            .ToListAsync(ct);

        var stations = new List<StationState>();
        foreach (string stationId in DisplayOrder)
        {
            ManufactureStation? row = rows.FirstOrDefault(r => r.StationId == stationId);
            if (row is not null) stations.Add(StateOf(row, now, codes));
        }

        return new ManufactureView(true, null, stations, now);
    }

    // --------------------------------------------------------------- collect

    /// <summary>
    /// Empties one station, or every station that has something when the request names none. Units
    /// only: the part of a unit still being made stays, so collecting early costs nothing.
    /// </summary>
    public async Task<Outcome<ManufactureActionResult>> CollectAsync(
        Guid accountId, CollectRequest? request, CancellationToken ct)
    {
        string? stationId = request?.StationId;
        if (stationId is not null && !ManufactureStations.IsStation(stationId)) return Fail(InvalidStation(stationId));

        DateTimeOffset now = clock.GetUtcNow();
        if (await OpenAsync(accountId, now, ct) is { } locked) return Fail(locked);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var settled = new List<(ManufactureStation Row, Settlement Settlement)>();
        foreach (ManufactureStation row in await LockAsync(accountId, stationId, ct))
        {
            Settlement settlement = await SettleAsync(accountId, row, now, ct);
            if (settlement.Made is not null) settled.Add((row, settlement));
        }

        // 什么都没做出来 / Nothing to hand out: the transaction rolls back, and no row was touched.
        if (settled.Count == 0)
        {
            return Fail(ApiProblems.NothingToClaim(stationId is null
                ? "Nothing has been made since the last collection."
                : "This station has made nothing since the last collection."));
        }

        foreach ((ManufactureStation row, Settlement settlement) in settled) Apply(row, settlement.AnchorAfter, now);
        var granted = settled.Select(s => (s.Row.StationId, Made: s.Settlement.Made!)).ToList();

        await db.SaveChangesAsync(ct);

        // 包变了 / The bag changed under the ETag the device is holding.
        await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        foreach ((string station, ItemStack made) in granted) metrics.ManufactureCollected(station, made.ItemId, made.Amount);

        return await ResultAsync(accountId, [.. granted.Select(g => g.Made)], ct);
    }

    // --------------------------------------------------------------- upgrade

    /// <summary>
    /// Raises a station one level: the player holds that level's blueprint - held, never spent - and
    /// pays its LMD. What the station made at its old level is collected first, before the price is
    /// taken, so LMD a station has made counts toward raising it, and records made as Basic are never
    /// paid out as Frontline. Either everything happens or nothing does.
    /// </summary>
    public async Task<Outcome<ManufactureActionResult>> UpgradeAsync(
        Guid accountId, UpgradeRequest? request, CancellationToken ct)
    {
        if (request is null || !ManufactureStations.IsStation(request.StationId)) return Fail(InvalidStation(request?.StationId));

        if (request.FromLevel < 1)
        {
            return Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["fromLevel"] = ["The level the station was seen at: 1 or more."],
            }));
        }

        DateTimeOffset now = clock.GetUtcNow();
        if (await OpenAsync(accountId, now, ct) is { } locked) return Fail(locked);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 先锁再判断 / Locked before anything is decided: of two upgrades from the same level, the
        // second waits, then finds the level moved and is refused - it never pays twice.
        ManufactureStation row = (await LockAsync(accountId, request.StationId, ct)).Single();

        int level = EffectiveLevel(row);
        if (level != request.FromLevel) return Fail(ApiProblems.StationMovedOn(level));
        if (level >= ManufactureRules.MaxLevel) return Fail(ApiProblems.StationLevelCapped(ManufactureRules.MaxLevel));

        int to = level + 1;
        ManufactureRules.Blueprint blueprint = ManufactureRules.BlueprintFor(to);
        if (!await HoldsAsync(accountId, blueprint.ItemId, ct))
        {
            return Fail(ApiProblems.BlueprintRequired(to, CodeOf(await campaign.StageCodesAsync(ct), blueprint.StageId)));
        }

        Settlement settlement = await SettleAsync(accountId, row, now, ct);
        ItemStack? made = settlement.Made;

        foreach (ItemStack cost in ManufactureRules.UpgradeCost(to))
        {
            if (!await inventory.TryDebitAsync(accountId, cost.ItemId, cost.Amount, ct))
            {
                return Fail(ApiProblems.InsufficientFunds());
            }
        }

        // 到这里才动行 / Only now, with nothing left that can refuse, is the row touched.
        StationLevel next = LevelAt(row.StationId, to);
        Apply(row, ManufactureClock.AnchorAtNewLevel(settlement.AnchorAfter, next.SecondsPerUnit, now), now);
        row.Level = to;

        await db.SaveChangesAsync(ct);
        await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        if (made is not null) metrics.ManufactureCollected(row.StationId, made.ItemId, made.Amount);
        metrics.ManufactureUpgraded(row.StationId, to);

        return await ResultAsync(accountId, made is null ? [] : [made], ct);
    }

    // --------------------------------------------------------------- helpers

    /// <summary>
    /// 开门 / Null when the player may use Manufacture - and then their stations exist. Done before
    /// the action's own transaction: having stations is not part of collecting or raising one, so an
    /// action that is refused must not take the stations' creation back with it.
    /// </summary>
    private async Task<ApiProblem?> OpenAsync(Guid accountId, DateTimeOffset now, CancellationToken ct)
    {
        if (!await HoldsAsync(accountId, KeyItems.ManufactureBlueprint1, ct))
        {
            return ApiProblems.ManufactureLocked(CodeOf(await campaign.StageCodesAsync(ct), ManufactureRules.UnlockStageId));
        }

        await EnsureStationsAsync(accountId, now, ct);
        return null;
    }

    /// <param name="Made">What went into the bag; null when the store was empty.</param>
    /// <param name="AnchorAfter">Where the row's anchor goes once the action succeeds.</param>
    private sealed record Settlement(ItemStack? Made, DateTimeOffset AnchorAfter);

    /// <summary>
    /// 收进包里 / Pays out what <paramref name="row"/> holds at <paramref name="now"/>, inside the
    /// caller's transaction with the row locked, and says where its anchor goes - without moving it.
    ///
    /// 行先不动 / The row is left alone on purpose. The idempotency filter stores its record through
    /// this same DbContext once the service has answered, and that save writes every tracked change -
    /// so an anchor moved here by a request that is refused a step later (an upgrade short of LMD)
    /// would be written after all, outside the transaction that took the LMD back, and the player
    /// would lose what the station held. The caller moves the anchor once nothing can refuse.
    /// </summary>
    private async Task<Settlement> SettleAsync(Guid accountId, ManufactureStation row, DateTimeOffset now, CancellationToken ct)
    {
        StationLevel level = LevelAt(row.StationId, EffectiveLevel(row));
        int units = ManufactureClock.Read(row.AnchorAt, level.SecondsPerUnit, level.CapacityUnits, now).StoredUnits;
        if (units == 0) return new Settlement(null, row.AnchorAt);

        var made = new ItemStack(level.ItemId, units * level.UnitAmount);
        await inventory.CreditAsync(accountId, made.ItemId, made.Amount, ct);

        return new Settlement(made,
            ManufactureClock.AnchorAfterCollect(row.AnchorAt, level.SecondsPerUnit, level.CapacityUnits, now));
    }

    private static void Apply(ManufactureStation row, DateTimeOffset anchorAt, DateTimeOffset now)
    {
        row.AnchorAt = anchorAt;
        row.UpdatedAt = now;
    }

    /// <summary>
    /// 第一次需要时建 / Creates the player's stations if they have none yet, at level 1, counting
    /// from their first clear of the stage that unlocks Manufacture - or from now, for a player who
    /// holds the blueprint without that clear. Two requests racing both try; one inserts.
    /// </summary>
    private async Task EnsureStationsAsync(Guid accountId, DateTimeOffset now, CancellationToken ct)
    {
        int have = await db.ManufactureStations.CountAsync(s => s.AccountId == accountId, ct);
        if (have >= ManufactureStations.All.Count) return;

        DateTimeOffset? cleared = await db.CampaignClears.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.StageId == ManufactureRules.UnlockStageId)
            .Select(c => (DateTimeOffset?)c.FirstClearedAt)
            .FirstOrDefaultAsync(ct);

        DateTimeOffset anchor = ManufactureClock.WholeSeconds(cleared is { } at && at < now ? at : now);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO manufacture_stations (account_id, station_id, level, anchor_at, updated_at)
            VALUES ({accountId}, {ManufactureStations.Exp}, 1, {anchor}, {now}),
                   ({accountId}, {ManufactureStations.Lmd}, 1, {anchor}, {now})
            ON CONFLICT (account_id, station_id) DO NOTHING
            """, ct);
    }

    /// <summary>
    /// The player's stations - one, or all - locked for this transaction. Always in station-id order,
    /// so two requests that each touch both lock them the same way round and cannot deadlock.
    /// </summary>
    private Task<List<ManufactureStation>> LockAsync(Guid accountId, string? stationId, CancellationToken ct) =>
        (stationId is null
            ? db.ManufactureStations.FromSqlInterpolated($"""
                SELECT * FROM manufacture_stations
                WHERE account_id = {accountId}
                ORDER BY station_id
                FOR UPDATE
                """)
            : db.ManufactureStations.FromSqlInterpolated($"""
                SELECT * FROM manufacture_stations
                WHERE account_id = {accountId} AND station_id = {stationId}
                FOR UPDATE
                """))
        .ToListAsync(ct);

    private StationState StateOf(ManufactureStation row, DateTimeOffset now, IReadOnlyDictionary<string, string> codes)
    {
        int level = EffectiveLevel(row);
        StationLevel current = LevelAt(row.StationId, level);
        StationReading reading = ManufactureClock.Read(row.AnchorAt, current.SecondsPerUnit, current.CapacityUnits, now);

        StationUpgrade? next = null;
        if (level < ManufactureRules.MaxLevel)
        {
            ManufactureRules.Blueprint blueprint = ManufactureRules.BlueprintFor(level + 1);
            next = new StationUpgrade(
                LevelAt(row.StationId, level + 1),
                ManufactureRules.UpgradeCost(level + 1),
                blueprint.ItemId,
                CodeOf(codes, blueprint.StageId));
        }

        return new StationState(
            row.StationId, level, ManufactureRules.MaxLevel, current, reading.StoredUnits,
            row.AnchorAt, reading.NextUnitAt, reading.FullAt, next);
    }

    /// <summary>
    /// The level a row is played at. One outside the table - a row nobody should be able to write -
    /// plays as the nearest level there is, and says so in the log rather than failing the request.
    /// </summary>
    private int EffectiveLevel(ManufactureStation row)
    {
        if (ManufactureRules.IsKnownLevel(row.Level)) return row.Level;

        int effective = Math.Clamp(row.Level, 1, ManufactureRules.MaxLevel);
        logger.LogWarning(
            "Manufacture station {Station} of {AccountId} is at level {Level}, which the table does not have; it plays as level {Effective}.",
            row.StationId, row.AccountId, row.Level, effective);
        return effective;
    }

    /// <summary>The level as this server runs it: at its time scale.</summary>
    private StationLevel LevelAt(string stationId, int level) =>
        ManufactureRules.Scaled(ManufactureRules.LevelOf(stationId, level), options.Value.TimeScale);

    private Task<bool> HoldsAsync(Guid accountId, int itemId, CancellationToken ct) =>
        db.PlayerItems.AnyAsync(i => i.AccountId == accountId && i.ItemId == itemId, ct);

    private async Task<Outcome<ManufactureActionResult>> ResultAsync(
        Guid accountId, IReadOnlyList<ItemStack> granted, CancellationToken ct)
    {
        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);
        if (!state.IsSuccess) return Fail(state.Problem!);

        return Outcome<ManufactureActionResult>.Ok(
            new ManufactureActionResult(await ViewAsync(accountId, ct), granted, state.Value!));
    }

    /// <summary>A stage's code ("1-3"); its id when the campaign no longer has it.</summary>
    private static string CodeOf(IReadOnlyDictionary<string, string> codes, string stageId) =>
        codes.TryGetValue(stageId, out string? code) ? code : stageId;

    private static ApiProblem InvalidStation(string? stationId) =>
        ApiProblems.ValidationFailed(new Dictionary<string, string[]>
        {
            ["stationId"] = [$"'{stationId}' is not a station: there are '{ManufactureStations.Lmd}' and '{ManufactureStations.Exp}'."],
        });

    private static Outcome<ManufactureActionResult> Fail(ApiProblem problem) =>
        Outcome<ManufactureActionResult>.Fail(problem);
}

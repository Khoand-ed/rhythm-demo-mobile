using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts.Gacha;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

// 两层同名 / The row and the wire shape share a name; in this file the bare name is the row.
using GachaPoolEntry = Promuse.Persistence.Entities.GachaPoolEntry;

namespace Promuse.Api.Gacha;

/// <summary>
/// Headhunting, with the server holding the odds, the pity counts and the dice.
///
/// 客户端只说抽哪个池几次 / A pull request names a banner and a count. It cannot name a price,
/// an operator or a rarity - the client used to advertise odds it had no way to enforce, and
/// that is the whole reason this lives here.
/// </summary>
public sealed class GachaService(
    PromuseDbContext db,
    TimeProvider clock,
    PlayerService players,
    Inventory inventory,
    IGachaDice dice,
    ILogger<GachaService> log)
{
    public const int MaxHistoryPage = 50;
    public const int DefaultHistoryPage = 10;

    public async Task<Outcome<GachaBannerList>> GetBannersAsync(Guid accountId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        List<GachaBanner> banners = await db.GachaBanners
            .AsNoTracking()
            .Include(b => b.Pool)
            .Where(b => b.IsActive && (b.EndsAt == null || b.EndsAt > now))
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        Dictionary<string, int> pity = await db.GachaPity
            .AsNoTracking()
            .Where(p => p.AccountId == accountId)
            .ToDictionaryAsync(p => p.BannerId, p => p.PullsSinceFiveStar, ct);

        List<GachaBannerInfo> served = [];

        foreach (GachaBanner banner in banners)
        {
            // 不发不能兑现的卡池 / A banner that cannot keep its promises is not shown at all,
            // rather than shown and then failing every pull.
            string? broken = GachaRoller.Validate(Odds(banner), Members(banner));
            if (broken is not null)
            {
                log.LogError("Gacha banner {BannerId} is not served: {Reason}", banner.BannerId, broken);
                continue;
            }

            served.Add(ToInfo(banner, pity.GetValueOrDefault(banner.BannerId)));
        }

        return Outcome<GachaBannerList>.Ok(new GachaBannerList(served, now));
    }

    /// <summary>
    /// 一次请求, 全有或全无 / One request, all or nothing. The payment, every operator granted,
    /// every duplicate converted, the pity count, the history rows and the state version move
    /// together or not at all - a crash between charging and granting would otherwise take a
    /// player's Orundum and give them nothing.
    /// </summary>
    public async Task<Outcome<GachaPullResult>> PullAsync(
        Guid accountId, string bannerId, int times, CancellationToken ct)
    {
        if (times != 1 && times != 10)
        {
            return Outcome<GachaPullResult>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["times"] = ["Must be 1 or 10."] }));
        }

        GachaBanner? banner = await db.GachaBanners
            .AsNoTracking()
            .Include(b => b.Pool)
            .FirstOrDefaultAsync(b => b.BannerId == bannerId && b.IsActive, ct);

        if (banner is null) return Outcome<GachaPullResult>.Fail(ApiProblems.BannerNotFound(bannerId));

        DateTimeOffset now = clock.GetUtcNow();

        if (banner.StartsAt is { } starts && now < starts)
        {
            return Outcome<GachaPullResult>.Fail(ApiProblems.BannerClosed(notYetOpen: true));
        }

        if (banner.EndsAt is { } ends && now >= ends)
        {
            return Outcome<GachaPullResult>.Fail(ApiProblems.BannerClosed(notYetOpen: false));
        }

        GachaOdds odds = Odds(banner);
        List<PoolMember> pool = Members(banner);

        string? broken = GachaRoller.Validate(odds, pool);
        if (broken is not null)
        {
            // The listing already hides such a banner; reaching here means someone pulled on it
            // by id. A 500 with a trace id, not a guess at odds nobody published.
            throw new InvalidOperationException($"Gacha banner {bannerId} is misconfigured: {broken}");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        ItemStack? charged = await ChargeAsync(accountId, banner, times, ct);
        if (charged is null) return Outcome<GachaPullResult>.Fail(ApiProblems.InsufficientFunds());

        int pity = await LockPityAsync(accountId, bannerId, ct);

        Guid batchId = Guid.NewGuid();
        List<GachaPullOutcome> outcomes = [];
        Dictionary<int, int> converted = [];

        for (int i = 0; i < times; i++)
        {
            GachaRoll roll = GachaRoller.Next(odds, pool, ref pity, dice);
            GachaPoolEntry entry = banner.Pool.First(p => p.CharacterId == roll.Member.CharacterId);

            bool isNew = await GrantAsync(accountId, entry.CharacterId, ct);
            ItemStack? copy = null;

            if (!isNew)
            {
                copy = new ItemStack(entry.DuplicateItemId, entry.DuplicateAmount);
                converted[entry.DuplicateItemId] = converted.GetValueOrDefault(entry.DuplicateItemId) + entry.DuplicateAmount;
            }

            outcomes.Add(new GachaPullOutcome(entry.CharacterId, entry.Rarity, isNew, copy, roll.Guaranteed));

            db.GachaPulls.Add(new GachaPull
            {
                AccountId = accountId,
                BatchId = batchId,
                BannerId = bannerId,
                CharacterId = entry.CharacterId,
                Rarity = entry.Rarity,
                IsNew = isNew,
                Guaranteed = roll.Guaranteed,
                CreatedAt = now,
            });
        }

        // 合并成每种物品一次 / One credit per item rather than one per duplicate: a ten-pull of
        // nine copies is one upsert, not nine.
        foreach ((int itemId, int amount) in converted)
        {
            await inventory.CreditAsync(accountId, itemId, amount, ct);
        }

        await db.GachaPity
            .Where(p => p.AccountId == accountId && p.BannerId == bannerId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PullsSinceFiveStar, pity), ct);

        await db.SaveChangesAsync(ct);

        // 版本要动 / Bumped so the ETag the client is holding stops matching: the roster and
        // the bag both changed.
        await inventory.BumpStateVersionAsync(accountId, now, ct);

        await transaction.CommitAsync(ct);

        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);
        if (!state.IsSuccess) return Outcome<GachaPullResult>.Fail(state.Problem!);

        return Outcome<GachaPullResult>.Ok(
            new GachaPullResult(batchId, bannerId, outcomes, charged, pity, state.Value!), created: true);
    }

    public async Task<Outcome<GachaHistoryPage>> GetHistoryAsync(
        Guid accountId, long? before, int limit, CancellationToken ct)
    {
        if (limit < 1 || limit > MaxHistoryPage)
        {
            return Outcome<GachaHistoryPage>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["limit"] = [$"Must be between 1 and {MaxHistoryPage}."] }));
        }

        IQueryable<GachaPull> query = db.GachaPulls.AsNoTracking().Where(p => p.AccountId == accountId);
        if (before is { } cursor) query = query.Where(p => p.Id < cursor);

        // 多取一条 / One more than asked, so "is there a next page" is answered by this query
        // rather than by a second one or by a page that turns out to be empty.
        List<GachaPull> rows = await query
            .OrderByDescending(p => p.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        bool more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);

        List<GachaHistoryEntry> entries = [.. rows.Select(p => new GachaHistoryEntry(
            p.Id, p.BatchId, p.BannerId, p.CharacterId, p.Rarity, p.IsNew, p.CreatedAt))];

        return Outcome<GachaHistoryPage>.Ok(new GachaHistoryPage(entries, more ? rows[^1].Id : null));
    }

    // ------------------------------------------------------------------ steps

    /// <summary>
    /// Permits first, when there are enough for the whole request; otherwise the banner's
    /// currency. Null when neither covers it. Never mixes the two in one request - a ten-pull
    /// paid with three permits and 1260 Orundum is a rule the player would have to read twice.
    /// </summary>
    private async Task<ItemStack?> ChargeAsync(Guid accountId, GachaBanner banner, int times, CancellationToken ct)
    {
        if (banner.TicketItemId is { } ticket && await inventory.TryDebitAsync(accountId, ticket, times, ct))
        {
            return new ItemStack(ticket, times);
        }

        int cost = times == 1 ? banner.CostSingle : banner.CostMulti;

        return await inventory.TryDebitAsync(accountId, banner.CurrencyItemId, cost, ct)
            ? new ItemStack(banner.CurrencyItemId, cost)
            : null;
    }

    /// <summary>
    /// The player's count on this banner, with the row locked until the transaction ends.
    ///
    /// 必须加锁 / Locked because two pulls on the same banner arriving together would otherwise
    /// both read 29, both give the guarantee, and both write 0 - one 5-star too many. The insert
    /// makes sure there is a row to lock; FOR UPDATE makes the second request wait and then read
    /// what the first one wrote.
    /// </summary>
    private async Task<int> LockPityAsync(Guid accountId, string bannerId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO gacha_pity (account_id, banner_id, pulls_since_five_star)
             VALUES ({accountId}, {bannerId}, 0)
             ON CONFLICT (account_id, banner_id) DO NOTHING
             """, ct);

        return await db.Database
            .SqlQuery<int>(
                $"""
                 SELECT pulls_since_five_star AS "Value" FROM gacha_pity
                 WHERE account_id = {accountId} AND banner_id = {bannerId}
                 FOR UPDATE
                 """)
            .FirstAsync(ct);
    }

    /// <summary>
    /// Adds the operator to the roster and reports whether it was new.
    ///
    /// 让数据库判断是不是新的 / The insert itself answers "new or duplicate": one row affected is
    /// a new operator, none is a copy already owned. Reading the roster first and deciding in code
    /// would let two banners pulled at once both grant the same operator as new - and the second
    /// insert would then fail on the primary key and throw the whole request away.
    /// </summary>
    private async Task<bool> GrantAsync(Guid accountId, string characterId, CancellationToken ct)
    {
        int inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO player_characters (account_id, character_id, elite, level, exp, trust)
             VALUES ({accountId}, {characterId}, 0, 1, 0, 0)
             ON CONFLICT (account_id, character_id) DO NOTHING
             """, ct);

        return inserted == 1;
    }

    private static GachaOdds Odds(GachaBanner banner) =>
        new(banner.RateFiveStar, banner.RateFourStar, banner.RateThreeStar, banner.PityThreshold);

    private static List<PoolMember> Members(GachaBanner banner) =>
        [.. banner.Pool.OrderBy(p => p.CharacterId, StringComparer.Ordinal).Select(p => new PoolMember(p.CharacterId, p.Rarity))];

    private static GachaBannerInfo ToInfo(GachaBanner banner, int pity) =>
        new(banner.BannerId,
            banner.StartsAt,
            banner.EndsAt,
            banner.RateFiveStar,
            banner.RateFourStar,
            banner.RateThreeStar,
            banner.PityThreshold,
            banner.CurrencyItemId,
            banner.CostSingle,
            banner.CostMulti,
            banner.TicketItemId,
            [.. banner.Pool
                .OrderByDescending(p => p.Rarity)
                .ThenBy(p => p.CharacterId, StringComparer.Ordinal)
                .Select(p => new Contracts.Gacha.GachaPoolEntry(p.CharacterId, p.Rarity, p.DuplicateItemId, p.DuplicateAmount))],
            pity);
}

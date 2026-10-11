using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.Missions;
using Promuse.Api.Players;
using Promuse.Contracts;
using Promuse.Contracts.Events;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Events;

/// <summary>
/// Events as players see them, and everything they can do in one: check in, claim missions and
/// milestones, buy from the event shop.
///
/// 全有或全无 / Every action that hands something out records it and pays it in one transaction,
/// the same as the daily board and the shop: a crash between the two would pay twice or not at
/// all. And every one answers with the event as it now stands plus the whole player.
/// </summary>
public sealed class EventService(
    PromuseDbContext db,
    TimeProvider clock,
    PlayerService players,
    Inventory inventory,
    MissionPeriod period)
{
    public const string MissionKind = "mission";
    public const string MilestoneKind = "milestone";
    public const int MaxQuantity = 99;

    // ------------------------------------------------------------------ reads

    /// <summary>Events running now or starting later, ending soonest first.</summary>
    public async Task<EventList> ListAsync(CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        List<GameEvent> rows = await db.GameEvents
            .AsNoTracking()
            .Where(e => e.IsActive && e.EndsAt > now)
            .OrderBy(e => e.EndsAt)
            .ToListAsync(ct);

        List<EventSummary> events = [.. rows.Select(e =>
        {
            EventDefinition d = EventRules.Parse(e.Definition);
            return new EventSummary(e.EventId, d.Title, d.Subtitle, d.StartsAt, d.EndsAt, EventRules.IsOpen(d, now));
        })];

        return new EventList(events, now);
    }

    public async Task<Outcome<EventView>> ViewAsync(Guid accountId, string eventId, CancellationToken ct)
    {
        EventDefinition? d = await DefinitionAsync(eventId, ct);
        if (d is null) return Outcome<EventView>.Fail(ApiProblems.EventNotFound(eventId));

        return Outcome<EventView>.Ok(await BuildViewAsync(accountId, eventId, d, clock.GetUtcNow(), ct));
    }

    // ---------------------------------------------------------------- actions

    /// <summary>
    /// The next day's reward. Days go in order - a missed day is not lost, it is simply claimed on
    /// a later one - but only one a server day, which the database enforces.
    /// </summary>
    public async Task<Outcome<EventActionResult>> CheckInAsync(Guid accountId, string eventId, CancellationToken ct)
    {
        (EventDefinition? d, DateTimeOffset now, ApiProblem? problem) = await OpenAsync(eventId, ct);
        if (problem is not null) return Fail(problem);

        if (d!.CheckIn.Count == 0) return Fail(ApiProblems.NothingToClaim("This event has no check-in."));

        int claimed = await db.EventCheckIns.CountAsync(c => c.AccountId == accountId && c.EventId == eventId, ct);
        if (claimed >= d.CheckIn.Count) return Fail(ApiProblems.NothingToClaim("Every day of this event has been checked in."));

        CheckInDay day = d.CheckIn[claimed];

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.EventCheckIns.Add(new EventCheckIn
        {
            AccountId = accountId, EventId = eventId, Day = day.Day,
            PeriodKey = period.DailyKey(now), ClaimedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 今天已经签过 / Either today's key or this day number is already taken - a second tap,
            // or a check-in earlier today. Both mean there is nothing more to give today.
            db.ChangeTracker.Clear();
            return Fail(ApiProblems.NothingToClaim("Today's check-in has been claimed. Come back tomorrow."));
        }

        await inventory.CreditAsync(accountId, day.Reward.ItemId, day.Reward.Amount, ct);
        await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        return await ResultAsync(accountId, eventId, d, [new ItemStack(day.Reward.ItemId, day.Reward.Amount)], ct);
    }

    /// <summary>A finished mission pays its points. Points only - nothing in the bag changes.</summary>
    public async Task<Outcome<EventActionResult>> ClaimMissionAsync(
        Guid accountId, string eventId, string missionId, CancellationToken ct)
    {
        (EventDefinition? d, DateTimeOffset now, ApiProblem? problem) = await OpenAsync(eventId, ct);
        if (problem is not null) return Fail(problem);

        EventMission? mission = d!.Missions.FirstOrDefault(m => m.MissionId == missionId);
        if (mission is null) return Fail(ApiProblems.MissionNotFound(missionId));

        int count = await db.EventCounters.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.EventId == eventId && c.MissionId == missionId)
            .Select(c => c.Count).FirstOrDefaultAsync(ct);

        if (count < mission.Target) return Fail(ApiProblems.MissionNotComplete(count, mission.Target));

        if (!await TryClaimAsync(accountId, eventId, MissionKind, missionId, now, ct))
        {
            return Fail(ApiProblems.AlreadyClaimed(missionId));
        }

        return await ResultAsync(accountId, eventId, d, [], ct);
    }

    public async Task<Outcome<EventActionResult>> ClaimMilestoneAsync(
        Guid accountId, string eventId, string rewardId, CancellationToken ct)
    {
        (EventDefinition? d, DateTimeOffset now, ApiProblem? problem) = await OpenAsync(eventId, ct);
        if (problem is not null) return Fail(problem);

        EventMilestone? milestone = d!.Milestones.FirstOrDefault(r => r.RewardId == rewardId);
        if (milestone is null) return Fail(ApiProblems.MissionNotFound(rewardId));

        int points = await PointsAsync(accountId, eventId, d, ct);
        if (points < milestone.RequiredPoints) return Fail(ApiProblems.NotEnoughPoints(points, milestone.RequiredPoints));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (!await TryClaimAsync(accountId, eventId, MilestoneKind, rewardId, now, ct))
        {
            return Fail(ApiProblems.AlreadyClaimed(rewardId));
        }

        await inventory.CreditAsync(accountId, milestone.Reward.ItemId, milestone.Reward.Amount, ct);
        await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        return await ResultAsync(accountId, eventId, d,
            [new ItemStack(milestone.Reward.ItemId, milestone.Reward.Amount)], ct);
    }

    /// <summary>
    /// Buys from the event shop with the event's currency.
    ///
    /// 限购在数据库里判 / The limit is checked by the UPDATE that records the purchase - it only
    /// adds when the new total stays within the limit - so two purchases racing past the last unit
    /// cannot both succeed. The currency is debited first and a refused purchase rolls it back.
    /// </summary>
    public async Task<Outcome<EventActionResult>> PurchaseAsync(
        Guid accountId, string eventId, string offerId, int quantity, CancellationToken ct)
    {
        if (quantity < 1 || quantity > MaxQuantity)
        {
            return Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["quantity"] = [$"Must be between 1 and {MaxQuantity}."],
            }));
        }

        (EventDefinition? d, DateTimeOffset now, ApiProblem? problem) = await OpenAsync(eventId, ct);
        if (problem is not null) return Fail(problem);

        EventShopOffer? offer = d!.Shop.FirstOrDefault(o => o.OfferId == offerId);
        if (offer is null || d.CurrencyItemId is not { } currency) return Fail(ApiProblems.OfferNotFound());

        if (offer.Limit is { } cap && quantity > cap) return Fail(ApiProblems.OutOfStock(offerId, cap));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (!await inventory.TryDebitAsync(accountId, currency, offer.Price * quantity, ct))
        {
            return Fail(ApiProblems.InsufficientFunds());
        }

        int limit = offer.Limit ?? int.MaxValue;
        int recorded = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO event_purchases (account_id, event_id, offer_id, bought)
             VALUES ({accountId}, {eventId}, {offerId}, {quantity})
             ON CONFLICT (account_id, event_id, offer_id)
             DO UPDATE SET bought = event_purchases.bought + EXCLUDED.bought
             WHERE event_purchases.bought + EXCLUDED.bought <= {limit}
             """, ct);

        if (recorded == 0)
        {
            int bought = await db.EventPurchases.AsNoTracking()
                .Where(p => p.AccountId == accountId && p.EventId == eventId && p.OfferId == offerId)
                .Select(p => p.Bought).FirstOrDefaultAsync(ct);

            return Fail(ApiProblems.OutOfStock(offerId, limit - bought));
        }

        int amount = offer.Sell.Amount * quantity;
        await inventory.CreditAsync(accountId, offer.Sell.ItemId, amount, ct);
        await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        return await ResultAsync(accountId, eventId, d, [new ItemStack(offer.Sell.ItemId, amount)], ct);
    }

    // ---------------------------------------------------------------- helpers

    private static Outcome<EventActionResult> Fail(ApiProblem problem) => Outcome<EventActionResult>.Fail(problem);

    private async Task<EventDefinition?> DefinitionAsync(string eventId, CancellationToken ct)
    {
        string? json = await db.GameEvents.AsNoTracking()
            .Where(e => e.EventId == eventId && e.IsActive)
            .Select(e => e.Definition)
            .FirstOrDefaultAsync(ct);

        return json is null ? null : EventRules.Parse(json);
    }

    /// <summary>The event, if it exists and is running right now; otherwise why not.</summary>
    private async Task<(EventDefinition?, DateTimeOffset, ApiProblem?)> OpenAsync(string eventId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        EventDefinition? d = await DefinitionAsync(eventId, ct);

        if (d is null) return (null, now, ApiProblems.EventNotFound(eventId));
        if (now < d.StartsAt) return (d, now, ApiProblems.EventClosed(notYetOpen: true));
        if (now >= d.EndsAt) return (d, now, ApiProblems.EventClosed(notYetOpen: false));

        return (d, now, null);
    }

    private async Task<bool> TryClaimAsync(
        Guid accountId, string eventId, string kind, string key, DateTimeOffset now, CancellationToken ct)
    {
        db.EventClaims.Add(new EventClaim { AccountId = accountId, EventId = eventId, Kind = kind, Key = key, ClaimedAt = now });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>Points from the missions claimed - only those still in the definition.</summary>
    private async Task<int> PointsAsync(Guid accountId, string eventId, EventDefinition d, CancellationToken ct)
    {
        List<string> claimed = await db.EventClaims.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.EventId == eventId && c.Kind == MissionKind)
            .Select(c => c.Key).ToListAsync(ct);

        return d.Missions.Where(m => claimed.Contains(m.MissionId)).Sum(m => m.Points);
    }

    private async Task<EventView> BuildViewAsync(
        Guid accountId, string eventId, EventDefinition d, DateTimeOffset now, CancellationToken ct)
    {
        bool open = EventRules.IsOpen(d, now);

        Dictionary<string, int> counts = await db.EventCounters.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.EventId == eventId)
            .ToDictionaryAsync(c => c.MissionId, c => c.Count, ct);

        List<EventClaim> claims = await db.EventClaims.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.EventId == eventId)
            .ToListAsync(ct);

        HashSet<string> missionsClaimed = [.. claims.Where(c => c.Kind == MissionKind).Select(c => c.Key)];
        HashSet<string> milestonesClaimed = [.. claims.Where(c => c.Kind == MilestoneKind).Select(c => c.Key)];

        int points = d.Missions.Where(m => missionsClaimed.Contains(m.MissionId)).Sum(m => m.Points);

        List<EventMissionState> missions = [.. d.Missions.Select(m =>
        {
            int progress = Math.Min(counts.GetValueOrDefault(m.MissionId), m.Target);
            return new EventMissionState(m, progress, progress >= m.Target, missionsClaimed.Contains(m.MissionId));
        })];

        List<EventMilestoneState> milestones = [.. d.Milestones.Select(r =>
        {
            bool taken = milestonesClaimed.Contains(r.RewardId);
            return new EventMilestoneState(r, taken, open && !taken && points >= r.RequiredPoints);
        })];

        Dictionary<string, int> bought = await db.EventPurchases.AsNoTracking()
            .Where(p => p.AccountId == accountId && p.EventId == eventId)
            .ToDictionaryAsync(p => p.OfferId, p => p.Bought, ct);

        List<EventShopState> shop = [.. d.Shop.Select(o =>
        {
            int had = bought.GetValueOrDefault(o.OfferId);
            return new EventShopState(o, had, o.Limit is { } cap ? Math.Max(0, cap - had) : null);
        })];

        CheckInState? checkIn = null;
        if (d.CheckIn.Count > 0)
        {
            List<string> days = await db.EventCheckIns.AsNoTracking()
                .Where(c => c.AccountId == accountId && c.EventId == eventId)
                .Select(c => c.PeriodKey).ToListAsync(ct);

            bool today = days.Contains(period.DailyKey(now));
            checkIn = new CheckInState(days.Count, open && !today && days.Count < d.CheckIn.Count,
                period.ResetsAt(MissionTab.Daily, now));
        }

        int balance = 0;
        if (d.CurrencyItemId is { } currency)
        {
            balance = await db.PlayerItems.AsNoTracking()
                .Where(i => i.AccountId == accountId && i.ItemId == currency)
                .Select(i => i.Amount).FirstOrDefaultAsync(ct);
        }

        return new EventView(eventId, d, open, balance, checkIn, points, missions, milestones, shop, now);
    }

    private async Task<Outcome<EventActionResult>> ResultAsync(
        Guid accountId, string eventId, EventDefinition d, IReadOnlyList<ItemStack> granted, CancellationToken ct)
    {
        EventView view = await BuildViewAsync(accountId, eventId, d, clock.GetUtcNow(), ct);
        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);

        if (!state.IsSuccess) return Fail(state.Problem!);

        return Outcome<EventActionResult>.Ok(new EventActionResult(granted, view, state.Value!), created: true);
    }
}

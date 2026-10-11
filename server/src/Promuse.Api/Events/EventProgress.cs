using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Contracts.Events;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Events;

/// <summary>
/// Counts what players do towards the events that are running, and pays the event stages.
///
/// 在调用方的事务里 / Called from inside the transaction of whatever happened - a run closing, a
/// checkout, a pull - and opens none of its own, the same way MissionService.NotifyAsync works. A
/// rollback of the happening takes the event progress with it, so a refused purchase cannot still
/// tick a mission.
///
/// 只算开着的活动 / Only events open at that moment count: progress made before an event starts
/// or after it ends is not carried into it.
/// </summary>
public sealed class EventProgress(PromuseDbContext db, Inventory inventory)
{
    /// <summary>
    /// A run was closed. Advances PlaySong / ClearStage / ReachScore missions when it counted as a
    /// clear, and pays the currency of every open event that lists the chart.
    /// </summary>
    /// <returns>True when the player's bag changed, so the caller bumps the state version.</returns>
    public async Task<bool> OnRunClosedAsync(
        Guid accountId, string stageId, bool cleared, bool accepted, int? score, DateTimeOffset now, CancellationToken ct)
    {
        if (!cleared) return false;

        bool paid = false;

        foreach ((string eventId, EventDefinition d) in await OpenAsync(now, ct))
        {
            foreach (EventMission m in EventRules.Matching(d, EventGoal.PlaySong, stageId, score, accepted))
            {
                await CountAsync(accountId, eventId, m.MissionId, 1, ct);
            }

            EventStage? stage = d.Stages.FirstOrDefault(s => s.StageId == stageId);
            if (stage is not null && d.CurrencyItemId is { } currency)
            {
                await inventory.CreditAsync(accountId, currency, stage.CurrencyPerClear, ct);
                paid = true;
            }
        }

        return paid;
    }

    public Task OnShopCheckoutAsync(Guid accountId, DateTimeOffset now, CancellationToken ct) =>
        CountGoalAsync(accountId, EventGoal.BuyShopItem, 1, now, ct);

    public Task OnHeadhuntAsync(Guid accountId, int pulls, DateTimeOffset now, CancellationToken ct) =>
        CountGoalAsync(accountId, EventGoal.Headhunt, pulls, now, ct);

    private async Task CountGoalAsync(Guid accountId, EventGoal goal, int amount, DateTimeOffset now, CancellationToken ct)
    {
        foreach ((string eventId, EventDefinition d) in await OpenAsync(now, ct))
        {
            foreach (EventMission m in EventRules.Matching(d, goal, null, null, accepted: false))
            {
                await CountAsync(accountId, eventId, m.MissionId, amount, ct);
            }
        }
    }

    /// <summary>The events running at <paramref name="now"/>, definitions parsed.</summary>
    public async Task<List<(string EventId, EventDefinition Definition)>> OpenAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<GameEvent> rows = await db.GameEvents
            .AsNoTracking()
            .Where(e => e.IsActive && e.StartsAt <= now && e.EndsAt > now)
            .ToListAsync(ct);

        return [.. rows.Select(e => (e.EventId, EventRules.Parse(e.Definition)))];
    }

    /// <summary>
    /// 用 upsert 累加 / An upsert that adds, rather than read-then-write: two clears closing at
    /// once would otherwise both read 2 and both write 3.
    /// </summary>
    private Task CountAsync(Guid accountId, string eventId, string missionId, int amount, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO event_counters (account_id, event_id, mission_id, count)
             VALUES ({accountId}, {eventId}, {missionId}, {amount})
             ON CONFLICT (account_id, event_id, mission_id)
             DO UPDATE SET count = event_counters.count + EXCLUDED.count
             """, ct);
}

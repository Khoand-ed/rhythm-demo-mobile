using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Contracts;
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
    /// A run was closed. Advances PlaySong / ClearStage / ReachScore missions of every open event
    /// when it counted as a clear, wherever the song was played from.
    ///
    /// 活动关卡只认活动里打的 / The stage itself - the clear that unlocks the next one, and the
    /// currency - only for a run opened as a stage of that event (<paramref name="runEventId"/>):
    /// the same chart played from the song list is not the event's stage.
    /// </summary>
    /// <returns>True when the player's bag changed, so the caller bumps the state version.</returns>
    public async Task<bool> OnRunClosedAsync(
        Guid accountId, string stageId, string? runEventId, bool cleared, bool accepted, int? score,
        DateTimeOffset now, CancellationToken ct)
    {
        if (!cleared) return false;

        bool paid = false;

        foreach ((string eventId, EventDefinition d) in await OpenAsync(now, ct))
        {
            foreach (EventMission m in EventRules.Matching(d, EventGoal.PlaySong, stageId, score, accepted))
            {
                await CountAsync(accountId, eventId, m.MissionId, 1, ct);
            }

            if (eventId != runEventId) continue;

            EventStage? stage = d.Stages.FirstOrDefault(s => s.StageId == stageId);
            if (stage is null) continue;

            await ClearAsync(accountId, eventId, stageId, now, ct);

            if (d.CurrencyItemId is { } currency)
            {
                await inventory.CreditAsync(accountId, currency, stage.CurrencyPerClear, ct);
                paid = true;
            }
        }

        return paid;
    }

    /// <summary>
    /// 开局前查 / Whether a run may open as a stage of this event: the event is running, the chart
    /// is one of its stages, and the stage before it has been cleared. Null when it may.
    /// </summary>
    public async Task<ApiProblem?> CanStartAsync(
        Guid accountId, string eventId, string stageId, DateTimeOffset now, CancellationToken ct)
    {
        string? json = await db.GameEvents.AsNoTracking()
            .Where(e => e.EventId == eventId && e.IsActive)
            .Select(e => e.Definition)
            .FirstOrDefaultAsync(ct);

        if (json is null) return ApiProblems.EventNotFound(eventId);

        EventDefinition d = EventRules.Parse(json);
        if (now < d.StartsAt) return ApiProblems.EventClosed(notYetOpen: true);
        if (now >= d.EndsAt) return ApiProblems.EventClosed(notYetOpen: false);

        int index = d.Stages.ToList().FindIndex(s => s.StageId == stageId);
        if (index < 0)
        {
            return ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["stageId"] = [$"'{stageId}' is not a stage of event '{eventId}'."],
            });
        }

        HashSet<string> cleared = await ClearedAsync(accountId, eventId, ct);
        return EventRules.IsUnlocked(d, index, cleared) ? null : ApiProblems.StageLocked(d.Stages[index - 1].StageId);
    }

    /// <summary>The stages of one event this player has cleared.</summary>
    public async Task<HashSet<string>> ClearedAsync(Guid accountId, string eventId, CancellationToken ct) =>
        [.. await db.EventStageClears.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.EventId == eventId)
            .Select(c => c.StageId)
            .ToListAsync(ct)];

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

    private Task ClearAsync(Guid accountId, string eventId, string stageId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO event_stage_clears (account_id, event_id, stage_id, clears, first_cleared_at)
             VALUES ({accountId}, {eventId}, {stageId}, 1, {now})
             ON CONFLICT (account_id, event_id, stage_id)
             DO UPDATE SET clears = event_stage_clears.clears + 1
             """, ct);

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

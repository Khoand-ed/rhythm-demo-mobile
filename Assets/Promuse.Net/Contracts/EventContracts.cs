// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.Events
{
    /// <summary>What an event mission counts.</summary>
    public enum EventGoal
    {
        /// <summary>Clear any song (ranked, not rejected).</summary>
        PlaySong,

        /// <summary>Check out in the store - one per checkout, whatever the quantity.</summary>
        BuyShopItem,

        /// <summary>Clear one particular chart - the mission's StageId.</summary>
        ClearStage,

        /// <summary>
        /// Reach MinScore, on the mission's StageId if it names one, otherwise on any song. Only a
        /// result the server accepted counts: a score it could not vouch for is not an achievement.
        /// </summary>
        ReachScore,

        /// <summary>Headhunting pulls - a ten-pull counts ten.</summary>
        Headhunt
    }

    public sealed record EventReward(int ItemId, int Amount);

    /// <summary>The reward for the Nth day checked in. Days are claimed in order, one per day.</summary>
    public sealed record CheckInDay(int Day, EventReward Reward);

    /// <param name="Target">How many times the goal has to happen during the event.</param>
    /// <param name="Points">Paid when the mission is claimed; milestones are reached with these.</param>
    /// <param name="StageId">Required for ClearStage; optional for ReachScore.</param>
    /// <param name="MinScore">Required for ReachScore.</param>
    public sealed record EventMission(
        string MissionId,
        string Description,
        EventGoal Goal,
        int Target,
        int Points,
        string? StageId,
        int? MinScore);

    public sealed record EventMilestone(string RewardId, int RequiredPoints, EventReward Reward);

    /// <param name="Price">In the event's currency.</param>
    /// <param name="Limit">How many one player may buy over the whole event; null for unlimited.</param>
    public sealed record EventShopOffer(string OfferId, EventReward Sell, int Price, int? Limit);

    /// <summary>A chart that pays the event currency each time it is cleared while the event is open.</summary>
    public sealed record EventStage(string StageId, int CurrencyPerClear);

    /// <summary>
    /// 一个活动就是一份文档 / An event is one document, edited whole by an operator.
    ///
    /// 模块按需 / Every module is optional and switched on by having content: an event with only
    /// check-in days is a check-in event, one with missions and a shop is a bigger one. A module
    /// added later - a mini game - is one more list here, not a new kind of event.
    /// </summary>
    /// <param name="CurrencyItemId">
    /// The event's own currency. Required when the event has a shop or stages; paid out by
    /// whatever rewards name it, and by the stages.
    /// </param>
    /// <param name="BannerId">A headhunting banner the event points to, if any.</param>
    /// <param name="IsActive">Off hides the event whatever its dates say.</param>
    public sealed record EventDefinition(
        string Title,
        string? Subtitle,
        string? Description,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        bool IsActive,
        int? CurrencyItemId,
        string? BannerId,
        IReadOnlyList<CheckInDay> CheckIn,
        IReadOnlyList<EventMission> Missions,
        IReadOnlyList<EventMilestone> Milestones,
        IReadOnlyList<EventShopOffer> Shop,
        IReadOnlyList<EventStage> Stages);

    // ------------------------------------------------------------------ player

    /// <param name="IsOpen">Started and not yet ended - claims and purchases are allowed.</param>
    public sealed record EventSummary(
        string EventId,
        string Title,
        string? Subtitle,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        bool IsOpen);

    /// <summary>Events open now or opening later, ending soonest first.</summary>
    public sealed record EventList(IReadOnlyList<EventSummary> Events, DateTimeOffset ServerTime);

    /// <param name="Claimed">Days checked in so far.</param>
    /// <param name="CanClaimToday">Open, a day left, and today not yet claimed.</param>
    /// <param name="NextDayAt">When the next day's check-in becomes available.</param>
    public sealed record CheckInState(int Claimed, bool CanClaimToday, DateTimeOffset NextDayAt);

    public sealed record EventMissionState(EventMission Mission, int Progress, bool IsComplete, bool IsClaimed);

    public sealed record EventMilestoneState(EventMilestone Milestone, bool IsClaimed, bool IsClaimable);

    /// <param name="Remaining">Null when the offer has no limit.</param>
    public sealed record EventShopState(EventShopOffer Offer, int Bought, int? Remaining);

    /// <summary>One event as the player sees it: the definition and their own progress in it.</summary>
    /// <param name="Points">From claimed missions.</param>
    public sealed record EventView(
        string EventId,
        EventDefinition Definition,
        bool IsOpen,
        int CurrencyBalance,
        CheckInState? CheckIn,
        int Points,
        IReadOnlyList<EventMissionState> Missions,
        IReadOnlyList<EventMilestoneState> Milestones,
        IReadOnlyList<EventShopState> Shop,
        DateTimeOffset ServerTime);

    /// <summary>
    /// 领取/购买都回全部 / Every claim and purchase answers with what was given, the event as it now
    /// stands and the whole player, so the screen never works out a balance itself.
    /// </summary>
    public sealed record EventActionResult(
        IReadOnlyList<ItemStack> Granted,
        EventView Event,
        PlayerState Player);

    public sealed record EventPurchaseRequest(int Quantity);

    // ------------------------------------------------------------------ admin

    /// <param name="Version">The ETag for the next save of this event.</param>
    public sealed record AdminEvent(
        string EventId,
        int Version,
        EventDefinition Definition,
        DateTimeOffset UpdatedAt,
        string? UpdatedBy);

    public sealed record AdminEventList(IReadOnlyList<AdminEvent> Events);

    public sealed record EventUpsertRequest(EventDefinition Definition);

    /// <summary>What an operator can point an event at: the charts and banners the server knows.</summary>
    public sealed record EventCatalog(IReadOnlyList<string> StageIds, IReadOnlyList<string> BannerIds);
}

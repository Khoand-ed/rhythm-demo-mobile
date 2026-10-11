namespace Promuse.Persistence.Entities;

/// <summary>
/// One live-ops event, as an operator wrote it.
///
/// 内容是一份文档 / The content - check-in days, missions, milestones, shop, stages - is a single
/// JSON document, because an operator edits it as a whole and it has no life of its own outside
/// the event. What players do with it is relational (the tables below), because that is what gets
/// counted, claimed and spent concurrently.
///
/// 时间窗复制出来 / The window and the on/off switch are also columns, copied from the document on
/// every save, so "which events are running" is an indexed query rather than parsing every
/// document on every song cleared.
/// </summary>
public class GameEvent
{
    /// <summary>Lower-case letters, digits and underscores - it appears in URLs and keys.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Bumped on every save; the ETag an admin edit must match.</summary>
    public int Version { get; set; }

    /// <summary>The EventDefinition as JSON (jsonb).</summary>
    public string Definition { get; set; } = "{}";

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// How far one player is on one event mission.
///
/// 按任务计数 / Per mission rather than per goal, unlike the daily board: event missions carry
/// parameters - this chart, that score - so two missions with the same goal can count different
/// things.
/// </summary>
public class EventCounter
{
    public Guid AccountId { get; set; }

    public string EventId { get; set; } = string.Empty;

    public string MissionId { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>
/// A mission or milestone a player has claimed. The row existing is the claim, so claiming twice
/// is a primary-key violation rather than a check that could race.
/// </summary>
public class EventClaim
{
    public Guid AccountId { get; set; }

    public string EventId { get; set; } = string.Empty;

    /// <summary>"mission" or "milestone".</summary>
    public string Kind { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public DateTimeOffset ClaimedAt { get; set; }
}

/// <summary>
/// One day checked in. The day number is the claim's order; the period key is the server day it
/// happened on, unique per player and event, which is what limits it to one a day.
/// </summary>
public class EventCheckIn
{
    public Guid AccountId { get; set; }

    public string EventId { get; set; } = string.Empty;

    public int Day { get; set; }

    public string PeriodKey { get; set; } = string.Empty;

    public DateTimeOffset ClaimedAt { get; set; }
}

/// <summary>How many of one event offer a player has bought, for the offer's limit.</summary>
public class EventPurchase
{
    public Guid AccountId { get; set; }

    public string EventId { get; set; } = string.Empty;

    public string OfferId { get; set; } = string.Empty;

    public int Bought { get; set; }
}

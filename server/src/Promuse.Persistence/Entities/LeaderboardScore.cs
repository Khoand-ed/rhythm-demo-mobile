namespace Promuse.Persistence.Entities;

/// <summary>
/// One player's best on one stage within one period.
///
/// 周期在主键里 / The period is part of the key - "all", or the ISO week - the same trick the
/// mission counters use. A new week is a key no row has yet, so the weekly board empties itself
/// at the boundary without a job, and last week's board is still there to be read.
///
/// 只升不降 / A row only ever moves up. The write is a single upsert guarded by
/// <c>WHERE EXCLUDED.score &gt; score</c>, so two results landing at once cannot leave the lower
/// one standing, and a worse run later leaves the best where it is.
/// </summary>
public class LeaderboardScore
{
    public string StageId { get; set; } = string.Empty;

    /// <summary>"all", or an ISO week such as "2026-W41".</summary>
    public string PeriodKey { get; set; } = string.Empty;

    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    public int Score { get; set; }

    public int MaxCombo { get; set; }

    public string? CharacterId { get; set; }

    /// <summary>The run that set it, so an entry can always be traced back to its evidence.</summary>
    public Guid RunId { get; set; }

    /// <summary>When it was set. Breaks ties: matching a score is not the same as holding it.</summary>
    public DateTimeOffset AchievedAt { get; set; }
}

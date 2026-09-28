using Promuse.Contracts.Missions;

namespace Promuse.Persistence.Entities;

/// <summary>
/// How many times one goal has happened, on one board, in one period.
///
/// 周期键就是重置 / The period key being part of the identity IS the reset. A new
/// day is a new key, a new key has no row, and a row that is not there reads as
/// zero. There is no scheduled job to wipe anything, which matters because a
/// scheduled job is a thing that can fail to run - and a reset that silently did
/// not happen is worse than one that visibly did not.
///
/// 旧的留着 / Yesterday's rows are simply left behind. They are the only record of
/// what a player did on a board that no longer exists, and they cost a row each.
/// </summary>
public class MissionCounter
{
    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    public MissionTab Tab { get; set; }

    public MissionGoal Goal { get; set; }

    /// <summary>
    /// <c>2026-09-28</c> for a daily board, <c>2026-W40</c> for a weekly one.
    /// Computed from the server clock by MissionPeriod, never sent by a client.
    /// </summary>
    public string PeriodKey { get; set; } = string.Empty;

    public int Count { get; set; }
}

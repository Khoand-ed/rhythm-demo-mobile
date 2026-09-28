using Promuse.Contracts.Missions;

namespace Promuse.Persistence.Entities;

/// <summary>
/// A mission whose points have been taken, in one period.
///
/// 存在即已领 / The row existing is the claim. That makes claiming twice a primary
/// key violation rather than something the service has to remember to check, and
/// a double-claim under concurrency is refused by the database rather than by a
/// race that sometimes loses.
/// </summary>
public class MissionClaim
{
    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    public MissionTab Tab { get; set; }

    public string MissionId { get; set; } = string.Empty;

    /// <summary>Part of the key, so a new period makes the mission claimable again.</summary>
    public string PeriodKey { get; set; } = string.Empty;

    public DateTimeOffset ClaimedAt { get; set; }
}

/// <summary>
/// A reward threshold that has been taken, in one period. Same shape and the
/// same reason as <see cref="MissionClaim"/>.
/// </summary>
public class MissionRewardClaim
{
    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    public MissionTab Tab { get; set; }

    public string RewardId { get; set; } = string.Empty;

    public string PeriodKey { get; set; } = string.Empty;

    public DateTimeOffset ClaimedAt { get; set; }
}

using Promuse.Contracts.Missions;

namespace Promuse.Persistence.Entities;

/// <summary>
/// A point threshold on a board and what crossing it hands over.
///
/// 两步领取 / The second half of a deliberately two-step flow: finishing a mission
/// makes its points claimable, claiming the points makes a reward claimable, and
/// neither happens on its own. Opening the board grants nothing.
/// </summary>
public class MissionRewardDefinition
{
    public MissionTab Tab { get; set; }

    /// <summary>Stable id like <c>daily.r1</c>. Part of the key with the tab.</summary>
    public string RewardId { get; set; } = string.Empty;

    /// <summary>
    /// Points that must have been CLAIMED on this board, not merely earned.
    /// Thresholds ascend, and the last one on each board is only reachable by
    /// claiming every mission on it.
    /// </summary>
    public int RequiredPoints { get; set; }

    public int ItemId { get; set; }

    public int Amount { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

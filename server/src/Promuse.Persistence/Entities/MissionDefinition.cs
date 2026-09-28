using Promuse.Contracts.Missions;

namespace Promuse.Persistence.Entities;

/// <summary>
/// One row of a mission board: a counter, a target on it, and the points it pays.
///
/// 从写死变成一张表 / Was hard-coded in MissionManager.Seed on the client, which
/// meant retuning a target was a store release. It is a table for the same reason
/// the shop catalogue is: this project is meant to be data-driven, and a number
/// the client holds is a number the client can be wrong about.
/// </summary>
public class MissionDefinition
{
    public MissionTab Tab { get; set; }

    /// <summary>Stable id like <c>daily.play1</c>. Part of the key with the tab.</summary>
    public string MissionId { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public MissionGoal Goal { get; set; }

    public int Target { get; set; }

    /// <summary>What claiming this mission pays into the board's point total.</summary>
    public int Points { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

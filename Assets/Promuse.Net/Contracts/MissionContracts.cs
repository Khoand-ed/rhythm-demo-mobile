// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;

namespace Promuse.Contracts.Missions
{
    /// <summary>
    /// 两个板子 / The two boards. Moved here from Data.Mission because the API
    /// returns them: an enum that crosses the wire is part of the wire format,
    /// and a second copy on the client is a second thing to keep in step.
    ///
    /// 名字进了存档键 / The member names are serialised as strings and are written
    /// into the period keys stored per player, so renaming one is a data
    /// migration and not a rename.
    /// </summary>
    public enum MissionTab
    {
        Daily,
        Weekly
    }

    /// <summary>
    /// What a mission counts. Deliberately a small closed set: every value needs
    /// a server-side place that advances it, and a goal nothing advances is a
    /// mission nobody can finish.
    /// </summary>
    public enum MissionGoal
    {
        PlaySong,
        BuyShopItem
    }

    /// <param name="Progress">Capped at <paramref name="Target"/>, so a bar never overfills.</param>
    /// <param name="IsClaimed">
    /// 做完和领了是两回事 / Finished and claimed are different states. A mission the
    /// player has not pressed is worth no points yet, which is the whole point of
    /// the manual step.
    /// </param>
    public sealed record MissionState(
        string MissionId,
        string Description,
        MissionGoal Goal,
        int Target,
        int Progress,
        int Points,
        bool IsComplete,
        bool IsClaimed);

    /// <param name="IsClaimable">
    /// Enough points have been claimed and this reward has not been taken.
    /// Computed by the server so the client never has to re-derive the threshold
    /// rule.
    /// </param>
    public sealed record RewardState(
        string RewardId,
        int RequiredPoints,
        int ItemId,
        int Amount,
        bool IsClaimed,
        bool IsClaimable);

    /// <param name="Points">
    /// The sum of the points from missions that have been CLAIMED on this board,
    /// not merely finished.
    /// </param>
    /// <param name="ResetsAt">
    /// When this board empties, in UTC. The client counts down from it rather
    /// than working out the boundary itself - the reset is 04:00 in the players'
    /// own timezone, and that arithmetic has no business being on a device whose
    /// clock is attacker-controlled.
    /// </param>
    public sealed record MissionBoard(
        MissionTab Tab,
        string Title,
        int Points,
        DateTimeOffset ResetsAt,
        IReadOnlyList<MissionState> Missions,
        IReadOnlyList<RewardState> Rewards);

    public sealed record MissionBoards(
        MissionBoard Daily,
        MissionBoard Weekly,
        DateTimeOffset ServerTime);

    public sealed record ClaimMissionRequest(MissionTab Tab, string MissionId);

    public sealed record ClaimRewardRequest(MissionTab Tab, string RewardId);

    /// <summary>
    /// What a claim handed over, and the state it left behind.
    /// </summary>
    /// <param name="Granted">
    /// Empty for a mission claim, which pays points rather than items. Populated
    /// for a reward claim.
    /// </param>
    public sealed record ClaimResult(
        IReadOnlyList<Players.ItemStack> Granted,
        MissionBoards Boards,
        Players.PlayerState Player);
}

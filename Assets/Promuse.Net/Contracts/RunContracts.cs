// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.Runs
{
    /// <param name="StageId">The chart being attempted, from <c>SongChart.stageId</c>.</param>
    public sealed record StartRunRequest(string StageId);

    /// <summary>
    /// Permission to play one song, and what it cost.
    ///
    /// 不是"扣理智"的回执 / Deliberately a ticket for a run rather than a receipt
    /// for spending stamina. The far end of this - submitting a result - can only
    /// be trusted if the server already knows a run was opened, on which chart,
    /// and with which seed.
    /// </summary>
    /// <param name="Seed">
    /// 服务端发, 从不接收 / Issued by the server and never accepted from the
    /// client. JudgeUpgradePassive rolls <c>Random.value</c> per judgement, so
    /// the scoring rules are not deterministic and the server cannot re-simulate
    /// a run without knowing the sequence. If the client chose this number it
    /// would simply re-roll until the draw was favourable.
    ///
    /// The client seeds its own generator from this before the first note.
    /// </param>
    public sealed record RunTicket(
        Guid RunId,
        string StageId,
        long Seed,
        int StaminaSpent,
        PlayerState Player,
        DateTimeOffset ServerTime);

    /// <param name="Won">
    /// 通关才算 / Only a win advances the "clear any song" missions, matching what
    /// GameManager already did: a run that ran out of HP is not a clear.
    /// </param>
    public sealed record CompleteRunRequest(bool Won);

    /// <summary>
    /// Closes a run.
    ///
    /// 现在很薄, 以后会厚 / Deliberately thin for now: it exists so the mission
    /// counters have something to advance them. Phase 4 adds the score and the
    /// input trace to the SAME endpoint and replays them against the seed this
    /// run was opened with - designing a separate "submit score" call now would
    /// mean two endpoints that both mean "the run ended".
    /// </summary>
    public sealed record RunCompletion(
        Guid RunId,
        bool Won,
        MissionBoards Boards,
        PlayerState Player,
        DateTimeOffset ServerTime);
}

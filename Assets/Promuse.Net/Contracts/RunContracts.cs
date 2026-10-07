// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.Runs
{
    /// <param name="StageId">The chart being attempted, from <c>SongChart.stageId</c>.</param>
    /// <param name="CharacterId">
    /// 选了谁要先说 / The operator, named before the song rather than with the result. The
    /// operator's modifiers are part of what a score is checked against, and a result that
    /// could name its own operator afterwards would simply name the strongest one. The server
    /// also refuses an operator the player does not own.
    /// </param>
    public sealed record StartRunRequest(string StageId, string CharacterId);

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
    /// <param name="Result">
    /// Required when <paramref name="Won"/> is true: a clear is only counted once its result
    /// has been checked, or "won" would be a word anyone could send. Optional for a failed run.
    /// </param>
    public sealed record CompleteRunRequest(bool Won, RunResult? Result);

    /// <summary>
    /// What the device says happened, and the raw input it happened from.
    ///
    /// 按判定命名 / Counts are named after the Judgement enum. The results screen calls Great
    /// "good" and Hit "normal"; those are older labels for the same tiers.
    /// </summary>
    /// <param name="RulesetFingerprint">
    /// RulesetFingerprint.Compute over the rules this run was played with. A mismatch means the
    /// device and the server disagree about the rules, and nothing else about the result can be
    /// judged until they agree.
    /// </param>
    /// <param name="Trace">
    /// The InputTrace, gzipped, then base64. Kept by the server so the run can be replayed
    /// against its seed.
    /// </param>
    public sealed record RunResult(
        int Score,
        int MaxCombo,
        int Perfect,
        int Great,
        int Hit,
        int Miss,
        bool FullCombo,
        string RulesetFingerprint,
        string Trace);

    /// <summary>
    /// 三种结论 / What the server made of a result.
    /// </summary>
    public enum ScoreVerdict
    {
        /// <summary>Consistent with the chart and the rules. Counted, and ranked if won.</summary>
        Accepted,

        /// <summary>
        /// Nothing about it is impossible, but something about it looks unlike a person
        /// playing. Counted as a clear; held off the leaderboards.
        /// </summary>
        Flagged,

        /// <summary>
        /// Impossible as submitted. Not ranked, and not a clear - missions included, since a
        /// forged clear is exactly what someone farming missions would send.
        /// </summary>
        Rejected
    }

    /// <summary>
    /// Why a result was rejected.
    ///
    /// 只解释不可能的 / These name arithmetic impossibilities in what the device itself sent, so
    /// an honest client with a bug can be told what it got wrong. A flagged run is told only
    /// <see cref="UnderReview"/>: which statistical signal fired stays on the server, because
    /// naming it is a recipe for avoiding it.
    /// </summary>
    public static class ScoreReviewCodes
    {
        /// <summary>The device played under different rules than the server holds.</summary>
        public const string RulesetMismatch = "RULESET_MISMATCH";

        /// <summary>A win arrived without its input trace.</summary>
        public const string TraceMissing = "TRACE_MISSING";

        /// <summary>The trace did not decode, or is not something the writer produces.</summary>
        public const string TraceInvalid = "TRACE_INVALID";

        /// <summary>A win whose trace does not cover the chart from first note to last.</summary>
        public const string TraceIncomplete = "TRACE_INCOMPLETE";

        /// <summary>The judgement counts do not add up to the chart.</summary>
        public const string CountsInconsistent = "COUNTS_INCONSISTENT";

        /// <summary>A max combo the counts could not have produced.</summary>
        public const string ComboInconsistent = "COMBO_INCONSISTENT";

        /// <summary>A Full Combo claim that disagrees with the misses.</summary>
        public const string FullComboInconsistent = "FULL_COMBO_INCONSISTENT";

        /// <summary>Above the most this chart and operator can score.</summary>
        public const string ScoreAboveCeiling = "SCORE_ABOVE_CEILING";

        /// <summary>Closed sooner after opening than the chart takes to play.</summary>
        public const string FinishedTooFast = "FINISHED_TOO_FAST";

        /// <summary>Fewer presses in the trace than the hits claimed.</summary>
        public const string NotEnoughInput = "NOT_ENOUGH_INPUT";

        /// <summary>The whole of what a flagged run is told.</summary>
        public const string UnderReview = "UNDER_REVIEW";
    }

    /// <param name="Reasons">
    /// Empty when accepted; the rejection codes when rejected;
    /// <see cref="ScoreReviewCodes.UnderReview"/> alone when flagged.
    /// </param>
    /// <param name="IsPersonalBest">True when this run is now the player's all-time entry for the stage.</param>
    /// <param name="AllTimeRank">The player's all-time position on this stage, null when not ranked.</param>
    /// <param name="WeeklyRank">The player's position this week, null when not ranked.</param>
    public sealed record ScoreReview(
        ScoreVerdict Verdict,
        IReadOnlyList<string> Reasons,
        int Score,
        bool IsPersonalBest,
        int? AllTimeRank,
        int? WeeklyRank);

    /// <summary>
    /// Closes a run: the mission counters, the review of the result, and the leaderboards, in
    /// one answer.
    ///
    /// 一个结束 / One endpoint for "the run ended" rather than a separate score submission, so a
    /// run cannot be counted as cleared by one call and scored differently by another.
    /// </summary>
    /// <param name="Won">
    /// Whether the server counted this run as a clear: what was claimed, unless the result
    /// was rejected.
    /// </param>
    /// <param name="Review">Null when no result was sent, which only a failed run may do.</param>
    public sealed record RunCompletion(
        Guid RunId,
        bool Won,
        ScoreReview? Review,
        MissionBoards Boards,
        PlayerState Player,
        DateTimeOffset ServerTime);
}

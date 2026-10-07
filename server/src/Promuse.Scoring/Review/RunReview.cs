#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Promuse.Contracts.Runs;
using Promuse.Scoring.Rules;

namespace Promuse.Scoring.Review;

/// <summary>
/// Everything a review looks at. Plain values, no database: the review is a pure function so
/// each rule can be tested on its own, and so the replay that follows can be added beside the
/// checks here without touching how a run is stored.
/// </summary>
public sealed record ReviewInput
{
    public required ScoringChart Chart { get; init; }

    public required Ruleset Rules { get; init; }

    /// <summary>Null when the run's operator is not in the ruleset - see ReviewFlags.Unverifiable.</summary>
    public required OperatorRules? Operator { get; init; }

    public required bool Won { get; init; }

    public required int Score { get; init; }
    public required int MaxCombo { get; init; }
    public required int Perfect { get; init; }
    public required int Great { get; init; }
    public required int Hit { get; init; }
    public required int Miss { get; init; }
    public required bool FullCombo { get; init; }

    public required string Fingerprint { get; init; }

    /// <summary>The raw trace, already decompressed. Null when none was sent.</summary>
    public byte[]? Trace { get; init; }

    /// <summary>Server time from opening the run to closing it.</summary>
    public required TimeSpan Elapsed { get; init; }
}

/// <param name="Reasons">Rejection codes from <see cref="ScoreReviewCodes"/>. Sent to the client.</param>
/// <param name="Flags">Statistical signals from <see cref="ReviewFlags"/>. Kept on the server.</param>
/// <param name="Ceiling">The bound the score was held to, kept so a rejection can be audited.</param>
public sealed record ReviewResult(
    ScoreVerdict Verdict,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Flags,
    int? Ceiling,
    TimingStats? Timing,
    int TraceFrames,
    int Presses);

/// <summary>
/// How the presses in a trace lined up with the notes they hit.
/// </summary>
/// <param name="Samples">Notes matched to a press.</param>
/// <param name="MeanMs">Positive is late.</param>
/// <param name="FrameIntervalMs">The median gap between recorded frames - the device's frame time.</param>
public sealed record TimingStats(
    int Samples,
    double MeanMs,
    double StdDevMs,
    double MaxAbsMs,
    double FrameIntervalMs);

/// <summary>
/// Signals that withhold ranking without voiding a clear. Never sent to the client.
/// </summary>
public static class ReviewFlags
{
    /// <summary>
    /// Every press landed within half a frame of its note. A device can only see a press on a
    /// frame, so an honest player's offsets carry their own timing spread plus up to half a
    /// frame of rounding; offsets that never leave the rounding are a script pressing on the
    /// frame nearest each note.
    /// </summary>
    public const string TimingFrameLocked = "TIMING_FRAME_LOCKED";

    /// <summary>
    /// Far more presses than notes. Not impossible - an empty press costs nothing in this game,
    /// which is a design gap of its own - but a run won by pressing every frame is not one the
    /// leaderboard should show.
    /// </summary>
    public const string InputSpam = "INPUT_SPAM";

    /// <summary>The run's operator is missing from the server's ruleset, so no ceiling exists.</summary>
    public const string Unverifiable = "UNVERIFIABLE";
}

/// <summary>
/// Decides what a submitted result is worth.
///
/// 两种问题, 两种处理 / Two kinds of finding, handled differently on purpose:
///
///   - Impossibilities: counts that do not add up to the chart, a combo the hits could not
///     make, a score above the ceiling, a win closed faster than the chart plays. Nothing
///     honest produces these, so they reject the result outright - not ranked, not a clear.
///   - Signals: timing that looks scripted, input that looks like mashing. Statistics can be
///     wrong about a person, so these only hold the run off the leaderboard; the clear stands.
///
/// 这不是重放 / This is not a replay. A cheat that forges a result consistent with itself and
/// under the ceiling passes every check here. What closes that gap is running the trace back
/// through the judging with the run's seed - which is why the trace is required and kept even
/// though nothing here reads it note by note.
/// </summary>
public static class RunReview
{
    /// <summary>Server time is the only clock involved, so this absorbs request latency and nothing else.</summary>
    public static readonly TimeSpan ElapsedSlack = TimeSpan.FromSeconds(2);

    /// <summary>Below this many matched notes the timing statistics say nothing.</summary>
    public const int TimingSampleFloor = 100;

    public static ReviewResult Evaluate(ReviewInput input)
    {
        var reasons = new List<string>();
        var flags = new List<string>();

        ScoringChart chart = input.Chart;
        int total = chart.JudgementCount;
        int hits = input.Perfect + input.Great + input.Hit;
        int counted = hits + input.Miss;
        float widest = Math.Max(input.Rules.judge.tapHit, input.Rules.judge.holdGreat);

        // ------------------------------------------------------------ the counts

        bool negative = input.Score < 0 || input.MaxCombo < 0 || input.Perfect < 0
                        || input.Great < 0 || input.Hit < 0 || input.Miss < 0;

        // A failed run stops part-way, so it may count fewer; a win resolves every judgement.
        if (negative || counted > total || (input.Won && counted != total))
        {
            reasons.Add(ScoreReviewCodes.CountsInconsistent);
        }

        // Combo counts hits only - a miss absorbed by the combo shield keeps the combo but does
        // not add to it - so it can never exceed them. With no misses at all on a win, nothing
        // ever broke it, so it must be every judgement.
        if (input.MaxCombo > hits || (input.Won && input.Miss == 0 && input.MaxCombo != total))
        {
            reasons.Add(ScoreReviewCodes.ComboInconsistent);
        }

        // RunState.IsFullCombo is exactly "won with nothing missed or dropped".
        if (input.FullCombo != (input.Won && input.Miss == 0))
        {
            reasons.Add(ScoreReviewCodes.FullComboInconsistent);
        }

        // ---------------------------------------------------- rules and ceiling

        int? ceiling = null;

        if (input.Operator is null)
        {
            flags.Add(ReviewFlags.Unverifiable);
        }
        else
        {
            if (!string.Equals(input.Fingerprint, RulesetFingerprint.Compute(input.Rules, input.Operator),
                               StringComparison.Ordinal))
            {
                reasons.Add(ScoreReviewCodes.RulesetMismatch);
            }

            ceiling = ScoreCeiling.Compute(chart, input.Rules, input.Operator);

            if (input.Score > ceiling) reasons.Add(ScoreReviewCodes.ScoreAboveCeiling);
        }

        // -------------------------------------------------------------- the clock

        if (input.Won && input.Elapsed < TimeSpan.FromSeconds(chart.PlayableSpan) - ElapsedSlack)
        {
            reasons.Add(ScoreReviewCodes.FinishedTooFast);
        }

        // -------------------------------------------------------------- the trace

        TimingStats? timing = null;
        int frames = 0;
        int presses = 0;

        if (input.Trace is null)
        {
            if (input.Won) reasons.Add(ScoreReviewCodes.TraceMissing);
        }
        else if (!InputTrace.TryRead(input.Trace, InputTraceWriter.MaxFrames, out InputTrace? trace, out _))
        {
            reasons.Add(ScoreReviewCodes.TraceInvalid);
        }
        else
        {
            frames = trace!.FrameCount;
            presses = CountPresses(trace);

            if (input.Won)
            {
                // A win's frames have to span the chart: recording starts before the first note
                // can be judged and runs until the song is over.
                bool covers = frames > 0
                              && trace.Times[0] <= chart.FirstNoteTime + widest
                              && trace.Times[frames - 1] >= chart.LastJudgementTime - widest;

                if (!covers) reasons.Add(ScoreReviewCodes.TraceIncomplete);

                // Song time runs no faster than the wall clock, so a trace cannot hold more of
                // it than the run had. Catches a trace made up after the fact.
                else if (input.Elapsed < TimeSpan.FromSeconds(trace.Times[frames - 1] - trace.Times[0]) - ElapsedSlack
                         && !reasons.Contains(ScoreReviewCodes.FinishedTooFast))
                {
                    reasons.Add(ScoreReviewCodes.FinishedTooFast);
                }
            }

            // Every hit except a hold's tail is earned by a press in its frame. Fewer presses
            // than that is a count the trace cannot back.
            if (presses < hits - chart.HoldCount) reasons.Add(ScoreReviewCodes.NotEnoughInput);

            timing = MeasureTiming(chart, input.Rules, trace);

            if (timing.Samples >= TimingSampleFloor && timing.FrameIntervalMs > 0
                && timing.MaxAbsMs <= timing.FrameIntervalMs / 2 + 1.0)
            {
                flags.Add(ReviewFlags.TimingFrameLocked);
            }

            if (presses > 40 && presses > 4 * chart.PressedJudgementCount)
            {
                flags.Add(ReviewFlags.InputSpam);
            }
        }

        ScoreVerdict verdict = reasons.Count > 0 ? ScoreVerdict.Rejected
                             : flags.Count > 0 ? ScoreVerdict.Flagged
                             : ScoreVerdict.Accepted;

        return new ReviewResult(verdict, reasons, flags, ceiling, timing, frames, presses);
    }

    private static int CountPresses(InputTrace trace)
    {
        int count = 0;
        foreach (byte mask in trace.Pressed)
        {
            for (int bits = mask; bits != 0; bits &= bits - 1) count++;
        }
        return count;
    }

    /// <summary>
    /// Pairs each note with the press that would have taken it, lane by lane, the way
    /// NoteSpawner.PeekJudgeable does: the earliest press still inside the note's window.
    ///
    /// 只为统计 / For statistics only. It reads offsets, not judgements, and never decides a
    /// score - the replay does that, with the real judging.
    /// </summary>
    public static TimingStats MeasureTiming(ScoringChart chart, Ruleset rules, InputTrace trace)
    {
        int lanes = trace.LaneCount;

        var notesByLane = new List<(float Time, float Window)>[lanes];
        var pressesByLane = new List<float>[lanes];

        for (int lane = 0; lane < lanes; lane++)
        {
            notesByLane[lane] = [];
            pressesByLane[lane] = [];
        }

        foreach (NoteData note in chart.Notes)
        {
            float window = rules.judge.MaxWindow(note.type);

            // A Twin is two presses, one in each of the first two lanes, as the spawner splits it.
            if (note.type == NoteType.Twin)
            {
                for (int lane = 0; lane < Math.Min(2, lanes); lane++) notesByLane[lane].Add((note.hitTime, window));
            }
            else if (note.lane >= 0 && note.lane < lanes)
            {
                notesByLane[note.lane].Add((note.hitTime, window));
            }
        }

        for (int i = 0; i < trace.FrameCount; i++)
        {
            for (int lane = 0; lane < lanes; lane++)
            {
                if ((trace.Pressed[i] & (1 << lane)) != 0) pressesByLane[lane].Add(trace.Times[i]);
            }
        }

        var offsets = new List<double>();

        for (int lane = 0; lane < lanes; lane++)
        {
            List<float> presses = pressesByLane[lane];
            int next = 0;

            foreach ((float time, float window) in notesByLane[lane])
            {
                while (next < presses.Count && presses[next] < time - window) next++;

                if (next < presses.Count && presses[next] <= time + window)
                {
                    offsets.Add((presses[next] - time) * 1000.0);
                    next++;
                }
            }
        }

        return new TimingStats(
            offsets.Count,
            offsets.Count > 0 ? offsets.Average() : 0,
            StdDev(offsets),
            offsets.Count > 0 ? offsets.Max(o => Math.Abs(o)) : 0,
            MedianFrameIntervalMs(trace));
    }

    private static double StdDev(List<double> values)
    {
        if (values.Count < 2) return 0;

        double mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }

    private static double MedianFrameIntervalMs(InputTrace trace)
    {
        var gaps = new List<double>(trace.FrameCount);

        for (int i = 1; i < trace.FrameCount; i++)
        {
            double gap = trace.Times[i] - trace.Times[i - 1];
            if (gap > 0) gaps.Add(gap * 1000.0);
        }

        if (gaps.Count == 0) return 0;

        gaps.Sort();
        return gaps[gaps.Count / 2];
    }
}

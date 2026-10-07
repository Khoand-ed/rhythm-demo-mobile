#nullable enable

using System;
using System.Collections.Generic;
using Promuse.Scoring.Rules;

namespace Promuse.Scoring.Review;

/// <summary>
/// The most a run on this chart, with this operator, could possibly score. A submitted score
/// above it did not come from playing.
///
/// 为什么不是"全 Perfect" / Not simply the score of an all-Perfect run, because that is not the
/// maximum. Fever doubles everything inside an eight-second window, and where that window falls
/// is partly the player's choice: missing a few notes on purpose keeps the gauge from filling,
/// which can push the window off a sparse stretch and onto a dense one. On the right chart that
/// trade wins - so a ceiling built from the all-Perfect run would reject a player who simply
/// played cleverly.
///
/// 怎么算 / Instead this takes every judgement at its best possible award - Perfect, at the
/// highest rung the multiplier ladder could have reached by then - and then asks where the fever
/// windows could have gone, choosing their placement to add the most. Two facts constrain the
/// placement and nothing else does:
///
///   - a window can only open once the gauge is full, which takes at least K judgements from
///     empty (K is counted with the game's own AddFever, so the operator's fever modifier is in
///     it), and
///   - the gauge is empty again when a window closes, and fills only from judgements after it.
///
/// The choice is a small dynamic program over the judgements in time order. Everything the
/// player would have paid to steer the windows - the missed notes, the broken ladder, the lost
/// gauge - is left unpaid, so this is an upper bound, not an estimate.
///
/// 时间上的余量 / The windows are widened at both edges. A press may land up to the widest
/// timing window early or late, so notes can swap order around an activation; and fever ends on
/// the first frame past its deadline, so a press inside a long frame is still paid as fever.
/// Both widenings only ever add to the ceiling.
///
/// 被动 / Passives enter through the game's own code. A window's length comes from
/// PassiveSO.ExtraFeverSeconds, asked with the largest combo possible at that judgement, which is
/// the most FeverExtendPassive can grant. JudgeUpgradePassive only promotes a Great to a Perfect
/// and every judgement here is already a Perfect; the combo shield and regen change no score.
/// </summary>
public static class ScoreCeiling
{
    /// <summary>
    /// How long a single frame may run past the fever deadline. A device that hitches for a
    /// quarter of a second still pays that whole frame's presses as fever.
    /// </summary>
    public const float FrameAllowance = 0.25f;

    public static int Compute(ScoringChart chart, Ruleset rules, OperatorRules? op)
    {
        ScoreRules score = rules.score;

        // ------------------------------------------------------------ the events

        var judgements = new List<float>(chart.JudgementCount);
        var ticks = new List<float>();

        foreach (NoteData note in chart.Notes)
        {
            if (note.type == NoteType.Twin)
            {
                judgements.Add(note.hitTime);
                judgements.Add(note.hitTime);
            }
            else if (note.type == NoteType.Hold && note.duration > 0f)
            {
                float end = note.hitTime + note.duration;
                judgements.Add(note.hitTime);
                judgements.Add(end);

                // One more than the body holds, because NoteView counts ticks by accumulating
                // float intervals and can land one past an exact division.
                int count = (int)Math.Floor(note.duration / score.holdTickInterval) + 1;
                for (int k = 1; k <= count; k++)
                {
                    ticks.Add(Math.Min(note.hitTime + k * score.holdTickInterval, end));
                }
            }
            else
            {
                judgements.Add(note.hitTime);
            }
        }

        judgements.Sort();
        ticks.Sort();

        int n = judgements.Count;
        if (n == 0) return 0;

        // ------------------------------------------------- awards, the game's way

        // Two RunStates doing what the game does: one climbs the ladder hit by hit, the other
        // prices a Perfect at that rung with and without fever - through ScoreFor, so the
        // operator's modifier is applied and rounded exactly as on the device.
        RunState ladder = rules.CreateRun(op);
        RunState price = rules.CreateRun(op);

        var plain = new long[n];
        var feverExtra = new long[n];
        int topRung = 1;

        for (int j = 0; j < n; j++)
        {
            // NoteHit climbs before it pays, so the rung after the call is the one hit j is
            // paid at.
            ladder.NoteHit(0);
            topRung = Math.Max(topRung, ladder.multiplier);

            price.multiplier = ladder.multiplier;
            price.feverActive = false;
            plain[j] = price.ScoreFor(score.perfect);

            price.feverActive = true;
            feverExtra[j] = price.ScoreFor(score.perfect) - plain[j];
        }

        // Ticks do not climb the ladder; pricing them at the top rung is the simple bound.
        price.multiplier = topRung;
        price.feverActive = false;
        long tickPlain = price.ScoreFor(score.holdTick);
        price.feverActive = true;
        long tickFeverExtra = price.ScoreFor(score.holdTick) - tickPlain;

        long ceiling = ticks.Count * tickPlain;
        for (int j = 0; j < n; j++) ceiling += plain[j];

        // ------------------------------------------------------- fever windows

        int fill = GaugeFillCount(rules, op);
        if (fill > n) return Clamp(ceiling);

        float widest = Math.Max(rules.judge.tapHit, rules.judge.holdGreat);
        float before = 2f * widest;
        float after = 2f * widest + FrameAllowance;

        var feverPrefix = new long[n + 1];
        for (int j = 0; j < n; j++) feverPrefix[j + 1] = feverPrefix[j] + feverExtra[j];

        RunState comboProbe = rules.CreateRun(op);
        var gain = new long[n];
        var next = new int[n];

        for (int s = 0; s < n; s++)
        {
            float opens = judgements[s];

            // The longest window this activation could get: the passive is asked with the
            // combo of an unbroken run up to and including the activating hit.
            comboProbe.combo = s + 1;
            float extra = comboProbe.passive != null ? comboProbe.passive.ExtraFeverSeconds(comboProbe) : 0f;
            float closes = opens + rules.fever.feverDuration + extra;

            int lo = LowerBound(judgements, opens - before);
            int hi = UpperBound(judgements, closes + after);
            int ticksIn = UpperBound(ticks, closes + after) - LowerBound(ticks, opens - before);

            gain[s] = feverPrefix[hi] - feverPrefix[lo] + ticksIn * tickFeverExtra;

            // The gauge refills only from judgements after the window - counted from slightly
            // before its nominal end, since an early activation ends it early too.
            next[s] = UpperBound(judgements, closes - before) + fill - 1;

            // Never at or before this activation, however short a window the rules allow.
            next[s] = Math.Max(next[s], s + 1);
        }

        // best[i]: the most fever can add using windows that open at judgement i or later.
        var best = new long[n + 1];
        for (int s = n - 1; s >= 0; s--)
        {
            long take = gain[s] + (next[s] < n ? best[next[s]] : 0);
            best[s] = Math.Max(best[s + 1], take);
        }

        // The first window needs a full gauge from empty: K judgements, the K-th opens it.
        ceiling += best[fill - 1];

        return Clamp(ceiling);
    }

    /// <summary>
    /// The fewest judgements that fill the gauge from empty: all Perfects, through the game's
    /// own AddFever so the operator's modifier and the float accumulation are exactly the
    /// device's. <see cref="int.MaxValue"/> when the gauge can never fill.
    /// </summary>
    public static int GaugeFillCount(Ruleset rules, OperatorRules? op)
    {
        if (rules.fever.perfectGain <= 0f || rules.fever.maxFever <= 0f) return int.MaxValue;

        RunState run = rules.CreateRun(op);

        for (int i = 1; i <= 1_000_000; i++)
        {
            // With autoActivate off the player opens the window by hand, any time the gauge is
            // full - so full is the condition either way.
            if (run.AddFever(rules.fever.perfectGain) || run.feverGauge >= rules.fever.maxFever) return i;
        }

        return int.MaxValue;
    }

    private static int Clamp(long value) => value > int.MaxValue ? int.MaxValue : (int)value;

    /// <summary>First index whose value is at least <paramref name="value"/>.</summary>
    private static int LowerBound(List<float> sorted, float value)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (sorted[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>First index whose value is greater than <paramref name="value"/>.</summary>
    private static int UpperBound(List<float> sorted, float value)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (sorted[mid] <= value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}

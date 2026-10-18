#nullable enable

using Promuse.Scoring.Rules;

namespace Promuse.Scoring.Review;

/// <summary>What the server's own judging of a trace came to - the numbers the device should have sent.</summary>
/// <param name="Failed">HP ran out somewhere in the trace.</param>
/// <param name="Frames">How many frames were judged: all of them, or up to the one HP ran out on.</param>
public sealed record ReplayResult(
    int Score,
    int MaxCombo,
    int Perfect,
    int Great,
    int Hit,
    int Miss,
    bool FullCombo,
    bool Failed,
    int Frames);

/// <summary>
/// 重放 / Plays a run's input trace back through RunJudge - the device's own judging, linked from
/// Rhythm.Core - with the rules and operator the run was opened with and the seed the server issued,
/// and reports what that judging scores.
///
/// 同一份代码 / Not a model of the game: the same source the device judged with. Given the same
/// frames, an honest device and this reach the same numbers to the point, so any difference is a
/// result that did not come from the input it was sent with.
/// </summary>
public static class RunReplay
{
    public static ReplayResult Play(ScoringChart chart, Ruleset rules, OperatorRules? op, long seed, InputTrace trace)
    {
        RunState state = rules.CreateRun(op, seed);
        var judge = new RunJudge(chart.Notes, trace.LaneCount, rules.CreateJudge(), rules.score, state);
        judge.Reset();

        int frames = 0;
        for (int i = 0; i < trace.FrameCount && !judge.Failed; i++)
        {
            judge.Step(trace.Times[i], trace.Pressed[i], trace.Held[i], null);
            frames++;
        }

        // The counters are floats on RunState for the accuracy maths; they only ever hold whole numbers.
        return new ReplayResult(
            state.score,
            state.maxCombo,
            (int)state.perfectHits,
            (int)state.goodHits,
            (int)state.normalHits,
            (int)state.missedHits,
            state.IsFullCombo,
            state.failed,
            frames);
    }
}

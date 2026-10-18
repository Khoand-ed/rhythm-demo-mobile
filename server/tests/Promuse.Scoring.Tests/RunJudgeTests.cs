using Promuse.Scoring.Review;
using Promuse.Scoring.Rules;

namespace Promuse.Scoring.Tests;

/// <summary>
/// RunJudge - the judging the device plays with and the server replays with - one rule per test,
/// on charts of a few notes with no operator, so the numbers are the ruleset's own: Perfect 150,
/// Great 125, Hit 100, a hold tick 10 every 0.1 s, the ladder at 4 / 8 / 16 hits, 100 HP, a tap miss
/// 10, fever 3 a Perfect to 100 and doubling for 8 s.
/// </summary>
public class RunJudgeTests
{
    private const int Left = 1, Right = 2, Both = 3;

    private static readonly Ruleset Rules = Fixtures.Ruleset();

    private static (RunJudge Judge, RunState State, Heard Heard) Judge(string notes, OperatorRules? op = null, long seed = 0)
    {
        ScoringChart chart = ScoringChart.Parse("{ \"stageId\": \"t\", \"notes\": [" + notes + "] }");
        RunState state = Rules.CreateRun(op, seed);
        var judge = new RunJudge(chart.Notes, 2, Rules.CreateJudge(), Rules.score, state);
        judge.Reset();
        return (judge, state, new Heard());
    }

    private static string Tap(float time, string lane = "Left") =>
        $"{{ \"type\": \"Tap\", \"lane\": \"{lane}\", \"time\": {time.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}";

    private static string Hold(float start, float end, string lane = "Left") =>
        $"{{ \"type\": \"Hold\", \"lane\": \"{lane}\", \"startTime\": {start.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"endTime\": {end.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}";

    // ------------------------------------------------------------------ presses

    [Fact]
    public void A_press_is_graded_by_how_far_it_lands_from_its_note()
    {
        var (judge, state, heard) = Judge(string.Join(",", Tap(1f), Tap(2f), Tap(3f)));

        judge.Step(1.03f, Left, Left, heard);   // 30 ms: Perfect
        judge.Step(2.08f, Left, Left, heard);   // 80 ms: Great
        judge.Step(2.85f, Left, Left, heard);   // 150 ms early: Hit

        Assert.Equal((1f, 1f, 1f), (state.perfectHits, state.goodHits, state.normalHits));
        Assert.Equal(150 + 125 + 100, state.score);
        Assert.Equal(3, state.combo);
        Assert.Equal(["hit 0:0 Perfect", "hit 0:1 Great", "hit 0:2 Hit"], heard.Events);
    }

    [Fact]
    public void One_press_takes_one_note_the_earliest_still_in_its_window()
    {
        var (judge, state, heard) = Judge(string.Join(",", Tap(1f), Tap(1.1f)));

        judge.Step(1.05f, Left, Left, heard);
        Assert.Equal(["hit 0:0 Perfect"], heard.Events);

        judge.Step(1.12f, Left, Left, heard);
        Assert.Equal(2, state.combo);

        // Nothing in range: a press does nothing at all.
        judge.Step(3f, Left, Left, heard);
        Assert.Equal(2, heard.Events.Count);
    }

    [Fact]
    public void A_twin_is_a_press_in_each_lane()
    {
        var (judge, state, _) = Judge("{ \"type\": \"Twin\", \"time\": 1.0 }");

        judge.Step(1f, Left, Left, null);
        judge.Step(1.02f, Right, Right, null);

        Assert.Equal(2f, state.perfectHits);
        Assert.Equal(2, judge.JudgementCount);
    }

    // ------------------------------------------------------------------ misses

    [Fact]
    public void A_note_left_alone_is_missed_the_frame_after_its_window_closes()
    {
        var (judge, state, heard) = Judge(Tap(1f));

        judge.Step(1.2f, 0, 0, heard);
        Assert.Empty(heard.Events);

        judge.Step(1.21f, 0, 0, heard);
        Assert.Equal(["miss 0:0"], heard.Events);
        Assert.Equal((90, 1f, false), (state.hp, state.missedHits, state.IsFullCombo));
    }

    [Fact]
    public void Running_out_of_HP_ends_the_judging()
    {
        var (judge, state, heard) = Judge(string.Join(",", Enumerable.Range(1, 12).Select(i => Tap(i * 0.5f))));

        judge.Step(10f, 0, 0, heard);

        Assert.True(judge.Failed);
        Assert.Equal(10f, state.missedHits);
        Assert.Equal("fail", heard.Events[^1]);

        // Nothing after the run ended counts.
        judge.Step(10.5f, Left, Left, heard);
        Assert.Equal(10f, state.missedHits);
    }

    // ------------------------------------------------------------------ holds

    [Fact]
    public void A_hold_held_to_the_end_pays_its_body_and_a_perfect_tail()
    {
        var (judge, state, heard) = Judge(Hold(1f, 1.95f));

        judge.Step(1f, Left, Left, heard);
        judge.Step(1.5f, 0, Left, heard);
        judge.Step(2f, 0, Left, heard);

        // Head 150, nine ticks of 10, tail 150 on the second hit.
        Assert.Equal(150 + 9 * 10 + 150, state.score);
        Assert.Equal((2f, 2, true), (state.perfectHits, state.combo, state.IsFullCombo));
        Assert.Equal("hold-end 0:0 Perfect", heard.Events[^1]);
        Assert.Equal(9, heard.Events.Count(e => e == "tick 0"));
    }

    [Fact]
    public void Letting_go_early_drops_the_rest_and_letting_go_just_before_the_end_is_a_great()
    {
        var (judge, state, heard) = Judge(string.Join(",", Hold(1f, 2f), Hold(3f, 4f)));

        judge.Step(1f, Left, Left, heard);
        judge.Step(1.3f, 0, 0, heard);

        Assert.Contains("dropped 0:0", heard.Events);
        Assert.Equal((0, 1f, 100), (state.combo, state.missedHits, state.hp));

        judge.Step(3f, Left, Left, heard);
        judge.Step(3.9f, 0, 0, heard);
        Assert.Equal("hold-end 0:1 Great", heard.Events[^1]);
    }

    // ------------------------------------------------------------------ fever and the passive

    [Fact]
    public void Fever_opens_when_the_gauge_fills_doubles_the_pay_and_ends_on_its_deadline()
    {
        // Thirty-four Perfects fill 100 at 3 each; the thirty-fourth opens fever.
        string notes = string.Join(",", Enumerable.Range(0, 40).Select(i => Tap(1f + i * 0.25f, i % 2 == 0 ? "Left" : "Right")));
        var (judge, state, heard) = Judge(notes);

        for (int i = 0; i < 40; i++)
        {
            float at = 1f + i * 0.25f;
            int lane = i % 2 == 0 ? Left : Right;
            int before = state.score;

            judge.Step(at, lane, lane, heard);

            if (i == 33) Assert.True(state.feverActive);
            if (i == 34) Assert.Equal(150 * 4 * 2, state.score - before);   // top rung, doubled
        }

        Assert.Equal(1, heard.Events.Count(e => e == "fever-start"));

        float opened = 1f + 33 * 0.25f;
        judge.Step(opened + 7.9f, 0, 0, heard);
        Assert.True(state.feverActive);
        judge.Step(opened + 8.01f, 0, 0, heard);
        Assert.False(state.feverActive);
        Assert.Equal("fever-end", heard.Events[^1]);
    }

    [Fact]
    public void A_regen_passive_ticks_on_song_time_between_judged_frames()
    {
        OperatorRules pulse = Fixtures.Operator(Rules, "PULSE");
        var (judge, state, _) = Judge(string.Join(",", Enumerable.Range(1, 5).Select(i => Tap(i * 0.5f))), pulse);

        judge.Step(5f, 0, 0, null);        // five misses
        int afterMisses = state.hp;

        // One frame twenty seconds on: two whole regen intervals pass in one delta.
        judge.Step(25f, 0, 0, null);
        Assert.True(state.hp > afterMisses);
    }

    // ------------------------------------------------------------------ golden

    /// <summary>
    /// 两边同一个文件 / The golden trace in server/tests/golden, replayed here on .NET and in the
    /// Unity EditMode test ReplayGoldenTests on Unity's runtime, each pinned to these numbers. If
    /// they ever disagree, the two runtimes judge differently and honest devices would be flagged.
    /// </summary>
    [Fact]
    public void The_golden_trace_replays_to_its_pinned_result()
    {
        byte[] bytes = File.ReadAllBytes(Fixtures.RepoPath(Path.Combine("server", "tests", "golden", "replay_stage_AIW_hard.trace")));
        Assert.True(InputTrace.TryRead(bytes, InputTraceWriter.MaxFrames, out InputTrace? trace, out string? error), error);

        ReplayResult result = RunReplay.Play(Fixtures.Chart("stage_AIW_hard"), Rules,
            Fixtures.Operator(Rules, "NOVA").AtLevel(10), 12345, trace!);

        Assert.Equal(new ReplayResult(370949, 479, 212, 109, 158, 0, true, false, 10370), result);
    }

    /// <summary>What a listener was told, as short strings in order.</summary>
    private sealed class Heard : IRunJudgeListener
    {
        public List<string> Events { get; } = [];

        public void OnHit(int lane, int index, NoteType type, Judgement judgement, float signedDelta, bool holdBegins) =>
            Events.Add($"hit {lane}:{index} {judgement}");

        public void OnHoldTick(int lane) => Events.Add($"tick {lane}");

        public void OnHoldEnd(int lane, int index, Judgement judgement) => Events.Add($"hold-end {lane}:{index} {judgement}");

        public void OnHoldDropped(int lane, int index) => Events.Add($"dropped {lane}:{index}");

        public void OnMiss(int lane, int index, NoteType type) => Events.Add($"miss {lane}:{index}");

        public void OnFeverStart() => Events.Add("fever-start");

        public void OnFeverEnd() => Events.Add("fever-end");

        public void OnFail() => Events.Add("fail");
    }
}

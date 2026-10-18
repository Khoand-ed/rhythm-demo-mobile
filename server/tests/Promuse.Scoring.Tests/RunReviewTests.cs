using Promuse.Contracts.Runs;
using Promuse.Scoring.Review;
using Promuse.Scoring.Rules;

namespace Promuse.Scoring.Tests;

/// <summary>
/// One honest run, then one way of lying about it per test. Every rejection must come from the
/// lie and never from the honest parts, so each test starts from an input that is accepted and
/// changes exactly one thing.
///
/// 诚实就是重放的结果 / Honest means: the numbers the server's replay of the trace comes to, which
/// is what a device sends. A test that swaps the trace claims what the new trace scores (Claim),
/// so the only thing it changes is the input.
/// </summary>
public class RunReviewTests
{
    private const string Stage = "stage_001";
    private const string OperatorId = "PULSE";

    private static ReviewInput Honest(Func<ReviewInput, ReviewInput>? change = null)
    {
        Ruleset rules = Fixtures.Ruleset();
        ScoringChart chart = Fixtures.Chart(Stage);
        OperatorRules op = Fixtures.Operator(rules, OperatorId);

        var input = Claim(new ReviewInput
        {
            Chart = chart,
            Rules = rules,
            Operator = op,
            Won = true,
            Score = 0,
            MaxCombo = 0,
            Perfect = 0,
            Great = 0,
            Hit = 0,
            Miss = 0,
            FullCombo = false,
            Fingerprint = RulesetFingerprint.Compute(rules, op),
            Trace = Fixtures.Trace(chart),
            Seed = Fixtures.Seed,
            Elapsed = TimeSpan.FromSeconds(chart.PlayableSpan + 10),
        });

        return change is null ? input : change(input);
    }

    /// <summary>The input with <paramref name="trace"/> (its own when null), claiming what that trace scores.</summary>
    private static ReviewInput Claim(ReviewInput input, byte[]? trace = null)
    {
        trace ??= input.Trace!;
        ReplayResult replay = Fixtures.Replay(input.Chart, input.Rules, input.Operator!, trace, input.Seed);

        return input with
        {
            Trace = trace,
            Won = !replay.Failed,
            Score = replay.Score,
            MaxCombo = replay.MaxCombo,
            Perfect = replay.Perfect,
            Great = replay.Great,
            Hit = replay.Hit,
            Miss = replay.Miss,
            FullCombo = replay.FullCombo,
        };
    }

    private static ReviewResult Review(Func<ReviewInput, ReviewInput>? change = null) =>
        RunReview.Evaluate(Honest(change));

    [Fact]
    public void An_honest_run_is_accepted()
    {
        ReviewResult result = Review();

        Assert.Equal(ScoreVerdict.Accepted, result.Verdict);
        Assert.Empty(result.Reasons);
        Assert.Empty(result.Flags);
        Assert.NotNull(result.Ceiling);

        // Every note found its press, with the spread a person has.
        Assert.Equal(179, result.Timing!.Samples);
        Assert.InRange(result.Timing.StdDevMs, 15, 35);
        Assert.InRange(result.Timing.FrameIntervalMs, 16, 17.5);

        // The replay ran and agrees; a person's spread is not all Perfects.
        Assert.NotNull(result.Replay);
        Assert.True(result.Replay!.Great + result.Replay.Hit > 0);
        Assert.Equal(0, result.Replay.Miss);
    }

    [Fact]
    public void A_score_the_input_did_not_make_is_flagged_by_the_replay_not_rejected()
    {
        // Self-consistent and under the ceiling - nothing above can see it.
        ReviewResult result = Review(i => i with { Score = i.Score + 500 });

        Assert.Equal(ScoreVerdict.Flagged, result.Verdict);
        Assert.Empty(result.Reasons);
        Assert.Equal(new[] { ReviewFlags.ReplayMismatch }, result.Flags);
        Assert.NotEqual(result.Replay!.Score, Honest().Score + 500);
    }

    [Fact]
    public void Greats_passed_off_as_perfects_are_flagged_by_the_replay()
    {
        ReviewResult result = Review(i => i with { Perfect = i.Perfect + i.Great, Great = 0 });

        Assert.Empty(result.Reasons);
        Assert.Contains(ReviewFlags.ReplayMismatch, result.Flags);
    }

    [Fact]
    public void A_win_the_input_lost_is_flagged_by_the_replay()
    {
        // The second half never pressed: HP runs out part-way, though the result says it was won.
        ScoringChart chart = Fixtures.Chart(Stage);
        byte[] gaveUp = Silence(Fixtures.Trace(chart), chart.FirstNoteTime + chart.PlayableSpan / 2);

        ReplayResult lost = Fixtures.Replay(chart, Fixtures.Ruleset(), Fixtures.Operator(Fixtures.Ruleset(), OperatorId), gaveUp);
        Assert.True(lost.Failed);

        ReviewResult result = Review(i => i with { Trace = gaveUp });
        Assert.Contains(ReviewFlags.ReplayMismatch, result.Flags);
    }

    [Fact]
    public void The_replay_rolls_the_passive_from_the_seed_the_server_issued()
    {
        // A different seed is a different sequence of rolls: the same input, other promotions.
        Ruleset rules = Fixtures.Ruleset();
        OperatorRules? roller = rules.operators.FirstOrDefault(o => o.passive.type == nameof(JudgeUpgradePassive));
        Assert.NotNull(roller);

        ScoringChart chart = Fixtures.Chart(Stage);
        byte[] trace = Fixtures.Trace(chart, jitterMs: 45);

        ReplayResult one = Fixtures.Replay(chart, rules, roller!, trace, seed: 1);
        ReplayResult again = Fixtures.Replay(chart, rules, roller!, trace, seed: 1);
        Assert.Equal(one, again);

        // Two seeds can promote as many Greats by chance; across eight, some must differ.
        Assert.Contains(Enumerable.Range(2, 8), seed => Fixtures.Replay(chart, rules, roller!, trace, seed) != one);
    }

    [Fact]
    public void A_score_above_the_ceiling_is_rejected()
    {
        ReviewResult result = Review(i => i with { Score = RunReview.Evaluate(i).Ceiling!.Value + 1 });

        Assert.Equal(ScoreVerdict.Rejected, result.Verdict);
        Assert.Equal(new[] { ScoreReviewCodes.ScoreAboveCeiling }, result.Reasons);
    }

    [Fact]
    public void Counts_that_do_not_add_up_to_the_chart_are_rejected()
    {
        ReviewResult result = Review(i => i with { Perfect = i.Perfect - 1, MaxCombo = i.MaxCombo - 1 });

        Assert.Contains(ScoreReviewCodes.CountsInconsistent, result.Reasons);
    }

    [Fact]
    public void A_failed_run_may_count_fewer_judgements_than_the_chart()
    {
        ReviewResult result = Review(i => i with
        {
            Won = false, Perfect = 50, Miss = 10, MaxCombo = 50, FullCombo = false, Score = 1000,
        });

        Assert.DoesNotContain(ScoreReviewCodes.CountsInconsistent, result.Reasons);
        Assert.DoesNotContain(ScoreReviewCodes.ComboInconsistent, result.Reasons);
    }

    [Fact]
    public void A_combo_longer_than_the_hits_is_rejected()
    {
        // Five hits become misses, but the combo still claims every hit there was.
        ReviewResult result = Review(i => i with
        {
            Perfect = i.Perfect - 5, Miss = 5, FullCombo = false, MaxCombo = i.Perfect + i.Great + i.Hit,
        });

        Assert.Equal(new[] { ScoreReviewCodes.ComboInconsistent }, result.Reasons);
    }

    [Fact]
    public void A_won_run_with_no_misses_must_have_combed_the_whole_chart()
    {
        ReviewResult result = Review(i => i with { MaxCombo = i.MaxCombo - 1 });

        Assert.Equal(new[] { ScoreReviewCodes.ComboInconsistent }, result.Reasons);
    }

    [Theory]
    [InlineData(true, 1)]    // full combo claimed despite a miss
    [InlineData(false, 0)]   // full combo denied on a clean win
    public void A_full_combo_claim_must_match_the_misses(bool claimed, int misses)
    {
        ReviewResult result = Review(i => i with
        {
            FullCombo = claimed,
            Perfect = i.Perfect - misses,
            Miss = misses,
            MaxCombo = i.MaxCombo - misses,
        });

        Assert.Contains(ScoreReviewCodes.FullComboInconsistent, result.Reasons);
    }

    [Fact]
    public void A_win_closed_faster_than_the_chart_plays_is_rejected()
    {
        ReviewResult result = Review(i => i with { Elapsed = TimeSpan.FromSeconds(5) });

        Assert.Contains(ScoreReviewCodes.FinishedTooFast, result.Reasons);
    }

    [Fact]
    public void Different_rules_on_the_device_are_named_as_such()
    {
        ReviewResult result = Review(i => i with { Fingerprint = "0000000000000000" });

        Assert.Equal(new[] { ScoreReviewCodes.RulesetMismatch }, result.Reasons);
    }

    [Fact]
    public void A_win_without_its_trace_is_rejected_and_a_garbled_one_too()
    {
        Assert.Equal(new[] { ScoreReviewCodes.TraceMissing }, Review(i => i with { Trace = null }).Reasons);
        Assert.Equal(new[] { ScoreReviewCodes.TraceInvalid }, Review(i => i with { Trace = [1, 2, 3] }).Reasons);
    }

    [Fact]
    public void A_failed_run_needs_no_trace()
    {
        ReviewResult result = Review(i => i with
        {
            Won = false, Trace = null, Perfect = 10, MaxCombo = 10, FullCombo = false, Score = 100,
        });

        Assert.Equal(ScoreVerdict.Accepted, result.Verdict);
    }

    [Fact]
    public void A_trace_that_stops_before_the_chart_ends_is_rejected()
    {
        ReviewResult result = Review(i => i with { Trace = Fixtures.Trace(i.Chart, end: 20f) });

        Assert.Contains(ScoreReviewCodes.TraceIncomplete, result.Reasons);
    }

    [Fact]
    public void Hits_without_the_presses_to_make_them_are_rejected()
    {
        // The frames are all there; nobody pressed anything.
        ReviewResult result = Review(i => i with { Trace = Fixtures.Trace(Chart(empty: true), start: 0f, end: 37f) });

        Assert.Contains(ScoreReviewCodes.NotEnoughInput, result.Reasons);
    }

    [Fact]
    public void Presses_that_never_leave_their_frame_are_flagged_not_rejected()
    {
        // jitter 0: every press on the frame nearest its note, which is what a script does.
        ReviewResult result = Review(i => Claim(i, Fixtures.Trace(i.Chart, jitterMs: 0)));

        Assert.Equal(ScoreVerdict.Flagged, result.Verdict);
        Assert.Equal(new[] { ReviewFlags.TimingFrameLocked }, result.Flags);
        Assert.Empty(result.Reasons);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(120f)]
    public void A_person_is_not_mistaken_for_a_script_at_any_frame_rate(float fps)
    {
        // A sharp player: 12 ms of spread, tighter than most people manage on glass.
        ReviewResult result = Review(i => Claim(i, Fixtures.Trace(i.Chart, fps: fps, jitterMs: 12)));

        Assert.DoesNotContain(ReviewFlags.TimingFrameLocked, result.Flags);
    }

    [Fact]
    public void Mashing_is_flagged()
    {
        ReviewResult result = Review(i => Claim(i, Fixtures.Trace(i.Chart, extraPressesPerNote: 4)));

        Assert.Equal(new[] { ReviewFlags.InputSpam }, result.Flags);
        Assert.Equal(ScoreVerdict.Flagged, result.Verdict);
    }

    [Fact]
    public void An_operator_missing_from_the_ruleset_is_flagged_as_unverifiable()
    {
        ReviewResult result = Review(i => i with { Operator = null });

        Assert.Equal(ScoreVerdict.Flagged, result.Verdict);
        Assert.Equal(new[] { ReviewFlags.Unverifiable }, result.Flags);
        Assert.Null(result.Ceiling);
    }

    private static ScoringChart Chart(bool empty) =>
        empty ? ScoringChart.Parse("""{ "stageId": "x", "notes": [] }""") : Fixtures.Chart(Stage);

    /// <summary>The same recording with nothing pressed or held from <paramref name="from"/> on.</summary>
    private static byte[] Silence(byte[] trace, float from)
    {
        Assert.True(InputTrace.TryRead(trace, InputTraceWriter.MaxFrames, out InputTrace? parsed, out _));

        var writer = new InputTraceWriter();
        writer.Begin(parsed!.LaneCount);
        for (int i = 0; i < parsed.FrameCount; i++)
        {
            bool quiet = parsed.Times[i] >= from;
            writer.Record(parsed.Times[i], quiet ? 0 : parsed.Pressed[i], quiet ? 0 : parsed.Held[i]);
        }

        return writer.ToArray();
    }
}

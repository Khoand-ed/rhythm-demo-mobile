using Promuse.Scoring.Rules;

namespace Promuse.Scoring.Tests;

/// <summary>
/// The data both sides read, and the formats both sides write.
///
/// 两边钉同一个数 / Several expectations here are pinned as literal values that
/// RhythmSharedFormatTests on the Unity side pins too: the fingerprint of the shipped ruleset
/// and the first rolls of a seeded run. Agreement between the two runtimes is the property
/// being protected - a value right on one and different on the other is precisely the bug that
/// would reject every honest device - so the same number is asserted on both.
/// </summary>
public class SharedDataTests
{
    // ----------------------------------------------------------------- charts

    [Theory]
    [InlineData("stage_001", 179, 179, 0, 0.036f, 36.004f)]
    [InlineData("stage_AIW", 326, 335, 5, 53.55f, 224.85f)]
    [InlineData("stage_AIW_hard", 462, 479, 8, 53.55f, 224.85f)]
    public void Shipped_charts_read_the_way_the_importer_reads_them(
        string stage, int notes, int judgements, int holds, float first, float last)
    {
        ScoringChart chart = Fixtures.Chart(stage);

        Assert.Equal(stage, chart.StageId);
        Assert.Equal(0, chart.SkippedNotes);
        Assert.Equal(notes, chart.Notes.Count);
        Assert.Equal(judgements, chart.JudgementCount);
        Assert.Equal(holds, chart.HoldCount);
        Assert.Equal(first, chart.FirstNoteTime, 3);
        Assert.Equal(last, chart.LastJudgementTime, 3);

        for (int i = 1; i < chart.Notes.Count; i++)
        {
            Assert.True(chart.Notes[i].hitTime >= chart.Notes[i - 1].hitTime, "notes must be sorted");
        }
    }

    [Fact]
    public void A_note_the_importer_refuses_is_skipped_here_too()
    {
        ScoringChart chart = ScoringChart.Parse("""
            { "stageId": "x", "notes": [
                { "type": "Tap",  "lane": "Left",  "time": 1 },
                { "type": "Slide","lane": "Left",  "time": 2 },
                { "type": "Tap",  "lane": "Up",    "time": 3 },
                { "type": "Hold", "lane": "Right", "startTime": 5, "endTime": 4 },
                { "type": "Twin", "time": 6 }
            ] }
            """);

        Assert.Equal(3, chart.SkippedNotes);
        Assert.Equal(2, chart.Notes.Count);
        Assert.Equal(3, chart.JudgementCount);   // a Tap, and a Twin's two halves
    }

    // ---------------------------------------------------------------- ruleset

    [Fact]
    public void The_shipped_ruleset_loads_and_builds_every_passive()
    {
        Ruleset rules = Fixtures.Ruleset();

        Assert.Equal(new[] { 4, 8, 16 }, rules.score.multiplierThresholds);

        foreach (string id in Fixtures.Operators)
        {
            Assert.NotNull(RulesetLoader.CreatePassive(Fixtures.Operator(rules, id).passive));
        }

        var echo = (FeverExtendPassive)RulesetLoader.CreatePassive(Fixtures.Operator(rules, "ECHO").passive)!;
        Assert.Equal(50, echo.comboThreshold);
        Assert.Equal(3f, echo.extraSeconds);
    }

    [Theory]
    [InlineData("\"type\": \"ComboShieldPassive\"", "\"type\": \"NoSuchPassive\"", "unknown passive")]
    [InlineData("\"version\": 1", "\"version\": 2", "version")]
    [InlineData("\"tapGreat\": 0.1", "\"tapGreat\": 0.5", "judge windows")]
    [InlineData("\"id\": \"NOVA\"", "\"id\": \"AMIYA\"", "appears twice")]
    public void A_ruleset_the_server_cannot_stand_behind_is_refused(string find, string replace, string expected)
    {
        string json = File.ReadAllText(Fixtures.RepoPath(Path.Combine("server", "data", "ruleset.json")));
        Assert.Contains(find, json);

        var error = Assert.Throws<InvalidDataException>(() => RulesetLoader.Parse(json.Replace(find, replace)));
        Assert.Contains(expected, error.Message);
    }

    // ------------------------------------------------------------ fingerprint

    /// <summary>Pinned on the Unity side too - see the class summary.</summary>
    public const string ShippedAmiyaFingerprint = "856c377e36e8ba6f";

    [Fact]
    public void The_fingerprint_of_the_shipped_rules_is_the_one_unity_computes()
    {
        Ruleset rules = Fixtures.Ruleset();

        Assert.Equal(ShippedAmiyaFingerprint,
                     RulesetFingerprint.Compute(rules, Fixtures.Operator(rules, "AMIYA")));
    }

    [Fact]
    public void Any_scoring_number_changes_the_fingerprint()
    {
        Ruleset rules = Fixtures.Ruleset();
        OperatorRules amiya = Fixtures.Operator(rules, "AMIYA");
        string before = RulesetFingerprint.Compute(rules, amiya);

        rules.judge.tapGreat = 0.11f;
        Assert.NotEqual(before, RulesetFingerprint.Compute(rules, amiya));
        rules.judge.tapGreat = 0.1f;

        amiya.passive.parameters[0].value = 2;
        Assert.NotEqual(before, RulesetFingerprint.Compute(rules, amiya));
        amiya.passive.parameters[0].value = 1;

        Assert.Equal(before, RulesetFingerprint.Compute(rules, amiya));
    }

    [Fact]
    public void Each_operator_has_its_own_fingerprint_and_parameter_order_does_not_matter()
    {
        Ruleset rules = Fixtures.Ruleset();
        OperatorRules echo = Fixtures.Operator(rules, "ECHO");
        string before = RulesetFingerprint.Compute(rules, echo);

        Assert.NotEqual(before, RulesetFingerprint.Compute(rules, Fixtures.Operator(rules, "NOVA")));

        echo.passive.parameters.Reverse();
        Assert.Equal(before, RulesetFingerprint.Compute(rules, echo));
    }

    // ----------------------------------------------------------- the run RNG

    [Fact]
    public void A_seeded_run_rolls_the_same_numbers_on_every_runtime()
    {
        var run = new RunState { seed = 42 };
        run.Reset();

        float[] rolls = [run.NextRoll(), run.NextRoll(), run.NextRoll()];

        Assert.Equal(PinnedRolls, rolls);
        Assert.All(rolls, r => Assert.InRange(r, 0f, 0.99999994f));
    }

    /// <summary>Pinned on the Unity side too.</summary>
    public static readonly float[] PinnedRolls = [0.74156487f, 0.159910381f, 0.27860111f];

    [Fact]
    public void Reset_rewinds_the_rolls_so_a_retry_draws_what_the_first_attempt_drew()
    {
        var run = new RunState { seed = 99 };
        run.Reset();
        float first = run.NextRoll();
        run.NextRoll();

        run.Reset();

        Assert.Equal(first, run.NextRoll());
    }

    [Fact]
    public void Judge_upgrade_promotes_about_as_often_as_its_chance_says()
    {
        var passive = new JudgeUpgradePassive { chance = 0.25f };
        var run = new RunState { seed = 2026, passive = passive };
        run.Reset();

        int promoted = 0;
        for (int i = 0; i < 20000; i++)
        {
            if (passive.Regrade(Judgement.Great, run) == Judgement.Perfect) promoted++;
        }

        Assert.InRange(promoted / 20000.0, 0.24, 0.26);
    }

    // ------------------------------------------------------------- the trace

    [Fact]
    public void A_trace_reads_back_exactly_what_was_written()
    {
        var writer = new InputTraceWriter();
        writer.Begin(2);
        writer.Record(0.016666668f, 0b01, 0b00);
        writer.Record(0.033333335f, 0b10, 0b01);
        writer.Record(0.033333335f, 0b00, 0b11);

        Assert.True(InputTrace.TryRead(writer.ToArray(), 1000, out InputTrace? trace, out string? error), error);

        Assert.Equal(2, trace!.LaneCount);
        Assert.Equal(new[] { 0.016666668f, 0.033333335f, 0.033333335f }, trace.Times);
        Assert.Equal(new byte[] { 1, 2, 0 }, trace.Pressed);
        Assert.Equal(new byte[] { 0, 1, 3 }, trace.Held);
    }

    [Fact]
    public void Begin_starts_a_fresh_trace_for_a_retry()
    {
        var writer = new InputTraceWriter();
        writer.Begin(2);
        writer.Record(1f, 1, 0);
        writer.Begin(2);

        Assert.Equal(0, writer.FrameCount);
        Assert.True(InputTrace.TryRead(writer.ToArray(), 10, out InputTrace? trace, out _));
        Assert.Equal(0, trace!.FrameCount);
    }

    public static TheoryData<string, Func<byte[], byte[]>> Corruptions() => new()
    {
        { "magic", b => { b[0] = (byte)'X'; return b; } },
        { "version", b => { b[2] = 9; return b; } },
        { "lane count", b => { b[3] = 0; return b; } },
        { "truncated", b => b[..^1] },
        { "padded", b => [.. b, 0] },
        { "frame count", b => { b[4] = 200; return b; } },
        { "backwards", b => { b[8 + 6 + 3] = 0; return b; } },
        { "phantom lane", b => { b[8 + 4] = 0b100; return b; } },
        { "not a number", b => { b[8] = 0; b[9] = 0; b[10] = 0xC0; b[11] = 0x7F; return b; } },
    };

    [Theory]
    [MemberData(nameof(Corruptions))]
    public void A_trace_the_writer_could_not_have_made_is_refused(string what, Func<byte[], byte[]> corrupt)
    {
        var writer = new InputTraceWriter();
        writer.Begin(2);
        writer.Record(1f, 1, 0);
        writer.Record(2f, 2, 0);

        Assert.False(InputTrace.TryRead(corrupt(writer.ToArray()), 100, out _, out string? error), what);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void A_trace_over_the_frame_limit_is_refused()
    {
        var writer = new InputTraceWriter();
        writer.Begin(1);
        for (int i = 0; i < 11; i++) writer.Record(i, 0, 0);

        Assert.False(InputTrace.TryRead(writer.ToArray(), 10, out _, out _));
    }
}

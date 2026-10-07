using Promuse.Scoring.Review;
using Promuse.Scoring.Rules;
using Xunit.Abstractions;

namespace Promuse.Scoring.Tests;

/// <summary>
/// 天花板只能高不能低 / The ceiling may be loose; it must never be low. A ceiling below what a
/// person can actually score rejects honest players, which is the one mistake a validator is
/// not allowed to make. Every test here is a way an honest run could try to get above it.
/// </summary>
public class ScoreCeilingTests(ITestOutputHelper output)
{
    public static TheoryData<string, string> EveryChartAndOperator()
    {
        var data = new TheoryData<string, string>();
        foreach (string stage in Fixtures.Stages)
        foreach (string op in Fixtures.Operators)
        {
            data.Add(stage, op);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryChartAndOperator))]
    public void A_flawless_run_fits_under_the_ceiling(string stage, string operatorId)
    {
        Ruleset rules = Fixtures.Ruleset();
        ScoringChart chart = Fixtures.Chart(stage);
        OperatorRules op = Fixtures.Operator(rules, operatorId);

        PlayedRun flawless = Fixtures.Play(chart, rules, op);
        int ceiling = ScoreCeiling.Compute(chart, rules, op);

        output.WriteLine($"{stage} {operatorId}: flawless {flawless.Score}, ceiling {ceiling} " +
                         $"({(double)ceiling / flawless.Score:P0} of flawless)");

        Assert.True(flawless.FullCombo);
        Assert.True(flawless.Score <= ceiling, $"flawless {flawless.Score} is above the ceiling {ceiling}");
    }

    /// <summary>
    /// 故意漏音来挪 Fever / The reason the ceiling is not simply the flawless score. A long sparse
    /// stretch, then a short dense burst: played flawlessly, fever fills on the sparse part and
    /// burns there; skip the first few notes and the gauge fills exactly as the burst begins.
    /// The test first proves the trick really does beat a flawless run - otherwise it would be
    /// testing nothing - and then that the ceiling still covers it.
    /// </summary>
    [Theory]
    [InlineData("PULSE")]
    [InlineData("AMIYA")]
    [InlineData("ECHO")]
    public void Missing_on_purpose_to_move_fever_still_fits_under_the_ceiling(string operatorId)
    {
        Ruleset rules = Fixtures.Ruleset();
        OperatorRules op = Fixtures.Operator(rules, operatorId);

        // 40 notes a second apart, then 30 notes a tenth of a second apart.
        var notes = new List<NoteData>();
        for (int i = 0; i < 40; i++) notes.Add(Tap(i * 1f));
        for (int i = 0; i < 30; i++) notes.Add(Tap(40f + i * 0.1f));
        ScoringChart chart = Synthetic(notes);

        int fill = ScoreCeiling.GaugeFillCount(rules, op);

        // Miss just enough of the opening that the last full-gauge Perfect is the burst's
        // first note.
        var missed = new HashSet<int>(Enumerable.Range(0, 40 - (fill - 1)));

        PlayedRun flawless = Fixtures.Play(chart, rules, op);
        PlayedRun clever = Fixtures.Play(chart, rules, op, missed);
        int ceiling = ScoreCeiling.Compute(chart, rules, op);

        output.WriteLine($"{operatorId}: flawless {flawless.Score}, clever {clever.Score}, ceiling {ceiling}");

        Assert.False(clever.Failed);
        Assert.True(clever.Score > flawless.Score,
                    "the scenario must actually reward the trick, or it proves nothing");
        Assert.True(clever.Score <= ceiling, $"the clever run {clever.Score} is above the ceiling {ceiling}");
    }

    [Theory]
    [MemberData(nameof(EveryChartAndOperator))]
    public void The_ceiling_is_tight_enough_to_mean_something(string stage, string operatorId)
    {
        Ruleset rules = Fixtures.Ruleset();
        ScoringChart chart = Fixtures.Chart(stage);
        OperatorRules op = Fixtures.Operator(rules, operatorId);

        int flawless = Fixtures.Play(chart, rules, op).Score;
        int ceiling = ScoreCeiling.Compute(chart, rules, op);

        // A bound twice the real thing would let a cheat double their score and pass. This is
        // not a correctness property - it documents how much room a forged score has left
        // until the replay closes it.
        Assert.True(ceiling < flawless * 1.6, $"ceiling {ceiling} is more than 1.6x the flawless {flawless}");
    }

    [Fact]
    public void A_stronger_operator_has_a_higher_ceiling()
    {
        Ruleset rules = Fixtures.Ruleset();
        ScoringChart chart = Fixtures.Chart("stage_AIW");

        int amiya = ScoreCeiling.Compute(chart, rules, Fixtures.Operator(rules, "AMIYA"));
        int pulse = ScoreCeiling.Compute(chart, rules, Fixtures.Operator(rules, "PULSE"));

        Assert.True(amiya > pulse);
    }

    [Theory]
    [InlineData("PULSE", 34)]   // 3.0 a Perfect: 33 leave the gauge at 99
    [InlineData("NOVA", 31)]    // 3.3 a Perfect
    [InlineData("ECHO", 31)]
    [InlineData("AMIYA", 28)]   // 3.6 a Perfect
    public void The_gauge_fills_after_the_number_of_perfects_the_device_needs(string operatorId, int expected)
    {
        Ruleset rules = Fixtures.Ruleset();

        Assert.Equal(expected, ScoreCeiling.GaugeFillCount(rules, Fixtures.Operator(rules, operatorId)));
    }

    [Fact]
    public void An_empty_chart_has_a_ceiling_of_zero()
    {
        Ruleset rules = Fixtures.Ruleset();

        Assert.Equal(0, ScoreCeiling.Compute(Synthetic([]), rules, Fixtures.Operator(rules, "PULSE")));
    }

    private static NoteData Tap(float time) => new() { hitTime = time, lane = 0, type = NoteType.Tap };

    private static ScoringChart Synthetic(List<NoteData> notes)
    {
        var json = new System.Text.StringBuilder("{\"stageId\":\"synthetic\",\"notes\":[");
        for (int i = 0; i < notes.Count; i++)
        {
            if (i > 0) json.Append(',');
            json.Append("{\"type\":\"Tap\",\"lane\":\"Left\",\"time\":")
                .Append(notes[i].hitTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Append('}');
        }
        json.Append("]}");
        return ScoringChart.Parse(json.ToString());
    }
}

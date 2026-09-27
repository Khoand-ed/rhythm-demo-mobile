// Proof that the mono-repo premise actually holds.
//
// These do not test the server. They run the game's own RunState - the very file
// under Assets/Source/Script/Core, linked into Promuse.Scoring, not a copy - and
// assert the results the Unity EditMode suite asserts. If this project ever goes
// red, the answer is never to edit the game code to suit the server; it is that
// a rule moved and replay validation would have started disagreeing with the
// device silently.
//
// 故意重复 / Deliberately overlapping with the EditMode tests. The same rule is
// checked on both runtimes on purpose: agreement between the two is the property
// being protected, and a rule tested only on one side is exactly the rule that
// drifts.

namespace Promuse.Scoring.Tests;

public class LinkedScoringTests
{
    private static RunState NewRun(
        int maxHp = 100,
        int[]? thresholds = null,
        float scoreModifier = 1f,
        int operatorMaxHp = 0,
        PassiveSO? passive = null)
    {
        var run = new RunState
        {
            health = new HealthSettings { maxHp = maxHp },
            fever = new FeverSettings(),
            multiplierThresholds = thresholds ?? [4, 8, 16],
            scoreModifier = scoreModifier,
            operatorMaxHp = operatorMaxHp,
            passive = passive,
        };

        run.Reset();
        return run;
    }

    [Fact]
    public void Run_state_from_unity_source_runs_on_dotnet()
    {
        var run = NewRun();

        Assert.Equal(100, run.hp);
        Assert.Equal(1, run.multiplier);
        Assert.True(run.IsFullCombo);
    }

    [Fact]
    public void Multiplier_rung_is_climbed_before_the_score_is_added()
    {
        var run = NewRun(thresholds: [4, 8, 16]);

        for (int i = 0; i < 4; i++) run.NoteHit(10);

        // 10 + 10 + 10 at 1x, then the fourth hit promotes to 2x and is paid at
        // the new rate - which is the order NoteHit uses, and the reason this is
        // 50 rather than 40.
        Assert.Equal(50, run.score);
        Assert.Equal(2, run.multiplier);
        Assert.Equal(4, run.combo);
    }

    [Fact]
    public void Operator_hp_pool_replaces_the_tuning_asset()
    {
        var run = NewRun(maxHp: 100, operatorMaxHp: 300);

        Assert.Equal(300, run.MaxHp);
        Assert.Equal(300, run.hp);

        run.Damage(50);
        run.Heal(999);

        // Heal clamps to the operator's ceiling, not the asset's. Three separate
        // reads of "the maximum" used to be able to disagree here.
        Assert.Equal(300, run.hp);
    }

    [Fact]
    public void Missing_a_hold_counts_two_misses_and_costs_hold_damage()
    {
        var run = NewRun();

        bool died = run.NoteMissed(NoteType.Hold);

        Assert.False(died);
        Assert.Equal(85, run.hp);          // holdMissDamage defaults to 15
        Assert.Equal(2f, run.missedHits);  // the head that was missed and the tail never reached
        Assert.False(run.IsFullCombo);
    }

    [Fact]
    public void Running_out_of_hp_reports_the_death_exactly_once()
    {
        var run = NewRun(maxHp: 20);

        Assert.False(run.NoteMissed(NoteType.Tap));  // 20 -> 10
        Assert.True(run.NoteMissed(NoteType.Tap));   // 10 -> 0, this is the one that kills
        Assert.False(run.NoteMissed(NoteType.Tap));  // already failed, not a second death

        Assert.True(run.failed);
        Assert.False(run.IsFullCombo);
    }

    /// <summary>
    /// The shim's rounding, pinned.
    ///
    /// ScoreFor puts every award through Mathf.RoundToInt, which Unity defines as
    /// (int)Math.Round(f) - banker's rounding. 10 * 1.25 is exactly 12.5, so the
    /// tie breaks to the even number, 12. An AwayFromZero shim would answer 13
    /// and the server would then accuse honest players of tampering at every .5
    /// boundary. This test is what stops someone "fixing" that.
    /// </summary>
    [Fact]
    public void Score_modifier_rounds_half_to_even_the_way_unity_does()
    {
        var run = NewRun(scoreModifier: 1.25f);

        Assert.Equal(12, run.ScoreFor(10));   // 12.5 -> 12, not 13
        Assert.Equal(18, run.ScoreFor(14));   // 17.5 -> 18, tie the other way
    }

    [Fact]
    public void Accuracy_with_nothing_to_hit_is_zero_rather_than_nan()
    {
        var run = NewRun();

        Assert.Equal(0f, run.Accuracy);
    }

    /// <summary>
    /// The polymorphic passive assets load and dispatch on .NET too. Both cases
    /// are chosen to be independent of the random draw: Random.value is in
    /// [0, 1), so it is always below a chance of 1 and never below a chance of 0.
    ///
    /// Anything between those two is NOT reproducible on the server yet - see the
    /// warning on the Random shim. That is a Phase 4 problem and this test is
    /// careful not to pretend otherwise.
    /// </summary>
    [Theory]
    [InlineData(1f, Judgement.Perfect)]
    [InlineData(0f, Judgement.Great)]
    public void Judge_upgrade_passive_dispatches_through_the_base_class(
        float chance, Judgement expected)
    {
        // Plain construction rather than ScriptableObject.CreateInstance: on the
        // server these are ordinary objects, and the shim deliberately does not
        // grow a factory it has no asset database to back.
        var passive = new JudgeUpgradePassive { chance = chance };

        var run = NewRun(passive: passive);

        Assert.Equal(expected, passive.Regrade(Judgement.Great, run));

        // A Miss never reaches Regrade in the game, but a passive must not
        // promote one if it somehow does.
        Assert.Equal(Judgement.Miss, passive.Regrade(Judgement.Miss, run));
    }
}

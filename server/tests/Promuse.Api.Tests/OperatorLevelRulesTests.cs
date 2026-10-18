using Promuse.Contracts.Characters;

namespace Promuse.Api.Tests;

/// <summary>The levelling rules the game and the server share, without a database.</summary>
public class OperatorLevelRulesTests
{
    [Fact]
    public void Exp_levels_up_while_it_covers_the_next_level_and_keeps_the_rest()
    {
        // Level 1 takes 120 to leave, level 2 takes 140: 300 is two levels and 40 into level 3.
        Assert.Equal(new LevelGain(3, 40, 300, 0), OperatorLevels.Gain(0, 1, 0, 40, 300));

        // EXP already in the bar counts toward the next level.
        Assert.Equal(new LevelGain(2, 0, 20, 0), OperatorLevels.Gain(0, 1, 100, 40, 20));
    }

    [Fact]
    public void At_the_cap_the_bar_is_empty_and_the_overflow_is_wasted()
    {
        // Level 39 takes 880 to reach 40, PULSE's cap.
        Assert.Equal(new LevelGain(40, 0, 880, 1120), OperatorLevels.Gain(0, 39, 0, 40, 2000));
        Assert.Equal(880, OperatorLevels.ExpToCap(0, 39, 0, 40));
        Assert.Equal(780, OperatorLevels.ExpToCap(0, 39, 100, 40));
        Assert.Equal(0, OperatorLevels.ExpToCap(0, 40, 0, 40));
    }

    [Fact]
    public void Lmd_is_a_share_of_the_exp_applied_rounded_up()
    {
        Assert.Equal(60, OperatorLevels.LmdFor(0, 200));
        Assert.Equal(1, OperatorLevels.LmdFor(0, 1));
        Assert.Equal(0, OperatorLevels.LmdFor(0, 0));
        Assert.Equal(100, OperatorLevels.LmdFor(1, 200));
        Assert.Equal(160, OperatorLevels.LmdFor(2, 200));
    }

    [Fact]
    public void Only_battle_records_carry_exp()
    {
        Assert.Equal([200, 400, 1000, 2000], OperatorLevels.Records.Select(r => r.Exp));
        Assert.Equal(400, OperatorLevels.ExpOf(14));
        Assert.Equal(0, OperatorLevels.ExpOf(OperatorLevels.LmdItemId));
    }

    [Fact]
    public void Every_shipped_operator_has_a_level_cap()
    {
        var rules = Promuse.Scoring.Rules.RulesetLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "gamedata", "ruleset.json"));

        Assert.All(rules.operators, op => Assert.True(OperatorLevels.MaxLevel(op.rarity, 0) > 1, op.id));
        Assert.Equal(40, OperatorLevels.MaxLevel(rules.FindOperator("PULSE")!.rarity, 0));
        Assert.Equal(50, OperatorLevels.MaxLevel(rules.FindOperator("AMIYA")!.rarity, 0));
    }
}

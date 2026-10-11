using Promuse.Api.Campaign;
using Promuse.Api.Gacha;
using Promuse.Api.Players;
using Promuse.Contracts.Campaign;
using Promuse.Contracts.Players;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>The campaign's rules and the EXP curve, without a database.</summary>
public class CampaignRulesTests
{
    private static readonly HashSet<string> Charts = ["stage_001", "stage_AIW", "stage_AIW_hard"];

    /// <summary>Dice that answer from a script, so a roll's outcome is pinned.</summary>
    private sealed class ScriptedDice(params int[] answers) : IGachaDice
    {
        private int next;
        public int Next(int max) => Math.Min(answers[next++ % answers.Length], max - 1);
    }

    [Fact]
    public void The_seeded_campaign_is_valid_and_unlocks_in_order_across_chapters()
    {
        CampaignDefinition d = SeedCampaign.Main();
        Assert.Empty(CampaignRules.Validate(d, Charts));

        var order = CampaignRules.Flatten(d);
        bool[] Open(params string[] cleared) =>
            [.. Enumerable.Range(0, order.Count).Select(i => CampaignRules.IsUnlocked(order, i, cleared.ToHashSet()))];

        Assert.Equal([true, false, false, false, false, false], Open());
        Assert.Equal([true, true, false, false, false, false], Open("main_1_1"));

        // 跨章 / The last stage of chapter 1 opens the first of chapter 2.
        Assert.True(Open("main_1_1", "main_1_2", "main_1_3")[3]);
        Assert.False(Open("main_1_1", "main_1_2")[3]);
    }

    [Fact]
    public void Each_drop_rolls_on_its_own_against_its_chance()
    {
        CampaignDrop[] drops =
        [
            new(2, 300, 500, 10000, DropRarity.Common),
            new(6, 1, 1, 2500, DropRarity.Uncommon),
            new(13, 1, 3, 100, DropRarity.Rare),
        ];

        // LMD always (any roll < 10000) with amount 300 + 50; Orirock: 2499 < 2500 drops; Skill Summary: 100 is not < 100.
        List<ItemStack> rolled = CampaignRules.Roll(drops, new ScriptedDice(0, 50, 2499, 100));
        Assert.Equal([new ItemStack(2, 350), new ItemStack(6, 1)], rolled);

        // Nothing under the chances: only the sure thing.
        Assert.Single(CampaignRules.Roll(drops, new ScriptedDice(9999, 0, 9999, 9999)));
    }

    [Theory]
    [InlineData("chapters[0].chapterId")]
    [InlineData("chapters[0].stages[0].chartId")]
    [InlineData("chapters[0].stages[0].code")]
    [InlineData("chapters[0].stages[1].stageId")]
    [InlineData("chapters[0].stages[0].drops[0].chance")]
    [InlineData("chapters[0].stages[0].drops[0].max")]
    [InlineData("chapters[0].stages[0].firstClearOrundum")]
    public void Each_broken_rule_names_its_field(string field)
    {
        CampaignDefinition d = SeedCampaign.Main();
        CampaignChapter c = d.Chapters[0];
        CampaignStage s = c.Stages[0];

        CampaignDefinition With(CampaignChapter chapter) => new([chapter, .. d.Chapters.Skip(1)]);
        CampaignDefinition WithStage(CampaignStage stage) => With(c with { Stages = [stage, .. c.Stages.Skip(1)] });

        CampaignDefinition broken = field switch
        {
            "chapters[0].chapterId" => With(c with { ChapterId = "Bad Id" }),
            "chapters[0].stages[0].chartId" => WithStage(s with { ChartId = "no_such_chart" }),
            "chapters[0].stages[0].code" => WithStage(s with { Code = "" }),
            "chapters[0].stages[1].stageId" => With(c with { Stages = [s, s with { Code = "1-2" }] }),
            "chapters[0].stages[0].drops[0].chance" => WithStage(s with { Drops = [s.Drops[0] with { Chance = 0 }] }),
            "chapters[0].stages[0].drops[0].max" => WithStage(s with { Drops = [s.Drops[0] with { Min = 5, Max = 2 }] }),
            "chapters[0].stages[0].firstClearOrundum" => WithStage(s with { FirstClearOrundum = -1 }),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.Contains(field, CampaignRules.Validate(broken, Charts).Keys);
    }

    [Fact]
    public void Exp_climbs_the_curve_and_every_new_level_pays_level_over_five()
    {
        // Level 1 needs 800: 72 is not enough.
        Assert.Equal((1, 72, 0), PlayerLevels.Climb(1, 0, 72));

        // 9 -> 10 pays 10/5 = 2.
        int need9 = PlayerProgression.MaxExp(9);
        Assert.Equal((10, 5, 2), PlayerLevels.Climb(9, need9 - 10, 15));

        // 9 -> 11 in one go pays 10/5 + 11/5 = 2 + 2.
        int both = need9 + PlayerProgression.MaxExp(10);
        Assert.Equal((11, 0, 4), PlayerLevels.Climb(9, 0, both));

        // Levels 2-4 pay nothing.
        Assert.Equal(0, PlayerLevels.Climb(1, 0, PlayerProgression.MaxExp(1)).Prime);

        // 满级停 / The curve's cap: no further, nothing left over.
        (int level, int exp, _) = PlayerLevels.Climb(119, 0, 10_000_000);
        Assert.Equal((120, 0), (level, exp));
    }
}

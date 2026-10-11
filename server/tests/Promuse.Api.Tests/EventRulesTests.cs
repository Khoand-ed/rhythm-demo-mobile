using Promuse.Api.Events;
using Promuse.Contracts.Events;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>What an event may contain, and what each happening counts towards - no database.</summary>
public class EventRulesTests
{
    private static readonly HashSet<string> Stages = ["stage_001", "stage_AIW"];
    private static readonly HashSet<string> Banners = ["standard"];

    private static EventDefinition Valid() => new(
        "Test", null, null,
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        true, 20, "standard",
        [new(1, new(1, 100)), new(2, new(2, 50))],
        [
            new("any", "Clear any song", EventGoal.PlaySong, 3, 10, null, null),
            new("one", "Clear stage_001", EventGoal.ClearStage, 1, 10, "stage_001", null),
            new("high", "Score 5000", EventGoal.ReachScore, 1, 5, null, 5000),
            new("high_aiw", "Score 5000 on AIW", EventGoal.ReachScore, 1, 5, "stage_AIW", 5000),
            new("pulls", "Pull 10", EventGoal.Headhunt, 10, 5, null, null),
        ],
        [new("m1", 20, new(10, 1))],
        [new("permit", new(10, 1), 100, 3)],
        [new("stage_001", 30)]);

    private static Dictionary<string, string[]> Check(EventDefinition d) => EventRules.Validate(d, Stages, Banners);

    [Fact]
    public void A_sound_event_and_the_seeded_one_pass()
    {
        Assert.Empty(Check(Valid()));
        Assert.Empty(EventRules.Validate(SeedEvents.ResonantDawn(),
            new HashSet<string> { "stage_AIW", "stage_AIW_hard" }, new HashSet<string> { "event_amiya" }));
    }

    [Fact]
    public void Missing_modules_are_simply_absent()
    {
        // 只有签到的活动 / A check-in-only event: no missions, shop or stages, so no currency needed.
        EventDefinition checkInOnly = Valid() with
        {
            CurrencyItemId = null, BannerId = null,
            Missions = null!, Milestones = null!, Shop = null!, Stages = null!,
        };

        Assert.Empty(Check(checkInOnly));
        Assert.Empty(EventRules.Normalize(checkInOnly).Missions);
    }

    [Theory]
    [InlineData("endsAt")]
    [InlineData("checkIn[1].day")]
    [InlineData("missions[1].stageId")]
    [InlineData("missions[2].minScore")]
    [InlineData("milestones[0].requiredPoints")]
    [InlineData("currencyItemId")]
    [InlineData("stages[0].stageId")]
    [InlineData("stages[0].title")]
    [InlineData("stages[0].storyBefore")]
    [InlineData("stages[0].storyAfter")]
    [InlineData("bannerId")]
    [InlineData("missions")]
    public void Each_broken_rule_names_its_field(string field)
    {
        EventDefinition d = Valid();
        EventDefinition broken = field switch
        {
            "endsAt" => d with { EndsAt = d.StartsAt },
            "checkIn[1].day" => d with { CheckIn = [new(1, new(1, 1)), new(3, new(1, 1))] },
            "missions[1].stageId" => d with { Missions = [d.Missions[0], d.Missions[1] with { StageId = null }] },
            "missions[2].minScore" => d with { Missions = [d.Missions[0], d.Missions[1], d.Missions[2] with { MinScore = null }] },
            "milestones[0].requiredPoints" => d with { Milestones = [new("m1", 999, new(10, 1))] },
            "currencyItemId" => d with { CurrencyItemId = null },
            "stages[0].stageId" => d with { Stages = [new("no_such_chart", 10)] },
            "stages[0].title" => d with { Stages = [new("stage_AIW", 10, new string('x', 25))] },
            "stages[0].storyBefore" => d with { Stages = [new("stage_AIW", 10, null, "Not An Id")] },
            "stages[0].storyAfter" => d with { Stages = [new("stage_AIW", 10, null, null, "../secret")] },
            "bannerId" => d with { BannerId = "no_such_banner" },
            "missions" => d with { Missions = [d.Missions[0], d.Missions[0]] },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.Contains(field, Check(broken).Keys);
    }

    [Fact]
    public void A_clear_counts_towards_any_song_its_chart_and_a_score_it_beat()
    {
        EventDefinition d = Valid();

        string[] Hits(string stage, int score, bool accepted) =>
            [.. EventRules.Matching(d, EventGoal.PlaySong, stage, score, accepted).Select(m => m.MissionId)];

        Assert.Equal(["any", "one", "high"], Hits("stage_001", 6000, accepted: true));
        Assert.Equal(["any", "high", "high_aiw"], Hits("stage_AIW", 6000, accepted: true));
        Assert.Equal(["any"], Hits("stage_AIW", 100, accepted: true));

        // 没通过审核的分不算 / A score the server could not vouch for reaches nothing.
        Assert.Equal(["any", "one"], Hits("stage_001", 6000, accepted: false));
    }

    [Fact]
    public void Stages_unlock_one_after_another()
    {
        EventDefinition d = Valid() with
        {
            Stages = [new("stage_001", 1), new("stage_AIW", 1), new("stage_AIW_hard", 1)],
        };

        bool[] Open(params string[] cleared) =>
            [.. Enumerable.Range(0, d.Stages.Count).Select(i => EventRules.IsUnlocked(d, i, cleared.ToHashSet()))];

        Assert.Equal([true, false, false], Open());
        Assert.Equal([true, true, false], Open("stage_001"));
        Assert.Equal([true, true, true], Open("stage_001", "stage_AIW"));

        // 只看前一关 / Only the stage right before counts: the third opens on the second alone.
        Assert.Equal([true, false, true], Open("stage_AIW"));
        Assert.False(EventRules.IsUnlocked(d, 3, new HashSet<string>()));
    }

    [Fact]
    public void A_pull_counts_only_headhunting()
    {
        Assert.Equal(["pulls"],
            EventRules.Matching(Valid(), EventGoal.Headhunt, null, null, false).Select(m => m.MissionId));
    }

    [Fact]
    public void The_stored_form_round_trips()
    {
        EventDefinition d = SeedEvents.ResonantDawn();
        EventDefinition back = EventRules.Parse(EventRules.Serialize(d));

        Assert.Equal(EventRules.Serialize(d), EventRules.Serialize(back));
        Assert.Equal(d.Missions.Count, back.Missions.Count);
        Assert.Equal(EventGoal.ReachScore, back.Missions.Single(m => m.MissionId == "score100k").Goal);
    }

    [Theory]
    [InlineData("summer_2026", true)]
    [InlineData("Summer", false)]
    [InlineData("a-b", false)]
    [InlineData("", false)]
    public void Event_ids_are_lower_case_words(string id, bool valid)
    {
        Assert.Equal(valid, EventRules.IsValidId(id));
    }
}

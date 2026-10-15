using Promuse.Api.Campaign;
using Promuse.Api.Players;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>The rules behind a profile card - the tag, the assistant, the progress - without a database.</summary>
public class PlayerProfileRulesTests
{
    [Fact]
    public void A_free_tag_is_counted_past_the_ones_the_name_already_uses()
    {
        HashSet<int> used = [0, 1, 3];

        Assert.Equal(2, PlayerTags.FreeAt(used, 0));
        Assert.Equal(4, PlayerTags.FreeAt(used, 1));
        Assert.Equal(9999, PlayerTags.FreeAt(used, PlayerTags.Count - used.Count - 1));
    }

    [Fact]
    public void The_last_free_tag_of_a_crowded_name_is_still_found()
    {
        HashSet<int> used = [.. Enumerable.Range(0, PlayerTags.Count).Where(t => t != 4242)];

        Assert.Equal(4242, PlayerTags.FreeAt(used, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerTags.FreeAt(used, 1));
    }

    // ----------------------------------------------------------- the assistant

    private static readonly PlayerProfiles.Owned[] Roster =
    [
        new("PULSE", 0, 40, 0),
        new("NOVA", 1, 10, 0),
        new("ECHO", 1, 10, 50),
    ];

    [Fact]
    public void The_desktop_character_wins_then_the_squad_leader_then_the_furthest_raised()
    {
        Assert.Equal("PULSE", PlayerProfiles.AssistantOf("PULSE", ["NOVA", null, null, null], Roster));
        Assert.Equal("NOVA", PlayerProfiles.AssistantOf(null, [null, null, "NOVA", "ECHO"], Roster));

        // Elite before level, trust before the id.
        Assert.Equal("ECHO", PlayerProfiles.AssistantOf(null, [null, null, null, null], Roster));

        Assert.Null(PlayerProfiles.AssistantOf(null, [null, null, null, null], []));
    }

    [Fact]
    public void Two_operators_raised_alike_always_resolve_to_the_same_one()
    {
        PlayerProfiles.Owned[] twins = [new("NOVA", 0, 1, 0), new("AMIYA", 0, 1, 0)];

        Assert.Equal("AMIYA", PlayerProfiles.AssistantOf(null, [], twins));
        Assert.Equal("AMIYA", PlayerProfiles.AssistantOf(null, [], twins.Reverse()));
    }

    // ------------------------------------------------------------ the campaign

    [Fact]
    public void Progress_is_the_furthest_stage_in_play_order_not_the_latest_cleared()
    {
        var order = CampaignRules.Flatten(SeedCampaign.Main());

        Assert.Null(CampaignRules.Furthest(order, new HashSet<string>()));
        Assert.Equal("1-1", CampaignRules.Furthest(order, new HashSet<string> { "main_1_1" })!.Code);
        Assert.Equal("2-1", CampaignRules.Furthest(order, new HashSet<string> { "main_2_1", "main_1_1" })!.Code);

        // A stage removed from the campaign since it was cleared is not progress.
        Assert.Null(CampaignRules.Furthest(order, new HashSet<string> { "main_9_9" }));
    }
}

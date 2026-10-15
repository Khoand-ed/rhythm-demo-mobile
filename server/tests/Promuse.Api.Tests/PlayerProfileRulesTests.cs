using Promuse.Api.Players;

namespace Promuse.Api.Tests;

/// <summary>The rules behind a profile - the tag after a name - without a database.</summary>
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
}

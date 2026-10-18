using Promuse.Api.Campaign;
using Promuse.Api.Players;
using Promuse.Contracts.Players;
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

    // ----------------------------------------------------------- the signature

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("​‮")]
    public void A_blank_signature_clears_it(string? raw)
    {
        Assert.Equal((null, null), PlayerProfiles.NormalizeSignature(raw));
    }

    [Fact]
    public void A_signature_is_kept_as_one_line_with_single_spaces()
    {
        Assert.Equal("Hello there Doctor",
            PlayerProfiles.NormalizeSignature("  Hello\r\n there\t  Doctor  ").Value);
    }

    [Fact]
    public void Invisible_characters_are_dropped_and_a_reversed_line_cannot_be_planted()
    {
        // U+202E would make the rest of the line read right to left on every card that shows it.
        Assert.Equal("abcdef", PlayerProfiles.NormalizeSignature("abc‮def").Value);
        Assert.Equal("ab", PlayerProfiles.NormalizeSignature("a​b\u0007").Value);

        // Private use, and an unpaired surrogate.
        Assert.Equal("ab", PlayerProfiles.NormalizeSignature("ab\uD800").Value);
    }

    [Fact]
    public void A_letter_typed_with_combining_marks_is_kept_whole_and_counted_once()
    {
        // "ệ" as some keyboards send it: e, a dot below, a circumflex - three code points, one letter.
        const string letter = "ệ";
        Assert.Equal("Vi" + letter + "t", PlayerProfiles.NormalizeSignature("Vi" + letter + "t").Value);

        string sixty = string.Concat(Enumerable.Repeat(letter, PlayerProfiles.SignatureMaxLength));
        Assert.Equal(sixty, PlayerProfiles.NormalizeSignature(sixty).Value);
        Assert.NotNull(PlayerProfiles.NormalizeSignature(sixty + letter).Error);
    }

    [Fact]
    public void Length_is_counted_in_characters_as_read_and_too_long_is_refused_not_cut()
    {
        string sixty = new('ệ', PlayerProfiles.SignatureMaxLength);
        Assert.Equal(sixty, PlayerProfiles.NormalizeSignature(sixty).Value);

        // Sixty emoji are sixty characters, though each is two UTF-16 units.
        string emoji = string.Concat(Enumerable.Repeat("\U0001F3B5", PlayerProfiles.SignatureMaxLength));
        Assert.Equal(emoji, PlayerProfiles.NormalizeSignature(emoji).Value);

        (string? value, string? error) = PlayerProfiles.NormalizeSignature(sixty + "!");
        Assert.Null(value);
        Assert.NotNull(error);
    }

    // ----------------------------------------------------------- the assistant

    [Fact]
    public void The_assistant_is_the_one_chosen_and_amiya_until_one_is()
    {
        Assert.Equal("ECHO", PlayerAssistant.Of("ECHO"));
        Assert.Equal("AMIYA", PlayerAssistant.Of(null));
        Assert.Equal("AMIYA", PlayerAssistant.Of(""));
        Assert.Equal("AMIYA", PlayerAssistant.Default);
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

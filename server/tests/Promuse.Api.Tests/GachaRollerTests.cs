using Promuse.Api.Gacha;

namespace Promuse.Api.Tests;

/// <summary>
/// The rules of a single pull, with the dice scripted.
///
/// 不用数据库 / No database: the roller is pure on purpose, so what the Details screen promises -
/// the tier boundaries, the guarantee, the reset - is pinned here exactly rather than inferred
/// from a few random pulls against a server.
/// </summary>
public class GachaRollerTests
{
    private static readonly GachaOdds Odds = new(200, 1000, 8800, 30);

    private static readonly PoolMember[] Pool =
    [
        new("AMIYA", 5),
        new("ECHO", 4),
        new("NOVA", 4),
        new("PULSE", 3),
    ];

    /// <summary>Hands back the numbers it was given, in order.</summary>
    private sealed class Scripted(params int[] rolls) : IGachaDice
    {
        private int _next;

        public int Next(int max)
        {
            int value = rolls[_next++];
            Assert.InRange(value, 0, max - 1);
            return value;
        }
    }

    [Theory]
    [InlineData(0, "AMIYA")]
    [InlineData(199, "AMIYA")]    // the last of the 200 five-star numbers
    [InlineData(200, "ECHO")]     // the first four-star
    [InlineData(1199, "ECHO")]    // the last four-star
    [InlineData(1200, "PULSE")]   // the first three-star
    [InlineData(9999, "PULSE")]
    public void The_roll_falls_into_the_tier_its_odds_cover(int roll, string expected)
    {
        int pity = 0;

        GachaRoll result = GachaRoller.Next(Odds, Pool, ref pity, new Scripted(roll, 0));

        Assert.Equal(expected, result.Member.CharacterId);
        Assert.False(result.Guaranteed);
    }

    [Fact]
    public void Inside_a_tier_the_second_draw_picks_the_operator()
    {
        int pity = 0;

        Assert.Equal("ECHO", GachaRoller.Next(Odds, Pool, ref pity, new Scripted(500, 0)).Member.CharacterId);
        Assert.Equal("NOVA", GachaRoller.Next(Odds, Pool, ref pity, new Scripted(500, 1)).Member.CharacterId);
    }

    [Fact]
    public void Thirty_pulls_without_a_five_star_make_the_next_one_a_five_star()
    {
        int pity = 0;

        for (int i = 0; i < 30; i++)
        {
            GachaRoll miss = GachaRoller.Next(Odds, Pool, ref pity, new Scripted(9999, 0));
            Assert.Equal(3, miss.Member.Rarity);
        }

        Assert.Equal(30, pity);

        // 不看骰子 / The odds are not consulted: a roll that would be a 3-star is never drawn.
        GachaRoll guaranteed = GachaRoller.Next(Odds, Pool, ref pity, new Scripted(0));

        Assert.Equal("AMIYA", guaranteed.Member.CharacterId);
        Assert.True(guaranteed.Guaranteed);
        Assert.Equal(0, pity);
    }

    [Fact]
    public void A_lucky_five_star_resets_the_count_too()
    {
        int pity = 17;

        GachaRoll lucky = GachaRoller.Next(Odds, Pool, ref pity, new Scripted(5, 0));

        Assert.Equal(5, lucky.Member.Rarity);
        Assert.False(lucky.Guaranteed);
        Assert.Equal(0, pity);
    }

    [Fact]
    public void Anything_below_five_star_counts_up()
    {
        int pity = 3;

        GachaRoller.Next(Odds, Pool, ref pity, new Scripted(500, 0));

        Assert.Equal(4, pity);
    }

    [Fact]
    public void Over_many_pulls_the_tiers_land_near_their_odds()
    {
        // 关掉保底 / The guarantee switched off, so what is measured is the odds alone.
        var odds = Odds with { PityThreshold = int.MaxValue };
        var dice = new SecureGachaDice();
        int pity = 0;
        int five = 0, four = 0;
        const int n = 200_000;

        for (int i = 0; i < n; i++)
        {
            int rarity = GachaRoller.Next(odds, Pool, ref pity, dice).Member.Rarity;
            if (rarity == 5) five++;
            else if (rarity == 4) four++;
        }

        // Five standard deviations either side - wide enough never to flake, narrow enough that
        // a tier off by a whole percentage point fails.
        Assert.InRange(five / (double)n, 0.0200 - 0.0016, 0.0200 + 0.0016);
        Assert.InRange(four / (double)n, 0.1000 - 0.0034, 0.1000 + 0.0034);
    }

    // --------------------------------------------------------------- validation

    [Fact]
    public void The_seeded_shape_is_valid()
    {
        Assert.Null(GachaRoller.Validate(Odds, Pool));
    }

    [Theory]
    [InlineData(200, 1000, 8799, "add up to 9999")]
    [InlineData(-1, 1001, 9000, "negative")]
    public void Odds_that_do_not_make_a_whole_are_refused(int five, int four, int three, string reason)
    {
        Assert.Contains(reason, GachaRoller.Validate(new GachaOdds(five, four, three, 30), Pool));
    }

    [Fact]
    public void A_pool_with_no_five_star_cannot_keep_the_guarantee()
    {
        PoolMember[] noFive = [new("NOVA", 4), new("PULSE", 3)];

        Assert.Contains("guarantee", GachaRoller.Validate(Odds with { FiveStar = 0, FourStar = 1200 }, noFive));
    }

    [Fact]
    public void A_tier_with_odds_but_nobody_in_it_is_refused()
    {
        PoolMember[] noFour = [new("AMIYA", 5), new("PULSE", 3)];

        Assert.Contains("4-star", GachaRoller.Validate(Odds, noFour));
    }
}

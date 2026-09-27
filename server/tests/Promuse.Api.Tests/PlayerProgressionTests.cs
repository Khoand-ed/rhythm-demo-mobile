using Promuse.Api.Players;
using Promuse.Contracts.Players;

namespace Promuse.Api.Tests;

/// <summary>
/// 不需要容器 / No collection attribute and no database: these cover pure
/// functions, so they run in microseconds next to integration tests that take
/// seconds. Splitting them that way is deliberate - a regression in a formula
/// should not need Postgres to be noticed.
/// </summary>
public class PlayerProgressionTests
{
    /// <summary>
    /// 钉住客户端的曲线 / Values taken from PlayerData.GetMaxExp, branch by
    /// branch and on both sides of every boundary. The server duplicates that
    /// formula (see PlayerProgression), so this is what turns a silent
    /// divergence into a failing test.
    /// </summary>
    [Theory]
    [InlineData(0, 500)]
    [InlineData(1, 800)]
    [InlineData(2, 1160)]
    [InlineData(27, 3160)]
    [InlineData(28, 2960)]     // the curve genuinely drops here; it is not a typo
    [InlineData(33, 3460)]
    [InlineData(34, 3900)]
    [InlineData(49, 8400)]
    [InlineData(50, 8500)]
    [InlineData(64, 15500)]
    [InlineData(65, 16000)]
    [InlineData(100, 51000)]
    [InlineData(101, 52000)]
    [InlineData(109, 68000)]
    [InlineData(110, 70000)]
    [InlineData(119, 97000)]
    [InlineData(120, -1)]
    public void Max_exp_matches_the_client_curve(int level, int expected) =>
        Assert.Equal(expected, PlayerProgression.MaxExp(level));

    /// <summary>
    /// From PlayerData.GetMaxReason. Two branches hinge on arithmetic that is
    /// easy to "tidy" into something else: `113 + level / 5` is integer division,
    /// and `105.75 + level / 4D` is not. Both are pinned here.
    /// </summary>
    [Theory]
    [InlineData(1, 82)]
    [InlineData(4, 88)]
    [InlineData(5, 90)]
    [InlineData(34, 119)]
    [InlineData(35, 120)]      // 113 + 35/5, integer division
    [InlineData(84, 129)]      // 113 + 84/5 = 113 + 16, not 113 + 16.8
    [InlineData(85, 130)]
    [InlineData(99, 130)]
    [InlineData(100, 130)]     // (int)(105.75 + 25.0)
    [InlineData(119, 135)]     // (int)(105.75 + 29.75) = (int)135.5
    [InlineData(120, -1)]
    public void Max_stamina_matches_the_client_curve(int level, int expected) =>
        Assert.Equal(expected, PlayerProgression.MaxStamina(level));

    // ------------------------------------------------------------- stamina

    private static readonly DateTimeOffset Anchor = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan PerPoint = TimeSpan.FromMinutes(3);

    [Fact]
    public void A_full_bar_does_not_accrue_and_reports_no_deadlines()
    {
        Stamina result = StaminaCalculator.Compute(82, Anchor, 82, PerPoint, Anchor.AddHours(10));

        Assert.Equal(82, result.Current);
        Assert.Null(result.NextPointAt);
        Assert.Null(result.FullAt);
    }

    /// <summary>
    /// An item can push the bar past its cap. That surplus is a real state and
    /// must survive a read - trimming it here would delete something the player
    /// paid for.
    /// </summary>
    [Fact]
    public void Over_the_cap_is_preserved_rather_than_trimmed()
    {
        Stamina result = StaminaCalculator.Compute(120, Anchor, 82, PerPoint, Anchor.AddHours(10));

        Assert.Equal(120, result.Current);
        Assert.Equal(82, result.Max);
    }

    [Theory]
    [InlineData(0, 0)]      // no time has passed
    [InlineData(2, 0)]      // not yet a whole point
    [InlineData(3, 1)]      // exactly one interval
    [InlineData(5, 1)]      // partial intervals do not round up
    [InlineData(30, 10)]
    public void Points_accrue_one_whole_interval_at_a_time(int minutesElapsed, int expectedGain)
    {
        Stamina result = StaminaCalculator.Compute(
            10, Anchor, 82, PerPoint, Anchor.AddMinutes(minutesElapsed));

        Assert.Equal(10 + expectedGain, result.Current);
    }

    [Fact]
    public void Accrual_stops_at_the_cap()
    {
        Stamina result = StaminaCalculator.Compute(80, Anchor, 82, PerPoint, Anchor.AddDays(7));

        Assert.Equal(82, result.Current);
        Assert.Null(result.NextPointAt);
    }

    /// <summary>
    /// 从锚点算, 不是从现在 / Both deadlines are measured from the stored anchor.
    /// Measuring from `now` would make two reads a second apart report two
    /// different times for the same next point, and a client counting down would
    /// visibly stutter.
    /// </summary>
    [Fact]
    public void Deadlines_are_measured_from_the_anchor_so_reading_does_not_move_them()
    {
        Stamina first = StaminaCalculator.Compute(10, Anchor, 82, PerPoint, Anchor.AddMinutes(4));
        Stamina later = StaminaCalculator.Compute(10, Anchor, 82, PerPoint, Anchor.AddMinutes(5));

        Assert.Equal(first.NextPointAt, later.NextPointAt);
        Assert.Equal(Anchor.AddMinutes(6), first.NextPointAt);

        // 82 - 10 = 72 points still to earn, three minutes each.
        Assert.Equal(Anchor.AddMinutes(72 * 3), first.FullAt);
    }

    /// <summary>
    /// A stored timestamp in the future means a clock moved, not that the player
    /// owes time. Subtracting would take points away from someone who did nothing
    /// wrong.
    /// </summary>
    [Fact]
    public void A_timestamp_in_the_future_never_removes_points()
    {
        Stamina result = StaminaCalculator.Compute(10, Anchor, 82, PerPoint, Anchor.AddHours(-5));

        Assert.Equal(10, result.Current);
    }

    [Theory]
    [InlineData("W/\"7\"", 7)]
    [InlineData("\"7\"", 7)]
    [InlineData("7", 7)]
    public void Etags_parse_in_the_shapes_clients_actually_send(string header, int expected)
    {
        Assert.True(PlayerService.TryParseETag(header, out int version));
        Assert.Equal(expected, version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    public void A_missing_or_malformed_etag_is_refused(string? header) =>
        Assert.False(PlayerService.TryParseETag(header, out _));
}

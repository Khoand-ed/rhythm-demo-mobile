using Microsoft.Extensions.Options;
using Promuse.Api.Missions;
using Promuse.Contracts.Missions;

namespace Promuse.Api.Tests;

/// <summary>
/// 不需要容器 / No database: the period boundaries are pure arithmetic and should
/// be testable without waiting for a clock or for Postgres to start.
///
/// The reset is 04:00 in the players' timezone (UTC+7), which puts the boundary
/// at 21:00 UTC the previous day. Most of these assert that relationship rather
/// than hard-coding ISO week numbers, so a test cannot pass because the author
/// and the code made the same mistake about which week a date falls in.
/// </summary>
public class MissionPeriodTests
{
    private static MissionPeriod Period(int offsetHours = 7, int resetHour = 4) =>
        new(Options.Create(new MissionOptions
        {
            TimeZoneOffsetHours = offsetHours,
            ResetHourLocal = resetHour,
        }));

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, TimeSpan.Zero);

    // ----------------------------------------------------------------- daily

    [Fact]
    public void The_daily_boundary_is_04_00_in_the_players_timezone()
    {
        MissionPeriod period = Period();

        // 20:59 UTC is 03:59 the next day in Vietnam - still yesterday's board.
        Assert.Equal("2026-09-28", period.DailyKey(Utc(2026, 9, 28, 20, 59)));

        // 21:00 UTC is exactly 04:00 - the new board opens.
        Assert.Equal("2026-09-29", period.DailyKey(Utc(2026, 9, 28, 21, 0)));
    }

    [Fact]
    public void Midnight_utc_is_not_a_boundary()
    {
        MissionPeriod period = Period();

        // 都在同一个板子 / An evening in Vietnam and the small hours after it are
        // one board, even though UTC midnight falls between them. Choosing the
        // boundary in UTC would have split a single session in two.
        Assert.Equal(
            period.DailyKey(Utc(2026, 9, 28, 16, 0)),   // 23:00 local
            period.DailyKey(Utc(2026, 9, 28, 20, 0)));  // 03:00 local, next day
    }

    [Fact]
    public void A_whole_day_shares_one_key()
    {
        MissionPeriod period = Period();
        string expected = period.DailyKey(Utc(2026, 9, 28, 21, 0));

        // Every hour from 04:00 local through 03:00 the next local day.
        for (int hour = 0; hour < 24; hour++)
        {
            Assert.Equal(expected, period.DailyKey(Utc(2026, 9, 28, 21, 0).AddHours(hour)));
        }

        Assert.NotEqual(expected, period.DailyKey(Utc(2026, 9, 28, 21, 0).AddHours(24)));
    }

    [Fact]
    public void The_daily_reset_is_the_next_boundary_and_is_in_the_future()
    {
        MissionPeriod period = Period();
        DateTimeOffset now = Utc(2026, 9, 28, 23, 30);

        DateTimeOffset resets = period.ResetsAt(MissionTab.Daily, now);

        Assert.Equal(Utc(2026, 9, 29, 21, 0), resets);
        Assert.True(resets > now);

        // 刚好在边界上也要往前 / Standing exactly on a boundary must give the NEXT
        // one, not the one just passed, or a client would render a countdown of
        // zero for a board that just opened.
        Assert.True(period.ResetsAt(MissionTab.Daily, resets) > resets);
    }

    // ---------------------------------------------------------------- weekly

    [Fact]
    public void The_weekly_reset_always_lands_on_monday_04_00_local()
    {
        MissionPeriod period = Period();

        // Every day of one week, to catch an off-by-one that only shows on the
        // day the week rolls.
        for (int day = 0; day < 14; day++)
        {
            DateTimeOffset now = Utc(2026, 9, 21, 12, 0).AddDays(day);
            DateTimeOffset resets = period.ResetsAt(MissionTab.Weekly, now);
            DateTimeOffset local = resets.ToOffset(TimeSpan.FromHours(7));

            Assert.Equal(DayOfWeek.Monday, local.DayOfWeek);
            Assert.Equal(4, local.Hour);
            Assert.Equal(0, local.Minute);
            Assert.True(resets > now);
        }
    }

    [Fact]
    public void A_week_shares_one_key_and_the_next_week_does_not()
    {
        MissionPeriod period = Period();

        // Start just after a weekly reset, wherever that falls.
        DateTimeOffset start = period.ResetsAt(MissionTab.Weekly, Utc(2026, 9, 21, 12, 0));
        string expected = period.WeeklyKey(start);

        for (int day = 0; day < 7; day++)
        {
            Assert.Equal(expected, period.WeeklyKey(start.AddDays(day)));
        }

        Assert.NotEqual(expected, period.WeeklyKey(start.AddDays(7)));
    }

    [Fact]
    public void Weekly_keys_are_iso_and_carry_their_year()
    {
        MissionPeriod period = Period();
        string key = period.WeeklyKey(Utc(2026, 9, 28, 12, 0));

        // "2026-W40" - the year matters because week 1 comes round again.
        Assert.Matches(@"^\d{4}-W\d{2}$", key);
    }

    // ------------------------------------------------------------ the config

    [Fact]
    public void Changing_the_configured_reset_moves_the_boundary()
    {
        // 配置真的有用 / Asserted rather than assumed: a setting that is read but
        // never affects anything is worse than no setting at all.
        MissionPeriod utcMidnight = Period(offsetHours: 0, resetHour: 0);

        Assert.Equal("2026-09-28", utcMidnight.DailyKey(Utc(2026, 9, 28, 23, 59)));
        Assert.Equal("2026-09-29", utcMidnight.DailyKey(Utc(2026, 9, 29, 0, 0)));

        // And the Vietnam configuration disagrees with it at that moment, which
        // is the whole reason the setting exists.
        Assert.NotEqual(
            utcMidnight.DailyKey(Utc(2026, 9, 28, 23, 59)),
            Period().DailyKey(Utc(2026, 9, 28, 23, 59)));
    }
}

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Options;
using Promuse.Contracts.Missions;

namespace Promuse.Api.Missions;

public sealed class MissionOptions
{
    public const string SectionName = "Missions";

    /// <summary>
    /// 玩家所在时区 / The offset the reset is expressed in. Players are in
    /// Vietnam, so a boundary chosen in UTC would land in the middle of their
    /// afternoon and take away a board they were halfway through.
    /// </summary>
    [Range(-12, 14)]
    public int TimeZoneOffsetHours { get; set; } = 7;

    /// <summary>
    /// 04:00 而不是午夜 / Four in the morning, not midnight, and that is the genre
    /// convention for a reason: midnight cuts across the evening session that
    /// most players are still in.
    /// </summary>
    [Range(0, 23)]
    public int ResetHourLocal { get; set; } = 4;
}

/// <summary>
/// Which daily and weekly board a moment belongs to, and when that board ends.
///
/// 纯函数 / Deliberately pure: every method takes the instant rather than reading
/// a clock, so the boundaries can be tested without waiting for one. The whole
/// point of moving this off the device is that the player's clock stops deciding
/// when their missions reset, and a server that read its own clock from three
/// different places would be no better.
///
/// 平移再取整 / The trick is the shift. Moving the instant by
/// (offset - resetHour) puts the reset boundary exactly on midnight of the
/// shifted clock, so "which day is this" becomes an ordinary date truncation and
/// "when does it end" becomes the next midnight, shifted back.
/// </summary>
public sealed class MissionPeriod(IOptions<MissionOptions> options)
{
    private MissionOptions Options => options.Value;

    private TimeSpan Shift => TimeSpan.FromHours(Options.TimeZoneOffsetHours - Options.ResetHourLocal);

    /// <summary>The instant, moved so that a reset falls on midnight.</summary>
    private DateTime Shifted(DateTimeOffset instant) => (instant + Shift).UtcDateTime;

    /// <summary>
    /// The daily board's key, as a plain date. Two instants share a key exactly
    /// when they belong to the same daily board.
    /// </summary>
    public string DailyKey(DateTimeOffset instant) =>
        Shifted(instant).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// ISO week, so the week starts on Monday and the year rolls over the way the
    /// rest of the world expects. "2026-W39" rather than a bare week number,
    /// because week 1 comes round again.
    /// </summary>
    public string WeeklyKey(DateTimeOffset instant)
    {
        DateTime day = Shifted(instant).Date;

        return string.Create(CultureInfo.InvariantCulture,
            $"{ISOWeek.GetYear(day):D4}-W{ISOWeek.GetWeekOfYear(day):D2}");
    }

    public string KeyFor(MissionTab tab, DateTimeOffset instant) =>
        tab == MissionTab.Weekly ? WeeklyKey(instant) : DailyKey(instant);

    /// <summary>
    /// When the current board ends, in UTC, so a client can show a countdown
    /// without knowing any of the arithmetic above.
    /// </summary>
    public DateTimeOffset ResetsAt(MissionTab tab, DateTimeOffset instant)
    {
        DateTime day = Shifted(instant).Date;

        DateTime nextShifted = tab == MissionTab.Weekly
            // 下周一 / The Monday that starts the next ISO week. Computed from
            // this week's Monday rather than by counting forward from today, so
            // it lands correctly whichever day of the week it is.
            ? ISOWeek.ToDateTime(ISOWeek.GetYear(day), ISOWeek.GetWeekOfYear(day), DayOfWeek.Monday).AddDays(7)
            : day.AddDays(1);

        return new DateTimeOffset(nextShifted, TimeSpan.Zero) - Shift;
    }
}

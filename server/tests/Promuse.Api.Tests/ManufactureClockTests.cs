using Promuse.Contracts.Manufacture;

namespace Promuse.Api.Tests;

/// <summary>
/// 不需要容器 / The production clock the game and the server share, without a database: every
/// case is a moment, a rate and a capacity.
///
/// The station below makes a unit every 30 minutes and holds 16 - LMD at level 1.
/// </summary>
public class ManufactureClockTests
{
    private const int Unit = 30 * 60;
    private const int Capacity = 16;

    private static readonly DateTimeOffset Anchor = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private static StationReading Read(TimeSpan after) =>
        ManufactureClock.Read(Anchor, Unit, Capacity, Anchor + after);

    // ----------------------------------------------------------------- read

    [Fact]
    public void A_fresh_store_is_empty_and_counts_from_its_anchor()
    {
        StationReading reading = Read(TimeSpan.Zero);

        Assert.Equal(0, reading.StoredUnits);
        Assert.Equal(Anchor.AddMinutes(30), reading.NextUnitAt);
        Assert.Equal(Anchor.AddHours(8), reading.FullAt);
    }

    [Fact]
    public void A_unit_lands_on_the_second_and_not_before()
    {
        Assert.Equal(0, Read(TimeSpan.FromSeconds(Unit - 1)).StoredUnits);
        Assert.Equal(1, Read(TimeSpan.FromSeconds(Unit)).StoredUnits);
    }

    [Fact]
    public void Reading_does_not_move_the_deadlines()
    {
        StationReading early = Read(TimeSpan.FromMinutes(31));
        StationReading later = Read(TimeSpan.FromMinutes(59));

        Assert.Equal(1, early.StoredUnits);
        Assert.Equal(Anchor.AddMinutes(60), early.NextUnitAt);
        Assert.Equal(early.NextUnitAt, later.NextUnitAt);
        Assert.Equal(early.FullAt, later.FullAt);
    }

    [Fact]
    public void A_full_store_stops_and_has_no_deadlines()
    {
        StationReading full = Read(TimeSpan.FromHours(8));

        Assert.Equal(Capacity, full.StoredUnits);
        Assert.Null(full.NextUnitAt);
        Assert.Null(full.FullAt);
    }

    [Fact]
    public void A_year_away_is_still_one_full_store()
    {
        Assert.Equal(Capacity, Read(TimeSpan.FromDays(365)).StoredUnits);

        // The arithmetic is in longs: a store left for decades must not wrap round to a small number.
        Assert.Equal(Capacity, ManufactureClock.Read(Anchor, 1, Capacity, Anchor.AddYears(80)).StoredUnits);
    }

    [Fact]
    public void An_anchor_in_the_future_reads_as_empty_rather_than_owing_time()
    {
        StationReading reading = ManufactureClock.Read(Anchor, Unit, Capacity, Anchor.AddHours(-3));

        Assert.Equal(0, reading.StoredUnits);
        Assert.Equal(Anchor.AddMinutes(30), reading.NextUnitAt);
    }

    [Fact]
    public void Fractions_of_a_second_never_tip_a_unit_over()
    {
        // 1799.9 seconds is not yet a unit, however the database rounded the anchor.
        DateTimeOffset now = Anchor.AddSeconds(Unit).AddTicks(-1_000_000);
        Assert.Equal(0, ManufactureClock.Read(Anchor, Unit, Capacity, now).StoredUnits);

        DateTimeOffset ragged = Anchor.AddTicks(7_654_321);
        Assert.Equal(Anchor, ManufactureClock.WholeSeconds(ragged));
        Assert.Equal(TimeSpan.Zero, ManufactureClock.WholeSeconds(ragged.ToOffset(TimeSpan.FromHours(7))).Offset);
    }

    // -------------------------------------------------------------- collect

    [Fact]
    public void Collecting_keeps_the_part_of_a_unit_already_made()
    {
        // Two units and 10 minutes in: the anchor moves past the two, so the next lands 20 minutes on.
        DateTimeOffset now = Anchor.AddMinutes(70);
        DateTimeOffset anchor = ManufactureClock.AnchorAfterCollect(Anchor, Unit, Capacity, now);

        Assert.Equal(Anchor.AddMinutes(60), anchor);
        StationReading after = ManufactureClock.Read(anchor, Unit, Capacity, now);
        Assert.Equal(0, after.StoredUnits);
        Assert.Equal(now.AddMinutes(20), after.NextUnitAt);
    }

    [Fact]
    public void Collecting_a_full_store_restarts_from_now()
    {
        // It filled at 8 hours and sat full for 4: that time is nobody's.
        DateTimeOffset now = Anchor.AddHours(12).AddMinutes(10);
        DateTimeOffset anchor = ManufactureClock.AnchorAfterCollect(Anchor, Unit, Capacity, now);

        Assert.Equal(now, anchor);
        Assert.Equal(now.AddMinutes(30), ManufactureClock.Read(anchor, Unit, Capacity, now).NextUnitAt);
    }

    [Fact]
    public void Collecting_an_empty_store_leaves_the_anchor_where_it_was()
    {
        DateTimeOffset now = Anchor.AddMinutes(10);
        Assert.Equal(Anchor, ManufactureClock.AnchorAfterCollect(Anchor, Unit, Capacity, now));
    }

    [Fact]
    public void Collected_anchors_are_whole_seconds()
    {
        DateTimeOffset now = Anchor.AddHours(9).AddTicks(4_999_999);
        Assert.Equal(0, ManufactureClock.AnchorAfterCollect(Anchor, Unit, Capacity, now).Ticks % TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void Collecting_every_few_hours_for_a_week_pays_exactly_what_the_rate_and_cap_allow()
    {
        foreach (int everyHours in new[] { 1, 5, 8, 12, 24 })
        {
            DateTimeOffset anchor = Anchor;
            int collected = 0;

            for (DateTimeOffset now = Anchor.AddHours(everyHours); now <= Anchor.AddDays(7); now = now.AddHours(everyHours))
            {
                collected += ManufactureClock.Read(anchor, Unit, Capacity, now).StoredUnits;
                anchor = ManufactureClock.AnchorAfterCollect(anchor, Unit, Capacity, now);
            }

            // Two units an hour, capped at 16 between visits: every visit pays min(2h, 16).
            int visits = 7 * 24 / everyHours;
            Assert.Equal(visits * Math.Min(2 * everyHours, Capacity), collected);
        }
    }

    // -------------------------------------------------------------- upgrade

    [Fact]
    public void A_new_level_keeps_the_part_of_a_unit_already_made()
    {
        // 20 minutes into a 2-hour record; the new level makes one every 3 hours.
        DateTimeOffset now = Anchor.AddMinutes(20);
        DateTimeOffset anchor = ManufactureClock.AnchorAtNewLevel(Anchor, 3 * 3600, now);

        Assert.Equal(Anchor, anchor);
        Assert.Equal(Anchor.AddHours(3), ManufactureClock.Read(anchor, 3 * 3600, 3, now).NextUnitAt);
    }

    [Fact]
    public void A_new_level_never_hands_out_a_unit_on_the_spot()
    {
        // 25 minutes made, but the new level's unit is 20 minutes: carried over as 19:59.
        DateTimeOffset now = Anchor.AddMinutes(25);
        DateTimeOffset anchor = ManufactureClock.AnchorAtNewLevel(Anchor, 20 * 60, now);

        Assert.Equal(now.AddSeconds(-(20 * 60 - 1)), anchor);
        StationReading reading = ManufactureClock.Read(anchor, 20 * 60, 36, now);
        Assert.Equal(0, reading.StoredUnits);
        Assert.Equal(now.AddSeconds(1), reading.NextUnitAt);
    }

    [Fact]
    public void A_station_that_was_full_starts_its_new_level_from_now()
    {
        // Collected at full, the anchor is now: nothing carries over.
        DateTimeOffset now = Anchor.AddHours(10);
        DateTimeOffset collected = ManufactureClock.AnchorAfterCollect(Anchor, Unit, Capacity, now);

        Assert.Equal(now, ManufactureClock.AnchorAtNewLevel(collected, 24 * 60, now));
    }

    // ---------------------------------------------------------------- guard

    [Fact]
    public void A_rate_or_capacity_below_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ManufactureClock.Read(Anchor, 0, Capacity, Anchor));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManufactureClock.Read(Anchor, Unit, 0, Anchor));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManufactureClock.AnchorAtNewLevel(Anchor, 0, Anchor));
    }
}

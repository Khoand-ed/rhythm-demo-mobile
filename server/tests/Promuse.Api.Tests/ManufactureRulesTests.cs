using Promuse.Api.Campaign;
using Promuse.Api.Manufacture;
using Promuse.Contracts.Characters;
using Promuse.Contracts.Items;
using Promuse.Contracts.Manufacture;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// 数字钉死 / The Manufacture table, pinned to the numbers agreed for its first version. A
/// failure here means a number moved: change it here too, in the same change, and say why in the
/// commit - the economy around it was sized on these.
/// </summary>
public class ManufactureRulesTests
{
    private const int Lmd = OperatorLevels.LmdItemId;

    [Fact]
    public void The_lmd_station_makes_100_125_150_an_hour_and_fills_in_8_10_12_hours()
    {
        Assert.Equal(new StationLevel(1, Lmd, 50, 1800, 16), ManufactureRules.LevelOf(ManufactureStations.Lmd, 1));
        Assert.Equal(new StationLevel(2, Lmd, 50, 1440, 25), ManufactureRules.LevelOf(ManufactureStations.Lmd, 2));
        Assert.Equal(new StationLevel(3, Lmd, 50, 1200, 36), ManufactureRules.LevelOf(ManufactureStations.Lmd, 3));
    }

    [Fact]
    public void The_exp_station_makes_basic_then_frontline_records()
    {
        Assert.Equal(new StationLevel(1, 11, 1, 7200, 4), ManufactureRules.LevelOf(ManufactureStations.Exp, 1));
        Assert.Equal(new StationLevel(2, 14, 1, 10800, 3), ManufactureRules.LevelOf(ManufactureStations.Exp, 2));
        Assert.Equal(new StationLevel(3, 14, 1, 9000, 5), ManufactureRules.LevelOf(ManufactureStations.Exp, 3));
    }

    [Fact]
    public void Collected_twice_a_day_the_stations_pay_what_the_plan_says()
    {
        // Ten days of visits 12 hours apart, run through the shared clock: LMD, or EXP for records.
        int Daily(string station, int level)
        {
            StationLevel l = ManufactureRules.LevelOf(station, level);
            DateTimeOffset start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset anchor = start;
            long units = 0;

            for (int visit = 1; visit <= 20; visit++)
            {
                DateTimeOffset now = start.AddHours(12 * visit);
                units += ManufactureClock.Read(anchor, l.SecondsPerUnit, l.CapacityUnits, now).StoredUnits;
                anchor = ManufactureClock.AnchorAfterCollect(anchor, l.SecondsPerUnit, l.CapacityUnits, now);
            }

            return (int)(units * l.UnitAmount * Math.Max(1, OperatorLevels.ExpOf(l.ItemId)) / 10);
        }

        Assert.Equal([1_600, 2_500, 3_600], new[] { 1, 2, 3 }.Select(l => Daily(ManufactureStations.Lmd, l)));

        // Level 3 alternates 4 and 5 records: every second visit finds the 12.5-hour store full, and a
        // full store restarts from the visit, so the part of a record it held is gone - 9 a day, not 9.6.
        Assert.Equal([1_600, 2_400, 3_600], new[] { 1, 2, 3 }.Select(l => Daily(ManufactureStations.Exp, l)));
    }

    [Fact]
    public void No_station_makes_the_rare_records()
    {
        foreach (string station in ManufactureStations.All)
        {
            for (int level = 1; level <= ManufactureRules.MaxLevel; level++)
            {
                Assert.DoesNotContain(ManufactureRules.LevelOf(station, level).ItemId, new[] { 15, 16 });
            }
        }
    }

    [Fact]
    public void Each_level_holds_at_least_as_many_hours_as_the_one_below()
    {
        foreach (string station in ManufactureStations.All)
        {
            for (int level = 2; level <= ManufactureRules.MaxLevel; level++)
            {
                StationLevel below = ManufactureRules.LevelOf(station, level - 1);
                StationLevel at = ManufactureRules.LevelOf(station, level);
                Assert.True((long)at.SecondsPerUnit * at.CapacityUnits >= (long)below.SecondsPerUnit * below.CapacityUnits,
                            $"{station} level {level} holds fewer hours than level {level - 1}.");
            }
        }
    }

    [Fact]
    public void Raising_a_station_costs_lmd_and_nothing_at_level_one()
    {
        Assert.Empty(ManufactureRules.UpgradeCost(1));
        Assert.Equal([new(Lmd, 8_000)], ManufactureRules.UpgradeCost(2));
        Assert.Equal([new(Lmd, 25_000)], ManufactureRules.UpgradeCost(3));
    }

    [Fact]
    public void A_level_outside_the_table_reads_as_the_nearest_one()
    {
        Assert.False(ManufactureRules.IsKnownLevel(4));
        Assert.Equal(3, ManufactureRules.LevelOf(ManufactureStations.Lmd, 4).Level);
        Assert.Equal(1, ManufactureRules.LevelOf(ManufactureStations.Exp, 0).Level);
        Assert.Throws<ArgumentOutOfRangeException>(() => ManufactureRules.LevelOf("trading_post", 1));
    }

    [Fact]
    public void Scaling_runs_a_level_faster_and_never_below_a_second()
    {
        StationLevel lmd = ManufactureRules.LevelOf(ManufactureStations.Lmd, 1);

        Assert.Equal(lmd, ManufactureRules.Scaled(lmd, 1));
        Assert.Equal(30, ManufactureRules.Scaled(lmd, 60).SecondsPerUnit);
        Assert.Equal(lmd.CapacityUnits, ManufactureRules.Scaled(lmd, 60).CapacityUnits);
        Assert.Equal(1, ManufactureRules.Scaled(lmd, 100_000).SecondsPerUnit);
    }

    [Fact]
    public void Only_a_development_server_may_run_faster_than_real_time()
    {
        Assert.True(ManufactureOptions.Allows(1, isDevelopment: false));
        Assert.True(ManufactureOptions.Allows(60, isDevelopment: true));
        Assert.False(ManufactureOptions.Allows(60, isDevelopment: false));
        Assert.False(ManufactureOptions.Allows(2, isDevelopment: false));
    }

    // ----------------------------------------------------------- blueprints

    [Fact]
    public void Each_level_has_its_own_blueprint_and_every_blueprint_is_a_key_item()
    {
        Assert.Equal([1, 2, 3], ManufactureRules.Blueprints.Select(b => b.Level));
        Assert.Equal(KeyItems.All, ManufactureRules.Blueprints.Select(b => b.ItemId));
        Assert.All(ManufactureRules.Blueprints, b => Assert.True(KeyItems.Contains(b.ItemId)));

        Assert.Equal(KeyItems.ManufactureBlueprint2, ManufactureRules.BlueprintFor(2).ItemId);
        Assert.Equal("main_1_1", ManufactureRules.UnlockStageId);
    }

    [Fact]
    public void Blueprints_come_from_the_ends_of_the_chapters_in_campaign_order()
    {
        var order = CampaignRules.Flatten(SeedCampaign.Main()).Select(o => o.Stage.StageId).ToList();

        int previous = -1;
        foreach (ManufactureRules.Blueprint blueprint in ManufactureRules.Blueprints)
        {
            int at = order.IndexOf(blueprint.StageId);
            Assert.True(at >= 0, $"{blueprint.StageId} is not a stage of the seeded campaign.");
            Assert.True(at > previous, $"{blueprint.StageId} comes before the blueprint of the level below it.");
            previous = at;

            Assert.Same(blueprint, ManufactureRules.BlueprintForStage(blueprint.StageId));
        }

        Assert.Null(ManufactureRules.BlueprintForStage("main_1_2"));
    }

    [Fact]
    public void Key_items_are_the_three_blueprints_and_nothing_a_player_spends()
    {
        Assert.Equal([30, 31, 32], KeyItems.All);

        Assert.False(KeyItems.Contains(Lmd));
        Assert.False(KeyItems.Contains(OperatorLevels.OrirockItemId));
        Assert.All(OperatorLevels.Records, r => Assert.False(KeyItems.Contains(r.ItemId)));
    }

    [Fact]
    public void Stations_are_listed_in_the_order_rows_are_locked()
    {
        Assert.Equal(ManufactureStations.All.Order(StringComparer.Ordinal), ManufactureStations.All);
        Assert.True(ManufactureStations.IsStation("lmd"));
        Assert.False(ManufactureStations.IsStation("LMD"));
        Assert.False(ManufactureStations.IsStation(null));
    }
}

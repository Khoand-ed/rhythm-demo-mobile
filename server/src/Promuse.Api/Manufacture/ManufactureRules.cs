using Promuse.Contracts.Characters;
using Promuse.Contracts.Items;
using Promuse.Contracts.Manufacture;
using Promuse.Contracts.Players;

namespace Promuse.Api.Manufacture;

/// <summary>
/// 制造站的规则 / What each station makes at each level, what raising it costs, and which story
/// stage pays the blueprint that allows it. The numbers are docs/design/manufacture-plan.md,
/// section 5.2; ManufactureRulesTests pins them, so changing one is a change to both.
///
/// 只放在服务端 / Server-side only. The client never needs the table: every screen reads the level
/// it shows, and the next one, from the view the server sends.
///
/// 已发布的等级不改产物 / A level that has shipped never changes what it makes. A store is read
/// at the level's current product, so a changed product would pay out what was made as the old one
/// as the new one. A different product is a new level. Likewise a capacity only ever grows: a smaller
/// one would cut off what players are already holding.
/// </summary>
public static class ManufactureRules
{
    public const int MaxLevel = 3;

    private const int Minute = 60;
    private const int Hour = 60 * Minute;

    private const int BasicRecord = 11;
    private const int FrontlineRecord = 14;

    // 龙门币: 每格 50 / LMD in steps of 50: 100, 125 and 150 an hour, full after 8, 10 and 12 hours.
    private static readonly StationLevel[] LmdLevels =
    [
        new(1, OperatorLevels.LmdItemId, 50, 30 * Minute, 16),
        new(2, OperatorLevels.LmdItemId, 50, 24 * Minute, 25),
        new(3, OperatorLevels.LmdItemId, 50, 20 * Minute, 36),
    ];

    // 作战记录 / One record a unit. Never Tactical or Strategic: those are 2-2 and 2-3's rare drops,
    // and a station that made them would take the point out of replaying those stages.
    private static readonly StationLevel[] ExpLevels =
    [
        new(1, BasicRecord, 1, 2 * Hour, 4),
        new(2, FrontlineRecord, 1, 3 * Hour, 3),
        new(3, FrontlineRecord, 1, 2 * Hour + 30 * Minute, 5),
    ];

    /// <param name="Level">The level this blueprint allows a station to reach; level 1 is Manufacture itself.</param>
    /// <param name="StageId">The campaign stage whose first clear pays it.</param>
    public sealed record Blueprint(int Level, int ItemId, string StageId);

    /// <summary>One per level, in campaign order. Both stations share them: a blueprint is held, not spent.</summary>
    public static readonly IReadOnlyList<Blueprint> Blueprints =
    [
        new(1, KeyItems.ManufactureBlueprint1, "main_1_1"),
        new(2, KeyItems.ManufactureBlueprint2, "main_1_3"),
        new(3, KeyItems.ManufactureBlueprint3, "main_2_3"),
    ];

    /// <summary>The stage that unlocks Manufacture: Blueprint I's.</summary>
    public static string UnlockStageId => Blueprints[0].StageId;

    /// <summary>
    /// <paramref name="station"/> at <paramref name="level"/>, unscaled. A level outside the table -
    /// a row nobody should be able to write - reads as the nearest level there is, so a damaged row
    /// shows a station rather than an error; the caller logs it.
    /// </summary>
    public static StationLevel LevelOf(string station, int level)
    {
        StationLevel[] levels = station switch
        {
            ManufactureStations.Lmd => LmdLevels,
            ManufactureStations.Exp => ExpLevels,
            _ => throw new ArgumentOutOfRangeException(nameof(station), station, "Not a Manufacture station."),
        };

        return levels[Math.Clamp(level, 1, levels.Length) - 1];
    }

    /// <summary>Whether <paramref name="level"/> is one the table has.</summary>
    public static bool IsKnownLevel(int level) => level is >= 1 and <= MaxLevel;

    /// <summary>
    /// 本地加速 / <paramref name="level"/> run <paramref name="timeScale"/> times faster: an hour a
    /// minute at 60. Only a Development server may run above 1 - see ManufactureOptions.
    /// </summary>
    public static StationLevel Scaled(StationLevel level, int timeScale) =>
        timeScale <= 1 ? level : level with { SecondsPerUnit = Math.Max(1, level.SecondsPerUnit / timeScale) };

    /// <summary>What raising a station to <paramref name="toLevel"/> spends. The blueprint is not in it.</summary>
    public static IReadOnlyList<ItemStack> UpgradeCost(int toLevel) => toLevel switch
    {
        2 => [new ItemStack(OperatorLevels.LmdItemId, 8_000)],
        3 => [new ItemStack(OperatorLevels.LmdItemId, 25_000)],
        _ => [],
    };

    /// <summary>The blueprint <paramref name="level"/> needs.</summary>
    public static Blueprint BlueprintFor(int level) => Blueprints[Math.Clamp(level, 1, MaxLevel) - 1];

    /// <summary>The blueprint the first clear of <paramref name="stageId"/> pays, or null when it pays none.</summary>
    public static Blueprint? BlueprintForStage(string stageId) =>
        Blueprints.FirstOrDefault(b => b.StageId == stageId);
}

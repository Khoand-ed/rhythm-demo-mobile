using System.Text.Json;
using Promuse.Contracts.Campaign;

namespace Promuse.Persistence;

/// <summary>
/// 示例主线 / The campaign the database starts with: two chapters built from the charts the game
/// ships today. Adding a song later is a row on the admin page, not a change here.
/// </summary>
public static class SeedCampaign
{
    public const string Key = "main";

    public const int OrundumItemId = 1;
    public const int OriginitePrimeItemId = 0;

    private const int Lmd = 2, Orirock = 6, BattleRecord = 11, SkillSummary = 13;

    // 更大的作战记录 / The larger battle records, for levelling operators (OperatorLevels.Records).
    private const int FrontlineRecord = 14, TacticalRecord = 15, StrategicRecord = 16;

    // 精英化材料 / The promotion material besides Orirock (OperatorLevels.PromotionCost).
    private const int SugarSubstitute = 7;

    private static CampaignDrop Common(int item, int min, int max, int chance) => new(item, min, max, chance, DropRarity.Common);
    private static CampaignDrop Uncommon(int item, int min, int max, int chance) => new(item, min, max, chance, DropRarity.Uncommon);
    private static CampaignDrop Rare(int item, int min, int max, int chance) => new(item, min, max, chance, DropRarity.Rare);

    public static CampaignDefinition Main() => new(
    [
        new CampaignChapter("chapter_1", "Chapter 1: First Light",
        [
            new("main_1_1", "1-1", "stage_001", 50, [Common(Lmd, 300, 500, 10000), Common(Orirock, 1, 2, 6000)],
                "campaign_1_1_before", "campaign_1_1_after"),
            new("main_1_2", "1-2", "stage_AIW", 50,
                [Common(Orirock, 1, 2, 7000), Uncommon(BattleRecord, 1, 1, 3500), Uncommon(SugarSubstitute, 1, 1, 3000)],
                "campaign_1_2_before", null),
            new("main_1_3", "1-3", "stage_AIW_hard", 100,
                [Common(Orirock, 2, 3, 8000), Uncommon(BattleRecord, 1, 2, 4000), Uncommon(FrontlineRecord, 1, 1, 3000),
                 Rare(SkillSummary, 1, 1, 1000)],
                null, "campaign_1_3_after"),
        ]),
        new CampaignChapter("chapter_2", "Chapter 2: Resonance",
        [
            new("main_2_1", "2-1", "stage_001", 80,
                [Common(Lmd, 500, 800, 10000), Uncommon(BattleRecord, 1, 1, 4000), Uncommon(FrontlineRecord, 1, 2, 4000),
                 Uncommon(SugarSubstitute, 1, 2, 3500)]),
            new("main_2_2", "2-2", "stage_AIW", 80,
                [Common(Orirock, 2, 3, 8000), Rare(SkillSummary, 1, 1, 1500), Rare(TacticalRecord, 1, 1, 2000)]),
            new("main_2_3", "2-3", "stage_AIW_hard", 150,
                [Uncommon(BattleRecord, 2, 3, 5000), Uncommon(TacticalRecord, 1, 1, 3500), Uncommon(SugarSubstitute, 1, 2, 4000),
                 Rare(SkillSummary, 1, 2, 2000), Rare(StrategicRecord, 1, 1, 1500)]),
        ]),
    ]);

    public static string MainJson => JsonSerializer.Serialize(Main(), SeedEvents.Json);
}

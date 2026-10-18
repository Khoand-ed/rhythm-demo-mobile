using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <summary>
    /// 精英化材料掉落 / The seeded campaign drops Sugar Substitute, the promotion material that until
    /// now only a weekly mission paid.
    ///
    /// 只改没动过的 / The same rule as BattleRecordDrops: only a campaign still exactly as it was
    /// seeded is updated, so one edited on the admin page keeps its edits (and gets the drop there).
    /// </summary>
    public partial class SugarSubstituteDrops : Migration
    {
        private const string Seeded = "{\"chapters\":[{\"chapterId\":\"chapter_1\",\"title\":\"Chapter 1: First Light\",\"stages\":[{\"stageId\":\"main_1_1\",\"code\":\"1-1\",\"chartId\":\"stage_001\",\"firstClearOrundum\":50,\"drops\":[{\"itemId\":2,\"min\":300,\"max\":500,\"chance\":10000,\"rarity\":\"Common\"},{\"itemId\":6,\"min\":1,\"max\":2,\"chance\":6000,\"rarity\":\"Common\"}],\"storyBefore\":\"campaign_1_1_before\",\"storyAfter\":\"campaign_1_1_after\"},{\"stageId\":\"main_1_2\",\"code\":\"1-2\",\"chartId\":\"stage_AIW\",\"firstClearOrundum\":50,\"drops\":[{\"itemId\":6,\"min\":1,\"max\":2,\"chance\":7000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":1,\"chance\":3500,\"rarity\":\"Uncommon\"}],\"storyBefore\":\"campaign_1_2_before\",\"storyAfter\":null},{\"stageId\":\"main_1_3\",\"code\":\"1-3\",\"chartId\":\"stage_AIW_hard\",\"firstClearOrundum\":100,\"drops\":[{\"itemId\":6,\"min\":2,\"max\":3,\"chance\":8000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":2,\"chance\":4000,\"rarity\":\"Uncommon\"},{\"itemId\":14,\"min\":1,\"max\":1,\"chance\":3000,\"rarity\":\"Uncommon\"},{\"itemId\":13,\"min\":1,\"max\":1,\"chance\":1000,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":\"campaign_1_3_after\"}]},{\"chapterId\":\"chapter_2\",\"title\":\"Chapter 2: Resonance\",\"stages\":[{\"stageId\":\"main_2_1\",\"code\":\"2-1\",\"chartId\":\"stage_001\",\"firstClearOrundum\":80,\"drops\":[{\"itemId\":2,\"min\":500,\"max\":800,\"chance\":10000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":1,\"chance\":4000,\"rarity\":\"Uncommon\"},{\"itemId\":14,\"min\":1,\"max\":2,\"chance\":4000,\"rarity\":\"Uncommon\"}],\"storyBefore\":null,\"storyAfter\":null},{\"stageId\":\"main_2_2\",\"code\":\"2-2\",\"chartId\":\"stage_AIW\",\"firstClearOrundum\":80,\"drops\":[{\"itemId\":6,\"min\":2,\"max\":3,\"chance\":8000,\"rarity\":\"Common\"},{\"itemId\":13,\"min\":1,\"max\":1,\"chance\":1500,\"rarity\":\"Rare\"},{\"itemId\":15,\"min\":1,\"max\":1,\"chance\":2000,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":null},{\"stageId\":\"main_2_3\",\"code\":\"2-3\",\"chartId\":\"stage_AIW_hard\",\"firstClearOrundum\":150,\"drops\":[{\"itemId\":11,\"min\":2,\"max\":3,\"chance\":5000,\"rarity\":\"Uncommon\"},{\"itemId\":15,\"min\":1,\"max\":1,\"chance\":3500,\"rarity\":\"Uncommon\"},{\"itemId\":13,\"min\":1,\"max\":2,\"chance\":2000,\"rarity\":\"Rare\"},{\"itemId\":16,\"min\":1,\"max\":1,\"chance\":1500,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":null}]}]}";

        private const string WithSugar = "{\"chapters\":[{\"chapterId\":\"chapter_1\",\"title\":\"Chapter 1: First Light\",\"stages\":[{\"stageId\":\"main_1_1\",\"code\":\"1-1\",\"chartId\":\"stage_001\",\"firstClearOrundum\":50,\"drops\":[{\"itemId\":2,\"min\":300,\"max\":500,\"chance\":10000,\"rarity\":\"Common\"},{\"itemId\":6,\"min\":1,\"max\":2,\"chance\":6000,\"rarity\":\"Common\"}],\"storyBefore\":\"campaign_1_1_before\",\"storyAfter\":\"campaign_1_1_after\"},{\"stageId\":\"main_1_2\",\"code\":\"1-2\",\"chartId\":\"stage_AIW\",\"firstClearOrundum\":50,\"drops\":[{\"itemId\":6,\"min\":1,\"max\":2,\"chance\":7000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":1,\"chance\":3500,\"rarity\":\"Uncommon\"},{\"itemId\":7,\"min\":1,\"max\":1,\"chance\":3000,\"rarity\":\"Uncommon\"}],\"storyBefore\":\"campaign_1_2_before\",\"storyAfter\":null},{\"stageId\":\"main_1_3\",\"code\":\"1-3\",\"chartId\":\"stage_AIW_hard\",\"firstClearOrundum\":100,\"drops\":[{\"itemId\":6,\"min\":2,\"max\":3,\"chance\":8000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":2,\"chance\":4000,\"rarity\":\"Uncommon\"},{\"itemId\":14,\"min\":1,\"max\":1,\"chance\":3000,\"rarity\":\"Uncommon\"},{\"itemId\":13,\"min\":1,\"max\":1,\"chance\":1000,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":\"campaign_1_3_after\"}]},{\"chapterId\":\"chapter_2\",\"title\":\"Chapter 2: Resonance\",\"stages\":[{\"stageId\":\"main_2_1\",\"code\":\"2-1\",\"chartId\":\"stage_001\",\"firstClearOrundum\":80,\"drops\":[{\"itemId\":2,\"min\":500,\"max\":800,\"chance\":10000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":1,\"chance\":4000,\"rarity\":\"Uncommon\"},{\"itemId\":14,\"min\":1,\"max\":2,\"chance\":4000,\"rarity\":\"Uncommon\"},{\"itemId\":7,\"min\":1,\"max\":2,\"chance\":3500,\"rarity\":\"Uncommon\"}],\"storyBefore\":null,\"storyAfter\":null},{\"stageId\":\"main_2_2\",\"code\":\"2-2\",\"chartId\":\"stage_AIW\",\"firstClearOrundum\":80,\"drops\":[{\"itemId\":6,\"min\":2,\"max\":3,\"chance\":8000,\"rarity\":\"Common\"},{\"itemId\":13,\"min\":1,\"max\":1,\"chance\":1500,\"rarity\":\"Rare\"},{\"itemId\":15,\"min\":1,\"max\":1,\"chance\":2000,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":null},{\"stageId\":\"main_2_3\",\"code\":\"2-3\",\"chartId\":\"stage_AIW_hard\",\"firstClearOrundum\":150,\"drops\":[{\"itemId\":11,\"min\":2,\"max\":3,\"chance\":5000,\"rarity\":\"Uncommon\"},{\"itemId\":15,\"min\":1,\"max\":1,\"chance\":3500,\"rarity\":\"Uncommon\"},{\"itemId\":7,\"min\":1,\"max\":2,\"chance\":4000,\"rarity\":\"Uncommon\"},{\"itemId\":13,\"min\":1,\"max\":2,\"chance\":2000,\"rarity\":\"Rare\"},{\"itemId\":16,\"min\":1,\"max\":1,\"chance\":1500,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":null}]}]}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Replace(Seeded, WithSugar));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Replace(WithSugar, Seeded));
        }

        private static string Replace(string from, string to) =>
            "UPDATE campaigns SET definition = " + Literal(to) + "::jsonb, version = version + 1, updated_at = now() " +
            "WHERE key = 'main' AND definition = " + Literal(from) + "::jsonb;";

        private static string Literal(string json) => "'" + json.Replace("'", "''") + "'";
    }
}

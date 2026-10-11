using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventStagesAndStoryPatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "story_patch_revisions");

            migrationBuilder.AddColumn<string>(
                name: "event_id",
                table: "runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "event_stage_clears",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stage_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    clears = table.Column<int>(type: "integer", nullable: false),
                    first_cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_stage_clears", x => new { x.account_id, x.event_id, x.stage_id });
                    table.CheckConstraint("ck_event_stage_clears_clears", "clears > 0");
                    table.ForeignKey(
                        name: "fk_event_stage_clears_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "story_patches",
                columns: table => new
                {
                    story_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    line_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    original = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('story_patch_revisions')"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_story_patches", x => new { x.story_id, x.line_id });
                    table.ForeignKey(
                        name: "fk_story_patches_accounts_updated_by",
                        column: x => x.updated_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.UpdateData(
                table: "game_events",
                keyColumn: "event_id",
                keyValue: "resonant_dawn",
                column: "definition",
                value: "{\"title\":\"Resonant Dawn\",\"subtitle\":\"Festival event\",\"description\":\"AMIYA\\u0027s headhunting banner is open, and Rhodes Island is celebrating. Check in every day, clear the festival charts for Event Tokens, and spend them in the festival shop.\",\"startsAt\":\"2026-10-01T00:00:00+00:00\",\"endsAt\":\"2026-12-31T23:59:59+00:00\",\"isActive\":true,\"currencyItemId\":20,\"bannerId\":\"event_amiya\",\"checkIn\":[{\"day\":1,\"reward\":{\"itemId\":1,\"amount\":100}},{\"day\":2,\"reward\":{\"itemId\":2,\"amount\":2000}},{\"day\":3,\"reward\":{\"itemId\":20,\"amount\":50}},{\"day\":4,\"reward\":{\"itemId\":6,\"amount\":20}},{\"day\":5,\"reward\":{\"itemId\":1,\"amount\":200}},{\"day\":6,\"reward\":{\"itemId\":20,\"amount\":100}},{\"day\":7,\"reward\":{\"itemId\":10,\"amount\":1}}],\"missions\":[{\"missionId\":\"clear3\",\"description\":\"Clear any song 3 times\",\"goal\":\"PlaySong\",\"target\":3,\"points\":10,\"stageId\":null,\"minScore\":null},{\"missionId\":\"hard1\",\"description\":\"Clear the festival chart AIW (Hard) once\",\"goal\":\"ClearStage\",\"target\":1,\"points\":20,\"stageId\":\"stage_AIW_hard\",\"minScore\":null},{\"missionId\":\"score100k\",\"description\":\"Score 100,000 or more on any song\",\"goal\":\"ReachScore\",\"target\":1,\"points\":20,\"stageId\":null,\"minScore\":100000},{\"missionId\":\"pull10\",\"description\":\"Headhunt 10 times\",\"goal\":\"Headhunt\",\"target\":10,\"points\":10,\"stageId\":null,\"minScore\":null},{\"missionId\":\"shop1\",\"description\":\"Buy anything from the Store once\",\"goal\":\"BuyShopItem\",\"target\":1,\"points\":5,\"stageId\":null,\"minScore\":null}],\"milestones\":[{\"rewardId\":\"m20\",\"requiredPoints\":20,\"reward\":{\"itemId\":20,\"amount\":100}},{\"rewardId\":\"m40\",\"requiredPoints\":40,\"reward\":{\"itemId\":1,\"amount\":300}},{\"rewardId\":\"m65\",\"requiredPoints\":65,\"reward\":{\"itemId\":10,\"amount\":2}}],\"shop\":[{\"offerId\":\"permit\",\"sell\":{\"itemId\":10,\"amount\":1},\"price\":150,\"limit\":3},{\"offerId\":\"orundum\",\"sell\":{\"itemId\":1,\"amount\":100},\"price\":40,\"limit\":10},{\"offerId\":\"certificates\",\"sell\":{\"itemId\":8,\"amount\":10},\"price\":30,\"limit\":5},{\"offerId\":\"lmd\",\"sell\":{\"itemId\":2,\"amount\":5000},\"price\":20,\"limit\":null}],\"stages\":[{\"stageId\":\"stage_AIW\",\"currencyPerClear\":30,\"title\":\"RD-1\",\"storyBefore\":\"resonant_dawn_1_before\",\"storyAfter\":\"resonant_dawn_1_after\"},{\"stageId\":\"stage_AIW_hard\",\"currencyPerClear\":60,\"title\":\"RD-2\",\"storyBefore\":\"resonant_dawn_2_before\",\"storyAfter\":\"resonant_dawn_2_after\"}]}");

            migrationBuilder.CreateIndex(
                name: "ix_story_patches_updated_by",
                table: "story_patches",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_stage_clears");

            migrationBuilder.DropTable(
                name: "story_patches");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "runs");

            migrationBuilder.DropSequence(
                name: "story_patch_revisions");

            migrationBuilder.UpdateData(
                table: "game_events",
                keyColumn: "event_id",
                keyValue: "resonant_dawn",
                column: "definition",
                value: "{\"title\":\"Resonant Dawn\",\"subtitle\":\"Festival event\",\"description\":\"AMIYA\\u0027s headhunting banner is open, and Rhodes Island is celebrating. Check in every day, clear the festival charts for Event Tokens, and spend them in the festival shop.\",\"startsAt\":\"2026-10-01T00:00:00+00:00\",\"endsAt\":\"2026-12-31T23:59:59+00:00\",\"isActive\":true,\"currencyItemId\":20,\"bannerId\":\"event_amiya\",\"checkIn\":[{\"day\":1,\"reward\":{\"itemId\":1,\"amount\":100}},{\"day\":2,\"reward\":{\"itemId\":2,\"amount\":2000}},{\"day\":3,\"reward\":{\"itemId\":20,\"amount\":50}},{\"day\":4,\"reward\":{\"itemId\":6,\"amount\":20}},{\"day\":5,\"reward\":{\"itemId\":1,\"amount\":200}},{\"day\":6,\"reward\":{\"itemId\":20,\"amount\":100}},{\"day\":7,\"reward\":{\"itemId\":10,\"amount\":1}}],\"missions\":[{\"missionId\":\"clear3\",\"description\":\"Clear any song 3 times\",\"goal\":\"PlaySong\",\"target\":3,\"points\":10,\"stageId\":null,\"minScore\":null},{\"missionId\":\"hard1\",\"description\":\"Clear the festival chart AIW (Hard) once\",\"goal\":\"ClearStage\",\"target\":1,\"points\":20,\"stageId\":\"stage_AIW_hard\",\"minScore\":null},{\"missionId\":\"score100k\",\"description\":\"Score 100,000 or more on any song\",\"goal\":\"ReachScore\",\"target\":1,\"points\":20,\"stageId\":null,\"minScore\":100000},{\"missionId\":\"pull10\",\"description\":\"Headhunt 10 times\",\"goal\":\"Headhunt\",\"target\":10,\"points\":10,\"stageId\":null,\"minScore\":null},{\"missionId\":\"shop1\",\"description\":\"Buy anything from the Store once\",\"goal\":\"BuyShopItem\",\"target\":1,\"points\":5,\"stageId\":null,\"minScore\":null}],\"milestones\":[{\"rewardId\":\"m20\",\"requiredPoints\":20,\"reward\":{\"itemId\":20,\"amount\":100}},{\"rewardId\":\"m40\",\"requiredPoints\":40,\"reward\":{\"itemId\":1,\"amount\":300}},{\"rewardId\":\"m65\",\"requiredPoints\":65,\"reward\":{\"itemId\":10,\"amount\":2}}],\"shop\":[{\"offerId\":\"permit\",\"sell\":{\"itemId\":10,\"amount\":1},\"price\":150,\"limit\":3},{\"offerId\":\"orundum\",\"sell\":{\"itemId\":1,\"amount\":100},\"price\":40,\"limit\":10},{\"offerId\":\"certificates\",\"sell\":{\"itemId\":8,\"amount\":10},\"price\":30,\"limit\":5},{\"offerId\":\"lmd\",\"sell\":{\"itemId\":2,\"amount\":5000},\"price\":20,\"limit\":null}],\"stages\":[{\"stageId\":\"stage_AIW\",\"currencyPerClear\":30},{\"stageId\":\"stage_AIW_hard\",\"currencyPerClear\":60}]}");
        }
    }
}

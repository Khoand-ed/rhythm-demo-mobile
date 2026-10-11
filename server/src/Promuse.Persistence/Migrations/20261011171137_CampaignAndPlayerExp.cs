using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignAndPlayerExp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "campaign_stage_id",
                table: "runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "campaign_clears",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    clears = table.Column<int>(type: "integer", nullable: false),
                    first_cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_clears", x => new { x.account_id, x.stage_id });
                    table.CheckConstraint("ck_campaign_clears_clears", "clears > 0");
                    table.ForeignKey(
                        name: "fk_campaign_clears_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaigns", x => x.key);
                    table.ForeignKey(
                        name: "fk_campaigns_accounts_updated_by",
                        column: x => x.updated_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "campaigns",
                columns: new[] { "key", "definition", "updated_at", "updated_by", "version" },
                values: new object[] { "main", "{\"chapters\":[{\"chapterId\":\"chapter_1\",\"title\":\"Chapter 1: First Light\",\"stages\":[{\"stageId\":\"main_1_1\",\"code\":\"1-1\",\"chartId\":\"stage_001\",\"firstClearOrundum\":50,\"drops\":[{\"itemId\":2,\"min\":300,\"max\":500,\"chance\":10000,\"rarity\":\"Common\"},{\"itemId\":6,\"min\":1,\"max\":2,\"chance\":6000,\"rarity\":\"Common\"}],\"storyBefore\":\"campaign_1_1_before\",\"storyAfter\":\"campaign_1_1_after\"},{\"stageId\":\"main_1_2\",\"code\":\"1-2\",\"chartId\":\"stage_AIW\",\"firstClearOrundum\":50,\"drops\":[{\"itemId\":6,\"min\":1,\"max\":2,\"chance\":7000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":1,\"chance\":3500,\"rarity\":\"Uncommon\"}],\"storyBefore\":\"campaign_1_2_before\",\"storyAfter\":null},{\"stageId\":\"main_1_3\",\"code\":\"1-3\",\"chartId\":\"stage_AIW_hard\",\"firstClearOrundum\":100,\"drops\":[{\"itemId\":6,\"min\":2,\"max\":3,\"chance\":8000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":2,\"chance\":4000,\"rarity\":\"Uncommon\"},{\"itemId\":13,\"min\":1,\"max\":1,\"chance\":1000,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":\"campaign_1_3_after\"}]},{\"chapterId\":\"chapter_2\",\"title\":\"Chapter 2: Resonance\",\"stages\":[{\"stageId\":\"main_2_1\",\"code\":\"2-1\",\"chartId\":\"stage_001\",\"firstClearOrundum\":80,\"drops\":[{\"itemId\":2,\"min\":500,\"max\":800,\"chance\":10000,\"rarity\":\"Common\"},{\"itemId\":11,\"min\":1,\"max\":1,\"chance\":4000,\"rarity\":\"Uncommon\"}],\"storyBefore\":null,\"storyAfter\":null},{\"stageId\":\"main_2_2\",\"code\":\"2-2\",\"chartId\":\"stage_AIW\",\"firstClearOrundum\":80,\"drops\":[{\"itemId\":6,\"min\":2,\"max\":3,\"chance\":8000,\"rarity\":\"Common\"},{\"itemId\":13,\"min\":1,\"max\":1,\"chance\":1500,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":null},{\"stageId\":\"main_2_3\",\"code\":\"2-3\",\"chartId\":\"stage_AIW_hard\",\"firstClearOrundum\":150,\"drops\":[{\"itemId\":11,\"min\":2,\"max\":3,\"chance\":5000,\"rarity\":\"Uncommon\"},{\"itemId\":13,\"min\":1,\"max\":2,\"chance\":2000,\"rarity\":\"Rare\"}],\"storyBefore\":null,\"storyAfter\":null}]}]}", new DateTimeOffset(new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 });

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_updated_by",
                table: "campaigns",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_clears");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropColumn(
                name: "campaign_stage_id",
                table: "runs");
        }
    }
}

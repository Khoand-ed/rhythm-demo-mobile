using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_check_ins",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_check_ins", x => new { x.account_id, x.event_id, x.day });
                    table.ForeignKey(
                        name: "fk_event_check_ins_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_claims",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_claims", x => new { x.account_id, x.event_id, x.kind, x.key });
                    table.ForeignKey(
                        name: "fk_event_claims_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_counters",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mission_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_counters", x => new { x.account_id, x.event_id, x.mission_id });
                    table.ForeignKey(
                        name: "fk_event_counters_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_purchases",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    offer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bought = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_purchases", x => new { x.account_id, x.event_id, x.offer_id });
                    table.CheckConstraint("ck_event_purchases_bought", "bought > 0");
                    table.ForeignKey(
                        name: "fk_event_purchases_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_events",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_events", x => x.event_id);
                    table.CheckConstraint("ck_game_events_window", "starts_at < ends_at");
                    table.ForeignKey(
                        name: "fk_game_events_accounts_updated_by",
                        column: x => x.updated_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "game_events",
                columns: new[] { "event_id", "created_at", "definition", "ends_at", "is_active", "starts_at", "updated_at", "updated_by", "version" },
                values: new object[] { "resonant_dawn", new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "{\"title\":\"Resonant Dawn\",\"subtitle\":\"Festival event\",\"description\":\"AMIYA\\u0027s headhunting banner is open, and Rhodes Island is celebrating. Check in every day, clear the festival charts for Event Tokens, and spend them in the festival shop.\",\"startsAt\":\"2026-10-01T00:00:00+00:00\",\"endsAt\":\"2026-12-31T23:59:59+00:00\",\"isActive\":true,\"currencyItemId\":20,\"bannerId\":\"event_amiya\",\"checkIn\":[{\"day\":1,\"reward\":{\"itemId\":1,\"amount\":100}},{\"day\":2,\"reward\":{\"itemId\":2,\"amount\":2000}},{\"day\":3,\"reward\":{\"itemId\":20,\"amount\":50}},{\"day\":4,\"reward\":{\"itemId\":6,\"amount\":20}},{\"day\":5,\"reward\":{\"itemId\":1,\"amount\":200}},{\"day\":6,\"reward\":{\"itemId\":20,\"amount\":100}},{\"day\":7,\"reward\":{\"itemId\":10,\"amount\":1}}],\"missions\":[{\"missionId\":\"clear3\",\"description\":\"Clear any song 3 times\",\"goal\":\"PlaySong\",\"target\":3,\"points\":10,\"stageId\":null,\"minScore\":null},{\"missionId\":\"hard1\",\"description\":\"Clear the festival chart AIW (Hard) once\",\"goal\":\"ClearStage\",\"target\":1,\"points\":20,\"stageId\":\"stage_AIW_hard\",\"minScore\":null},{\"missionId\":\"score100k\",\"description\":\"Score 100,000 or more on any song\",\"goal\":\"ReachScore\",\"target\":1,\"points\":20,\"stageId\":null,\"minScore\":100000},{\"missionId\":\"pull10\",\"description\":\"Headhunt 10 times\",\"goal\":\"Headhunt\",\"target\":10,\"points\":10,\"stageId\":null,\"minScore\":null},{\"missionId\":\"shop1\",\"description\":\"Buy anything from the Store once\",\"goal\":\"BuyShopItem\",\"target\":1,\"points\":5,\"stageId\":null,\"minScore\":null}],\"milestones\":[{\"rewardId\":\"m20\",\"requiredPoints\":20,\"reward\":{\"itemId\":20,\"amount\":100}},{\"rewardId\":\"m40\",\"requiredPoints\":40,\"reward\":{\"itemId\":1,\"amount\":300}},{\"rewardId\":\"m65\",\"requiredPoints\":65,\"reward\":{\"itemId\":10,\"amount\":2}}],\"shop\":[{\"offerId\":\"permit\",\"sell\":{\"itemId\":10,\"amount\":1},\"price\":150,\"limit\":3},{\"offerId\":\"orundum\",\"sell\":{\"itemId\":1,\"amount\":100},\"price\":40,\"limit\":10},{\"offerId\":\"certificates\",\"sell\":{\"itemId\":8,\"amount\":10},\"price\":30,\"limit\":5},{\"offerId\":\"lmd\",\"sell\":{\"itemId\":2,\"amount\":5000},\"price\":20,\"limit\":null}],\"stages\":[{\"stageId\":\"stage_AIW\",\"currencyPerClear\":30},{\"stageId\":\"stage_AIW_hard\",\"currencyPerClear\":60}]}", new DateTimeOffset(new DateTime(2026, 12, 31, 23, 59, 59, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, new DateTimeOffset(new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 });

            migrationBuilder.CreateIndex(
                name: "ix_event_check_ins_account_id_event_id_period_key",
                table: "event_check_ins",
                columns: new[] { "account_id", "event_id", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_game_events_is_active_starts_at_ends_at",
                table: "game_events",
                columns: new[] { "is_active", "starts_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_game_events_updated_by",
                table: "game_events",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_check_ins");

            migrationBuilder.DropTable(
                name: "event_claims");

            migrationBuilder.DropTable(
                name: "event_counters");

            migrationBuilder.DropTable(
                name: "event_purchases");

            migrationBuilder.DropTable(
                name: "game_events");
        }
    }
}

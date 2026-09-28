using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MissionBoards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "won",
                table: "runs",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mission_claims",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tab = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    mission_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mission_claims", x => new { x.account_id, x.tab, x.mission_id, x.period_key });
                    table.ForeignKey(
                        name: "fk_mission_claims_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mission_counters",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tab = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    goal = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mission_counters", x => new { x.account_id, x.tab, x.goal, x.period_key });
                    table.ForeignKey(
                        name: "fk_mission_counters_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mission_definitions",
                columns: table => new
                {
                    tab = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    mission_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    goal = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mission_definitions", x => new { x.tab, x.mission_id });
                    table.CheckConstraint("ck_mission_definitions_target_positive", "target > 0 AND points > 0");
                });

            migrationBuilder.CreateTable(
                name: "mission_reward_claims",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tab = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reward_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mission_reward_claims", x => new { x.account_id, x.tab, x.reward_id, x.period_key });
                    table.ForeignKey(
                        name: "fk_mission_reward_claims_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mission_reward_definitions",
                columns: table => new
                {
                    tab = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reward_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    required_points = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mission_reward_definitions", x => new { x.tab, x.reward_id });
                    table.CheckConstraint("ck_mission_rewards_positive", "required_points > 0 AND amount > 0");
                });

            migrationBuilder.InsertData(
                table: "mission_definitions",
                columns: new[] { "mission_id", "tab", "description", "goal", "is_active", "points", "sort_order", "target" },
                values: new object[,]
                {
                    { "daily.buy1", "Daily", "Purchase any item from the Store 1 time(s)", "BuyShopItem", true, 3, 3, 1 },
                    { "daily.play1", "Daily", "Clear any song 1 time(s)", "PlaySong", true, 1, 1, 1 },
                    { "daily.play2", "Daily", "Clear any song 2 time(s)", "PlaySong", true, 2, 2, 2 },
                    { "weekly.buy3", "Weekly", "Purchase any item from the Store 3 time(s)", "BuyShopItem", true, 5, 3, 3 },
                    { "weekly.play10", "Weekly", "Clear any song 10 time(s)", "PlaySong", true, 3, 2, 10 },
                    { "weekly.play5", "Weekly", "Clear any song 5 time(s)", "PlaySong", true, 2, 1, 5 }
                });

            migrationBuilder.InsertData(
                table: "mission_reward_definitions",
                columns: new[] { "reward_id", "tab", "amount", "is_active", "item_id", "required_points", "sort_order" },
                values: new object[,]
                {
                    { "daily.r1", "Daily", 500, true, 2, 1, 1 },
                    { "daily.r2", "Daily", 100, true, 1, 2, 2 },
                    { "daily.r3", "Daily", 3, true, 6, 3, 3 },
                    { "weekly.r1", "Weekly", 2000, true, 2, 3, 1 },
                    { "weekly.r2", "Weekly", 300, true, 1, 6, 2 },
                    { "weekly.r3", "Weekly", 5, true, 7, 10, 3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_claims");

            migrationBuilder.DropTable(
                name: "mission_counters");

            migrationBuilder.DropTable(
                name: "mission_definitions");

            migrationBuilder.DropTable(
                name: "mission_reward_claims");

            migrationBuilder.DropTable(
                name: "mission_reward_definitions");

            migrationBuilder.DropColumn(
                name: "won",
                table: "runs");
        }
    }
}

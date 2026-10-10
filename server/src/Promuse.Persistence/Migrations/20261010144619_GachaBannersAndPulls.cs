using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GachaBannersAndPulls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gacha_banners",
                columns: table => new
                {
                    banner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rate_five_star = table.Column<int>(type: "integer", nullable: false),
                    rate_four_star = table.Column<int>(type: "integer", nullable: false),
                    rate_three_star = table.Column<int>(type: "integer", nullable: false),
                    pity_threshold = table.Column<int>(type: "integer", nullable: false),
                    currency_item_id = table.Column<int>(type: "integer", nullable: false),
                    cost_single = table.Column<int>(type: "integer", nullable: false),
                    cost_multi = table.Column<int>(type: "integer", nullable: false),
                    ticket_item_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gacha_banners", x => x.banner_id);
                    table.CheckConstraint("ck_gacha_banners_positive", "pity_threshold > 0 AND cost_single > 0 AND cost_multi > 0");
                    table.CheckConstraint("ck_gacha_banners_rates", "rate_five_star >= 0 AND rate_four_star >= 0 AND rate_three_star >= 0 AND rate_five_star + rate_four_star + rate_three_star = 10000");
                    table.CheckConstraint("ck_gacha_banners_window", "starts_at IS NULL OR ends_at IS NULL OR starts_at < ends_at");
                });

            migrationBuilder.CreateTable(
                name: "gacha_pity",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    banner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pulls_since_five_star = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gacha_pity", x => new { x.account_id, x.banner_id });
                    table.CheckConstraint("ck_gacha_pity_not_negative", "pulls_since_five_star >= 0");
                    table.ForeignKey(
                        name: "fk_gacha_pity_gacha_banners_banner_id",
                        column: x => x.banner_id,
                        principalTable: "gacha_banners",
                        principalColumn: "banner_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_gacha_pity_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gacha_pool_entries",
                columns: table => new
                {
                    banner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    character_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rarity = table.Column<int>(type: "integer", nullable: false),
                    duplicate_item_id = table.Column<int>(type: "integer", nullable: false),
                    duplicate_amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gacha_pool_entries", x => new { x.banner_id, x.character_id });
                    table.CheckConstraint("ck_gacha_pool_entries_values", "rarity BETWEEN 3 AND 5 AND duplicate_amount > 0");
                    table.ForeignKey(
                        name: "fk_gacha_pool_entries_gacha_banners_banner_id",
                        column: x => x.banner_id,
                        principalTable: "gacha_banners",
                        principalColumn: "banner_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gacha_pulls",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    banner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    character_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rarity = table.Column<int>(type: "integer", nullable: false),
                    is_new = table.Column<bool>(type: "boolean", nullable: false),
                    guaranteed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gacha_pulls", x => x.id);
                    table.ForeignKey(
                        name: "fk_gacha_pulls_gacha_banners_banner_id",
                        column: x => x.banner_id,
                        principalTable: "gacha_banners",
                        principalColumn: "banner_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gacha_pulls_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "gacha_banners",
                columns: new[] { "banner_id", "cost_multi", "cost_single", "currency_item_id", "ends_at", "is_active", "pity_threshold", "rate_five_star", "rate_four_star", "rate_three_star", "sort_order", "starts_at", "ticket_item_id" },
                values: new object[,]
                {
                    { "event_amiya", 1800, 180, 1, new DateTimeOffset(new DateTime(2026, 12, 31, 23, 59, 59, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 30, 200, 1000, 8800, 1, new DateTimeOffset(new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 10 },
                    { "standard", 1800, 180, 1, null, true, 30, 200, 1000, 8800, 2, null, 10 }
                });

            migrationBuilder.InsertData(
                table: "shop_offers",
                columns: new[] { "id", "is_active", "price_amount", "price_item_id", "sell_amount", "sell_item_id", "sort_order" },
                values: new object[,]
                {
                    { new Guid("11111111-0000-0000-0000-000000000007"), true, 40, 8, 1, 10, 7 },
                    { new Guid("11111111-0000-0000-0000-000000000008"), true, 5, 8, 2000, 2, 8 }
                });

            migrationBuilder.InsertData(
                table: "gacha_pool_entries",
                columns: new[] { "banner_id", "character_id", "duplicate_amount", "duplicate_item_id", "rarity" },
                values: new object[,]
                {
                    { "event_amiya", "AMIYA", 20, 8, 5 },
                    { "event_amiya", "ECHO", 5, 8, 4 },
                    { "event_amiya", "NOVA", 5, 8, 4 },
                    { "event_amiya", "PULSE", 1, 8, 3 },
                    { "standard", "AMIYA", 20, 8, 5 },
                    { "standard", "ECHO", 5, 8, 4 },
                    { "standard", "NOVA", 5, 8, 4 },
                    { "standard", "PULSE", 1, 8, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_gacha_banners_sort_order",
                table: "gacha_banners",
                column: "sort_order");

            migrationBuilder.CreateIndex(
                name: "ix_gacha_pity_banner_id",
                table: "gacha_pity",
                column: "banner_id");

            migrationBuilder.CreateIndex(
                name: "ix_gacha_pulls_account_id_id",
                table: "gacha_pulls",
                columns: new[] { "account_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_gacha_pulls_banner_id",
                table: "gacha_pulls",
                column: "banner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gacha_pity");

            migrationBuilder.DropTable(
                name: "gacha_pool_entries");

            migrationBuilder.DropTable(
                name: "gacha_pulls");

            migrationBuilder.DropTable(
                name: "gacha_banners");

            migrationBuilder.DeleteData(
                table: "shop_offers",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "shop_offers",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000008"));
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShopRunsAndStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stamina_spent = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_runs_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sell_item_id = table.Column<int>(type: "integer", nullable: false),
                    sell_amount = table.Column<int>(type: "integer", nullable: false),
                    price_item_id = table.Column<int>(type: "integer", nullable: false),
                    price_amount = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_offers", x => x.id);
                    table.CheckConstraint("ck_shop_offers_amounts_positive", "sell_amount > 0 AND price_amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "stages",
                columns: table => new
                {
                    stage_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stamina_cost = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stages", x => x.stage_id);
                    table.CheckConstraint("ck_stages_cost_not_negative", "stamina_cost >= 0");
                });

            migrationBuilder.CreateTable(
                name: "purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    sell_item_id = table.Column<int>(type: "integer", nullable: false),
                    sell_amount_total = table.Column<int>(type: "integer", nullable: false),
                    price_item_id = table.Column<int>(type: "integer", nullable: false),
                    price_amount_total = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchases", x => x.id);
                    table.CheckConstraint("ck_purchases_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_purchases_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchases_shop_offers_shop_offer_id",
                        column: x => x.shop_offer_id,
                        principalTable: "shop_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "shop_offers",
                columns: new[] { "id", "is_active", "price_amount", "price_item_id", "sell_amount", "sell_item_id", "sort_order" },
                values: new object[,]
                {
                    { new Guid("11111111-0000-0000-0000-000000000001"), true, 240, 6, 1, 10, 1 },
                    { new Guid("11111111-0000-0000-0000-000000000002"), true, 10, 6, 4000, 2, 2 },
                    { new Guid("11111111-0000-0000-0000-000000000003"), true, 10, 6, 1, 11, 3 },
                    { new Guid("11111111-0000-0000-0000-000000000004"), true, 40, 6, 100, 1, 4 },
                    { new Guid("11111111-0000-0000-0000-000000000005"), true, 8, 6, 1, 12, 5 },
                    { new Guid("11111111-0000-0000-0000-000000000006"), true, 12, 6, 1, 13, 6 }
                });

            migrationBuilder.InsertData(
                table: "stages",
                columns: new[] { "stage_id", "is_active", "stamina_cost" },
                values: new object[,]
                {
                    { "stage_001", true, 6 },
                    { "stage_AIW", true, 6 },
                    { "stage_AIW_hard", true, 12 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchases_account_id_created_at",
                table: "purchases",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchases_shop_offer_id",
                table: "purchases",
                column: "shop_offer_id");

            migrationBuilder.CreateIndex(
                name: "ix_runs_account_id_started_at",
                table: "runs",
                columns: new[] { "account_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_offers_sort_order",
                table: "shop_offers",
                column: "sort_order");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchases");

            migrationBuilder.DropTable(
                name: "runs");

            migrationBuilder.DropTable(
                name: "stages");

            migrationBuilder.DropTable(
                name: "shop_offers");
        }
    }
}

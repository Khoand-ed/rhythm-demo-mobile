using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerCharactersItemsAndSquad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_characters",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    elite = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    exp = table.Column<int>(type: "integer", nullable: false),
                    trust = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_characters", x => new { x.account_id, x.character_id });
                    table.ForeignKey(
                        name: "fk_player_characters_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_items",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_items", x => new { x.account_id, x.item_id });
                    table.CheckConstraint("ck_player_items_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_player_items_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "squad_slots",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    character_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_squad_slots", x => new { x.account_id, x.slot });
                    table.CheckConstraint("ck_squad_slots_range", "slot >= 0 AND slot <= 3");
                    table.ForeignKey(
                        name: "fk_squad_slots_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_characters");

            migrationBuilder.DropTable(
                name: "player_items");

            migrationBuilder.DropTable(
                name: "squad_slots");
        }
    }
}

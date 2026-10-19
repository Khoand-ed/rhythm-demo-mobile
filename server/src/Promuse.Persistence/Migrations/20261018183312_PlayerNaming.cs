using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "tag",
                table: "players",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "players",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AddCheckConstraint(
                name: "ck_players_name_and_tag",
                table: "players",
                sql: "(display_name IS NULL) = (tag IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_players_name_and_tag",
                table: "players");

            // 回到用户名当名字 / Back to the old rule, the username as the name: only a signed-up
            // account can be unnamed, so each has one, and no two share it. Tag 0 collides only
            // with a player who chose that exact username as a name and drew 0.
            migrationBuilder.Sql("""
                UPDATE players AS p
                SET display_name = a.username, tag = 0
                FROM accounts AS a
                WHERE a.id = p.account_id AND p.display_name IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "tag",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "players",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldNullable: true);
        }
    }
}

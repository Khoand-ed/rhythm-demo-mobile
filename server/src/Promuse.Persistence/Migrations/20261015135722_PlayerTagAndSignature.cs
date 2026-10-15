using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerTagAndSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "signature",
                table: "players",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tag",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // 给已有的玩家编号 / Players created before this have no tag yet, and every one sharing a
            // name would collide on the index below at 0. Within each name, the n-th player (oldest
            // first) gets start + n * 7919 mod 10000: 7919 shares no factor with 10000, so the first ten
            // thousand of a name all differ, and the start comes from the name so they do not all
            // begin at #0000. New players draw theirs at random in PlayerTags instead.
            migrationBuilder.Sql("""
                UPDATE players AS p
                SET tag = n.tag
                FROM (
                    SELECT account_id,
                           (((hashtext(display_name)::bigint % 10000) + 10000) % 10000
                            + (row_number() OVER (PARTITION BY display_name ORDER BY created_at, account_id) - 1)
                              * 7919) % 10000 AS tag
                    FROM players
                ) AS n
                WHERE p.account_id = n.account_id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_players_display_name_tag",
                table: "players",
                columns: new[] { "display_name", "tag" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_players_tag_range",
                table: "players",
                sql: "tag >= 0 AND tag <= 9999");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_players_display_name_tag",
                table: "players");

            migrationBuilder.DropCheckConstraint(
                name: "ck_players_tag_range",
                table: "players");

            migrationBuilder.DropColumn(
                name: "signature",
                table: "players");

            migrationBuilder.DropColumn(
                name: "tag",
                table: "players");
        }
    }
}

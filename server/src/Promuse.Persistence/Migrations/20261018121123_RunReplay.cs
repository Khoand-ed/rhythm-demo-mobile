using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RunReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "replay",
                table: "run_scores",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "replay",
                table: "run_scores");
        }
    }
}

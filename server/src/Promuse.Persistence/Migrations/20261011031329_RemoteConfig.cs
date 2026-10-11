using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoteConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_admin",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "remote_config_versions",
                columns: table => new
                {
                    version = table.Column<int>(type: "integer", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rolled_back_from = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remote_config_versions", x => x.version);
                    table.CheckConstraint("ck_remote_config_versions_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_remote_config_versions_accounts_created_by",
                        column: x => x.created_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "remote_config_versions",
                columns: new[] { "version", "created_at", "created_by", "document", "note", "rolled_back_from" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "{\"maintenance\":{\"enabled\":false,\"message\":null,\"endsAt\":null},\"minClientVersion\":\"0.0.0\",\"features\":{\"gacha\":true,\"shop\":true,\"ranked\":true,\"leaderboards\":true},\"announcement\":null}", "Initial config", null });

            migrationBuilder.CreateIndex(
                name: "ix_remote_config_versions_created_by",
                table: "remote_config_versions",
                column: "created_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "remote_config_versions");

            migrationBuilder.DropColumn(
                name: "is_admin",
                table: "accounts");
        }
    }
}

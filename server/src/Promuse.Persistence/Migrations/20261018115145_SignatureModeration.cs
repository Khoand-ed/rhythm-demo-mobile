using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignatureModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "signature_locked_until",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "signature_updated_at",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            // 已有的签名 / A line written before this column existed puts its player's last save on
            // the moderators' list: the nearest moment on record to when it was written.
            migrationBuilder.Sql("UPDATE players SET signature_updated_at = updated_at WHERE signature IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "signature_moderations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    removed_signature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mail_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signature_moderations", x => x.id);
                    table.ForeignKey(
                        name: "fk_signature_moderations_accounts_created_by",
                        column: x => x.created_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_signature_moderations_mails_mail_id",
                        column: x => x.mail_id,
                        principalTable: "mails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_signature_moderations_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_players_signature_updated_at",
                table: "players",
                column: "signature_updated_at",
                filter: "signature IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_signature_moderations_account_id_created_at",
                table: "signature_moderations",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_signature_moderations_created_at",
                table: "signature_moderations",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_signature_moderations_created_by",
                table: "signature_moderations",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_signature_moderations_mail_id",
                table: "signature_moderations",
                column: "mail_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signature_moderations");

            migrationBuilder.DropIndex(
                name: "ix_players_signature_updated_at",
                table: "players");

            migrationBuilder.DropColumn(
                name: "signature_locked_until",
                table: "players");

            migrationBuilder.DropColumn(
                name: "signature_updated_at",
                table: "players");
        }
    }
}

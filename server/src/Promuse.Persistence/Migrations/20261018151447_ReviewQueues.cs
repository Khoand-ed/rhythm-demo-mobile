using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewQueues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "run_scores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                table: "run_scores",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision",
                table: "run_scores",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "run_scores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ranked_score",
                table: "run_scores",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "signature_reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    mail_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signature_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_signature_reports_accounts_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_signature_reports_accounts_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_signature_reports_mails_mail_id",
                        column: x => x.mail_id,
                        principalTable: "mails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_signature_reports_players_target_id",
                        column: x => x.target_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_run_scores_decided_by",
                table: "run_scores",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "ix_signature_reports_mail_id",
                table: "signature_reports",
                column: "mail_id");

            migrationBuilder.CreateIndex(
                name: "ix_signature_reports_reporter_id_created_at",
                table: "signature_reports",
                columns: new[] { "reporter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_signature_reports_resolved_by",
                table: "signature_reports",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "ix_signature_reports_target_id_resolved_at",
                table: "signature_reports",
                columns: new[] { "target_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ux_signature_reports_open",
                table: "signature_reports",
                columns: new[] { "reporter_id", "target_id" },
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_run_scores_accounts_decided_by",
                table: "run_scores",
                column: "decided_by",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_run_scores_accounts_decided_by",
                table: "run_scores");

            migrationBuilder.DropTable(
                name: "signature_reports");

            migrationBuilder.DropIndex(
                name: "ix_run_scores_decided_by",
                table: "run_scores");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "run_scores");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "run_scores");

            migrationBuilder.DropColumn(
                name: "decision",
                table: "run_scores");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "run_scores");

            migrationBuilder.DropColumn(
                name: "ranked_score",
                table: "run_scores");
        }
    }
}

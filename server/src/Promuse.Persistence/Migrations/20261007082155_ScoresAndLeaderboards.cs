using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScoresAndLeaderboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "character_id",
                table: "runs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leaderboard_scores",
                columns: table => new
                {
                    stage_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    max_combo = table.Column<int>(type: "integer", nullable: false),
                    character_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaderboard_scores", x => new { x.stage_id, x.period_key, x.account_id });
                    table.ForeignKey(
                        name: "fk_leaderboard_scores_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leaderboard_scores_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "run_scores",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    claimed_won = table.Column<bool>(type: "boolean", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    max_combo = table.Column<int>(type: "integer", nullable: false),
                    perfect = table.Column<int>(type: "integer", nullable: false),
                    great = table.Column<int>(type: "integer", nullable: false),
                    hit = table.Column<int>(type: "integer", nullable: false),
                    miss = table.Column<int>(type: "integer", nullable: false),
                    full_combo = table.Column<bool>(type: "boolean", nullable: false),
                    verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    flags = table.Column<string[]>(type: "text[]", nullable: false),
                    ceiling = table.Column<int>(type: "integer", nullable: true),
                    ruleset_fingerprint = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    trace = table.Column<byte[]>(type: "bytea", nullable: true),
                    trace_frames = table.Column<int>(type: "integer", nullable: false),
                    presses = table.Column<int>(type: "integer", nullable: false),
                    timing_samples = table.Column<int>(type: "integer", nullable: true),
                    timing_mean_ms = table.Column<double>(type: "double precision", nullable: true),
                    timing_std_dev_ms = table.Column<double>(type: "double precision", nullable: true),
                    frame_interval_ms = table.Column<double>(type: "double precision", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_scores", x => x.run_id);
                    table.CheckConstraint("ck_run_scores_not_negative", "score >= 0 AND max_combo >= 0 AND perfect >= 0 AND great >= 0 AND hit >= 0 AND miss >= 0");
                    table.ForeignKey(
                        name: "fk_run_scores_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_scores_account_id",
                table: "leaderboard_scores",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_scores_run_id",
                table: "leaderboard_scores",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_scores_stage_id_period_key_score_achieved_at",
                table: "leaderboard_scores",
                columns: new[] { "stage_id", "period_key", "score", "achieved_at" },
                descending: new[] { false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "ix_run_scores_account_id_submitted_at",
                table: "run_scores",
                columns: new[] { "account_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_run_scores_verdict",
                table: "run_scores",
                column: "verdict");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leaderboard_scores");

            migrationBuilder.DropTable(
                name: "run_scores");

            migrationBuilder.DropColumn(
                name: "character_id",
                table: "runs");
        }
    }
}

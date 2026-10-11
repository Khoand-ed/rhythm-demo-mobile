using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Promuse.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MailNoticesAndSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    sender = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sender_character_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    attachments = table.Column<string>(type: "jsonb", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mails", x => x.id);
                    table.ForeignKey(
                        name: "fk_mails_accounts_sent_by",
                        column: x => x.sent_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mails_accounts_to_account_id",
                        column: x => x.to_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    list_title = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    banner = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notices", x => x.id);
                    table.ForeignKey(
                        name: "fk_notices_accounts_updated_by",
                        column: x => x.updated_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sign_in_claims",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_in_claims", x => new { x.account_id, x.month_key, x.day });
                    table.ForeignKey(
                        name: "fk_sign_in_claims_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sign_in_tables",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rewards = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_in_tables", x => x.key);
                    table.ForeignKey(
                        name: "fk_sign_in_tables_accounts_updated_by",
                        column: x => x.updated_by,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "mail_receipts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_receipts", x => new { x.account_id, x.mail_id });
                    table.ForeignKey(
                        name: "fk_mail_receipts_mails_mail_id",
                        column: x => x.mail_id,
                        principalTable: "mails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mail_receipts_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notice_reads",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notice_reads", x => new { x.account_id, x.notice_id });
                    table.ForeignKey(
                        name: "fk_notice_reads_notices_notice_id",
                        column: x => x.notice_id,
                        principalTable: "notices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notice_reads_players_account_id",
                        column: x => x.account_id,
                        principalTable: "players",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "sign_in_tables",
                columns: new[] { "key", "rewards", "updated_at", "updated_by" },
                values: new object[] { "default", "[{\"itemId\":2,\"amount\":2000},{\"itemId\":11,\"amount\":5},{\"itemId\":6,\"amount\":10},{\"itemId\":12,\"amount\":1},{\"itemId\":13,\"amount\":3},{\"itemId\":2,\"amount\":3000},{\"itemId\":1,\"amount\":100},{\"itemId\":2,\"amount\":2000},{\"itemId\":11,\"amount\":5},{\"itemId\":6,\"amount\":10},{\"itemId\":12,\"amount\":1},{\"itemId\":13,\"amount\":3},{\"itemId\":2,\"amount\":3000},{\"itemId\":1,\"amount\":200},{\"itemId\":2,\"amount\":2000},{\"itemId\":11,\"amount\":5},{\"itemId\":6,\"amount\":10},{\"itemId\":12,\"amount\":1},{\"itemId\":13,\"amount\":3},{\"itemId\":2,\"amount\":3000},{\"itemId\":1,\"amount\":300},{\"itemId\":2,\"amount\":2000},{\"itemId\":11,\"amount\":5},{\"itemId\":6,\"amount\":10},{\"itemId\":12,\"amount\":1},{\"itemId\":13,\"amount\":3},{\"itemId\":2,\"amount\":3000},{\"itemId\":10,\"amount\":1},{\"itemId\":2,\"amount\":2000},{\"itemId\":11,\"amount\":5},{\"itemId\":8,\"amount\":10}]", new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.CreateIndex(
                name: "ix_mail_receipts_mail_id",
                table: "mail_receipts",
                column: "mail_id");

            migrationBuilder.CreateIndex(
                name: "ix_mails_sent_by",
                table: "mails",
                column: "sent_by");

            migrationBuilder.CreateIndex(
                name: "ix_mails_to_account_id_sent_at",
                table: "mails",
                columns: new[] { "to_account_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notice_reads_notice_id",
                table: "notice_reads",
                column: "notice_id");

            migrationBuilder.CreateIndex(
                name: "ix_notices_is_active_starts_at",
                table: "notices",
                columns: new[] { "is_active", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notices_updated_by",
                table: "notices",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_claims_account_id_period_key",
                table: "sign_in_claims",
                columns: new[] { "account_id", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_tables_updated_by",
                table: "sign_in_tables",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_receipts");

            migrationBuilder.DropTable(
                name: "notice_reads");

            migrationBuilder.DropTable(
                name: "sign_in_claims");

            migrationBuilder.DropTable(
                name: "sign_in_tables");

            migrationBuilder.DropTable(
                name: "mails");

            migrationBuilder.DropTable(
                name: "notices");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramAccountLinking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_telegram_account_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_telegram_account_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_telegram_account_links_auth_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "auth_tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_auth_telegram_account_links_auth_users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "auth_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_telegram_link_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_telegram_link_audits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "auth_telegram_link_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedByTelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    TelegramUpdateId = table.Column<long>(type: "bigint", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_telegram_link_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_telegram_link_tokens_auth_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "auth_tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_auth_telegram_link_tokens_auth_users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "auth_users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_account_links_ActorId_TenantId",
                table: "auth_telegram_account_links",
                columns: new[] { "ActorId", "TenantId" },
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_account_links_TelegramUserId",
                table: "auth_telegram_account_links",
                column: "TelegramUserId",
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_account_links_TenantId",
                table: "auth_telegram_account_links",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_link_audits_ActorId_OccurredAtUtc",
                table: "auth_telegram_link_audits",
                columns: new[] { "ActorId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_link_audits_TenantId_OccurredAtUtc",
                table: "auth_telegram_link_audits",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_link_tokens_ActorId_Status_ExpiresAtUtc",
                table: "auth_telegram_link_tokens",
                columns: new[] { "ActorId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_link_tokens_TelegramUpdateId",
                table: "auth_telegram_link_tokens",
                column: "TelegramUpdateId",
                unique: true,
                filter: "\"TelegramUpdateId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_link_tokens_TelegramUserId_Status_ExpiresAtUtc",
                table: "auth_telegram_link_tokens",
                columns: new[] { "TelegramUserId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_link_tokens_TenantId",
                table: "auth_telegram_link_tokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_link_tokens_TokenHash",
                table: "auth_telegram_link_tokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_telegram_account_links");

            migrationBuilder.DropTable(
                name: "auth_telegram_link_audits");

            migrationBuilder.DropTable(
                name: "auth_telegram_link_tokens");
        }
    }
}

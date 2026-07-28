using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramAiAssistantAdapter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_telegram_conversation_bindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    MessageThreadId = table.Column<int>(type: "integer", nullable: true),
                    MessageThreadKey = table.Column<int>(type: "integer", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastMessageAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_telegram_conversation_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_telegram_conversation_bindings_auth_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "auth_tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_auth_telegram_conversation_bindings_auth_users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "auth_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_telegram_processed_updates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TelegramUpdateId = table.Column<long>(type: "bigint", nullable: false),
                    CallbackQueryId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    MessageThreadId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_telegram_processed_updates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_telegram_processed_updates_auth_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "auth_tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_auth_telegram_processed_updates_auth_users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "auth_users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_conversation_bindings_ActorId_TenantId_Teleg~1",
                table: "auth_telegram_conversation_bindings",
                columns: new[] { "ActorId", "TenantId", "TelegramChatId", "MessageThreadKey", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_conversation_bindings_ActorId_TenantId_Telegr~",
                table: "auth_telegram_conversation_bindings",
                columns: new[] { "ActorId", "TenantId", "TelegramChatId", "MessageThreadKey" },
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_conversation_bindings_TenantId",
                table: "auth_telegram_conversation_bindings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_processed_updates_ActorId_TenantId_TelegramCh~",
                table: "auth_telegram_processed_updates",
                columns: new[] { "ActorId", "TenantId", "TelegramChatId", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_processed_updates_ExpiresAtUtc",
                table: "auth_telegram_processed_updates",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_processed_updates_IdempotencyKey",
                table: "auth_telegram_processed_updates",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_processed_updates_TenantId",
                table: "auth_telegram_processed_updates",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_telegram_conversation_bindings");

            migrationBuilder.DropTable(
                name: "auth_telegram_processed_updates");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature165InAppMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO auth_permissions ("Id", "Code") VALUES
                    ('2b63f0d5-c92a-4d1d-979a-351c427b43fe', 'admin.in-app-messages.send'),
                    ('0e3a69a5-58ca-402d-b8f6-e23386688365', 'admin.in-app-messages.support')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO auth_role_permissions ("RoleId", "PermissionId")
                SELECT r."Id", p."Id"
                  FROM auth_roles r CROSS JOIN auth_permissions p
                 WHERE r."NormalizedName" = 'SUPERADMIN'
                   AND p."Code" IN ('admin.in-app-messages.send', 'admin.in-app-messages.support')
                ON CONFLICT DO NOTHING;
                """);
            migrationBuilder.CreateTable(
                name: "auth_in_app_conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RepliesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    LatestActivityUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PendingSinceUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_in_app_conversations", x => x.Id);
                    table.UniqueConstraint("AK_auth_in_app_conversations_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_auth_in_app_conversations_auth_user_tenants_RecipientUserId~",
                        columns: x => new { x.RecipientUserId, x.TenantId },
                        principalTable: "auth_user_tenants",
                        principalColumns: new[] { "UserId", "TenantId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_in_app_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    FromStaff = table.Column<bool>(type: "boolean", nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_in_app_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_in_app_messages_auth_in_app_conversations_Conversation~",
                        columns: x => new { x.ConversationId, x.TenantId },
                        principalTable: "auth_in_app_conversations",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_in_app_conversations_RecipientUserId_TenantId",
                table: "auth_in_app_conversations",
                columns: new[] { "RecipientUserId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_in_app_conversations_TenantId_LatestActivityUtc_Id",
                table: "auth_in_app_conversations",
                columns: new[] { "TenantId", "LatestActivityUtc", "Id" },
                descending: new[] { false, true, true },
                filter: "\"RepliesEnabled\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_auth_in_app_conversations_TenantId_PendingSinceUtc_Id",
                table: "auth_in_app_conversations",
                columns: new[] { "TenantId", "PendingSinceUtc", "Id" },
                filter: "\"PendingSinceUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_auth_in_app_conversations_TenantId_RecipientUserId_LatestAc~",
                table: "auth_in_app_conversations",
                columns: new[] { "TenantId", "RecipientUserId", "LatestActivityUtc", "Id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_auth_in_app_messages_ConversationId",
                table: "auth_in_app_messages",
                column: "ConversationId",
                filter: "\"FromStaff\" = TRUE AND \"ReadAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_auth_in_app_messages_ConversationId_Sequence",
                table: "auth_in_app_messages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_in_app_messages_ConversationId_TenantId",
                table: "auth_in_app_messages",
                columns: new[] { "ConversationId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_auth_in_app_messages_idempotency",
                table: "auth_in_app_messages",
                columns: new[] { "TenantId", "AuthorUserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM auth_role_permissions
                 WHERE "PermissionId" IN (
                    SELECT "Id" FROM auth_permissions
                     WHERE "Code" IN ('admin.in-app-messages.send', 'admin.in-app-messages.support'));
                DELETE FROM auth_permissions
                 WHERE "Code" IN ('admin.in-app-messages.send', 'admin.in-app-messages.support');
                """);
            migrationBuilder.DropTable(
                name: "auth_in_app_messages");

            migrationBuilder.DropTable(
                name: "auth_in_app_conversations");
        }
    }
}

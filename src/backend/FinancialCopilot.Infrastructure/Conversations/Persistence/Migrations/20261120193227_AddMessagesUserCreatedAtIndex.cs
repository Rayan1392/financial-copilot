using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Conversations.Persistence.Migrations
{
    /// <summary>
    /// Feature 164: index for the admin conversation logs (questions newest first over a date range; measured in
    /// specs/164-admin-ai-conversation-logs/evidence/index-decision.md). Partial on User rows so assistant inserts do
    /// not maintain it. Built CONCURRENTLY (outside a transaction) so applying it on a live database does not block
    /// chat writes, and IF NOT EXISTS so a manual hotfix of the same index does not fail the migration.
    /// </summary>
    public partial class AddMessagesUserCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Messages_User_CreatedAt_Id" ON "Messages" ("CreatedAt", "Id") INCLUDE ("ConversationId") WHERE "Role" = 'User';""",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_Messages_User_CreatedAt_Id";""",
                suppressTransaction: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Conversations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssistantPayloadJson",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Conversations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """UPDATE "Conversations" SET "Title" = 'New conversation' WHERE "Title" = '';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssistantPayloadJson",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Conversations");
        }
    }
}

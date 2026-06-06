using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(BillingDbContext))]
    [Migration("20260606090000_AddAiProviderUsageLedgerFields")]
    public partial class AddAiProviderUsageLedgerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompletionTokens",
                table: "billing_usage_ledger_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                table: "billing_usage_ledger_entries",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelName",
                table: "billing_usage_ledger_entries",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptTokens",
                table: "billing_usage_ledger_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "billing_usage_ledger_entries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTokens",
                table: "billing_usage_ledger_entries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionTokens",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "ModelName",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "PromptTokens",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "TotalTokens",
                table: "billing_usage_ledger_entries");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotSubscriptionCreditAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AllowanceCredits",
                table: "billing_subscription_credit_periods",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE billing_subscription_credit_periods AS period
                SET "AllowanceCredits" = COALESCE(grant_entry."CreditsCharged", period."GrantedCredits")
                FROM billing_usage_ledger_entries AS grant_entry
                WHERE grant_entry."Id" = period."GrantLedgerEntryId";
                UPDATE billing_subscription_credit_periods
                SET "AllowanceCredits" = "GrantedCredits"
                WHERE "AllowanceCredits" IS NULL;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "AllowanceCredits",
                table: "billing_subscription_credit_periods",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowanceCredits",
                table: "billing_subscription_credit_periods");
        }
    }
}

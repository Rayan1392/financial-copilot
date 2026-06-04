using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateFreePlanIncludedCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Free",
                column: "IncludedCredits",
                value: 10000m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Free",
                column: "IncludedCredits",
                value: 10m);
        }
    }
}

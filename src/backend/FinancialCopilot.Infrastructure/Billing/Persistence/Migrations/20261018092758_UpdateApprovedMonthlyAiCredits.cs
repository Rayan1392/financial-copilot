using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateApprovedMonthlyAiCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Free",
                column: "IncludedCredits",
                value: 25m);

            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Plus",
                column: "IncludedCredits",
                value: 260m);

            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Pro",
                column: "IncludedCredits",
                value: 2600m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Free",
                column: "IncludedCredits",
                value: 10000m);

            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Plus",
                column: "IncludedCredits",
                value: 300m);

            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Pro",
                column: "IncludedCredits",
                value: 100m);
        }
    }
}

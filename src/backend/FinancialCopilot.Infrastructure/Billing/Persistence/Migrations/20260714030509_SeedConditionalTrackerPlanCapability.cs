using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedConditionalTrackerPlanCapability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "billing_plan_capabilities",
                columns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion", "IsEnabled", "Limit" },
                values: new object[,]
                {
                    { "Tracker.Rules", "Free", "v1", true, 3m },
                    { "Tracker.Rules", "Plus", "v1", true, 50m },
                    { "Tracker.Rules", "Premium", "v1", true, 100m },
                    { "Tracker.Rules", "Pro", "v1", true, 20m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Tracker.Rules", "Free", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Tracker.Rules", "Plus", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Tracker.Rules", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Tracker.Rules", "Pro", "v1" });
        }
    }
}

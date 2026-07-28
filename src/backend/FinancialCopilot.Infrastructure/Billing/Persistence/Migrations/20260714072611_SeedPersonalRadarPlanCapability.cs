using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedPersonalRadarPlanCapability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "billing_plan_capabilities",
                columns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion", "IsEnabled", "Limit" },
                values: new object[,]
                {
                    { "Radar.Symbols", "Free", "v1", true, 5m },
                    { "Radar.Symbols", "Plus", "v1", true, 50m },
                    { "Radar.Symbols", "Premium", "v1", true, 100m },
                    { "Radar.Symbols", "Pro", "v1", true, 20m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Radar.Symbols", "Free", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Radar.Symbols", "Plus", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Radar.Symbols", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Radar.Symbols", "Pro", "v1" });
        }
    }
}

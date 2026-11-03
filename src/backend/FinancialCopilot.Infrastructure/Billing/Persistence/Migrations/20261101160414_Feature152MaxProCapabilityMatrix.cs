using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature152MaxProCapabilityMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "billing_plan_capabilities",
                columns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion", "IsEnabled", "Limit" },
                values: new object[,]
                {
                    { "AiQuery.CodalAnalysis", "Max", "v1", true, 30m },
                    { "AiQuery.FinancialComparison", "Max", "v1", true, null },
                    { "AiQuery.PersonalDigest", "Max", "v1", true, 30m },
                    { "AiQuery.PortfolioAnalysis", "Max", "v1", true, 10m },
                    { "AiQuery.Scanner", "Max", "v1", true, null },
                    { "AiQuery.StockAnalysis", "Max", "v1", true, null },
                    { "MarketPulse.Read", "Max", "v1", true, null },
                    { "Notifications.Telegram", "Max", "v1", true, null },
                    { "Portfolio.Records", "Max", "v1", true, 10m },
                    { "Radar.Symbols", "Max", "v1", true, 20m },
                    { "Reports.Read", "Max", "v1", true, null },
                    { "Tracker.Rules", "Max", "v1", true, 20m },
                    { "Watchlist.Symbols", "Max", "v1", true, 20m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.CodalAnalysis", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.FinancialComparison", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.PersonalDigest", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.PortfolioAnalysis", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.Scanner", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.StockAnalysis", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "MarketPulse.Read", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Notifications.Telegram", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Portfolio.Records", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Radar.Symbols", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Reports.Read", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Tracker.Rules", "Max", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Watchlist.Symbols", "Max", "v1" });
        }
    }
}

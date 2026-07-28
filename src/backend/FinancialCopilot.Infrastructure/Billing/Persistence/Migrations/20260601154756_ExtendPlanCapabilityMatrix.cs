using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendPlanCapabilityMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "billing_plan_capabilities",
                columns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion", "IsEnabled", "Limit" },
                values: new object[,]
                {
                    { "AiQuery.StockAnalysis", "Free", "v1", true, 5m },
                    { "Reports.Read", "Free", "v1", true, 100m },
                    { "Watchlist.Symbols", "Free", "v1", true, 5m },
                    { "AiQuery.PortfolioAnalysis", "Plus", "v1", true, 50m },
                    { "AiQuery.StockAnalysis", "Plus", "v1", true, null },
                    { "Portfolio.Records", "Plus", "v1", true, 50m },
                    { "Reports.Read", "Plus", "v1", true, null },
                    { "Watchlist.Symbols", "Plus", "v1", true, 50m },
                    { "AiQuery.PortfolioAnalysis", "Premium", "v1", true, null },
                    { "AiQuery.StockAnalysis", "Premium", "v1", true, null },
                    { "Portfolio.Records", "Premium", "v1", true, 100m },
                    { "Reports.Read", "Premium", "v1", true, null },
                    { "Watchlist.Symbols", "Premium", "v1", true, 100m },
                    { "AiQuery.PortfolioAnalysis", "Pro", "v1", true, 10m },
                    { "AiQuery.StockAnalysis", "Pro", "v1", true, null },
                    { "Portfolio.Records", "Pro", "v1", true, 10m },
                    { "Reports.Read", "Pro", "v1", true, null },
                    { "Watchlist.Symbols", "Pro", "v1", true, 20m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.StockAnalysis", "Free", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Reports.Read", "Free", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Watchlist.Symbols", "Free", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.PortfolioAnalysis", "Plus", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.StockAnalysis", "Plus", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Portfolio.Records", "Plus", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Reports.Read", "Plus", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Watchlist.Symbols", "Plus", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.PortfolioAnalysis", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.StockAnalysis", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Portfolio.Records", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Reports.Read", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Watchlist.Symbols", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.PortfolioAnalysis", "Pro", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.StockAnalysis", "Pro", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Portfolio.Records", "Pro", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Reports.Read", "Pro", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Watchlist.Symbols", "Pro", "v1" });
        }
    }
}

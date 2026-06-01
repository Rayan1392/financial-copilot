using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_plan_capabilities",
                columns: table => new
                {
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CapabilityCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Limit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_plan_capabilities", x => new { x.PlanCode, x.CapabilityCode, x.PolicyVersion });
                    table.ForeignKey(
                        name: "FK_billing_plan_capabilities_billing_subscription_plans_PlanCo~",
                        column: x => x.PlanCode,
                        principalTable: "billing_subscription_plans",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "billing_subscription_plans",
                columns: new[] { "Code", "IncludedCredits", "Name", "PricingPolicyVersion" },
                values: new object[,]
                {
                    { "Free", 10m, "Free", "v1" },
                    { "Plus", 300m, "Plus", "v1" },
                    { "Premium", 1000m, "Premium", "v1" },
                    { "Pro", 100m, "Pro", "v1" }
                });

            migrationBuilder.InsertData(
                table: "billing_plan_capabilities",
                columns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion", "IsEnabled", "Limit" },
                values: new object[,]
                {
                    { "AiQuery.FinancialComparison", "Free", "v1", true, 5m },
                    { "AiQuery.Scanner", "Free", "v1", true, 10m },
                    { "AiQuery.CodalAnalysis", "Plus", "v1", true, null },
                    { "AiQuery.DeepResearch", "Plus", "v1", true, 10m },
                    { "AiQuery.FinancialComparison", "Plus", "v1", true, null },
                    { "AiQuery.Scanner", "Plus", "v1", true, null },
                    { "AiQuery.CodalAnalysis", "Premium", "v1", true, null },
                    { "AiQuery.DeepResearch", "Premium", "v1", true, null },
                    { "AiQuery.FinancialComparison", "Premium", "v1", true, null },
                    { "AiQuery.Scanner", "Premium", "v1", true, null },
                    { "AiQuery.CodalAnalysis", "Pro", "v1", true, 30m },
                    { "AiQuery.FinancialComparison", "Pro", "v1", true, null },
                    { "AiQuery.Scanner", "Pro", "v1", true, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_plan_capabilities");

            migrationBuilder.DeleteData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Free");

            migrationBuilder.DeleteData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Plus");

            migrationBuilder.DeleteData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Premium");

            migrationBuilder.DeleteData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Pro");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestrictCanonicalSubscriptionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Checkout intents are immutable financial evidence; never rewrite or cascade-delete them.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM billing_checkout_intents
                               WHERE "ProductCode" = 'TG-PREMIUM-30D' OR "PlanCode" = 'Premium') THEN
                        RAISE EXCEPTION 'Cannot retire the Premium plan: billing_checkout_intents still reference it. Resolve them manually before migrating.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                "UPDATE billing_customer_accounts SET \"SubscriptionPlanCode\" = 'Pro' WHERE \"SubscriptionPlanCode\" = 'Premium';");

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.CodalAnalysis", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.DeepResearch", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.FinancialComparison", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.PersonalDigest", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.PortfolioAnalysis", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.Scanner", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "AiQuery.StockAnalysis", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "MarketPulse.Read", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Notifications.Telegram", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Portfolio.Records", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Radar.Symbols", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Reports.Read", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Tracker.Rules", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_plan_capabilities",
                keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
                keyValues: new object[] { "Watchlist.Symbols", "Premium", "v1" });

            migrationBuilder.DeleteData(
                table: "billing_purchase_products",
                keyColumn: "Code",
                keyValue: "TG-PREMIUM-30D");

            migrationBuilder.DeleteData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Premium");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "billing_subscription_plans",
                columns: new[] { "Code", "IncludedCredits", "Name", "PricingPolicyVersion" },
                values: new object[] { "Premium", 1000m, "Premium", "v1" });

            migrationBuilder.InsertData(
                table: "billing_plan_capabilities",
                columns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion", "IsEnabled", "Limit" },
                values: new object[,]
                {
                    { "AiQuery.CodalAnalysis", "Premium", "v1", true, null },
                    { "AiQuery.DeepResearch", "Premium", "v1", true, null },
                    { "AiQuery.FinancialComparison", "Premium", "v1", true, null },
                    { "AiQuery.PersonalDigest", "Premium", "v1", true, null },
                    { "AiQuery.PortfolioAnalysis", "Premium", "v1", true, null },
                    { "AiQuery.Scanner", "Premium", "v1", true, null },
                    { "AiQuery.StockAnalysis", "Premium", "v1", true, null },
                    { "MarketPulse.Read", "Premium", "v1", true, null },
                    { "Notifications.Telegram", "Premium", "v1", true, null },
                    { "Portfolio.Records", "Premium", "v1", true, 100m },
                    { "Radar.Symbols", "Premium", "v1", true, 100m },
                    { "Reports.Read", "Premium", "v1", true, null },
                    { "Tracker.Rules", "Premium", "v1", true, 100m },
                    { "Watchlist.Symbols", "Premium", "v1", true, 100m }
                });

            migrationBuilder.InsertData(
                table: "billing_purchase_products",
                columns: new[] { "Code", "Amount", "Channel", "CreatedAtUtc", "Credits", "Currency", "DisplayName", "DurationDays", "IsActive", "PlanCode", "ProductType", "SortOrder", "Version" },
                values: new object[] { "TG-PREMIUM-30D", 3900000m, "Telegram", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0m, "IRR", "Telegram Premium 30 days", 30, true, "Premium", "Subscription", 50, "v1" });
        }
    }
}

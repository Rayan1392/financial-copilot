using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature152UnlimitedQuotaMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuotaMode",
                table: "billing_usage_reservations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Finite");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceSubscriptionExpiresAtUtc",
                table: "billing_usage_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceSubscriptionId",
                table: "billing_usage_reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotaMode",
                table: "billing_usage_ledger_entries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Finite");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceSubscriptionExpiresAtUtc",
                table: "billing_usage_ledger_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceSubscriptionId",
                table: "billing_usage_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotaMode",
                table: "billing_subscription_plans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Finite");

            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Free",
                column: "QuotaMode",
                value: "Finite");

            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Plus",
                column: "QuotaMode",
                value: "Finite");

            migrationBuilder.UpdateData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Pro",
                column: "QuotaMode",
                value: "Finite");

            migrationBuilder.InsertData(
                table: "billing_subscription_plans",
                columns: new[] { "Code", "IncludedCredits", "Name", "PricingPolicyVersion", "QuotaMode" },
                values: new object[] { "Max", 0m, "Max", "v1", "Unlimited" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_reservations_SourceSubscriptionId",
                table: "billing_usage_reservations",
                column: "SourceSubscriptionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_billing_usage_reservations_quota_mode",
                table: "billing_usage_reservations",
                sql: "(\"QuotaMode\" = 'Finite' AND \"ReservedCredits\" > 0 AND \"SourceSubscriptionId\" IS NULL) OR (\"QuotaMode\" = 'Unlimited' AND \"ReservedCredits\" = 0 AND \"SourceSubscriptionId\" IS NOT NULL AND \"SourceSubscriptionExpiresAtUtc\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_ledger_entries_SourceSubscriptionId",
                table: "billing_usage_ledger_entries",
                column: "SourceSubscriptionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_billing_usage_ledger_entries_quota_mode",
                table: "billing_usage_ledger_entries",
                sql: "\"QuotaMode\" = 'Finite' OR (\"QuotaMode\" = 'Unlimited' AND \"CreditsCharged\" = 0 AND \"SourceSubscriptionId\" IS NOT NULL AND \"SourceSubscriptionExpiresAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_billing_subscription_plans_quota_mode",
                table: "billing_subscription_plans",
                sql: "(\"QuotaMode\" = 'Finite' AND \"IncludedCredits\" >= 0) OR (\"QuotaMode\" = 'Unlimited' AND \"IncludedCredits\" = 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_billing_usage_ledger_entries_billing_external_plan_subscrip~",
                table: "billing_usage_ledger_entries",
                column: "SourceSubscriptionId",
                principalTable: "billing_external_plan_subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_billing_usage_reservations_billing_external_plan_subscripti~",
                table: "billing_usage_reservations",
                column: "SourceSubscriptionId",
                principalTable: "billing_external_plan_subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_billing_usage_ledger_entries_billing_external_plan_subscrip~",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_billing_usage_reservations_billing_external_plan_subscripti~",
                table: "billing_usage_reservations");

            migrationBuilder.DropIndex(
                name: "IX_billing_usage_reservations_SourceSubscriptionId",
                table: "billing_usage_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_billing_usage_reservations_quota_mode",
                table: "billing_usage_reservations");

            migrationBuilder.DropIndex(
                name: "IX_billing_usage_ledger_entries_SourceSubscriptionId",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_billing_usage_ledger_entries_quota_mode",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_billing_subscription_plans_quota_mode",
                table: "billing_subscription_plans");

            migrationBuilder.DeleteData(
                table: "billing_subscription_plans",
                keyColumn: "Code",
                keyValue: "Max");

            migrationBuilder.DropColumn(
                name: "QuotaMode",
                table: "billing_usage_reservations");

            migrationBuilder.DropColumn(
                name: "SourceSubscriptionExpiresAtUtc",
                table: "billing_usage_reservations");

            migrationBuilder.DropColumn(
                name: "SourceSubscriptionId",
                table: "billing_usage_reservations");

            migrationBuilder.DropColumn(
                name: "QuotaMode",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "SourceSubscriptionExpiresAtUtc",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "SourceSubscriptionId",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "QuotaMode",
                table: "billing_subscription_plans");
        }
    }
}

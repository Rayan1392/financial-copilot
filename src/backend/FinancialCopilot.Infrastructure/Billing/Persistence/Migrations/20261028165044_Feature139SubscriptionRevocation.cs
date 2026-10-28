using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature139SubscriptionRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevocationObservedAtUtc",
                table: "billing_paid_expiry_cooldowns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRevoked",
                table: "billing_external_plan_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAtUtc",
                table: "billing_external_plan_subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevocationObservedAtUtc",
                table: "billing_paid_expiry_cooldowns");

            migrationBuilder.DropColumn(
                name: "IsRevoked",
                table: "billing_external_plan_subscriptions");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                table: "billing_external_plan_subscriptions");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature139OfficialPlanSubscriptionsAndCooldown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_billing_subscription_credit_periods_CustomerAccountId_Activ~",
                table: "billing_subscription_credit_periods");

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionIdentity",
                table: "billing_subscription_credit_periods",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE billing_subscription_credit_periods SET \"SubscriptionIdentity\" = " +
                "'legacy:' || \"PlanCode\" || ':' || \"ActivationRevision\"::text || ':' || " +
                "(extract(epoch FROM \"AnchorAtUtc\") * 1000000)::bigint::text;");

            migrationBuilder.AddColumn<bool>(
                name: "ExternalSubscriptionManaged",
                table: "billing_customer_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "billing_external_plan_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlusTransferApplied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_external_plan_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_external_plan_subscriptions_billing_customer_accoun~",
                        column: x => x.CustomerAccountId,
                        principalTable: "billing_customer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_paid_expiry_cooldowns",
                columns: table => new
                {
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastPaidExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CooldownUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_paid_expiry_cooldowns", x => x.CustomerAccountId);
                    table.ForeignKey(
                        name: "FK_billing_paid_expiry_cooldowns_billing_customer_accounts_Cus~",
                        column: x => x.CustomerAccountId,
                        principalTable: "billing_customer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_usage_credit_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionCreditPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditsApplied = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_usage_credit_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_usage_credit_allocations_billing_subscription_credi~",
                        column: x => x.SubscriptionCreditPeriodId,
                        principalTable: "billing_subscription_credit_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_usage_credit_allocations_billing_usage_ledger_entri~",
                        column: x => x.UsageLedgerEntryId,
                        principalTable: "billing_usage_ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_subscription_credit_periods_CustomerAccountId_Subsc~",
                table: "billing_subscription_credit_periods",
                columns: new[] { "CustomerAccountId", "SubscriptionIdentity", "PeriodNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_external_plan_subscriptions_CustomerAccountId_Expir~",
                table: "billing_external_plan_subscriptions",
                columns: new[] { "CustomerAccountId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_external_plan_subscriptions_CustomerAccountId_Ident~",
                table: "billing_external_plan_subscriptions",
                columns: new[] { "CustomerAccountId", "IdentityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_credit_allocations_GrantLedgerEntryId",
                table: "billing_usage_credit_allocations",
                column: "GrantLedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_credit_allocations_SubscriptionCreditPeriodId",
                table: "billing_usage_credit_allocations",
                column: "SubscriptionCreditPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_credit_allocations_UsageLedgerEntryId_Subscri~",
                table: "billing_usage_credit_allocations",
                columns: new[] { "UsageLedgerEntryId", "SubscriptionCreditPeriodId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_external_plan_subscriptions");

            migrationBuilder.DropTable(
                name: "billing_paid_expiry_cooldowns");

            migrationBuilder.DropTable(
                name: "billing_usage_credit_allocations");

            migrationBuilder.DropIndex(
                name: "IX_billing_subscription_credit_periods_CustomerAccountId_Subsc~",
                table: "billing_subscription_credit_periods");

            migrationBuilder.DropColumn(
                name: "SubscriptionIdentity",
                table: "billing_subscription_credit_periods");

            migrationBuilder.DropColumn(
                name: "ExternalSubscriptionManaged",
                table: "billing_customer_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_billing_subscription_credit_periods_CustomerAccountId_Activ~",
                table: "billing_subscription_credit_periods",
                columns: new[] { "CustomerAccountId", "ActivationRevision", "PeriodNumber" },
                unique: true);
        }
    }
}

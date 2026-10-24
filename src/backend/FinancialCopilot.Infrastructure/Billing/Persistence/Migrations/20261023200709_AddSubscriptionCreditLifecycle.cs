using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionCreditLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SubscriptionCreditsApplied",
                table: "billing_usage_ledger_entries",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_subscription_credit_periods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AnchorAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivationRevision = table.Column<long>(type: "bigint", nullable: false),
                    PeriodNumber = table.Column<long>(type: "bigint", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrantedCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    GrantLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_subscription_credit_periods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_subscription_credit_periods_billing_customer_accoun~",
                        column: x => x.CustomerAccountId,
                        principalTable: "billing_customer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_subscription_credit_periods_CustomerAccountId_Activ~",
                table: "billing_subscription_credit_periods",
                columns: new[] { "CustomerAccountId", "ActivationRevision", "PeriodNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_subscription_credit_periods_IdempotencyKey",
                table: "billing_subscription_credit_periods",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_subscription_credit_periods_Status_ExpiresAtUtc",
                table: "billing_subscription_credit_periods",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_subscription_credit_periods");

            migrationBuilder.DropColumn(
                name: "SubscriptionCreditsApplied",
                table: "billing_usage_ledger_entries");
        }
    }
}

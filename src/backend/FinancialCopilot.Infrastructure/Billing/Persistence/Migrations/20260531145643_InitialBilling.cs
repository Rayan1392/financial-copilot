using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_financial_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_financial_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "billing_invoice_accounts",
                columns: table => new
                {
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    BillingEmail = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    SettlementTerms = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_invoice_accounts", x => x.CustomerAccountId);
                });

            migrationBuilder.CreateTable(
                name: "billing_outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "billing_subscription_plans",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IncludedCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PricingPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_subscription_plans", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "billing_usage_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntryType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreditsCharged = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PricingPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    AuditDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RelatedEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletionStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_usage_ledger_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "billing_usage_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OperationCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReservedCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CommittedCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FinalizationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_usage_reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "billing_wallet_projections",
                columns: table => new
                {
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReservedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_wallet_projections", x => x.CustomerAccountId);
                });

            migrationBuilder.CreateTable(
                name: "billing_customer_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BillingMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreditLineApprovedLimit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CreditLineWarningThreshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SubscriptionPlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_customer_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_customer_accounts_billing_subscription_plans_Subscr~",
                        column: x => x.SubscriptionPlanCode,
                        principalTable: "billing_subscription_plans",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_customer_accounts_SubscriptionPlanCode",
                table: "billing_customer_accounts",
                column: "SubscriptionPlanCode");

            migrationBuilder.CreateIndex(
                name: "IX_billing_customer_accounts_TenantId_UserId",
                table: "billing_customer_accounts",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_financial_transactions_IdempotencyKey",
                table: "billing_financial_transactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_outbox_messages_IdempotencyKey",
                table: "billing_outbox_messages",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_outbox_messages_ProcessedAt_OccurredAt",
                table: "billing_outbox_messages",
                columns: new[] { "ProcessedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_ledger_entries_CustomerAccountId_OccurredAt",
                table: "billing_usage_ledger_entries",
                columns: new[] { "CustomerAccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_ledger_entries_IdempotencyKey",
                table: "billing_usage_ledger_entries",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_ledger_entries_RelatedEntryId",
                table: "billing_usage_ledger_entries",
                column: "RelatedEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_reservations_IdempotencyKey",
                table: "billing_usage_reservations",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_customer_accounts");

            migrationBuilder.DropTable(
                name: "billing_financial_transactions");

            migrationBuilder.DropTable(
                name: "billing_invoice_accounts");

            migrationBuilder.DropTable(
                name: "billing_outbox_messages");

            migrationBuilder.DropTable(
                name: "billing_usage_ledger_entries");

            migrationBuilder.DropTable(
                name: "billing_usage_reservations");

            migrationBuilder.DropTable(
                name: "billing_wallet_projections");

            migrationBuilder.DropTable(
                name: "billing_subscription_plans");
        }
    }
}

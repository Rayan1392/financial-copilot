using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature151TahlilAppPaidCreditTopUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_tahlilapp_topup_purchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Partner = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PurchaseId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExternalPlanSubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanSlug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PlanStartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlanExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreditsPurchased = table.Column<int>(type: "integer", nullable: false),
                    PackageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PackageVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_tahlilapp_topup_purchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_tahlilapp_topup_purchases_billing_customer_accounts~",
                        column: x => x.CustomerAccountId,
                        principalTable: "billing_customer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_tahlilapp_topup_purchases_billing_external_plan_sub~",
                        column: x => x.ExternalPlanSubscriptionId,
                        principalTable: "billing_external_plan_subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_topup_credit_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalPlanSubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_topup_credit_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_topup_credit_sources_billing_customer_accounts_Cust~",
                        column: x => x.CustomerAccountId,
                        principalTable: "billing_customer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_billing_topup_credit_sources_billing_external_plan_subscrip~",
                        column: x => x.ExternalPlanSubscriptionId,
                        principalTable: "billing_external_plan_subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_topup_offers",
                columns: table => new
                {
                    OfferId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Credits = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_topup_offers", x => new { x.OfferId, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "billing_topup_usage_credit_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopUpCreditSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditsApplied = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_topup_usage_credit_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_topup_usage_credit_allocations_billing_topup_credit~",
                        column: x => x.TopUpCreditSourceId,
                        principalTable: "billing_topup_credit_sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_topup_usage_credit_allocations_billing_usage_ledger~",
                        column: x => x.UsageLedgerEntryId,
                        principalTable: "billing_usage_ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_topup_usage_credit_holds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopUpCreditSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditsHeld = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_topup_usage_credit_holds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_topup_usage_credit_holds_billing_topup_credit_sourc~",
                        column: x => x.TopUpCreditSourceId,
                        principalTable: "billing_topup_credit_sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_topup_usage_credit_holds_billing_usage_reservations~",
                        column: x => x.UsageReservationId,
                        principalTable: "billing_usage_reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "billing_topup_offers",
                columns: new[] { "OfferId", "Version", "Credits", "IsActive" },
                values: new object[,]
                {
                    { "TA-TOPUP-100", "v1", 100, true },
                    { "TA-TOPUP-1000", "v1", 1000, true },
                    { "TA-TOPUP-500", "v1", 500, true }
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_tahlilapp_topup_purchases_CustomerAccountId_Receive~",
                table: "billing_tahlilapp_topup_purchases",
                columns: new[] { "CustomerAccountId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_tahlilapp_topup_purchases_ExternalPlanSubscriptionId",
                table: "billing_tahlilapp_topup_purchases",
                column: "ExternalPlanSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_EventId",
                table: "billing_tahlilapp_topup_purchases",
                columns: new[] { "Partner", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_PaymentId",
                table: "billing_tahlilapp_topup_purchases",
                columns: new[] { "Partner", "PaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_PurchaseId",
                table: "billing_tahlilapp_topup_purchases",
                columns: new[] { "Partner", "PurchaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_credit_sources_CustomerAccountId_Status_Expir~",
                table: "billing_topup_credit_sources",
                columns: new[] { "CustomerAccountId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_credit_sources_ExternalPlanSubscriptionId",
                table: "billing_topup_credit_sources",
                column: "ExternalPlanSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_credit_sources_PurchaseId",
                table: "billing_topup_credit_sources",
                column: "PurchaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_offers_Credits_IsActive",
                table: "billing_topup_offers",
                columns: new[] { "Credits", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_usage_credit_allocations_GrantLedgerEntryId",
                table: "billing_topup_usage_credit_allocations",
                column: "GrantLedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_usage_credit_allocations_TopUpCreditSourceId",
                table: "billing_topup_usage_credit_allocations",
                column: "TopUpCreditSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_usage_credit_allocations_UsageLedgerEntryId_T~",
                table: "billing_topup_usage_credit_allocations",
                columns: new[] { "UsageLedgerEntryId", "TopUpCreditSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_usage_credit_holds_TopUpCreditSourceId",
                table: "billing_topup_usage_credit_holds",
                column: "TopUpCreditSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_topup_usage_credit_holds_UsageReservationId_TopUpCr~",
                table: "billing_topup_usage_credit_holds",
                columns: new[] { "UsageReservationId", "TopUpCreditSourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_tahlilapp_topup_purchases");

            migrationBuilder.DropTable(
                name: "billing_topup_offers");

            migrationBuilder.DropTable(
                name: "billing_topup_usage_credit_allocations");

            migrationBuilder.DropTable(
                name: "billing_topup_usage_credit_holds");

            migrationBuilder.DropTable(
                name: "billing_topup_credit_sources");
        }
    }
}

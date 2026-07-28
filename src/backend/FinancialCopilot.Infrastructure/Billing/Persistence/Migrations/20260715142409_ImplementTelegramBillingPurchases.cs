using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementTelegramBillingPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_purchase_products",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Credits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_purchase_products", x => x.Code);
                    table.ForeignKey(
                        name: "FK_billing_purchase_products_billing_subscription_plans_PlanCo~",
                        column: x => x.PlanCode,
                        principalTable: "billing_subscription_plans",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_checkout_intents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    ProductVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductDisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Credits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ReceiptIdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ReviewIdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ProviderReferenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReceiptAttachmentKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReceiptAttachmentReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReceiptContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceiptSubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewerActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FulfillmentLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    FulfillmentFinancialTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FulfilledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_checkout_intents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_checkout_intents_billing_customer_accounts_Customer~",
                        column: x => x.CustomerAccountId,
                        principalTable: "billing_customer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_billing_checkout_intents_billing_financial_transactions_Ful~",
                        column: x => x.FulfillmentFinancialTransactionId,
                        principalTable: "billing_financial_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_checkout_intents_billing_purchase_products_ProductC~",
                        column: x => x.ProductCode,
                        principalTable: "billing_purchase_products",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_checkout_intents_billing_usage_ledger_entries_Fulfi~",
                        column: x => x.FulfillmentLedgerEntryId,
                        principalTable: "billing_usage_ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "billing_purchase_products",
                columns: new[] { "Code", "Amount", "Channel", "CreatedAtUtc", "Credits", "Currency", "DisplayName", "DurationDays", "IsActive", "PlanCode", "ProductType", "SortOrder", "Version" },
                values: new object[,]
                {
                    { "TG-CREDITS-150", 690000m, "Telegram", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 150m, "IRR", "Telegram 150 AI credits", null, true, null, "CreditPack", 20, "v1" },
                    { "TG-CREDITS-50", 250000m, "Telegram", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 50m, "IRR", "Telegram 50 AI credits", null, true, null, "CreditPack", 10, "v1" },
                    { "TG-PLUS-30D", 2200000m, "Telegram", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0m, "IRR", "Telegram Plus 30 days", 30, true, "Plus", "Subscription", 40, "v1" },
                    { "TG-PREMIUM-30D", 3900000m, "Telegram", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0m, "IRR", "Telegram Premium 30 days", 30, true, "Premium", "Subscription", 50, "v1" },
                    { "TG-PRO-30D", 1200000m, "Telegram", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0m, "IRR", "Telegram Pro 30 days", 30, true, "Pro", "Subscription", 30, "v1" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_CustomerAccountId",
                table: "billing_checkout_intents",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_FulfillmentFinancialTransactionId",
                table: "billing_checkout_intents",
                column: "FulfillmentFinancialTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_FulfillmentLedgerEntryId",
                table: "billing_checkout_intents",
                column: "FulfillmentLedgerEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_IdempotencyKey",
                table: "billing_checkout_intents",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_PaymentReference",
                table: "billing_checkout_intents",
                column: "PaymentReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_ProductCode",
                table: "billing_checkout_intents",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_ProviderReferenceHash",
                table: "billing_checkout_intents",
                column: "ProviderReferenceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_Status_ExpiresAtUtc",
                table: "billing_checkout_intents",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_TenantId_ActorId_Status_CreatedAtU~",
                table: "billing_checkout_intents",
                columns: new[] { "TenantId", "ActorId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_purchase_products_Channel_IsActive_SortOrder",
                table: "billing_purchase_products",
                columns: new[] { "Channel", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_purchase_products_PlanCode",
                table: "billing_purchase_products",
                column: "PlanCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_checkout_intents");

            migrationBuilder.DropTable(
                name: "billing_purchase_products");
        }
    }
}

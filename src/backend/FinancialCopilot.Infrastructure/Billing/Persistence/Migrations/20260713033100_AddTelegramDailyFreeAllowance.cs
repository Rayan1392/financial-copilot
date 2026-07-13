using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramDailyFreeAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllocationSource",
                table: "billing_usage_ledger_entries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowanceDateKey",
                table: "billing_usage_ledger_entries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_daily_free_allowance_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllowanceDateKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExpiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredCredits = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_daily_free_allowance_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_daily_free_allowance_grants_billing_customer_accoun~",
                        column: x => x.CustomerAccountId,
                        principalTable: "billing_customer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_billing_daily_free_allowance_grants_billing_usage_ledger_en~",
                        column: x => x.LedgerEntryId,
                        principalTable: "billing_usage_ledger_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_daily_free_allowance_grants_CustomerAccountId_Actor~",
                table: "billing_daily_free_allowance_grants",
                columns: new[] { "CustomerAccountId", "ActorId", "AllowanceDateKey", "PolicyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_daily_free_allowance_grants_ExpiresAtUtc_ExpiredAtU~",
                table: "billing_daily_free_allowance_grants",
                columns: new[] { "ExpiresAtUtc", "ExpiredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_daily_free_allowance_grants_LedgerEntryId",
                table: "billing_daily_free_allowance_grants",
                column: "LedgerEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_daily_free_allowance_grants");

            migrationBuilder.DropColumn(
                name: "AllocationSource",
                table: "billing_usage_ledger_entries");

            migrationBuilder.DropColumn(
                name: "AllowanceDateKey",
                table: "billing_usage_ledger_entries");
        }
    }
}

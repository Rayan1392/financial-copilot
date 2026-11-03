using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FullWindowCreditGrantEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_free_credit_grant_evidence",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvidenceSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_free_credit_grant_evidence", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_billing_free_credit_grant_evidence_billing_customer_account~",
                        column: x => x.CustomerAccountId,
                        principalTable: "billing_customer_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Update only the original catalog seed. A locally configured allowance wins.
            migrationBuilder.Sql("""
                UPDATE billing_subscription_plans
                SET "IncludedCredits" = 250
                WHERE "Code" = 'Free' AND "IncludedCredits" = 25 AND "PricingPolicyVersion" = 'v1';
                """);

            // A historical Free period, including a 25-credit one, is permanent proof of
            // receipt. Do not add credits or reinterpret the existing wallet projection.
            migrationBuilder.Sql("""
                INSERT INTO billing_free_credit_grant_evidence
                    ("UserId", "CustomerAccountId", "GrantLedgerEntryId", "GrantedAtUtc",
                     "AssignedExpiresAtUtc", "EvidenceSource")
                SELECT DISTINCT ON (account."UserId")
                    account."UserId", account."Id", period."GrantLedgerEntryId",
                    period."CreatedAtUtc", NULL, 'historical-period'
                FROM billing_subscription_credit_periods AS period
                JOIN billing_customer_accounts AS account ON account."Id" = period."CustomerAccountId"
                WHERE period."PlanCode" = 'Free' AND account."UserId" IS NOT NULL
                ORDER BY account."UserId", period."CreatedAtUtc", period."Id"
                ON CONFLICT ("UserId") DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_billing_free_credit_grant_evidence_CustomerAccountId",
                table: "billing_free_credit_grant_evidence",
                column: "CustomerAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_free_credit_grant_evidence_GrantLedgerEntryId",
                table: "billing_free_credit_grant_evidence",
                column: "GrantLedgerEntryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_free_credit_grant_evidence");

            // Catalog edits and historical Free evidence must never be reversed automatically.
        }
    }
}

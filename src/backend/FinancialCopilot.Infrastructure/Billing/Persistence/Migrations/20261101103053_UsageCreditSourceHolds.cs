using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsageCreditSourceHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_usage_credit_holds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionCreditPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditsHeld = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_usage_credit_holds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_usage_credit_holds_billing_subscription_credit_peri~",
                        column: x => x.SubscriptionCreditPeriodId,
                        principalTable: "billing_subscription_credit_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_usage_credit_holds_billing_usage_reservations_Usage~",
                        column: x => x.UsageReservationId,
                        principalTable: "billing_usage_reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_credit_holds_SubscriptionCreditPeriodId",
                table: "billing_usage_credit_holds",
                column: "SubscriptionCreditPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_usage_credit_holds_UsageReservationId_SubscriptionC~",
                table: "billing_usage_credit_holds",
                columns: new[] { "UsageReservationId", "SubscriptionCreditPeriodId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_usage_credit_holds");
        }
    }
}

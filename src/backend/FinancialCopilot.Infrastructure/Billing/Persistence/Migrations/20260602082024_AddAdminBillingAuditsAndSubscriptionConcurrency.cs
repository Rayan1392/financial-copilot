using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminBillingAuditsAndSubscriptionConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubscriptionEffectiveFrom",
                table: "billing_customer_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubscriptionEffectiveTo",
                table: "billing_customer_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SubscriptionRevision",
                table: "billing_customer_accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "billing_admin_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Before = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    After = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_admin_audits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_admin_audits_TenantId_OccurredAt",
                table: "billing_admin_audits",
                columns: new[] { "TenantId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_admin_audits");

            migrationBuilder.DropColumn(
                name: "SubscriptionEffectiveFrom",
                table: "billing_customer_accounts");

            migrationBuilder.DropColumn(
                name: "SubscriptionEffectiveTo",
                table: "billing_customer_accounts");

            migrationBuilder.DropColumn(
                name: "SubscriptionRevision",
                table: "billing_customer_accounts");
        }
    }
}

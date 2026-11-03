using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthoritativeSubscriptionActivationRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExternalSubscriptionRevision",
                table: "billing_customer_accounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SubscriptionActivationIsAuthoritative",
                table: "billing_customer_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalSubscriptionRevision",
                table: "billing_customer_accounts");

            migrationBuilder.DropColumn(
                name: "SubscriptionActivationIsAuthoritative",
                table: "billing_customer_accounts");
        }
    }
}

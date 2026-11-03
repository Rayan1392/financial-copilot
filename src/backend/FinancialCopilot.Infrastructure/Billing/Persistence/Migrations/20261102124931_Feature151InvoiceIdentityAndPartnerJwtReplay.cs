using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature151InvoiceIdentityAndPartnerJwtReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_EventId",
                table: "billing_tahlilapp_topup_purchases");

            migrationBuilder.DropIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_PurchaseId",
                table: "billing_tahlilapp_topup_purchases");

            migrationBuilder.AlterColumn<string>(
                name: "PurchaseId",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentId",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceId",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("UPDATE billing_tahlilapp_topup_purchases SET \"InvoiceId\" = COALESCE(NULLIF(\"PurchaseId\", ''), \"Id\"::text), \"RequestFingerprint\" = 'legacy-receipt' WHERE \"InvoiceId\" IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceId",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RequestFingerprint",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "billing_consumed_partner_tokens",
                columns: table => new
                {
                    Issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Jti = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_consumed_partner_tokens", x => new { x.Issuer, x.Jti });
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_InvoiceId",
                table: "billing_tahlilapp_topup_purchases",
                columns: new[] { "Partner", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_consumed_partner_tokens_ExpiresAtUtc",
                table: "billing_consumed_partner_tokens",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_consumed_partner_tokens");

            migrationBuilder.DropIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_InvoiceId",
                table: "billing_tahlilapp_topup_purchases");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "billing_tahlilapp_topup_purchases");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "billing_tahlilapp_topup_purchases");

            migrationBuilder.AlterColumn<string>(
                name: "PurchaseId",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentId",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                table: "billing_tahlilapp_topup_purchases",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_EventId",
                table: "billing_tahlilapp_topup_purchases",
                columns: new[] { "Partner", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_tahlilapp_topup_purchases_Partner_PurchaseId",
                table: "billing_tahlilapp_topup_purchases",
                columns: new[] { "Partner", "PurchaseId" },
                unique: true);
        }
    }
}

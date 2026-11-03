using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations;

/// <inheritdoc />
public partial class Feature151SourceResolutionAndOptionalAuditWindow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "PlanSlug",
            table: "billing_tahlilapp_topup_purchases",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "PlanStartsAtUtc",
            table: "billing_tahlilapp_topup_purchases",
            type: "timestamp with time zone",
            nullable: true,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "PlanExpiresAtUtc",
            table: "billing_tahlilapp_topup_purchases",
            type: "timestamp with time zone",
            nullable: true,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM billing_tahlilapp_topup_purchases WHERE \"PlanSlug\" IS NULL OR \"PlanStartsAtUtc\" IS NULL OR \"PlanExpiresAtUtc\" IS NULL) THEN RAISE EXCEPTION 'Cannot restore required top-up source fields while unresolved invoices exist.'; END IF; END $$;");

        migrationBuilder.AlterColumn<string>(
            name: "PlanSlug",
            table: "billing_tahlilapp_topup_purchases",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100,
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "PlanStartsAtUtc",
            table: "billing_tahlilapp_topup_purchases",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "PlanExpiresAtUtc",
            table: "billing_tahlilapp_topup_purchases",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);
    }
}

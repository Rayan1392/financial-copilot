using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature136MonthlyProductTrendRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older databases may not contain this legacy index because the original
            // hand-authored migration that introduced it was not discoverable by EF.
            migrationBuilder.Sql("DROP INDEX IF EXISTS public.\"IX_MonthlyReports_LogicalPeriod\";");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyReports_ProviderName_ExternalReportId",
                table: "MonthlyReports");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyReportLineItems_MonthlyReportId_ProductCode",
                table: "MonthlyReportLineItems");

            migrationBuilder.AddColumn<bool>(
                name: "IsAccepted",
                table: "MonthlyReports",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LogicalReportKey",
                table: "MonthlyReports",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderPublishedAtUtc",
                table: "MonthlyReports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevisionFingerprint",
                table: "MonthlyReports",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RevisionStatus",
                table: "MonthlyReports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Accepted");

            migrationBuilder.AddColumn<string>(
                name: "ProviderProductCode",
                table: "MonthlyReportLineItems",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProviderProductId",
                table: "MonthlyReportLineItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceMultiplicity",
                table: "MonthlyReportLineItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SourcePayloadChecksum",
                table: "MonthlyReportLineItems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceRowFingerprint",
                table: "MonthlyReportLineItems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceRowKey",
                table: "MonthlyReportLineItems",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "MonthlyReports"
                SET "IsAccepted" = TRUE,
                    "LogicalReportKey" = "ProviderName" || '|' || "ExternalCompanyId" || '|' ||
                        to_char("PeriodStart", 'YYYY-MM-DD') || '|' || to_char("PeriodEnd", 'YYYY-MM-DD') || '|' ||
                        coalesce("ReportType", 'null') || '|' || coalesce("OutputType"::text, 'null'),
                    "RevisionFingerprint" = CASE WHEN "SourcePayloadChecksum" = '' THEN md5("Id"::text) ELSE "SourcePayloadChecksum" END,
                    "RevisionStatus" = 'Accepted'
                """);

            migrationBuilder.Sql("""
                UPDATE "MonthlyReportLineItems" AS li
                SET "SourceRowFingerprint" = md5(li."Id"::text),
                    "SourceMultiplicity" = 1,
                    "SourcePayloadChecksum" = report."SourcePayloadChecksum"
                FROM "MonthlyReports" AS report
                WHERE report."Id" = li."MonthlyReportId"
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_LogicalPeriod",
                table: "MonthlyReports",
                columns: new[] { "ProviderName", "ExternalCompanyId", "PeriodStart", "PeriodEnd", "OutputType", "ReportType", "IsAccepted" },
                unique: true,
                filter: "\"ExternalCompanyId\" IS NOT NULL AND \"ReportType\" IS NOT NULL AND \"IsAccepted\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_ProviderName_ExternalReportId_RevisionFinger~",
                table: "MonthlyReports",
                columns: new[] { "ProviderName", "ExternalReportId", "RevisionFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReportLineItems_MonthlyReportId_SourceRowFingerprint~",
                table: "MonthlyReportLineItems",
                columns: new[] { "MonthlyReportId", "SourceRowFingerprint", "SourceMultiplicity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReportLineItems_MonthlyReportId_SourceRowKey",
                table: "MonthlyReportLineItems",
                columns: new[] { "MonthlyReportId", "SourceRowKey" },
                unique: true,
                filter: "\"SourceRowKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonthlyReports_LogicalPeriod",
                table: "MonthlyReports");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyReports_ProviderName_ExternalReportId_RevisionFinger~",
                table: "MonthlyReports");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyReportLineItems_MonthlyReportId_SourceRowFingerprint~",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyReportLineItems_MonthlyReportId_SourceRowKey",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropColumn(
                name: "IsAccepted",
                table: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "LogicalReportKey",
                table: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "ProviderPublishedAtUtc",
                table: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "RevisionFingerprint",
                table: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "RevisionStatus",
                table: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "ProviderProductCode",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropColumn(
                name: "ProviderProductId",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropColumn(
                name: "SourceMultiplicity",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropColumn(
                name: "SourcePayloadChecksum",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropColumn(
                name: "SourceRowFingerprint",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropColumn(
                name: "SourceRowKey",
                table: "MonthlyReportLineItems");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_LogicalPeriod",
                table: "MonthlyReports",
                columns: new[] { "ProviderName", "ExternalCompanyId", "PeriodStart", "OutputType", "ReportType" },
                unique: true,
                filter: "\"ExternalCompanyId\" IS NOT NULL AND \"ReportType\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_ProviderName_ExternalReportId",
                table: "MonthlyReports",
                columns: new[] { "ProviderName", "ExternalReportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReportLineItems_MonthlyReportId_ProductCode",
                table: "MonthlyReportLineItems",
                columns: new[] { "MonthlyReportId", "ProductCode" },
                unique: true);
        }
    }
}

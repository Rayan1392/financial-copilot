using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundPortfolioImportOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundPortfolioExtractionIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SheetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    IssueCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    RawValue = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundPortfolioExtractionIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundPortfolioReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalReportId = table.Column<string>(type: "text", nullable: true),
                    ReportType = table.Column<int>(type: "integer", nullable: false),
                    PeriodStartJalali = table.Column<string>(type: "text", nullable: true),
                    PeriodEndJalali = table.Column<string>(type: "text", nullable: true),
                    PeriodStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FiscalYearStartJalali = table.Column<string>(type: "text", nullable: true),
                    FiscalYearEndJalali = table.Column<string>(type: "text", nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawStorageKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    RawFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RawMimeType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParseStatus = table.Column<int>(type: "integer", nullable: false),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupersedesReportId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundPortfolioReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundPortfolioReportSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalSheetName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedSheetName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LogicalSheetType = table.Column<int>(type: "integer", nullable: false),
                    SheetIndex = table.Column<int>(type: "integer", nullable: false),
                    UsedRange = table.Column<string>(type: "text", nullable: true),
                    ClassificationConfidence = table.Column<decimal>(type: "numeric", nullable: false),
                    HeaderFingerprint = table.Column<string>(type: "text", nullable: true),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundPortfolioReportSheets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentFunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalFundId = table.Column<string>(type: "text", nullable: true),
                    FundName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NormalizedFundName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FundSymbol = table.Column<string>(type: "text", nullable: true),
                    RegistrationNumber = table.Column<string>(type: "text", nullable: true),
                    ManagerName = table.Column<string>(type: "text", nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentFunds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioExtractionIssues_IssueCode",
                table: "FundPortfolioExtractionIssues",
                column: "IssueCode");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioExtractionIssues_ReportId_Severity",
                table: "FundPortfolioExtractionIssues",
                columns: new[] { "ReportId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioExtractionIssues_Severity_IssueCode",
                table: "FundPortfolioExtractionIssues",
                columns: new[] { "Severity", "IssueCode" });

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioReports_FundId_PeriodEndDate",
                table: "FundPortfolioReports",
                columns: new[] { "FundId", "PeriodEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioReports_FundId_ProviderName_PeriodEndDate_Repo~",
                table: "FundPortfolioReports",
                columns: new[] { "FundId", "ProviderName", "PeriodEndDate", "ReportType", "SourceRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioReports_ParseStatus",
                table: "FundPortfolioReports",
                column: "ParseStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioReports_ProviderName",
                table: "FundPortfolioReports",
                column: "ProviderName");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioReports_ProviderName_FileSha256",
                table: "FundPortfolioReports",
                columns: new[] { "ProviderName", "FileSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioReportSheets_LogicalSheetType",
                table: "FundPortfolioReportSheets",
                column: "LogicalSheetType");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioReportSheets_ReportId_SheetIndex",
                table: "FundPortfolioReportSheets",
                columns: new[] { "ReportId", "SheetIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFunds_ProviderName_ExternalFundId",
                table: "InvestmentFunds",
                columns: new[] { "ProviderName", "ExternalFundId" },
                unique: true,
                filter: "\"ExternalFundId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFunds_ProviderName_NormalizedFundName",
                table: "InvestmentFunds",
                columns: new[] { "ProviderName", "NormalizedFundName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundPortfolioExtractionIssues");

            migrationBuilder.DropTable(
                name: "FundPortfolioReports");

            migrationBuilder.DropTable(
                name: "FundPortfolioReportSheets");

            migrationBuilder.DropTable(
                name: "InvestmentFunds");
        }
    }
}

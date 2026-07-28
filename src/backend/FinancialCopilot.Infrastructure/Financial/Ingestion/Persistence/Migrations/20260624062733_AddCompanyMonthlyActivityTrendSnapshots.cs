using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyMonthlyActivityTrendSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyMonthlyActivityTrendSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanySymbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CompanyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IndustryId = table.Column<int>(type: "integer", nullable: true),
                    IndustryTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    CategoryTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReportYear = table.Column<int>(type: "integer", nullable: false),
                    ReportMonth = table.Column<byte>(type: "smallint", nullable: false),
                    FiscalEndDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FiscalYear = table.Column<int>(type: "integer", nullable: true),
                    FiscalMonthIndex = table.Column<int>(type: "integer", nullable: true),
                    FiscalMonthNameFa = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CalendarYear = table.Column<int>(type: "integer", nullable: true),
                    CalendarMonth = table.Column<int>(type: "integer", nullable: true),
                    MonthlySalesAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    MonthlyProductionQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    MonthlySalesQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    MonthlyAverageSalesRate = table.Column<decimal>(type: "numeric", nullable: true),
                    HasMixedProductUnits = table.Column<bool>(type: "boolean", nullable: false),
                    ProductUnitSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SameMonthPreviousYearSalesAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    SameMonthPreviousYearProductionQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    SameMonthPreviousYearSalesQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    Average12MonthSalesAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Average12MonthPeriodCount = table.Column<int>(type: "integer", nullable: false),
                    YtdSalesAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    YtdProductionQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    YtdSalesQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    YtdPreviousMonthSalesAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesAmountMomGrowthPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesAmountYoYGrowthPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    ProductionQuantityYoYGrowthPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesQuantityYoYGrowthPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrentMonthOutputType = table.Column<int>(type: "integer", nullable: true),
                    YtdOutputType = table.Column<int>(type: "integer", nullable: true),
                    YtdPreviousMonthOutputType = table.Column<int>(type: "integer", nullable: true),
                    SourceProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceReportId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceRawPayloadId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsComparablePreviousYearAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    IsAverage12MonthComplete = table.Column<bool>(type: "boolean", nullable: false),
                    DataCompletenessScore = table.Column<decimal>(type: "numeric", nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMonthlyActivityTrendSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMonthlyActivityTrendSnapshots_FiscalPeriod",
                table: "CompanyMonthlyActivityTrendSnapshots",
                columns: new[] { "ExternalCompanyId", "FiscalYear", "FiscalMonthIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMonthlyActivityTrendSnapshots_ProviderCalculated",
                table: "CompanyMonthlyActivityTrendSnapshots",
                columns: new[] { "SourceProviderName", "CalculatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMonthlyActivityTrendSnapshots_SymbolPeriod",
                table: "CompanyMonthlyActivityTrendSnapshots",
                columns: new[] { "CompanySymbol", "ReportYear", "ReportMonth" });

            migrationBuilder.CreateIndex(
                name: "UIX_CompanyMonthlyActivityTrendSnapshots_CompanyPeriod",
                table: "CompanyMonthlyActivityTrendSnapshots",
                columns: new[] { "ExternalCompanyId", "ReportYear", "ReportMonth" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyMonthlyActivityTrendSnapshots");
        }
    }
}

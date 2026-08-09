using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundIncomeAttributionAndNavQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundCommodityIncomeDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    RawInstrumentName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UnrealizedIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    RealizedIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    TotalIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundCommodityIncomeDetails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundDepositIncomeDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    RawBankName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    GrossIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    DiscountCost = table.Column<decimal>(type: "numeric", nullable: true),
                    NetIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundDepositIncomeDetails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundDividendIncomeDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    RawSecurityName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "text", nullable: true),
                    MeetingDateJalali = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MeetingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EntitledQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    DividendPerShare = table.Column<decimal>(type: "numeric", nullable: true),
                    GrossDividendIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    DiscountCost = table.Column<decimal>(type: "numeric", nullable: true),
                    NetDividendIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundDividendIncomeDetails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundInvestmentIncomeSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    IncomeCategory = table.Column<int>(type: "integer", nullable: false),
                    RawCategory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    SourcePercentageOfTotalIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    CalculatedPercentageOfTotalIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    PercentageOfTotalAssets = table.Column<decimal>(type: "numeric", nullable: true),
                    CumulativeAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    HasSourceFormulaError = table.Column<bool>(type: "boolean", nullable: false),
                    IsSourceTotal = table.Column<bool>(type: "boolean", nullable: false),
                    ReconciliationStatus = table.Column<int>(type: "integer", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CalculationVersion = table.Column<string>(type: "text", nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundInvestmentIncomeSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundPortfolioValuationQualitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustedSecurityCount = table.Column<int>(type: "integer", nullable: false),
                    AdjustedValueAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    AdjustedValueExposurePercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    MaterialReconciliationIssueCount = table.Column<int>(type: "integer", nullable: false),
                    QualityStatus = table.Column<int>(type: "integer", nullable: false),
                    QualityScore = table.Column<decimal>(type: "numeric", nullable: true),
                    CalculationVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundPortfolioValuationQualitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundSecurityIncomeAttributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    RawSecurityName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "text", nullable: true),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DividendIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    UnrealizedPriceChangeIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    RealizedSaleIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    TotalIncome = table.Column<decimal>(type: "numeric", nullable: true),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    ReconciliationStatus = table.Column<int>(type: "integer", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundSecurityIncomeAttributions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundValuationAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    RawSecurityName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    ClosingPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    AdjustedPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    SourceAdjustmentPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    CalculatedAdjustmentPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    AdjustedValue = table.Column<decimal>(type: "numeric", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    IsMaterial = table.Column<bool>(type: "boolean", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundValuationAdjustments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundCommodityIncomeDetails_FundId_PeriodContext",
                table: "FundCommodityIncomeDetails",
                columns: new[] { "FundId", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundCommodityIncomeDetails_ReportId_PeriodContext_SourceLog~",
                table: "FundCommodityIncomeDetails",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundCommodityIncomeDetails_ResolutionStatus",
                table: "FundCommodityIncomeDetails",
                column: "ResolutionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FundDepositIncomeDetails_FundId_PeriodContext",
                table: "FundDepositIncomeDetails",
                columns: new[] { "FundId", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundDepositIncomeDetails_ReportId_PeriodContext_SourceLogic~",
                table: "FundDepositIncomeDetails",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundDepositIncomeDetails_ResolutionStatus",
                table: "FundDepositIncomeDetails",
                column: "ResolutionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FundDividendIncomeDetails_ExternalCompanyId",
                table: "FundDividendIncomeDetails",
                column: "ExternalCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FundDividendIncomeDetails_FundId_PeriodContext",
                table: "FundDividendIncomeDetails",
                columns: new[] { "FundId", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundDividendIncomeDetails_ReportId_PeriodContext_SourceLogi~",
                table: "FundDividendIncomeDetails",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundInvestmentIncomeSummaries_FundId_PeriodContext",
                table: "FundInvestmentIncomeSummaries",
                columns: new[] { "FundId", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundInvestmentIncomeSummaries_IncomeCategory",
                table: "FundInvestmentIncomeSummaries",
                column: "IncomeCategory");

            migrationBuilder.CreateIndex(
                name: "IX_FundInvestmentIncomeSummaries_ReportId_PeriodContext_Source~",
                table: "FundInvestmentIncomeSummaries",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow", "IncomeCategory" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioValuationQualitySnapshots_QualityStatus",
                table: "FundPortfolioValuationQualitySnapshots",
                column: "QualityStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioValuationQualitySnapshots_ReportId",
                table: "FundPortfolioValuationQualitySnapshots",
                column: "ReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundSecurityIncomeAttributions_ExternalCompanyId",
                table: "FundSecurityIncomeAttributions",
                column: "ExternalCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FundSecurityIncomeAttributions_FundId_PeriodContext",
                table: "FundSecurityIncomeAttributions",
                columns: new[] { "FundId", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundSecurityIncomeAttributions_ReconciliationStatus",
                table: "FundSecurityIncomeAttributions",
                column: "ReconciliationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FundSecurityIncomeAttributions_ReportId_PeriodContext_Sourc~",
                table: "FundSecurityIncomeAttributions",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundValuationAdjustments_ExternalCompanyId",
                table: "FundValuationAdjustments",
                column: "ExternalCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FundValuationAdjustments_FundId_PeriodContext",
                table: "FundValuationAdjustments",
                columns: new[] { "FundId", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundValuationAdjustments_IsMaterial",
                table: "FundValuationAdjustments",
                column: "IsMaterial");

            migrationBuilder.CreateIndex(
                name: "IX_FundValuationAdjustments_ReportId_PeriodContext_SourceLogic~",
                table: "FundValuationAdjustments",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundValuationAdjustments_ResolutionStatus",
                table: "FundValuationAdjustments",
                column: "ResolutionStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundCommodityIncomeDetails");

            migrationBuilder.DropTable(
                name: "FundDepositIncomeDetails");

            migrationBuilder.DropTable(
                name: "FundDividendIncomeDetails");

            migrationBuilder.DropTable(
                name: "FundInvestmentIncomeSummaries");

            migrationBuilder.DropTable(
                name: "FundPortfolioValuationQualitySnapshots");

            migrationBuilder.DropTable(
                name: "FundSecurityIncomeAttributions");

            migrationBuilder.DropTable(
                name: "FundValuationAdjustments");
        }
    }
}

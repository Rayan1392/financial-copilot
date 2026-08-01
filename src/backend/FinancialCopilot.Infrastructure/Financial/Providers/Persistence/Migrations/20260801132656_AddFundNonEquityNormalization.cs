using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundNonEquityNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundAssetAllocationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AssetClass = table.Column<int>(type: "integer", nullable: false),
                    RawAssetClassLabel = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedAssetClassCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    MarketOrNetSaleValue = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightOfTotalAssetsPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    IsSectionTotal = table.Column<bool>(type: "boolean", nullable: false),
                    HasSourceFormulaError = table.Column<bool>(type: "boolean", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MonetaryUnit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PercentageScale = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundAssetAllocationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundBankDepositPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BankCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RawBankName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedBankName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    BeginningBalance = table.Column<decimal>(type: "numeric", nullable: true),
                    IncreaseAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    DecreaseAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    EndingBalance = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightOfTotalAssetsPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    BalanceReconciliationDifference = table.Column<decimal>(type: "numeric", nullable: true),
                    ReconciliationStatus = table.Column<int>(type: "integer", nullable: false),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    IsSectionTotal = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_FundBankDepositPositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundCommodityCertificatePositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CommodityType = table.Column<int>(type: "integer", nullable: false),
                    CommodityCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExtractedInstrumentSymbol = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawInstrumentName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedInstrumentName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    BeginningQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    BeginningCostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    BeginningMarketValue = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchasedQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchaseCostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    SoldQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    SaleProceedsAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    EndingQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    EndingUnitPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    EndingCostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    EndingMarketValue = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightOfTotalAssetsPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    QuantityReconciliationDifference = table.Column<decimal>(type: "numeric", nullable: true),
                    ReconciliationStatus = table.Column<int>(type: "integer", nullable: false),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    IsSectionTotal = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_FundCommodityCertificatePositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundDerivativePositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DerivativeType = table.Column<int>(type: "integer", nullable: false),
                    OptionType = table.Column<int>(type: "integer", nullable: false),
                    PositionSide = table.Column<int>(type: "integer", nullable: false),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UnderlyingExternalCompanyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UnderlyingTradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawInstrumentName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedInstrumentName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RawUnderlyingName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ContractQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    ContractMultiplier = table.Column<decimal>(type: "numeric", nullable: true),
                    UnderlyingCoverageQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    StrikePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    ExpiryOrExerciseJalali = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ExpiryOrExerciseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveReturnPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    CostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    MarketValue = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightOfTotalAssetsPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    HedgeCoverageStatus = table.Column<int>(type: "integer", nullable: false),
                    HedgeCoverageCalculationVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    HedgeCoverageEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
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
                    table.PrimaryKey("PK_FundDerivativePositions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundAssetAllocationSnapshots_AssetClass",
                table: "FundAssetAllocationSnapshots",
                column: "AssetClass");

            migrationBuilder.CreateIndex(
                name: "IX_FundAssetAllocationSnapshots_FundId_PeriodEndDate_PeriodCon~",
                table: "FundAssetAllocationSnapshots",
                columns: new[] { "FundId", "PeriodEndDate", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundAssetAllocationSnapshots_ReportId_PeriodContext_SourceL~",
                table: "FundAssetAllocationSnapshots",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow", "NormalizedAssetClassCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundBankDepositPositions_BankCode",
                table: "FundBankDepositPositions",
                column: "BankCode");

            migrationBuilder.CreateIndex(
                name: "IX_FundBankDepositPositions_FundId_PeriodEndDate_PeriodContext",
                table: "FundBankDepositPositions",
                columns: new[] { "FundId", "PeriodEndDate", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundBankDepositPositions_ReportId_PeriodContext_SourceLogic~",
                table: "FundBankDepositPositions",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow", "NormalizedBankName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundBankDepositPositions_ResolutionStatus",
                table: "FundBankDepositPositions",
                column: "ResolutionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FundCommodityCertificatePositions_CommodityCode",
                table: "FundCommodityCertificatePositions",
                column: "CommodityCode");

            migrationBuilder.CreateIndex(
                name: "IX_FundCommodityCertificatePositions_FundId_PeriodEndDate_Peri~",
                table: "FundCommodityCertificatePositions",
                columns: new[] { "FundId", "PeriodEndDate", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundCommodityCertificatePositions_ReportId_PeriodContext_So~",
                table: "FundCommodityCertificatePositions",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow", "NormalizedInstrumentName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundCommodityCertificatePositions_ResolutionStatus",
                table: "FundCommodityCertificatePositions",
                column: "ResolutionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FundCommodityCertificatePositions_TradingInstrumentId",
                table: "FundCommodityCertificatePositions",
                column: "TradingInstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FundDerivativePositions_DerivativeType",
                table: "FundDerivativePositions",
                column: "DerivativeType");

            migrationBuilder.CreateIndex(
                name: "IX_FundDerivativePositions_ExpiryOrExerciseDate",
                table: "FundDerivativePositions",
                column: "ExpiryOrExerciseDate");

            migrationBuilder.CreateIndex(
                name: "IX_FundDerivativePositions_FundId_PeriodEndDate_PeriodContext",
                table: "FundDerivativePositions",
                columns: new[] { "FundId", "PeriodEndDate", "PeriodContext" });

            migrationBuilder.CreateIndex(
                name: "IX_FundDerivativePositions_ReportId_PeriodContext_SourceLogica~",
                table: "FundDerivativePositions",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow", "NormalizedInstrumentName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundDerivativePositions_ResolutionStatus",
                table: "FundDerivativePositions",
                column: "ResolutionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FundDerivativePositions_TradingInstrumentId",
                table: "FundDerivativePositions",
                column: "TradingInstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FundDerivativePositions_UnderlyingExternalCompanyId",
                table: "FundDerivativePositions",
                column: "UnderlyingExternalCompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundAssetAllocationSnapshots");

            migrationBuilder.DropTable(
                name: "FundBankDepositPositions");

            migrationBuilder.DropTable(
                name: "FundCommodityCertificatePositions");

            migrationBuilder.DropTable(
                name: "FundDerivativePositions");
        }
    }
}

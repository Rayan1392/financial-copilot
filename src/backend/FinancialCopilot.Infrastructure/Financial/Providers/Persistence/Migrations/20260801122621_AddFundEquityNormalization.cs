using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundEquityNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundEquityPeriodActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SecurityType = table.Column<int>(type: "integer", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawSecurityName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedSecurityName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PurchasedQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchaseCostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    SoldQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    SaleProceedsAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    ActivityClassification = table.Column<int>(type: "integer", nullable: false),
                    QuantityReconciliationDifference = table.Column<decimal>(type: "numeric", nullable: true),
                    ReconciliationStatus = table.Column<int>(type: "integer", nullable: false),
                    KnownCorporateActionAdjustment = table.Column<decimal>(type: "numeric", nullable: true),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MonetaryUnit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundEquityPeriodActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundEquityPositionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PositionState = table.Column<int>(type: "integer", nullable: false),
                    SecurityType = table.Column<int>(type: "integer", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TradingInstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawSecurityName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedSecurityName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    UnitMarketPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    CostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    MarketOrNetSaleValue = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightOfTotalAssetsPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    ResolutionStatus = table.Column<int>(type: "integer", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: true),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    ParserProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MonetaryUnit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PercentageScale = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundEquityPositionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundEquitySectionTotals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodContext = table.Column<int>(type: "integer", nullable: false),
                    SourceLogicalRow = table.Column<int>(type: "integer", nullable: false),
                    RawLabel = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    CostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    MarketOrNetSaleValue = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightOfTotalAssetsPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    SourceEvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundEquitySectionTotals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPeriodActivities_ExternalCompanyId_PeriodEndDate",
                table: "FundEquityPeriodActivities",
                columns: new[] { "ExternalCompanyId", "PeriodEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPeriodActivities_FundId_PeriodEndDate_ActivityCla~",
                table: "FundEquityPeriodActivities",
                columns: new[] { "FundId", "PeriodEndDate", "ActivityClassification" });

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPeriodActivities_ReconciliationStatus_SecurityType",
                table: "FundEquityPeriodActivities",
                columns: new[] { "ReconciliationStatus", "SecurityType" });

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPeriodActivities_ReportId_PeriodContext_SourceLog~",
                table: "FundEquityPeriodActivities",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow", "NormalizedSecurityName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPeriodActivities_TradingInstrumentId",
                table: "FundEquityPeriodActivities",
                column: "TradingInstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPositionSnapshots_ExternalCompanyId_PeriodEndDate",
                table: "FundEquityPositionSnapshots",
                columns: new[] { "ExternalCompanyId", "PeriodEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPositionSnapshots_FundId_PeriodEndDate_PositionSt~",
                table: "FundEquityPositionSnapshots",
                columns: new[] { "FundId", "PeriodEndDate", "PositionState" });

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPositionSnapshots_ReportId_PeriodContext_Position~",
                table: "FundEquityPositionSnapshots",
                columns: new[] { "ReportId", "PeriodContext", "PositionState", "SourceLogicalRow", "NormalizedSecurityName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPositionSnapshots_ResolutionStatus_SecurityType",
                table: "FundEquityPositionSnapshots",
                columns: new[] { "ResolutionStatus", "SecurityType" });

            migrationBuilder.CreateIndex(
                name: "IX_FundEquityPositionSnapshots_TradingInstrumentId",
                table: "FundEquityPositionSnapshots",
                column: "TradingInstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FundEquitySectionTotals_ReportId_PeriodContext_SourceLogica~",
                table: "FundEquitySectionTotals",
                columns: new[] { "ReportId", "PeriodContext", "SourceLogicalRow" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundEquityPeriodActivities");

            migrationBuilder.DropTable(
                name: "FundEquityPositionSnapshots");

            migrationBuilder.DropTable(
                name: "FundEquitySectionTotals");
        }
    }
}

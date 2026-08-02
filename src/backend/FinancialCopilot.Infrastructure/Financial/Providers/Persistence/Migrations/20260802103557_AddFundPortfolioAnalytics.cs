using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundPortfolioAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundPortfolioAnalyticsSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousComparableReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    EquityWeight = table.Column<decimal>(type: "numeric", nullable: true),
                    DepositWeight = table.Column<decimal>(type: "numeric", nullable: true),
                    CommodityWeight = table.Column<decimal>(type: "numeric", nullable: true),
                    DerivativeWeight = table.Column<decimal>(type: "numeric", nullable: true),
                    Top5Concentration = table.Column<decimal>(type: "numeric", nullable: true),
                    Top10Concentration = table.Column<decimal>(type: "numeric", nullable: true),
                    HerfindahlIndex = table.Column<decimal>(type: "numeric", nullable: true),
                    PurchaseAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    SaleAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    NetEquityDeploymentAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    TurnoverRatio = table.Column<decimal>(type: "numeric", nullable: true),
                    NewPositionCount = table.Column<int>(type: "integer", nullable: false),
                    FullExitCount = table.Column<int>(type: "integer", nullable: false),
                    RiskPosture = table.Column<int>(type: "integer", nullable: false),
                    LiquidityRiskStatus = table.Column<int>(type: "integer", nullable: false),
                    ValuationQualityStatus = table.Column<int>(type: "integer", nullable: false),
                    InputCompletenessJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "numeric", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceJson = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_FundPortfolioAnalyticsSnapshots", x => x.Id));

            migrationBuilder.CreateTable(
                name: "FundPortfolioSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignalType = table.Column<int>(type: "integer", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IndustryCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Magnitude = table.Column<decimal>(type: "numeric", nullable: true),
                    ImportanceScore = table.Column<decimal>(type: "numeric", nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "numeric", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EvidenceJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_FundPortfolioSignals", x => x.Id));

            migrationBuilder.CreateIndex("IX_FundPortfolioAnalyticsSnapshots_FundId_PeriodEndDate_CalculationVersion", "FundPortfolioAnalyticsSnapshots", new[] { "FundId", "PeriodEndDate", "CalculationVersion" }, unique: true);
            migrationBuilder.CreateIndex("IX_FundPortfolioAnalyticsSnapshots_ReportId", "FundPortfolioAnalyticsSnapshots", "ReportId");
            migrationBuilder.CreateIndex("IX_FundPortfolioSignals_DeduplicationKey", "FundPortfolioSignals", "DeduplicationKey", unique: true);
            migrationBuilder.CreateIndex("IX_FundPortfolioSignals_SnapshotId_SignalType", "FundPortfolioSignals", new[] { "SnapshotId", "SignalType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FundPortfolioSignals");
            migrationBuilder.DropTable(name: "FundPortfolioAnalyticsSnapshots");
        }
    }
}

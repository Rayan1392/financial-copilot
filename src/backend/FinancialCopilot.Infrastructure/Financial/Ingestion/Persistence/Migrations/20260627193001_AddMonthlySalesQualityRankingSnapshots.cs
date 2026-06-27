using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlySalesQualityRankingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonthlySalesQualityRankingSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanySymbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IndustryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IndustryTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IndustryGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    IndustryGroupTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReportYear = table.Column<int>(type: "integer", nullable: false),
                    ReportMonth = table.Column<byte>(type: "smallint", nullable: false),
                    MonthlySalesAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Avg12MonthSalesAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesVsAvg12MPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesMonthOverMonthPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesYearOverYearPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    QualityScore = table.Column<decimal>(type: "numeric", nullable: false),
                    QualityLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "numeric", nullable: false),
                    RankMarket = table.Column<int>(type: "integer", nullable: false),
                    RankIndustry = table.Column<int>(type: "integer", nullable: true),
                    DimensionScoresJson = table.Column<string>(type: "jsonb", nullable: false),
                    PositiveDriversJson = table.Column<string>(type: "jsonb", nullable: false),
                    NegativeDriversJson = table.Column<string>(type: "jsonb", nullable: false),
                    DataCoverageJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsEligible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlySalesQualityRankingSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlySalesQualityRankingSnapshots_PeriodIndustryRank",
                table: "MonthlySalesQualityRankingSnapshots",
                columns: new[] { "ReportYear", "ReportMonth", "IndustryId", "RankIndustry" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlySalesQualityRankingSnapshots_PeriodMarketRank",
                table: "MonthlySalesQualityRankingSnapshots",
                columns: new[] { "ReportYear", "ReportMonth", "RankMarket" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlySalesQualityRankingSnapshots_SymbolPeriod",
                table: "MonthlySalesQualityRankingSnapshots",
                columns: new[] { "CompanySymbol", "ReportYear", "ReportMonth" });

            migrationBuilder.CreateIndex(
                name: "UIX_MonthlySalesQualityRankingSnapshots_CompanyPeriod",
                table: "MonthlySalesQualityRankingSnapshots",
                columns: new[] { "ExternalCompanyId", "ReportYear", "ReportMonth" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlySalesQualityRankingSnapshots");
        }
    }
}

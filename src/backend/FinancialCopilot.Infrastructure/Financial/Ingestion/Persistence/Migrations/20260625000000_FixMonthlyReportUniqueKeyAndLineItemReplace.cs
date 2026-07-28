using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixMonthlyReportUniqueKeyAndLineItemReplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard against duplicate MonthlyReport rows for the same logical period when
            // the API response lacks an activityId (categoryId was previously included in
            // the ExternalReportId fallback path, producing one row per category).
            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_LogicalPeriod",
                table: "MonthlyReports",
                columns: new[] { "ProviderName", "ExternalCompanyId", "PeriodStart", "OutputType", "ReportType" },
                unique: true,
                filter: "\"ExternalCompanyId\" IS NOT NULL AND \"ReportType\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonthlyReports_LogicalPeriod",
                table: "MonthlyReports");
        }
    }
}

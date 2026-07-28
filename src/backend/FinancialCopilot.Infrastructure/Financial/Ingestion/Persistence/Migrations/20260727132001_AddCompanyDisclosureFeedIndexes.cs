using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyDisclosureFeedIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_ProviderName_ExternalCompanyId_LastSynchroni~",
                table: "MonthlyReports",
                columns: new[] { "ProviderName", "ExternalCompanyId", "LastSynchronizedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_ProviderName_LastSynchronizedAt",
                table: "MonthlyReports",
                columns: new[] { "ProviderName", "LastSynchronizedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_ProviderName_StatementType_IsComposing_~",
                table: "FinancialStatements",
                columns: new[] { "ProviderName", "StatementType", "IsComposing", "LastSynchronizedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonthlyReports_ProviderName_ExternalCompanyId_LastSynchroni~",
                table: "MonthlyReports");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyReports_ProviderName_LastSynchronizedAt",
                table: "MonthlyReports");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatements_ProviderName_StatementType_IsComposing_~",
                table: "FinancialStatements");
        }
    }
}

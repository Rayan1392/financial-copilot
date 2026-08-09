using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundPortfolioImportFileHashConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioImportItems_ProviderName_FileSha256",
                table: "FundPortfolioImportItems",
                columns: new[] { "ProviderName", "FileSha256" },
                unique: true,
                filter: "\"FileSha256\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FundPortfolioImportItems_ProviderName_FileSha256",
                table: "FundPortfolioImportItems");
        }
    }
}

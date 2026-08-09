using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

public partial class AddFundPortfolioCorrelationIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("CorrelationId", "FundPortfolioImportItems", type: "character varying(128)", maxLength: 128, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("CorrelationId", "FundPortfolioReports", type: "character varying(128)", maxLength: 128, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("CorrelationId", "FundPortfolioImportItems");
        migrationBuilder.DropColumn("CorrelationId", "FundPortfolioReports");
    }
}

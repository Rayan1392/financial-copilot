using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

public partial class AddFundPortfolioSourceObjectTrace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("SourceObjectId", "FundPortfolioReports", type: "character varying(512)", maxLength: 512, nullable: true);
        migrationBuilder.CreateIndex("IX_FundPortfolioReports_SourceObjectId", "FundPortfolioReports", "SourceObjectId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_FundPortfolioReports_SourceObjectId", "FundPortfolioReports");
        migrationBuilder.DropColumn("SourceObjectId", "FundPortfolioReports");
    }
}

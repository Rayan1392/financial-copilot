using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

public partial class AddFundPortfolioItemQueueTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>("QueuedAtUtc", "FundPortfolioImportItems", type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP");
        migrationBuilder.CreateIndex("IX_FundPortfolioImportItems_QueuedAtUtc", "FundPortfolioImportItems", "QueuedAtUtc");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_FundPortfolioImportItems_QueuedAtUtc", "FundPortfolioImportItems");
        migrationBuilder.DropColumn("QueuedAtUtc", "FundPortfolioImportItems");
    }
}

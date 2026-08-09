using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

public partial class AddFundPortfolioImportLeases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>("LeaseUntilUtc", "FundPortfolioImportItems", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("NextAttemptAtUtc", "FundPortfolioImportItems", type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex("IX_FundPortfolioImportItems_Status_NextAttemptAtUtc_LeaseUntilUtc", "FundPortfolioImportItems", new[] { "Status", "NextAttemptAtUtc", "LeaseUntilUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_FundPortfolioImportItems_Status_NextAttemptAtUtc_LeaseUntilUtc", "FundPortfolioImportItems");
        migrationBuilder.DropColumn("LeaseUntilUtc", "FundPortfolioImportItems");
        migrationBuilder.DropColumn("NextAttemptAtUtc", "FundPortfolioImportItems");
    }
}

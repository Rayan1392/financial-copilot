using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

public partial class AddFundPortfolioRetentionAndWatermarks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "FundPortfolioSourceWatermarks", columns: table => new { ProviderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false), LastModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), LastSourceObjectId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true), LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true) }, constraints: table => table.PrimaryKey("PK_FundPortfolioSourceWatermarks", x => x.ProviderName));
        migrationBuilder.CreateIndex(name: "IX_FundPortfolioSourceWatermarks_LeaseUntilUtc", table: "FundPortfolioSourceWatermarks", column: "LeaseUntilUtc");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "FundPortfolioSourceWatermarks");
}

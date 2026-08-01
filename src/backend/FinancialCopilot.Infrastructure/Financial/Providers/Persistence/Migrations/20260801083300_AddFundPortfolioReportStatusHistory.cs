using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

public partial class AddFundPortfolioReportStatusHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "FundPortfolioReportStatusHistory", columns: table => new { Id = table.Column<Guid>(type: "uuid", nullable: false), ReportId = table.Column<Guid>(type: "uuid", nullable: false), Status = table.Column<int>(type: "integer", nullable: false), EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false), CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true), Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true), CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false) }, constraints: table => table.PrimaryKey("PK_FundPortfolioReportStatusHistory", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_FundPortfolioReportStatusHistory_ReportId_CreatedAtUtc", table: "FundPortfolioReportStatusHistory", columns: new[] { "ReportId", "CreatedAtUtc" });
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "FundPortfolioReportStatusHistory");
}

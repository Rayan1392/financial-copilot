using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

public partial class AddFundPortfolioSourceTraces : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "FundPortfolioSourceTraces", columns: table => new { Id = table.Column<Guid>(type: "uuid", nullable: false), ReportId = table.Column<Guid>(type: "uuid", nullable: false), SourceObjectId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false), SourceRevision = table.Column<int>(type: "integer", nullable: false), NormalizedRowCount = table.Column<int>(type: "integer", nullable: false), SignalCount = table.Column<int>(type: "integer", nullable: false), UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false) }, constraints: table => table.PrimaryKey("PK_FundPortfolioSourceTraces", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_FundPortfolioSourceTraces_SourceObjectId_SourceRevision", table: "FundPortfolioSourceTraces", columns: new[] { "SourceObjectId", "SourceRevision" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_FundPortfolioSourceTraces_ReportId", table: "FundPortfolioSourceTraces", column: "ReportId");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "FundPortfolioSourceTraces");
}

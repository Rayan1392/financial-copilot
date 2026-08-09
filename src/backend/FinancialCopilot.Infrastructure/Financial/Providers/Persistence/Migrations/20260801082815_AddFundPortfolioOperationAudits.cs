using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundPortfolioOperationAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundPortfolioOperationAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_FundPortfolioOperationAudits", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_FundPortfolioOperationAudits_EventType_CreatedAtUtc", table: "FundPortfolioOperationAudits", columns: new[] { "EventType", "CreatedAtUtc" });
            migrationBuilder.CreateIndex(name: "IX_FundPortfolioOperationAudits_RunId", table: "FundPortfolioOperationAudits", column: "RunId");
            migrationBuilder.CreateIndex(name: "IX_FundPortfolioOperationAudits_ReportId", table: "FundPortfolioOperationAudits", column: "ReportId");
            migrationBuilder.CreateIndex(name: "IX_FundPortfolioOperationAudits_ReviewId", table: "FundPortfolioOperationAudits", column: "ReviewId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FundPortfolioOperationAudits");
        }
    }
}

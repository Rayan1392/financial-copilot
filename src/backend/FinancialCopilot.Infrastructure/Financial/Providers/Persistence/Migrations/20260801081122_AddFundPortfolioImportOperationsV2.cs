using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundPortfolioImportOperationsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundPortfolioImportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceObjectId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ObservedFundName = table.Column<string>(type: "text", nullable: true),
                    ObservedPeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    DownloadToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FileSha256 = table.Column<string>(type: "text", nullable: true),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastErrorSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundPortfolioImportItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundPortfolioImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedByActorId = table.Column<string>(type: "text", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DiscoveredCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    PartialCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundPortfolioImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundPortfolioMappingReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingType = table.Column<int>(type: "integer", nullable: false),
                    RawValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CandidateJson = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolutionJson = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    ResolvedByActorId = table.Column<string>(type: "text", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundPortfolioMappingReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioImportItems_ImportRunId",
                table: "FundPortfolioImportItems",
                column: "ImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioImportItems_ProviderName_SourceObjectId",
                table: "FundPortfolioImportItems",
                columns: new[] { "ProviderName", "SourceObjectId" },
                unique: true,
                filter: "\"SourceObjectId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioImportItems_Status_AttemptCount_StartedAtUtc",
                table: "FundPortfolioImportItems",
                columns: new[] { "Status", "AttemptCount", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioImportRuns_ProviderName",
                table: "FundPortfolioImportRuns",
                column: "ProviderName");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioImportRuns_Status_StartedAtUtc",
                table: "FundPortfolioImportRuns",
                columns: new[] { "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioMappingReviews_ReportId",
                table: "FundPortfolioMappingReviews",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_FundPortfolioMappingReviews_Status_MappingType",
                table: "FundPortfolioMappingReviews",
                columns: new[] { "Status", "MappingType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundPortfolioImportItems");

            migrationBuilder.DropTable(
                name: "FundPortfolioImportRuns");

            migrationBuilder.DropTable(
                name: "FundPortfolioMappingReviews");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinancialIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialStatementLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialStatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricCode = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialStatementLineItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "text", nullable: false),
                    ExternalStatementId = table.Column<string>(type: "text", nullable: false),
                    PeriodType = table.Column<string>(type: "text", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    SourcePayloadChecksum = table.Column<string>(type: "text", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialStatements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetricRecalculationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDataset = table.Column<string>(type: "text", nullable: false),
                    ExternalReference = table.Column<string>(type: "text", nullable: true),
                    SourcePayloadChecksum = table.Column<string>(type: "text", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricRecalculationRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyReportLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthlyReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "text", nullable: false),
                    ProductionQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesAmount = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyReportLineItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "text", nullable: false),
                    ExternalReportId = table.Column<string>(type: "text", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    SourcePayloadChecksum = table.Column<string>(type: "text", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    Dataset = table.Column<string>(type: "text", nullable: false),
                    ExternalReference = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedRecords = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SourcePayloadChecksum = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Symbols",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalSymbolId = table.Column<string>(type: "text", nullable: false),
                    SymbolCode = table.Column<string>(type: "text", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Symbols", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ProviderName_ExternalCompanyId",
                table: "Companies",
                columns: new[] { "ProviderName", "ExternalCompanyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatementLineItems_FinancialStatementId_MetricCode",
                table: "FinancialStatementLineItems",
                columns: new[] { "FinancialStatementId", "MetricCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId",
                table: "FinancialStatements",
                columns: new[] { "ProviderName", "ExternalStatementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetricRecalculationRequests_SourceDataset_SourcePayloadChec~",
                table: "MetricRecalculationRequests",
                columns: new[] { "SourceDataset", "SourcePayloadChecksum" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReportLineItems_MonthlyReportId_ProductCode",
                table: "MonthlyReportLineItems",
                columns: new[] { "MonthlyReportId", "ProductCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_ProviderName_ExternalReportId",
                table: "MonthlyReports",
                columns: new[] { "ProviderName", "ExternalReportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSyncRuns_IdempotencyKey",
                table: "ProviderSyncRuns",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Symbols_ProviderName_ExternalSymbolId",
                table: "Symbols",
                columns: new[] { "ProviderName", "ExternalSymbolId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Symbols_SymbolCode",
                table: "Symbols",
                column: "SymbolCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "FinancialStatementLineItems");

            migrationBuilder.DropTable(
                name: "FinancialStatements");

            migrationBuilder.DropTable(
                name: "MetricRecalculationRequests");

            migrationBuilder.DropTable(
                name: "MonthlyReportLineItems");

            migrationBuilder.DropTable(
                name: "MonthlyReports");

            migrationBuilder.DropTable(
                name: "ProviderSyncRuns");

            migrationBuilder.DropTable(
                name: "Symbols");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyActivityBackfillAndLineItemDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SalesRate",
                table: "MonthlyReportLineItems",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "MonthlyReportLineItems",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "MonthlyReportLineItems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MonthlyActivityBackfillStates",
                columns: table => new
                {
                    SourceName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PlannedMonthsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyActivityBackfillStates", x => x.SourceName);
                });

            // Operator-facing mirror of NoavaranCompanyScope (the authoritative code-side filter):
            // equities only (PrecedencyRight = 0, no حق تقدم) on بورس / فرابورس / بازار پایه.
            // Every per-company Noavaran current-API request enumerates companies with this filter.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE VIEW "NoavaranEligibleCompanies" AS
                SELECT *
                FROM "Companies"
                WHERE "ProviderName" = 'NoavaranCurrentApi'
                  AND "PrecedencyRight" = 0
                  AND "MarketId" IN (
                      '037c69ad-f519-419f-ae62-59003b6b2428',
                      'a3ccb30a-caed-4f26-a84a-ac0eb8c78c76',
                      '86c05022-632c-44cd-96c9-5c4f58c51ef5');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP VIEW IF EXISTS "NoavaranEligibleCompanies";""");

            migrationBuilder.DropTable(
                name: "MonthlyActivityBackfillStates");

            migrationBuilder.DropColumn(
                name: "SalesRate",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "MonthlyReportLineItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "MonthlyReportLineItems");
        }
    }
}

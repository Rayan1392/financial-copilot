using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecalculationOutboxAndCodalSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessedAt",
                table: "MetricRecalculationRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "MetricRecalculationRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAt",
                table: "MetricRecalculationRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "MetricRecalculationRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetricRecalculationRequests_ProcessedAt",
                table: "MetricRecalculationRequests",
                column: "ProcessedAt");

            migrationBuilder.CreateTable(
                name: "CodalDbSyncStates",
                columns: table => new
                {
                    Dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSyncedModifiedDateTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodalDbSyncStates", x => x.Dataset);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CodalDbSyncStates");

            migrationBuilder.DropIndex(
                name: "IX_MetricRecalculationRequests_ProcessedAt",
                table: "MetricRecalculationRequests");

            migrationBuilder.DropColumn(name: "ProcessedAt", table: "MetricRecalculationRequests");
            migrationBuilder.DropColumn(name: "AttemptCount", table: "MetricRecalculationRequests");
            migrationBuilder.DropColumn(name: "LastAttemptAt", table: "MetricRecalculationRequests");
            migrationBuilder.DropColumn(name: "LastError", table: "MetricRecalculationRequests");
        }
    }
}

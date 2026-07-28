using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNadpcoScheduledSyncRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NadpcoScheduledSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggerSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulExecutionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedBatches = table.Column<int>(type: "integer", nullable: false),
                    FailedBatches = table.Column<int>(type: "integer", nullable: false),
                    RetryAttempts = table.Column<int>(type: "integer", nullable: false),
                    Diagnostics = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ScheduleSnapshotJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DatasetSelectionJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    LockOwner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LockLeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AlertEmitted = table.Column<bool>(type: "boolean", nullable: false),
                    ManualReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NadpcoScheduledSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NadpcoScheduledSyncRuns_CompletedAt",
                table: "NadpcoScheduledSyncRuns",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_NadpcoScheduledSyncRuns_LockLeaseExpiresAt",
                table: "NadpcoScheduledSyncRuns",
                column: "LockLeaseExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_NadpcoScheduledSyncRuns_StartedAt",
                table: "NadpcoScheduledSyncRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_NadpcoScheduledSyncRuns_Status",
                table: "NadpcoScheduledSyncRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NadpcoScheduledSyncRuns");
        }
    }
}

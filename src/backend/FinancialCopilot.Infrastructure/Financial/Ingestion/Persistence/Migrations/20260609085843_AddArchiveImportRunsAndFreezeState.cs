using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveImportRunsAndFreezeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchiveFreezeStates",
                columns: table => new
                {
                    SourceName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsFrozen = table.Column<bool>(type: "boolean", nullable: false),
                    FrozenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FrozenByRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveFreezeStates", x => x.SourceName);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DatasetSelectionJson = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompaniesConsidered = table.Column<int>(type: "integer", nullable: false),
                    RequestsEnqueued = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    ConflictCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    Frozen = table.Column<bool>(type: "boolean", nullable: false),
                    Diagnostics = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LockOwner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LockLeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveImportRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveImportRuns_LockLeaseExpiresAt",
                table: "ArchiveImportRuns",
                column: "LockLeaseExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveImportRuns_StartedAt",
                table: "ArchiveImportRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveImportRuns_Status",
                table: "ArchiveImportRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveFreezeStates");

            migrationBuilder.DropTable(
                name: "ArchiveImportRuns");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNadpcoApiSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NadpcoApiSyncStates",
                columns: table => new
                {
                    Dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSuccessfulSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastOverlapFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCompaniesConsidered = table.Column<int>(type: "integer", nullable: false),
                    LastCompaniesEnqueued = table.Column<int>(type: "integer", nullable: false),
                    LastFailedCompanies = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NadpcoApiSyncStates", x => x.Dataset);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NadpcoApiSyncStates");
        }
    }
}

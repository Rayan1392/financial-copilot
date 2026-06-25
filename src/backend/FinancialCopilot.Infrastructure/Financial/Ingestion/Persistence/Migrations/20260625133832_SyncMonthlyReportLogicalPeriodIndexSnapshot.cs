using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncMonthlyReportLogicalPeriodIndexSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The index was introduced in the preceding hand-authored migration
            // 20260625000000_FixMonthlyReportUniqueKeyAndLineItemReplace.
            // This migration exists only to align the EF model snapshot with that
            // already-declared schema change so database update can proceed.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: preserve the existing logical-period index on rollback of this
            // snapshot-sync migration because the actual DDL belongs to the prior
            // hand-authored migration.
        }
    }
}

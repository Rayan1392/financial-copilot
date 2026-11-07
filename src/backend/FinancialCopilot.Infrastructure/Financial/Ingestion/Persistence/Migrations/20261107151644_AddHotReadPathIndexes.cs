using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <summary>
    /// Indexes for read paths that were full-table scans in production (2026-11-07):
    /// the insight detector's "snapshots since T" window, recent/per-source sync-run lists,
    /// and the monthly backfill IdempotencyKey prefix match.
    /// Built CONCURRENTLY (outside a transaction) so applying on a live database does not
    /// block ingestion writes, and IF NOT EXISTS because production received them as a
    /// zero-downtime hotfix before this migration shipped.
    /// </summary>
    public partial class AddHotReadPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_IntradayTradeSnapshots_ReceivedAt" ON "IntradayTradeSnapshots" ("ReceivedAt");""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_ProviderSyncRuns_RequestedAt" ON "ProviderSyncRuns" ("RequestedAt");""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_ProviderSyncRuns_ProviderName_RequestedAt" ON "ProviderSyncRuns" ("ProviderName", "RequestedAt");""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_ProviderSyncRuns_IdempotencyKey_Pattern" ON "ProviderSyncRuns" ("IdempotencyKey" text_pattern_ops);""",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX CONCURRENTLY IF EXISTS "IX_ProviderSyncRuns_IdempotencyKey_Pattern";""", suppressTransaction: true);
            migrationBuilder.Sql("""DROP INDEX CONCURRENTLY IF EXISTS "IX_ProviderSyncRuns_ProviderName_RequestedAt";""", suppressTransaction: true);
            migrationBuilder.Sql("""DROP INDEX CONCURRENTLY IF EXISTS "IX_ProviderSyncRuns_RequestedAt";""", suppressTransaction: true);
            migrationBuilder.Sql("""DROP INDEX CONCURRENTLY IF EXISTS "IX_IntradayTradeSnapshots_ReceivedAt";""", suppressTransaction: true);
        }
    }
}

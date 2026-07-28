using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <summary>
    /// Spec 029 — Financial Statement Schema Fix.
    /// <para>
    /// Adds <c>StatementType</c> to <c>FinancialStatements</c> so the kind of statement
    /// (<c>IncomeStatement</c>/<c>BalanceSheet</c>/<c>CashFlow</c>) is queryable and the
    /// CodalDb <c>:INC</c>/<c>:BS</c> suffix workaround can be retired. Changes the unique key
    /// from <c>(ProviderName, ExternalStatementId)</c> to
    /// <c>(ProviderName, ExternalStatementId, StatementType)</c>.
    /// </para>
    /// <para>
    /// <b>DESTRUCTIVE — read before applying.</b> Because <c>StatementType</c> is NOT NULL and
    /// existing rows cannot be backfilled deterministically (CyclicalWaves rows have a corrupt
    /// <c>PeriodType</c>, and CodalDb rows would invalidate <c>DerivedMetrics.SourceEvidenceJson</c>
    /// references if their <c>ExternalStatementId</c>s were rewritten), the migration truncates
    /// all financial-statement-derived tables before adding the column. After applying the
    /// migration, an operator must trigger
    /// <c>POST /api/v1/admin/codaldb/full-sync</c> to repopulate the data. The Worker process
    /// MUST be stopped before applying this migration to avoid racing the truncate cascade.
    /// </para>
    /// <para>
    /// <c>Down()</c> reverses the schema only; truncated data cannot be restored.
    /// </para>
    /// </summary>
    public partial class AddStatementTypeAndFixUniqueKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec 029: clean slate before adding the NOT NULL StatementType column. The Worker
            // process MUST be stopped before applying this migration.
            migrationBuilder.Sql(@"
                TRUNCATE TABLE
                    ""FinancialStatementLineItems"",
                    ""FinancialStatements"",
                    ""DerivedMetrics"",
                    ""MetricRecalculationRequests"",
                    ""MonthlyReportLineItems"",
                    ""MonthlyReports"",
                    ""ProviderRawPayloads""
                RESTART IDENTITY CASCADE;

                -- Reset CodalDb watermark so the next sync re-ingests every company.
                DELETE FROM ""CodalDbSyncStates"";
            ");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId",
                table: "FinancialStatements");

            migrationBuilder.AddColumn<string>(
                name: "StatementType",
                table: "FinancialStatements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId_Statem~",
                table: "FinancialStatements",
                columns: new[] { "ProviderName", "ExternalStatementId", "StatementType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_ProviderName_StatementType",
                table: "FinancialStatements",
                columns: new[] { "ProviderName", "StatementType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema-only reversal — truncated rows are NOT restored.
            migrationBuilder.DropIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId_Statem~",
                table: "FinancialStatements");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatements_ProviderName_StatementType",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "StatementType",
                table: "FinancialStatements");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId",
                table: "FinancialStatements",
                columns: new[] { "ProviderName", "ExternalStatementId" },
                unique: true);
        }
    }
}

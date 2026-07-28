using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNoavaranSourceProvenanceAndRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogicalVendor",
                table: "ProviderSyncRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhysicalSource",
                table: "ProviderSyncRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDateRangeEndJalali",
                table: "ProviderSyncRuns",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDateRangeStartJalali",
                table: "ProviderSyncRuns",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceMode",
                table: "ProviderSyncRuns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogicalVendor",
                table: "MonthlyReports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceMode",
                table: "MonthlyReports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogicalVendor",
                table: "FinancialStatements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceMode",
                table: "FinancialStatements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogicalVendor",
                table: "Companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceMode",
                table: "Companies",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSyncRuns_LogicalVendor_PhysicalSource",
                table: "ProviderSyncRuns",
                columns: new[] { "LogicalVendor", "PhysicalSource" });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_LogicalVendor_SourceMode",
                table: "Companies",
                columns: new[] { "LogicalVendor", "SourceMode" });

            // Spec 051: rename persisted physical-source names in place so already-ingested rows stay
            // valid under the new catalog (CodalDb -> NoavaranArchiveSql, NadpcoApi -> NoavaranCurrentApi),
            // and backfill the new provenance columns from the (now renamed) source name.
            foreach (var table in new[]
            {
                "Companies", "Symbols", "Industries", "IndustryGroups", "Markets",
                "FinancialStatements", "MonthlyReports", "ProviderSyncRuns"
            })
            {
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"ProviderName\" = 'NoavaranArchiveSql' WHERE \"ProviderName\" = 'CodalDb';");
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"ProviderName\" = 'NoavaranCurrentApi' WHERE \"ProviderName\" = 'NadpcoApi';");
            }

            // Backfill row-level provenance for the renamed Noavaran rows.
            foreach (var table in new[] { "Companies", "FinancialStatements", "MonthlyReports" })
            {
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"LogicalVendor\" = 'NoavaranAmin', \"SourceMode\" = 'ArchiveOneTime' " +
                    $"WHERE \"ProviderName\" = 'NoavaranArchiveSql';");
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"LogicalVendor\" = 'NoavaranAmin', \"SourceMode\" = 'CurrentIncremental' " +
                    $"WHERE \"ProviderName\" = 'NoavaranCurrentApi';");
            }

            // Backfill batch-level provenance on prior sync runs.
            migrationBuilder.Sql(
                "UPDATE \"ProviderSyncRuns\" SET \"LogicalVendor\" = 'NoavaranAmin', " +
                "\"PhysicalSource\" = 'NoavaranArchiveSql', \"SourceMode\" = 'ArchiveOneTime' " +
                "WHERE \"ProviderName\" = 'NoavaranArchiveSql';");
            migrationBuilder.Sql(
                "UPDATE \"ProviderSyncRuns\" SET \"LogicalVendor\" = 'NoavaranAmin', " +
                "\"PhysicalSource\" = 'NoavaranCurrentApi', \"SourceMode\" = 'CurrentIncremental' " +
                "WHERE \"ProviderName\" = 'NoavaranCurrentApi';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the in-place source-name rename.
            foreach (var table in new[]
            {
                "Companies", "Symbols", "Industries", "IndustryGroups", "Markets",
                "FinancialStatements", "MonthlyReports", "ProviderSyncRuns"
            })
            {
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"ProviderName\" = 'CodalDb' WHERE \"ProviderName\" = 'NoavaranArchiveSql';");
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"ProviderName\" = 'NadpcoApi' WHERE \"ProviderName\" = 'NoavaranCurrentApi';");
            }

            migrationBuilder.DropIndex(
                name: "IX_ProviderSyncRuns_LogicalVendor_PhysicalSource",
                table: "ProviderSyncRuns");

            migrationBuilder.DropIndex(
                name: "IX_Companies_LogicalVendor_SourceMode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "LogicalVendor",
                table: "ProviderSyncRuns");

            migrationBuilder.DropColumn(
                name: "PhysicalSource",
                table: "ProviderSyncRuns");

            migrationBuilder.DropColumn(
                name: "SourceDateRangeEndJalali",
                table: "ProviderSyncRuns");

            migrationBuilder.DropColumn(
                name: "SourceDateRangeStartJalali",
                table: "ProviderSyncRuns");

            migrationBuilder.DropColumn(
                name: "SourceMode",
                table: "ProviderSyncRuns");

            migrationBuilder.DropColumn(
                name: "LogicalVendor",
                table: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "SourceMode",
                table: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "LogicalVendor",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "SourceMode",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "LogicalVendor",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SourceMode",
                table: "Companies");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameProviderRawPayloadSourceNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec 051: keep stored raw-payload provenance consistent with the renamed physical sources
            // (CodalDb -> NoavaranArchiveSql, NadpcoApi -> NoavaranCurrentApi). Data-only; no schema change.
            migrationBuilder.Sql(
                "UPDATE \"ProviderRawPayloads\" SET \"ProviderName\" = 'NoavaranArchiveSql' WHERE \"ProviderName\" = 'CodalDb';");
            migrationBuilder.Sql(
                "UPDATE \"ProviderRawPayloads\" SET \"ProviderName\" = 'NoavaranCurrentApi' WHERE \"ProviderName\" = 'NadpcoApi';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"ProviderRawPayloads\" SET \"ProviderName\" = 'CodalDb' WHERE \"ProviderName\" = 'NoavaranArchiveSql';");
            migrationBuilder.Sql(
                "UPDATE \"ProviderRawPayloads\" SET \"ProviderName\" = 'NadpcoApi' WHERE \"ProviderName\" = 'NoavaranCurrentApi';");
        }
    }
}

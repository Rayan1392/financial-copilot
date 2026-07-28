using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingFinancialIngestionChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_class
                        WHERE relkind = 'i'
                          AND relname = 'IX_CompanyProductRevenueMix_CompanyPeriod'
                    ) THEN
                        RETURN;
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM pg_class
                        WHERE relkind = 'i'
                          AND relname = 'IX_CompanyProductRevenueMix_ExternalCompanyId_ReportYear_Repor~'
                    ) THEN
                        ALTER INDEX "IX_CompanyProductRevenueMix_ExternalCompanyId_ReportYear_Repor~"
                        RENAME TO "IX_CompanyProductRevenueMix_CompanyPeriod";
                    ELSIF EXISTS (
                        SELECT 1
                        FROM pg_class
                        WHERE relkind = 'i'
                          AND relname = 'IX_CompanyProductRevenueMix_ExternalCompanyId_ReportYear_ReportMonth'
                    ) THEN
                        ALTER INDEX "IX_CompanyProductRevenueMix_ExternalCompanyId_ReportYear_ReportMonth"
                        RENAME TO "IX_CompanyProductRevenueMix_CompanyPeriod";
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_class
                        WHERE relkind = 'i'
                          AND relname = 'IX_CompanyProductRevenueMix_CompanyPeriod'
                    ) THEN
                        ALTER INDEX "IX_CompanyProductRevenueMix_CompanyPeriod"
                        RENAME TO "IX_CompanyProductRevenueMix_ExternalCompanyId_ReportYear_ReportMonth";
                    END IF;
                END
                $$;
                """);
        }
    }
}

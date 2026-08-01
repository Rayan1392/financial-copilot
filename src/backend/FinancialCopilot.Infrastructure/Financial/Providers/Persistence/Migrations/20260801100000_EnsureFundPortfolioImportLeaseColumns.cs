using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

/// <summary>
/// Repairs databases created from an intermediate Fund Portfolio migration set.
/// The operations are intentionally idempotent so this remains safe when any
/// of the original post-operations migrations were already applied.
/// </summary>
[Migration("20260801100000_EnsureFundPortfolioImportLeaseColumns")]
[DbContext(typeof(FinancialProviderDbContext))]
public partial class EnsureFundPortfolioImportLeaseColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"FundPortfolioImportItems\" ADD COLUMN IF NOT EXISTS \"LeaseUntilUtc\" timestamp with time zone NULL;");
        migrationBuilder.Sql("ALTER TABLE \"FundPortfolioImportItems\" ADD COLUMN IF NOT EXISTS \"NextAttemptAtUtc\" timestamp with time zone NULL;");
        migrationBuilder.Sql("ALTER TABLE \"FundPortfolioImportItems\" ADD COLUMN IF NOT EXISTS \"CorrelationId\" character varying(128) NOT NULL DEFAULT '';");
        migrationBuilder.Sql("ALTER TABLE \"FundPortfolioImportItems\" ADD COLUMN IF NOT EXISTS \"QueuedAtUtc\" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP;");
        migrationBuilder.Sql("ALTER TABLE \"FundPortfolioReports\" ADD COLUMN IF NOT EXISTS \"CorrelationId\" character varying(128) NULL;");
        migrationBuilder.Sql("ALTER TABLE \"FundPortfolioReports\" ADD COLUMN IF NOT EXISTS \"SourceObjectId\" character varying(512) NULL;");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_FundPortfolioImportItems_Status_NextAttemptAtUtc_LeaseUntilUtc\" ON \"FundPortfolioImportItems\" (\"Status\", \"NextAttemptAtUtc\", \"LeaseUntilUtc\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_FundPortfolioImportItems_QueuedAtUtc\" ON \"FundPortfolioImportItems\" (\"QueuedAtUtc\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_FundPortfolioReports_SourceObjectId\" ON \"FundPortfolioReports\" (\"SourceObjectId\");");
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "FundPortfolioSourceWatermarks" (
                "ProviderName" character varying(128) NOT NULL,
                "LastModifiedUtc" timestamp with time zone NULL,
                "LastSourceObjectId" character varying(512) NULL,
                "LeaseUntilUtc" timestamp with time zone NULL,
                CONSTRAINT "PK_FundPortfolioSourceWatermarks" PRIMARY KEY ("ProviderName")
            );
            CREATE INDEX IF NOT EXISTS "IX_FundPortfolioSourceWatermarks_LeaseUntilUtc"
                ON "FundPortfolioSourceWatermarks" ("LeaseUntilUtc");
            """);
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "FundPortfolioReportStatusHistory" (
                "Id" uuid NOT NULL,
                "ReportId" uuid NOT NULL,
                "Status" integer NOT NULL,
                "EventType" character varying(64) NOT NULL,
                "CorrelationId" character varying(128) NULL,
                "Details" character varying(1000) NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_FundPortfolioReportStatusHistory" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_FundPortfolioReportStatusHistory_ReportId_CreatedAtUtc"
                ON "FundPortfolioReportStatusHistory" ("ReportId", "CreatedAtUtc");
            """);
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "FundPortfolioGovernedMappings" (
                "Id" uuid NOT NULL,
                "MappingType" integer NOT NULL,
                "RawValue" character varying(1000) NOT NULL,
                "NormalizedValue" character varying(1000) NOT NULL,
                "ResolutionJson" character varying(10000) NOT NULL,
                "IsApproved" boolean NOT NULL,
                "ResolvedByActorId" character varying(256) NOT NULL,
                "ResolvedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_FundPortfolioGovernedMappings" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FundPortfolioGovernedMappings_MappingType_RawValue"
                ON "FundPortfolioGovernedMappings" ("MappingType", "RawValue");
            """);
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS "FundPortfolioSourceTraces" (
                "Id" uuid NOT NULL,
                "ReportId" uuid NOT NULL,
                "SourceObjectId" character varying(512) NOT NULL,
                "SourceRevision" integer NOT NULL,
                "NormalizedRowCount" integer NOT NULL,
                "SignalCount" integer NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_FundPortfolioSourceTraces" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FundPortfolioSourceTraces_SourceObjectId_SourceRevision"
                ON "FundPortfolioSourceTraces" ("SourceObjectId", "SourceRevision");
            CREATE INDEX IF NOT EXISTS "IX_FundPortfolioSourceTraces_ReportId"
                ON "FundPortfolioSourceTraces" ("ReportId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This is a repair migration. Its objects may also be supplied by an
        // earlier migration that was applied outside EF history, so rollback
        // must not remove shared schema.
    }
}

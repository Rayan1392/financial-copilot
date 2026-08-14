using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCyclicalWavesDataAcquisitionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CyclicalWavesMetricSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SymbolIsin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MetricType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RawResponseJson = table.Column<string>(type: "text", nullable: false),
                    ResponseHash = table.Column<string>(type: "char(64)", nullable: false),
                    AcquisitionDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceEndpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PreviousSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CyclicalWavesMetricSnapshots", x => x.Id);
                    table.CheckConstraint("CK_CyclicalWavesMetricSnapshots_MetricType", "\"MetricType\" IN ('PS', 'PE', 'Equilibrium')");
                    table.CheckConstraint("CK_CyclicalWavesMetricSnapshots_ProviderName", "\"ProviderName\" = 'CyclicalWaves'");
                    table.CheckConstraint("CK_CyclicalWavesMetricSnapshots_ResponseHash", "\"ResponseHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_CyclicalWavesMetricSnapshots_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CyclicalWavesMetricSnapshots_CyclicalWavesMetricSnapshots_P~",
                        column: x => x.PreviousSnapshotId,
                        principalTable: "CyclicalWavesMetricSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CyclicalWavesAcquisitionChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SymbolIsin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MetricType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CheckedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResponseHash = table.Column<string>(type: "char(64)", nullable: true),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceEndpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HttpStatusCode = table.Column<short>(type: "smallint", nullable: true),
                    AttemptCount = table.Column<short>(type: "smallint", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CyclicalWavesAcquisitionChecks", x => x.Id);
                    table.CheckConstraint("CK_CyclicalWavesAcquisitionChecks_AttemptCount", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_CyclicalWavesAcquisitionChecks_Consistency", "((\"Result\" IN ('Changed', 'NoChange') AND \"ResponseHash\" IS NOT NULL AND \"SnapshotId\" IS NOT NULL AND \"FailureCode\" IS NULL) OR (\"Result\" = 'Failed' AND \"ResponseHash\" IS NULL AND \"SnapshotId\" IS NULL AND \"FailureCode\" IS NOT NULL))");
                    table.CheckConstraint("CK_CyclicalWavesAcquisitionChecks_MetricType", "\"MetricType\" IN ('PS', 'PE', 'Equilibrium')");
                    table.CheckConstraint("CK_CyclicalWavesAcquisitionChecks_ProviderName", "\"ProviderName\" = 'CyclicalWaves'");
                    table.CheckConstraint("CK_CyclicalWavesAcquisitionChecks_Result", "\"Result\" IN ('Changed', 'NoChange', 'Failed')");
                    table.ForeignKey(
                        name: "FK_CyclicalWavesAcquisitionChecks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CyclicalWavesAcquisitionChecks_CyclicalWavesMetricSnapshots~",
                        column: x => x.SnapshotId,
                        principalTable: "CyclicalWavesMetricSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesAcquisitionChecks_Diagnostics",
                table: "CyclicalWavesAcquisitionChecks",
                columns: new[] { "CompanyId", "MetricType", "CheckedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesAcquisitionChecks_Restart",
                table: "CyclicalWavesAcquisitionChecks",
                columns: new[] { "CycleDateUtc", "CompanyId", "MetricType", "Result" });

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesAcquisitionChecks_SnapshotId",
                table: "CyclicalWavesAcquisitionChecks",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesMetricSnapshots_Hash",
                table: "CyclicalWavesMetricSnapshots",
                columns: new[] { "CompanyId", "ProviderName", "MetricType", "ResponseHash" });

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesMetricSnapshots_Latest",
                table: "CyclicalWavesMetricSnapshots",
                columns: new[] { "CompanyId", "ProviderName", "MetricType", "AcquisitionDateUtc", "CreatedAtUtc" },
                descending: new[] { false, false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_CyclicalWavesMetricSnapshots_PreviousSnapshotId",
                table: "CyclicalWavesMetricSnapshots",
                column: "PreviousSnapshotId");

            migrationBuilder.CreateIndex(
                name: "UX_CyclicalWavesMetricSnapshots_Predecessor",
                table: "CyclicalWavesMetricSnapshots",
                columns: new[] { "CompanyId", "ProviderName", "MetricType", "PreviousSnapshotId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CyclicalWavesAcquisitionChecks");

            migrationBuilder.DropTable(
                name: "CyclicalWavesMetricSnapshots");
        }
    }
}

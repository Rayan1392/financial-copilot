using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    // The canonical migration identifier is declared by the generated designer metadata.
    /// <inheritdoc />
    public partial class Feature125Slice3Persistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GaugeAverage",
                table: "CompanyPsGaugeSnapshots",
                type: "numeric(28,14)",
                precision: 28,
                scale: 14,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "IndustryRelativeValuationCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IndustryId = table.Column<Guid>(type: "uuid", nullable: false),
                    IndustryExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IndustryTitleSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CalculationVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MembershipHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceBarrierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceBarrierEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsLatestEvaluation = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelectedCurrent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryRelativeValuationCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryRelativeValuationCalculations_Industries_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IndustryRelativeValuationSourceFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceObservationId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CurrentValue = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    ReferenceValue = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PersistedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceEndpoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceWatermark = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Readiness = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QualityCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IdentityEvidence = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RawPayload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryRelativeValuationSourceFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryRelativeValuationSourceFacts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IndustryRelativeValuationSourceLeases",
                columns: table => new
                {
                    LeaseName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryRelativeValuationSourceLeases", x => x.LeaseName);
                });

            migrationBuilder.CreateTable(
                name: "IndustryWatchStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IndustryId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntryStreak = table.Column<int>(type: "integer", nullable: false),
                    ExitStreak = table.Column<int>(type: "integer", nullable: false),
                    LastEvaluatedCalculationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastTransitionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastTransitionReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryWatchStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryWatchStates_Industries_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyIndustryRelativeValuations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeSourceObservationId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PeSourceFactId = table.Column<Guid>(type: "uuid", nullable: true),
                    PeSourceVersion = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PeSourceObservationTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PePersistedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PsSourceObservationId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PsSourceFactId = table.Column<Guid>(type: "uuid", nullable: true),
                    PsSourceVersion = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PsSourceObservationTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PsPersistedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EquilibriumSourceObservationId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    EquilibriumSourceFactId = table.Column<Guid>(type: "uuid", nullable: true),
                    EquilibriumSourceVersion = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    EquilibriumSourceObservationTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EquilibriumPersistedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PeSourceWatermark = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PsSourceWatermark = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    EquilibriumSourceWatermark = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CurrentPE = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    HistoricalAveragePE = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    CurrentPS = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    HistoricalAveragePS = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    CurrentMarketPrice = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    EquilibriumPrice = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    PEPercent = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    PSPercent = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    EquilibriumPercent = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    PEIsValid = table.Column<bool>(type: "boolean", nullable: false),
                    PSIsValid = table.Column<bool>(type: "boolean", nullable: false),
                    EquilibriumIsValid = table.Column<bool>(type: "boolean", nullable: false),
                    PEIsOutlier = table.Column<bool>(type: "boolean", nullable: false),
                    PSIsOutlier = table.Column<bool>(type: "boolean", nullable: false),
                    EquilibriumIsOutlier = table.Column<bool>(type: "boolean", nullable: false),
                    PEClassification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PSClassification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EquilibriumClassification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PEReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PSReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EquilibriumReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PositiveMetricCount = table.Column<int>(type: "integer", nullable: false),
                    ValidMetricCount = table.Column<int>(type: "integer", nullable: false),
                    GlobalRank = table.Column<int>(type: "integer", nullable: true),
                    RankVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyIndustryRelativeValuations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyIndustryRelativeValuations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyIndustryRelativeValuations_IndustryRelativeValuation~",
                        column: x => x.CalculationId,
                        principalTable: "IndustryRelativeValuationCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndustryRelativeValuationMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidCount = table.Column<int>(type: "integer", nullable: false),
                    OutlierCount = table.Column<int>(type: "integer", nullable: false),
                    CleanCount = table.Column<int>(type: "integer", nullable: false),
                    Quartile1 = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    Quartile3 = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    LowerBound = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    UpperBound = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    CleanAverage = table.Column<decimal>(type: "numeric(28,14)", precision: 28, scale: 14, nullable: true),
                    Readiness = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryRelativeValuationMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryRelativeValuationMetrics_IndustryRelativeValuationC~",
                        column: x => x.CalculationId,
                        principalTable: "IndustryRelativeValuationCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndustryRelativeValuationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IndustryId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventIdentity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryRelativeValuationOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryRelativeValuationOutbox_Industries_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IndustryRelativeValuationOutbox_IndustryRelativeValuationCa~",
                        column: x => x.CalculationId,
                        principalTable: "IndustryRelativeValuationCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IndustryWatchEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IndustryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CalculationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NewState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreviousEntryStreak = table.Column<int>(type: "integer", nullable: false),
                    NewEntryStreak = table.Column<int>(type: "integer", nullable: false),
                    PreviousExitStreak = table.Column<int>(type: "integer", nullable: false),
                    NewExitStreak = table.Column<int>(type: "integer", nullable: false),
                    TransitionReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEffective = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryWatchEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryWatchEvaluations_Industries_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IndustryWatchEvaluations_IndustryRelativeValuationCalculati~",
                        column: x => x.CalculationId,
                        principalTable: "IndustryRelativeValuationCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IndustryWatchTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IndustryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NextState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvaluationOutcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousEntryStreak = table.Column<int>(type: "integer", nullable: false),
                    NewEntryStreak = table.Column<int>(type: "integer", nullable: false),
                    PreviousExitStreak = table.Column<int>(type: "integer", nullable: false),
                    NewExitStreak = table.Column<int>(type: "integer", nullable: false),
                    TransitionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventIdentity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryWatchTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndustryWatchTransitions_Industries_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IndustryWatchTransitions_IndustryRelativeValuationCalculati~",
                        column: x => x.CalculationId,
                        principalTable: "IndustryRelativeValuationCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyIndustryRelativeValuations_CalculationId_CompanyId",
                table: "CompanyIndustryRelativeValuations",
                columns: new[] { "CalculationId", "CompanyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyIndustryRelativeValuations_CalculationId_GlobalRank",
                table: "CompanyIndustryRelativeValuations",
                columns: new[] { "CalculationId", "GlobalRank" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyIndustryRelativeValuations_CompanyId",
                table: "CompanyIndustryRelativeValuations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_CalculationDate_Indus~",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "CalculationDate", "IndustryId", "CalculationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~1",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "IsSelectedCurrent" },
                unique: true,
                filter: "\"IsSelectedCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~2",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculatio~",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "IsLatestEvaluation" },
                unique: true,
                filter: "\"IsLatestEvaluation\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationMetrics_CalculationId_MetricKind",
                table: "IndustryRelativeValuationMetrics",
                columns: new[] { "CalculationId", "MetricKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationOutbox_CalculationId",
                table: "IndustryRelativeValuationOutbox",
                column: "CalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationOutbox_EventIdentity",
                table: "IndustryRelativeValuationOutbox",
                column: "EventIdentity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationOutbox_IndustryId",
                table: "IndustryRelativeValuationOutbox",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationOutbox_PublishedAtUtc_CreatedAtUtc",
                table: "IndustryRelativeValuationOutbox",
                columns: new[] { "PublishedAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationSourceFacts_CompanyId_SourceKind_F~",
                table: "IndustryRelativeValuationSourceFacts",
                columns: new[] { "CompanyId", "SourceKind", "FetchedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationSourceFacts_ProviderName_SourceKin~",
                table: "IndustryRelativeValuationSourceFacts",
                columns: new[] { "ProviderName", "SourceKind", "SourceObservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_CalculationId",
                table: "IndustryWatchEvaluations",
                column: "CalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationDate_IsEffec~",
                table: "IndustryWatchEvaluations",
                columns: new[] { "IndustryId", "CalculationDate", "IsEffective" });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchEvaluations",
                columns: new[] { "IndustryId", "CalculationId", "EvaluationKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchStates_IndustryId",
                table: "IndustryWatchStates",
                column: "IndustryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_CalculationId",
                table: "IndustryWatchTransitions",
                column: "CalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchTransitions",
                columns: new[] { "IndustryId", "CalculationId", "EvaluationKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_TransitionDate",
                table: "IndustryWatchTransitions",
                columns: new[] { "IndustryId", "TransitionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyIndustryRelativeValuations");

            migrationBuilder.DropTable(
                name: "IndustryRelativeValuationMetrics");

            migrationBuilder.DropTable(
                name: "IndustryRelativeValuationOutbox");

            migrationBuilder.DropTable(
                name: "IndustryRelativeValuationSourceFacts");

            migrationBuilder.DropTable(
                name: "IndustryRelativeValuationSourceLeases");

            migrationBuilder.DropTable(
                name: "IndustryWatchEvaluations");

            migrationBuilder.DropTable(
                name: "IndustryWatchStates");

            migrationBuilder.DropTable(
                name: "IndustryWatchTransitions");

            migrationBuilder.DropTable(
                name: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropColumn(
                name: "GaugeAverage",
                table: "CompanyPsGaugeSnapshots");
        }
    }
}

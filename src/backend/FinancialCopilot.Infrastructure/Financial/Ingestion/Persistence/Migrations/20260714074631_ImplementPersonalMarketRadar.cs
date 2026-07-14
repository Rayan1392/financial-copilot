using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementPersonalMarketRadar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RadarProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventTypesJson = table.Column<string>(type: "jsonb", nullable: false),
                    MinimumSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MinimumImportance = table.Column<decimal>(type: "numeric", nullable: false),
                    Sensitivity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastEvaluatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSourceFreshnessUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailure = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RadarEventMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RadarProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InsightEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SuppressionReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppliedSensitivity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppliedPolicyVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NotificationPolicyVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MatchScore = table.Column<decimal>(type: "numeric", nullable: false),
                    HistoricalPercentile = table.Column<decimal>(type: "numeric", nullable: false),
                    ComponentInsightEventIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceFreshnessUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarEventMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadarEventMatches_InsightEvents_InsightEventId",
                        column: x => x.InsightEventId,
                        principalTable: "InsightEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RadarEventMatches_RadarProfiles_RadarProfileId",
                        column: x => x.RadarProfileId,
                        principalTable: "RadarProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RadarPreferenceAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RadarProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarPreferenceAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadarPreferenceAudits_RadarProfiles_RadarProfileId",
                        column: x => x.RadarProfileId,
                        principalTable: "RadarProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RadarSymbolOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RadarProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventTypesJson = table.Column<string>(type: "jsonb", nullable: true),
                    MinimumSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MinimumImportance = table.Column<decimal>(type: "numeric", nullable: true),
                    Sensitivity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarSymbolOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadarSymbolOverrides_RadarProfiles_RadarProfileId",
                        column: x => x.RadarProfileId,
                        principalTable: "RadarProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RadarEventMatches_Company_Insight",
                table: "RadarEventMatches",
                columns: new[] { "ExternalCompanyId", "InsightEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_RadarEventMatches_InsightEventId",
                table: "RadarEventMatches",
                column: "InsightEventId");

            migrationBuilder.CreateIndex(
                name: "IX_RadarEventMatches_NotificationIntentId",
                table: "RadarEventMatches",
                column: "NotificationIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_RadarEventMatches_Profile_EvaluatedAt",
                table: "RadarEventMatches",
                columns: new[] { "RadarProfileId", "EvaluatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UIX_RadarEventMatches_DeduplicationKey",
                table: "RadarEventMatches",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadarPreferenceAudits_Profile_OccurredAt",
                table: "RadarPreferenceAudits",
                columns: new[] { "RadarProfileId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RadarProfiles_EvaluationDue",
                table: "RadarProfiles",
                columns: new[] { "State", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UIX_RadarProfiles_Actor",
                table: "RadarProfiles",
                columns: new[] { "TenantId", "ActorId", "ActorType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadarSymbolOverrides_Company_State",
                table: "RadarSymbolOverrides",
                columns: new[] { "ExternalCompanyId", "State" });

            migrationBuilder.CreateIndex(
                name: "UIX_RadarSymbolOverrides_Profile_Company",
                table: "RadarSymbolOverrides",
                columns: new[] { "RadarProfileId", "ExternalCompanyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RadarEventMatches");

            migrationBuilder.DropTable(
                name: "RadarPreferenceAudits");

            migrationBuilder.DropTable(
                name: "RadarSymbolOverrides");

            migrationBuilder.DropTable(
                name: "RadarProfiles");
        }
    }
}

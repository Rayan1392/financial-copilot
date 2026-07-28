using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementAiMarketReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TradingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    WindowKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SupersedesReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvidenceSchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PromptPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RenderingPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SafetyPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    SnapshotIdsJson = table.Column<string>(type: "text", nullable: false),
                    InsightEventIdsJson = table.Column<string>(type: "text", nullable: false),
                    Narrative = table.Column<string>(type: "text", nullable: true),
                    CaveatsJson = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: true),
                    ModelName = table.Column<string>(type: "text", nullable: true),
                    ModelMetadataJson = table.Column<string>(type: "text", nullable: false),
                    GenerationIdempotencyKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ReservationIdempotencyKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketReports_MarketReports_SupersedesReportId",
                        column: x => x.SupersedesReportId,
                        principalTable: "MarketReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketReports_GenerationIdempotencyKey",
                table: "MarketReports",
                column: "GenerationIdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketReports_Scope_IsCurrent_PublishedAtUtc",
                table: "MarketReports",
                columns: new[] { "Scope", "IsCurrent", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketReports_Scope_TenantId_ActorId_ActorType_TradingDate_~",
                table: "MarketReports",
                columns: new[] { "Scope", "TenantId", "ActorId", "ActorType", "TradingDate", "WindowKey", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketReports_Status_NextAttemptAtUtc_LeaseExpiresAtUtc",
                table: "MarketReports",
                columns: new[] { "Status", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketReports_SupersedesReportId",
                table: "MarketReports",
                column: "SupersedesReportId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketReports_TenantId_ActorId_ActorType_Scope_IsCurrent_Pu~",
                table: "MarketReports",
                columns: new[] { "TenantId", "ActorId", "ActorType", "Scope", "IsCurrent", "PublishedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketReports");
        }
    }
}

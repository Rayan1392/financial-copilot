using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationIntentsAndCodalAlertSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NotBeforeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_NotificationIntents", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CodalAlertSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InsightEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SummaryText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReservationIdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CodalAlertSummaries", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_Actor_History",
                table: "NotificationIntents",
                columns: new[] { "TenantId", "ActorId", "ActorType", "CreatedAtUtc" });
            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_Due",
                table: "NotificationIntents",
                columns: new[] { "Status", "NotBeforeUtc", "ExpiresAtUtc" });
            migrationBuilder.CreateIndex(
                name: "UIX_NotificationIntents_Actor_Channel_Dedup",
                table: "NotificationIntents",
                columns: new[] { "TenantId", "ActorId", "ActorType", "Channel", "DeduplicationKey" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_CodalAlertSummaries_NotificationIntentId",
                table: "CodalAlertSummaries",
                column: "NotificationIntentId");
            migrationBuilder.CreateIndex(
                name: "UIX_CodalAlertSummaries_Actor_Insight",
                table: "CodalAlertSummaries",
                columns: new[] { "TenantId", "ActorId", "ActorType", "InsightEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CodalAlertSummaries");
            migrationBuilder.DropTable(name: "NotificationIntents");
        }
    }
}

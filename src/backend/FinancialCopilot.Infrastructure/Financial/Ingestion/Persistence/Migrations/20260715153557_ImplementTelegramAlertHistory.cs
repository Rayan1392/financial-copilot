using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementTelegramAlertHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAlertRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutcomeHandoffId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutcomeSequence = table.Column<int>(type: "integer", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlertRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlertRuleTriggerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SymbolKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuppressedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TerminalAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EvidenceSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DetectorVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleVersion = table.Column<int>(type: "integer", nullable: true),
                    PreferenceVersion = table.Column<int>(type: "integer", nullable: true),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WhyText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SimilarityKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RestoredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MutedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MutedScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Feedback = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FeedbackAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetainEvidenceUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetainFeedbackUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RedactedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAlertRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAlertRecords_AlertRuleTriggers_AlertRuleTriggerId",
                        column: x => x.AlertRuleTriggerId,
                        principalTable: "AlertRuleTriggers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserAlertRecords_AlertRules_AlertRuleId",
                        column: x => x.AlertRuleId,
                        principalTable: "AlertRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserAlertRecords_InsightEvents_SourceEventId",
                        column: x => x.SourceEventId,
                        principalTable: "InsightEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserAlertRecords_NotificationIntents_NotificationIntentId",
                        column: x => x.NotificationIntentId,
                        principalTable: "NotificationIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAlertRecords_NotificationOutcomeHandoffs_OutcomeHandoff~",
                        column: x => x.OutcomeHandoffId,
                        principalTable: "NotificationOutcomeHandoffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserAlertDeliveryTimeline",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAlertRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAlertDeliveryTimeline", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAlertDeliveryTimeline_NotificationIntents_NotificationI~",
                        column: x => x.NotificationIntentId,
                        principalTable: "NotificationIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAlertDeliveryTimeline_UserAlertRecords_UserAlertRecordId",
                        column: x => x.UserAlertRecordId,
                        principalTable: "UserAlertRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAlertReactionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAlertRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    HorizonCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnchorPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    AnchorAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReactionPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InputRevision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAlertReactionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAlertReactionSnapshots_UserAlertRecords_UserAlertRecord~",
                        column: x => x.UserAlertRecordId,
                        principalTable: "UserAlertRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertDeliveryTimeline_NotificationIntentId",
                table: "UserAlertDeliveryTimeline",
                column: "NotificationIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertDeliveryTimeline_Record_Time",
                table: "UserAlertDeliveryTimeline",
                columns: new[] { "UserAlertRecordId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertReactionSnapshots_Status_Updated",
                table: "UserAlertReactionSnapshots",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UIX_UserAlertReactionSnapshots_Record_Horizon_Input",
                table: "UserAlertReactionSnapshots",
                columns: new[] { "UserAlertRecordId", "HorizonCode", "InputRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_Actor_Category_Type",
                table: "UserAlertRecords",
                columns: new[] { "TenantId", "ActorId", "ActorType", "Category", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_Actor_Cursor",
                table: "UserAlertRecords",
                columns: new[] { "TenantId", "ActorId", "ActorType", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_Actor_Delivery",
                table: "UserAlertRecords",
                columns: new[] { "TenantId", "ActorId", "ActorType", "DeliveryStatus", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_Actor_Dismissed",
                table: "UserAlertRecords",
                columns: new[] { "TenantId", "ActorId", "ActorType", "DismissedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_Actor_Feedback",
                table: "UserAlertRecords",
                columns: new[] { "TenantId", "ActorId", "ActorType", "FeedbackAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_Actor_Muted",
                table: "UserAlertRecords",
                columns: new[] { "TenantId", "ActorId", "ActorType", "MutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_Actor_Symbol",
                table: "UserAlertRecords",
                columns: new[] { "TenantId", "ActorId", "ActorType", "SymbolKey", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_AlertRuleId",
                table: "UserAlertRecords",
                column: "AlertRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_AlertRuleTriggerId",
                table: "UserAlertRecords",
                column: "AlertRuleTriggerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_OutcomeHandoffId",
                table: "UserAlertRecords",
                column: "OutcomeHandoffId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_Similarity",
                table: "UserAlertRecords",
                columns: new[] { "TenantId", "ActorId", "ActorType", "SimilarityKey", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertRecords_SourceEventId",
                table: "UserAlertRecords",
                column: "SourceEventId");

            migrationBuilder.CreateIndex(
                name: "UIX_UserAlertRecords_Intent_Sequence",
                table: "UserAlertRecords",
                columns: new[] { "NotificationIntentId", "OutcomeSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAlertDeliveryTimeline");

            migrationBuilder.DropTable(
                name: "UserAlertReactionSnapshots");

            migrationBuilder.DropTable(
                name: "UserAlertRecords");
        }
    }
}

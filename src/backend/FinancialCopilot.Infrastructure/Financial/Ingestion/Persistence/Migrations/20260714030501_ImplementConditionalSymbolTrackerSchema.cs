using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementConditionalSymbolTrackerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertRuleEvaluationStates",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastValue = table.Column<decimal>(type: "numeric", nullable: true),
                    LastObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastEvidenceIdentity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Armed = table.Column<bool>(type: "boolean", nullable: false),
                    TriggerSequence = table.Column<int>(type: "integer", nullable: false),
                    LastTriggeredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CooldownEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastEvaluatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDecision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastSkipReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRuleEvaluationStates", x => x.RuleId);
                });

            migrationBuilder.CreateTable(
                name: "AlertRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MetricOrEventCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Operator = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric", nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BaselineWindow = table.Column<int>(type: "integer", nullable: true),
                    Recurrence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    ResetPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SessionPolicy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Hysteresis = table.Column<decimal>(type: "numeric", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OriginalText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ParserVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConfirmationNonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertRuleTriggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersion = table.Column<int>(type: "integer", nullable: false),
                    TriggerSequence = table.Column<int>(type: "integer", nullable: false),
                    EvidenceIdentity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ObservedValue = table.Column<decimal>(type: "numeric", nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric", nullable: false),
                    Operator = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourcePeriod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceFreshnessUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TriggeredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRuleTriggers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRuleEvaluationStates_LastEvaluated",
                table: "AlertRuleEvaluationStates",
                column: "LastEvaluatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_Actor_State",
                table: "AlertRules",
                columns: new[] { "TenantId", "ActorId", "ActorType", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_Company_State_Type",
                table: "AlertRules",
                columns: new[] { "ExternalCompanyId", "State", "RuleType" });

            migrationBuilder.CreateIndex(
                name: "UIX_AlertRules_Actor_IdempotencyKey",
                table: "AlertRules",
                columns: new[] { "TenantId", "ActorId", "ActorType", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRuleTriggers_NotificationIntentId",
                table: "AlertRuleTriggers",
                column: "NotificationIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRuleTriggers_Rule_History",
                table: "AlertRuleTriggers",
                columns: new[] { "RuleId", "TriggeredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UIX_AlertRuleTriggers_DeduplicationKey",
                table: "AlertRuleTriggers",
                column: "DeduplicationKey",
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertRuleEvaluationStates");

            migrationBuilder.DropTable(
                name: "AlertRules");

            migrationBuilder.DropTable(
                name: "AlertRuleTriggers");

        }
    }
}

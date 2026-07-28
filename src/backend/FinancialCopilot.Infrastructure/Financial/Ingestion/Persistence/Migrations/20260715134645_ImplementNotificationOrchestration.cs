using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementNotificationOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "NotificationIntents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "NotificationIntents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "NotificationIntents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "NotificationIntents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CooldownKey",
                table: "NotificationIntents",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadLetteredAtUtc",
                table: "NotificationIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecisionAtUtc",
                table: "NotificationIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionExplanation",
                table: "NotificationIntents",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                table: "NotificationIntents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAtUtc",
                table: "NotificationIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceReference",
                table: "NotificationIntents",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "NotificationIntents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorRedacted",
                table: "NotificationIntents",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAtUtc",
                table: "NotificationIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "NotificationIntents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAtUtc",
                table: "NotificationIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyVersion",
                table: "NotificationIntents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreferenceVersion",
                table: "NotificationIntents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEventId",
                table: "NotificationIntents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuppressedAtUtc",
                table: "NotificationIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "NotificationIntents"
                SET "Category" = "EventType",
                    "CooldownKey" = "EventType" || ':' || "EntityKey",
                    "ConcurrencyToken" = gen_random_uuid()
                WHERE "Category" = '' OR "CooldownKey" IS NULL OR "ConcurrencyToken" = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.CreateTable(
                name: "NotificationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScheduledForUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaximumItems = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartNumber = table.Column<int>(type: "integer", nullable: false),
                    DeliveryPartKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorRedacted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRetryAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveryAttempts_NotificationIntents_Notificati~",
                        column: x => x.NotificationIntentId,
                        principalTable: "NotificationIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOperationAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOperationAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationOperationAudits_NotificationIntents_Notificatio~",
                        column: x => x.NotificationIntentId,
                        principalTable: "NotificationIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOutcomeHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TerminalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutcomeHandoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationOutcomeHandoffs_NotificationIntents_Notificatio~",
                        column: x => x.NotificationIntentId,
                        principalTable: "NotificationIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferenceAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferenceAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QuietHoursStart = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    QuietHoursEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    MinimumSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DailyCap = table.Column<int>(type: "integer", nullable: false),
                    DigestTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationCategoryPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CooldownMinutes = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationCategoryPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationCategoryPreferences_NotificationPreferences_Pre~",
                        column: x => x.PreferenceId,
                        principalTable: "NotificationPreferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSymbolPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Muted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSymbolPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationSymbolPreferences_NotificationPreferences_Prefe~",
                        column: x => x.PreferenceId,
                        principalTable: "NotificationPreferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_BatchId",
                table: "NotificationIntents",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_Cooldown",
                table: "NotificationIntents",
                columns: new[] { "TenantId", "ActorId", "ActorType", "CooldownKey", "DeliveredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationIntents_RetryLease",
                table: "NotificationIntents",
                columns: new[] { "Status", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationBatches_Window",
                table: "NotificationBatches",
                columns: new[] { "TenantId", "ActorId", "ActorType", "Channel", "ScheduledForUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "UIX_NotificationBatches_ActorWindow",
                table: "NotificationBatches",
                columns: new[] { "TenantId", "ActorId", "ActorType", "Channel", "ScheduledForUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCategoryPreferences_PreferenceId_EventType",
                table: "NotificationCategoryPreferences",
                columns: new[] { "PreferenceId", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_IdempotencyKey",
                table: "NotificationDeliveryAttempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempts_NotificationIntentId_PartNumbe~",
                table: "NotificationDeliveryAttempts",
                columns: new[] { "NotificationIntentId", "PartNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "UIX_NotificationDeliveryAttempts_DeliveredPart",
                table: "NotificationDeliveryAttempts",
                column: "DeliveryPartKey",
                unique: true,
                filter: "\"Status\" = 'Delivered'");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOperationAudits_NotificationIntentId_OccurredAt~",
                table: "NotificationOperationAudits",
                columns: new[] { "NotificationIntentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutcomeHandoffs_NotificationIntentId_Sequence",
                table: "NotificationOutcomeHandoffs",
                columns: new[] { "NotificationIntentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutcomeHandoffs_Status_CreatedAtUtc",
                table: "NotificationOutcomeHandoffs",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferenceAudits_PreferenceId_OccurredAtUtc",
                table: "NotificationPreferenceAudits",
                columns: new[] { "PreferenceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UIX_NotificationPreferences_Actor",
                table: "NotificationPreferences",
                columns: new[] { "TenantId", "ActorId", "ActorType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSymbolPreferences_PreferenceId_ExternalCompanyId",
                table: "NotificationSymbolPreferences",
                columns: new[] { "PreferenceId", "ExternalCompanyId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationIntents_NotificationBatches_BatchId",
                table: "NotificationIntents",
                column: "BatchId",
                principalTable: "NotificationBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationIntents_NotificationBatches_BatchId",
                table: "NotificationIntents");

            migrationBuilder.DropTable(
                name: "NotificationBatches");

            migrationBuilder.DropTable(
                name: "NotificationCategoryPreferences");

            migrationBuilder.DropTable(
                name: "NotificationDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "NotificationOperationAudits");

            migrationBuilder.DropTable(
                name: "NotificationOutcomeHandoffs");

            migrationBuilder.DropTable(
                name: "NotificationPreferenceAudits");

            migrationBuilder.DropTable(
                name: "NotificationSymbolPreferences");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropIndex(
                name: "IX_NotificationIntents_BatchId",
                table: "NotificationIntents");

            migrationBuilder.DropIndex(
                name: "IX_NotificationIntents_Cooldown",
                table: "NotificationIntents");

            migrationBuilder.DropIndex(
                name: "IX_NotificationIntents_RetryLease",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "CooldownKey",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAtUtc",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "DecisionAtUtc",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "DecisionExplanation",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "DecisionReason",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "EvidenceReference",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "LastErrorRedacted",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "PolicyVersion",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "PreferenceVersion",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "SourceEventId",
                table: "NotificationIntents");

            migrationBuilder.DropColumn(
                name: "SuppressedAtUtc",
                table: "NotificationIntents");
        }
    }
}

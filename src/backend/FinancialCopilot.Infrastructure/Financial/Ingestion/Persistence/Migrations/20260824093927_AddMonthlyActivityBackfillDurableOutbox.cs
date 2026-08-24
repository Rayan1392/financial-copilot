using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyActivityBackfillDurableOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonthlyActivityBackfillBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActiveSlot = table.Column<int>(type: "integer", nullable: true),
                    TargetShamsiYear = table.Column<int>(type: "integer", nullable: true),
                    TargetShamsiMonth = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishingStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedCount = table.Column<int>(type: "integer", nullable: false),
                    ProcessedCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    RetryableCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyActivityBackfillBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyActivityBackfillOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyActivityBackfillOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyActivityBackfillOutbox_MonthlyActivityBackfillBatche~",
                        column: x => x.BatchId,
                        principalTable: "MonthlyActivityBackfillBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyActivityBackfillBatches_ActiveSlot",
                table: "MonthlyActivityBackfillBatches",
                column: "ActiveSlot",
                unique: true,
                filter: "\"ActiveSlot\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyActivityBackfillBatches_CreatedAt",
                table: "MonthlyActivityBackfillBatches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyActivityBackfillBatches_Status",
                table: "MonthlyActivityBackfillBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyActivityBackfillOutbox_BatchId_IdempotencyKey",
                table: "MonthlyActivityBackfillOutbox",
                columns: new[] { "BatchId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyActivityBackfillOutbox_BatchId_Sequence",
                table: "MonthlyActivityBackfillOutbox",
                columns: new[] { "BatchId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyActivityBackfillOutbox_Status_LeaseExpiresAt_Created~",
                table: "MonthlyActivityBackfillOutbox",
                columns: new[] { "Status", "LeaseExpiresAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyActivityBackfillOutbox");

            migrationBuilder.DropTable(
                name: "MonthlyActivityBackfillBatches");
        }
    }
}

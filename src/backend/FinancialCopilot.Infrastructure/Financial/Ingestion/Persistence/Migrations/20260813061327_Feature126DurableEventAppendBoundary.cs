using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature126DurableEventAppendBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Feature126Events",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RunId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventSequence = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedPredecessorState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FencingToken = table.Column<Guid>(type: "uuid", nullable: false),
                    TehranDate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RecoveredFromRunId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FieldsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    AppendedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feature126Events", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "Feature126EventStreams",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TehranDate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FencingToken = table.Column<Guid>(type: "uuid", nullable: false),
                    NextSequence = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsTerminal = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feature126EventStreams", x => x.RunId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Feature126Events_RunId_AppendedAtUtc",
                table: "Feature126Events",
                columns: new[] { "RunId", "AppendedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Feature126Events_RunId_EventSequence",
                table: "Feature126Events",
                columns: new[] { "RunId", "EventSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Feature126Events");

            migrationBuilder.DropTable(
                name: "Feature126EventStreams");
        }
    }
}

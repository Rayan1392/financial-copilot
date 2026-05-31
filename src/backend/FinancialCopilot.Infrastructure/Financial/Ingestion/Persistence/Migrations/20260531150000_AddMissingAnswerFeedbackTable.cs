using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingAnswerFeedbackTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MissingAnswerFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    QueryText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    QueryHashSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedMetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AffectedDataCodeOrName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SymbolCountTotal = table.Column<int>(type: "integer", nullable: false),
                    SymbolCountMatched = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DateBucket = table.Column<DateOnly>(type: "date", nullable: false),
                    Context = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FrequencyCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissingAnswerFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MissingAnswerFeedbacks_ActorId",
                table: "MissingAnswerFeedbacks",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingAnswerFeedbacks_Classification",
                table: "MissingAnswerFeedbacks",
                column: "Classification");

            migrationBuilder.CreateIndex(
                name: "IX_MissingAnswerFeedbacks_RequestedMetricCode",
                table: "MissingAnswerFeedbacks",
                column: "RequestedMetricCode");

            migrationBuilder.CreateIndex(
                name: "IX_MissingAnswerFeedbacks_SubmittedAt",
                table: "MissingAnswerFeedbacks",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MissingAnswerFeedbacks_DateBucket",
                table: "MissingAnswerFeedbacks",
                column: "DateBucket");

            migrationBuilder.CreateIndex(
                name: "IX_MissingAnswerFeedbacks_ActorId_QueryHashSha256_Classification_DateBucket",
                table: "MissingAnswerFeedbacks",
                columns: new[] { "ActorId", "QueryHashSha256", "Classification", "DateBucket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MissingAnswerFeedbacks");
        }
    }
}

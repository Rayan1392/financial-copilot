using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInsightEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsightEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IndustryCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InsightType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ImportanceScore = table.Column<decimal>(type: "numeric", nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "numeric", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceEntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceEntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourcePeriod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeduplicationKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SuggestedActionsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsightEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsightEvents_DetectedAtUtc",
                table: "InsightEvents",
                column: "DetectedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InsightEvents_ExternalCompanyId",
                table: "InsightEvents",
                column: "ExternalCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_InsightEvents_IndustryCode",
                table: "InsightEvents",
                column: "IndustryCode");

            migrationBuilder.CreateIndex(
                name: "IX_InsightEvents_InsightType",
                table: "InsightEvents",
                column: "InsightType");

            migrationBuilder.CreateIndex(
                name: "IX_InsightEvents_Severity",
                table: "InsightEvents",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_InsightEvents_Symbol",
                table: "InsightEvents",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "UIX_InsightEvents_DeduplicationKey",
                table: "InsightEvents",
                column: "DeduplicationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsightEvents");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Semantics.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicMetricAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DynamicMetricAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Expression = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NormalizedExpression = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    FrequencyCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DisableReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicMetricAliases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetricAliasCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Expression = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NormalizedExpression = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SuggestedMetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SuggestedMetricVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    FrequencyCount = table.Column<int>(type: "integer", nullable: false),
                    DistinctActorCount = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceExamplesJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PromotedAliasId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricAliasCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicMetricAliases_Language_Status",
                table: "DynamicMetricAliases",
                columns: new[] { "Language", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicMetricAliases_NormalizedExpression_Language_MetricCo~",
                table: "DynamicMetricAliases",
                columns: new[] { "NormalizedExpression", "Language", "MetricCode" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_MetricAliasCandidates_LastSeenAt",
                table: "MetricAliasCandidates",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_MetricAliasCandidates_NormalizedExpression_Language_Suggest~",
                table: "MetricAliasCandidates",
                columns: new[] { "NormalizedExpression", "Language", "SuggestedMetricCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetricAliasCandidates_Status",
                table: "MetricAliasCandidates",
                column: "Status");

            // Seed well-known PE / PS aliases (ManualSeed, Active, confidence=1.0)
            var seedCreatedAt = new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);
            var seedRows = new (string Id, string Expr, string Norm, string Lang, string Code)[]
            {
                ("11111111-1111-1111-1111-000000000001", "pe",       "pe",       "en", "PE_TTM"),
                ("11111111-1111-1111-1111-000000000002", "p/e",      "p/e",      "en", "PE_TTM"),
                ("11111111-1111-1111-1111-000000000003", "p e",      "p e",      "en", "PE_TTM"),
                ("11111111-1111-1111-1111-000000000004", "پی ای",     "پی ای",     "fa", "PE_TTM"),
                ("11111111-1111-1111-1111-000000000005", "پی به ای",  "پی به ای",  "fa", "PE_TTM"),
                ("11111111-1111-1111-1111-000000000006", "ps",       "ps",       "en", "PS_TTM"),
                ("11111111-1111-1111-1111-000000000007", "p/s",      "p/s",      "en", "PS_TTM"),
                ("11111111-1111-1111-1111-000000000008", "p s",      "p s",      "en", "PS_TTM"),
                ("11111111-1111-1111-1111-000000000009", "پی اس",     "پی اس",     "fa", "PS_TTM"),
                ("11111111-1111-1111-1111-000000000010", "پی به اس",  "پی به اس",  "fa", "PS_TTM"),
            };

            foreach (var (id, expr, norm, lang, code) in seedRows)
            {
                migrationBuilder.InsertData(
                    table: "DynamicMetricAliases",
                    columns: new[] { "Id", "Expression", "NormalizedExpression", "Language", "MetricCode",
                                     "MetricVersion", "Source", "Status", "ConfidenceScore", "FrequencyCount",
                                     "CreatedAt", "CreatedBy" },
                    values: new object[] { Guid.Parse(id), expr, norm, lang, code,
                                           "v1", "ManualSeed", "Active", 1.0m, 0,
                                           seedCreatedAt, "seed" });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DynamicMetricAliases");

            migrationBuilder.DropTable(
                name: "MetricAliasCandidates");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivedMetricResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DerivedMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SymbolId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CalculationPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeriodType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WarningsJson = table.Column<string>(type: "text", nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    DependencyEvidenceJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivedMetrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DerivedMetrics_SymbolId_MetricCode_MetricVersion_Calculatio~",
                table: "DerivedMetrics",
                columns: new[] { "SymbolId", "MetricCode", "MetricVersion", "CalculationPolicyVersion", "PeriodEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DerivedMetrics");
        }
    }
}

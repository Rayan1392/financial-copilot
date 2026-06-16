using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSymbolsAddExternalCompanyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                TRUNCATE TABLE
                    "FeatureComputationJobs",
                    "FeatureSnapshots",
                    "DerivedMetrics"
                CASCADE;
                """);

            migrationBuilder.DropTable(
                name: "Symbols");

            migrationBuilder.DropIndex(
                name: "IX_FeatureSnapshots_SymbolId_FeatureCode_FeatureVersion_Policy~",
                table: "FeatureSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_DerivedMetrics_SymbolId_MetricCode_MetricVersion_Calculatio~",
                table: "DerivedMetrics");

            migrationBuilder.DropColumn(
                name: "SymbolId",
                table: "FeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "SymbolId",
                table: "FeatureComputationJobs");

            migrationBuilder.DropColumn(
                name: "SymbolId",
                table: "DerivedMetrics");

            migrationBuilder.AddColumn<string>(
                name: "ExternalCompanyId",
                table: "FeatureSnapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalCompanyId",
                table: "FeatureComputationJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCompanyId",
                table: "DerivedMetrics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureSnapshots_ExternalCompanyId_FeatureCode_FeatureVersi~",
                table: "FeatureSnapshots",
                columns: new[] { "ExternalCompanyId", "FeatureCode", "FeatureVersion", "PolicyVersion", "PeriodEnd", "InputFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DerivedMetrics_ExternalCompanyId_MetricCode_MetricVersion_C~",
                table: "DerivedMetrics",
                columns: new[] { "ExternalCompanyId", "MetricCode", "MetricVersion", "CalculationPolicyVersion", "PeriodEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeatureSnapshots_ExternalCompanyId_FeatureCode_FeatureVersi~",
                table: "FeatureSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_DerivedMetrics_ExternalCompanyId_MetricCode_MetricVersion_C~",
                table: "DerivedMetrics");

            migrationBuilder.DropColumn(
                name: "ExternalCompanyId",
                table: "FeatureSnapshots");

            migrationBuilder.DropColumn(
                name: "ExternalCompanyId",
                table: "FeatureComputationJobs");

            migrationBuilder.DropColumn(
                name: "ExternalCompanyId",
                table: "DerivedMetrics");

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolId",
                table: "FeatureSnapshots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolId",
                table: "FeatureComputationJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolId",
                table: "DerivedMetrics",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Symbols",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalSymbolId = table.Column<string>(type: "text", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinkageBasis = table.Column<string>(type: "text", nullable: true),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    SymbolCode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Symbols", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureSnapshots_SymbolId_FeatureCode_FeatureVersion_Policy~",
                table: "FeatureSnapshots",
                columns: new[] { "SymbolId", "FeatureCode", "FeatureVersion", "PolicyVersion", "PeriodEnd", "InputFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DerivedMetrics_SymbolId_MetricCode_MetricVersion_Calculatio~",
                table: "DerivedMetrics",
                columns: new[] { "SymbolId", "MetricCode", "MetricVersion", "CalculationPolicyVersion", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Symbols_ProviderName_ExternalSymbolId",
                table: "Symbols",
                columns: new[] { "ProviderName", "ExternalSymbolId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Symbols_SymbolCode",
                table: "Symbols",
                column: "SymbolCode");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Semantics.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSemanticCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialMetricDefinitions",
                columns: table => new
                {
                    MetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialMetricDefinitions", x => new { x.MetricCode, x.MetricVersion });
                });

            migrationBuilder.CreateTable(
                name: "MetricAliases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Expression = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ComparisonQualifier = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricAliases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetricCalculationPolicies",
                columns: table => new
                {
                    MetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Comparison = table.Column<string>(type: "text", nullable: true),
                    MissingDataPolicy = table.Column<string>(type: "text", nullable: false),
                    FormulaIdentifier = table.Column<string>(type: "text", nullable: true),
                    FormulaDescription = table.Column<string>(type: "text", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricCalculationPolicies", x => new { x.MetricCode, x.PolicyVersion });
                });

            migrationBuilder.CreateTable(
                name: "MetricDependencies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DependencyMetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequiredDefinitionVersion = table.Column<string>(type: "text", nullable: true),
                    Required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricDependencies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricAliases_Language_Expression",
                table: "MetricAliases",
                columns: new[] { "Language", "Expression" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricDependencies_MetricCode_MetricVersion",
                table: "MetricDependencies",
                columns: new[] { "MetricCode", "MetricVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialMetricDefinitions");

            migrationBuilder.DropTable(
                name: "MetricAliases");

            migrationBuilder.DropTable(
                name: "MetricCalculationPolicies");

            migrationBuilder.DropTable(
                name: "MetricDependencies");
        }
    }
}

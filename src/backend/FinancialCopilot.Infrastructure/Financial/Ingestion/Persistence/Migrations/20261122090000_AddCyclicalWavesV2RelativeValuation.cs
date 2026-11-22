using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations;

[DbContext(typeof(FinancialIngestionDbContext))]
[Migration("20261122090000_AddCyclicalWavesV2RelativeValuation")]
public sealed class AddCyclicalWavesV2RelativeValuation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DetailsFetchedAtUtc", schema: "cyclicalwaves", table: "EquilibriumRanges",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "InputSourceVersion", table: "IndustryRelativeValuationCalculations",
            type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ValidUntilUtc", table: "IndustryRelativeValuationCalculations",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_RelativeValuation_GroupSourceVersionDate",
            table: "IndustryRelativeValuationCalculations",
            columns: ["GroupId", "InputSourceVersion", "CalculationDate", "CalculationVersion"],
            filter: "\"InputSourceVersion\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_RelativeValuation_GroupSourceVersionDate", "IndustryRelativeValuationCalculations");
        migrationBuilder.DropColumn("ValidUntilUtc", "IndustryRelativeValuationCalculations");
        migrationBuilder.DropColumn("InputSourceVersion", "IndustryRelativeValuationCalculations");
        migrationBuilder.DropColumn("DetailsFetchedAtUtc", "EquilibriumRanges", "cyclicalwaves");
    }
}

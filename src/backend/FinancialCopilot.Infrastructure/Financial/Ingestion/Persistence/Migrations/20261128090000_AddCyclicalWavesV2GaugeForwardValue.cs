using System.Numerics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations;

/// <summary>
/// Adds the provider's forward ratio to a stored gauge. Additive and nullable: existing rows keep null until the next
/// gauge detail sync writes the value.
/// </summary>
[DbContext(typeof(FinancialIngestionDbContext))]
[Migration("20261128090000_AddCyclicalWavesV2GaugeForwardValue")]
public sealed class AddCyclicalWavesV2GaugeForwardValue : Migration
{
    private const string Schema = "cyclicalwaves";
    private const string Table = "GaugeDetails";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var part in new[] { "ForwardHigh", "ForwardMiddle", "ForwardLow" })
            migrationBuilder.AddColumn<BigInteger>(name: part, schema: Schema, table: Table, type: "numeric", nullable: true);
        foreach (var part in new[] { "ForwardSign", "ForwardScale" })
            migrationBuilder.AddColumn<short>(name: part, schema: Schema, table: Table, type: "smallint", nullable: true);
        migrationBuilder.AddCheckConstraint("CK_CyclicalWavesV2GaugeDetails_ForwardParts", Table,
            CyclicalWavesV2GaugeDetailRowConfiguration.ForwardPartsSql, Schema);
        migrationBuilder.AddCheckConstraint("CK_CyclicalWavesV2GaugeDetails_ForwardRanges", Table,
            CyclicalWavesV2GaugeDetailRowConfiguration.ForwardRangesSql, Schema);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_CyclicalWavesV2GaugeDetails_ForwardRanges", Table, Schema);
        migrationBuilder.DropCheckConstraint("CK_CyclicalWavesV2GaugeDetails_ForwardParts", Table, Schema);
        foreach (var part in new[] { "ForwardScale", "ForwardSign", "ForwardLow", "ForwardMiddle", "ForwardHigh" })
            migrationBuilder.DropColumn(part, Table, Schema);
    }
}

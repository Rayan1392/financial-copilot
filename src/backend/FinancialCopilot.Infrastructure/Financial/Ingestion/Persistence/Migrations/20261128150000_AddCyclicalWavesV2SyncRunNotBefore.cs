using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations;

/// <summary>
/// Adds the due time of a queued CyclicalWaves v2 run. Additive and nullable: existing rows keep null, which means
/// "due at once", so the queue behaves as before for them.
/// </summary>
[DbContext(typeof(FinancialIngestionDbContext))]
[Migration("20261128150000_AddCyclicalWavesV2SyncRunNotBefore")]
public sealed class AddCyclicalWavesV2SyncRunNotBefore : Migration
{
    private const string Schema = "cyclicalwaves";
    private const string Table = "SyncRuns";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NotBeforeUtc",
            schema: Schema,
            table: Table,
            type: "timestamp with time zone",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("NotBeforeUtc", Table, Schema);
}

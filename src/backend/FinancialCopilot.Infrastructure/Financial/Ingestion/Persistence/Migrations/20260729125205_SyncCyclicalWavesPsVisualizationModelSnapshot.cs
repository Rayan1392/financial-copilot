using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations;

/// <summary>
/// Snapshot-repair migration for the already-applied CyclicalWaves P/S schema.
/// The generated designer and model snapshot describe the current model; the
/// schema itself was created by 20260729120000_AddCyclicalWavesPsVisualization.
/// </summary>
public partial class SyncCyclicalWavesPsVisualizationModelSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Metadata repair only. Existing database objects are unchanged.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Metadata repair only. Existing database objects are unchanged.
    }
}

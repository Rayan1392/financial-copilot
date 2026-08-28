using Microsoft.EntityFrameworkCore.Migrations;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations;

[DbContext(typeof(FinancialIngestionDbContext))]
[Migration("20260828100000_AddCyclicalWavesProviderObservationDates")]
public partial class AddCyclicalWavesProviderObservationDates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "ProviderObservationDate",
            table: "CyclicalWavesMetricSnapshots",
            type: "date",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "UX_CyclicalWavesMetricSnapshots_ProviderObservationDate",
            table: "CyclicalWavesMetricSnapshots",
            columns: new[] { "CompanyId", "ProviderName", "MetricType", "ProviderObservationDate" },
            unique: true);

        migrationBuilder.DropCheckConstraint(
            name: "CK_CyclicalWavesMetricSnapshots_MetricType",
            table: "CyclicalWavesMetricSnapshots");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CyclicalWavesMetricSnapshots_MetricType",
            table: "CyclicalWavesMetricSnapshots",
            sql: "\"MetricType\" IN ('PS', 'LastPS', 'PE', 'LastPE', 'Equilibrium')");

        migrationBuilder.DropCheckConstraint(
            name: "CK_CyclicalWavesAcquisitionChecks_MetricType",
            table: "CyclicalWavesAcquisitionChecks");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CyclicalWavesAcquisitionChecks_MetricType",
            table: "CyclicalWavesAcquisitionChecks",
            sql: "\"MetricType\" IN ('PS', 'LastPS', 'PE', 'LastPE', 'Equilibrium')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_CyclicalWavesMetricSnapshots_MetricType",
            table: "CyclicalWavesMetricSnapshots");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CyclicalWavesMetricSnapshots_MetricType",
            table: "CyclicalWavesMetricSnapshots",
            sql: "\"MetricType\" IN ('PS', 'PE', 'Equilibrium')");

        migrationBuilder.DropCheckConstraint(
            name: "CK_CyclicalWavesAcquisitionChecks_MetricType",
            table: "CyclicalWavesAcquisitionChecks");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CyclicalWavesAcquisitionChecks_MetricType",
            table: "CyclicalWavesAcquisitionChecks",
            sql: "\"MetricType\" IN ('PS', 'PE', 'Equilibrium')");

        migrationBuilder.DropIndex(
            name: "UX_CyclicalWavesMetricSnapshots_ProviderObservationDate",
            table: "CyclicalWavesMetricSnapshots");

        migrationBuilder.DropColumn(
            name: "ProviderObservationDate",
            table: "CyclicalWavesMetricSnapshots");
    }
}

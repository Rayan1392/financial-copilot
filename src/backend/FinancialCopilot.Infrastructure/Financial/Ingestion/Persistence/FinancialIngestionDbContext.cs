using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class FinancialIngestionDbContext(DbContextOptions<FinancialIngestionDbContext> options) : DbContext(options)
{
    public DbSet<NormalizedCompanyRow> Companies => Set<NormalizedCompanyRow>();

    public DbSet<NormalizedSymbolRow> Symbols => Set<NormalizedSymbolRow>();

    public DbSet<NormalizedFinancialStatementRow> FinancialStatements => Set<NormalizedFinancialStatementRow>();

    public DbSet<NormalizedFinancialStatementLineItemRow> FinancialStatementLineItems =>
        Set<NormalizedFinancialStatementLineItemRow>();

    public DbSet<NormalizedMonthlyReportRow> MonthlyReports => Set<NormalizedMonthlyReportRow>();

    public DbSet<NormalizedMonthlyReportLineItemRow> MonthlyReportLineItems =>
        Set<NormalizedMonthlyReportLineItemRow>();

    public DbSet<DataSyncRunRow> SyncRuns => Set<DataSyncRunRow>();

    public DbSet<MetricRecalculationRequestRow> MetricRecalculationRequests =>
        Set<MetricRecalculationRequestRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FinancialIngestionDbContext).Assembly,
            type => type.Namespace == typeof(FinancialIngestionDbContext).Namespace);
}

using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Application.Scanner;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class PersistedDerivedMetricResultStore(
    FinancialIngestionDbContext dbContext,
    IScannerCache? scannerCache = null,
    TimeProvider? timeProvider = null) : IDerivedMetricResultStore
{
    public async Task StoreAsync(DerivedMetric metric, CancellationToken cancellationToken)
    {
        var periodEnd = metric.Period.EndDate ??
            throw new ArgumentException("Persisted derived metrics require a closed period.", nameof(metric));
        var periodStart = metric.Period.StartDate ??
            throw new ArgumentException("Persisted derived metrics require a closed period.", nameof(metric));
        var row = await dbContext.DerivedMetrics.SingleOrDefaultAsync(
            candidate =>
                candidate.SymbolId == metric.SymbolId &&
                candidate.MetricCode == metric.Code.Value &&
                candidate.MetricVersion == metric.MetricVersion.Value &&
                candidate.CalculationPolicyVersion == metric.CalculationPolicyVersion.Value &&
                candidate.PeriodEnd == periodEnd,
            cancellationToken);

        if (row is null)
        {
            row = new DerivedMetricRow
            {
                Id = metric.Id,
                SymbolId = metric.SymbolId,
                MetricCode = metric.Code.Value,
                MetricVersion = metric.MetricVersion.Value,
                CalculationPolicyVersion = metric.CalculationPolicyVersion.Value,
                PeriodEnd = periodEnd
            };
            dbContext.DerivedMetrics.Add(row);
        }

        Apply(row, metric, periodStart);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Another concurrent worker inserted the same row between our read and write.
            // Detach the conflicting entity, reload the winner, and apply our values as an update.
            dbContext.ChangeTracker.Clear();
            row = await dbContext.DerivedMetrics.SingleAsync(
                candidate =>
                    candidate.SymbolId == metric.SymbolId &&
                    candidate.MetricCode == metric.Code.Value &&
                    candidate.MetricVersion == metric.MetricVersion.Value &&
                    candidate.CalculationPolicyVersion == metric.CalculationPolicyVersion.Value &&
                    candidate.PeriodEnd == periodEnd,
                cancellationToken);
            Apply(row, metric, periodStart);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (scannerCache is not null)
        {
            await scannerCache.InvalidateAsync(
                new ScannerCacheInvalidation(
                    $"DerivedMetric.{metric.Code.Value}",
                    (timeProvider ?? TimeProvider.System).GetUtcNow()),
                cancellationToken);
        }
    }

    private static void Apply(DerivedMetricRow row, DerivedMetric metric, DateOnly periodStart)
    {
        row.PeriodType = metric.Period.Type.ToString();
        row.PeriodStart = periodStart;
        row.Value = metric.Value;
        row.Unit = metric.Unit.ToString();
        row.ObservedAt = metric.Quality.ObservedAt;
        row.LastSynchronizedAt = metric.Quality.LastSynchronizedAt;
        row.WarningsJson = JsonSerializer.Serialize(metric.Quality.Warnings, JsonOptions);
        row.SourceEvidenceJson = JsonSerializer.Serialize(metric.SourceEvidence, JsonOptions);
        row.DependencyEvidenceJson = JsonSerializer.Serialize(metric.DependencyEvidence, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

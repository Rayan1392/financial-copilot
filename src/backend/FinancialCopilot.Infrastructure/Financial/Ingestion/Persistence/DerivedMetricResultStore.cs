using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class PersistedDerivedMetricResultStore(
    FinancialIngestionDbContext dbContext) : IDerivedMetricResultStore
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

        row.PeriodType = metric.Period.Type.ToString();
        row.PeriodStart = periodStart;
        row.Value = metric.Value;
        row.Unit = metric.Unit.ToString();
        row.ObservedAt = metric.Quality.ObservedAt;
        row.LastSynchronizedAt = metric.Quality.LastSynchronizedAt;
        row.WarningsJson = JsonSerializer.Serialize(metric.Quality.Warnings, JsonOptions);
        row.SourceEvidenceJson = JsonSerializer.Serialize(metric.SourceEvidence, JsonOptions);
        row.DependencyEvidenceJson = JsonSerializer.Serialize(metric.DependencyEvidence, JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

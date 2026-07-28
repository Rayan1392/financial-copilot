namespace FinancialCopilot.Application.FinancialData.Metrics;

/// <summary>
/// Drains the <c>MetricRecalculationRequests</c> outbox written by ingestion and triggers the
/// existing <see cref="IDerivedMetricRecalculationCommand"/> for affected symbol(s) and dependent
/// registered metrics. Provider-agnostic — the same processor serves CyclicalWaves, CodalDb, and
/// any future provider. Bounded batch + idempotent (DerivedMetrics upsert).
/// </summary>
public interface IMetricRecalculationProcessor
{
    /// <returns>Number of outbox rows processed in this tick (success + failure).</returns>
    Task<MetricRecalculationProcessingResult> ProcessPendingAsync(
        int maximumBatch,
        CancellationToken cancellationToken);
}

public sealed record MetricRecalculationProcessingResult(
    int ProcessedRequestCount,
    int CompletedRequestCount,
    int MetricsRecomputed,
    int FailedRequestCount);

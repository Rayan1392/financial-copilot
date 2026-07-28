using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed record DataSyncRequest(
    Guid RequestId,
    ProviderDataset Dataset,
    string? ExternalReference,
    DateTimeOffset RequestedAt,
    string IdempotencyKey,
    string? ProviderName = null,
    /// <summary>
    /// Explicit import mode for this run's provenance (spec 051). When null the mode is derived from
    /// the resolved <see cref="ProviderSources"/> descriptor for <see cref="ProviderName"/>.
    /// </summary>
    SourceMode? Mode = null,
    string? SourceDateRangeStartJalali = null,
    string? SourceDateRangeEndJalali = null,
    /// <summary>
    /// One-off Shamsi start-year override for a current-API backfill (spec 053 AC #3). Travels with
    /// the request so the override reaches the worker scope that performs the fetch; null means use
    /// the configured boundary. Ignored by providers that have no Shamsi-year boundary.
    /// </summary>
    int? FromShamsiYearOverride = null);

public enum DataSyncRunStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public sealed record DataSyncRun(
    Guid Id,
    string IdempotencyKey,
    ProviderDataset Dataset,
    string? ExternalReference,
    DataSyncRunStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int ProcessedRecords,
    int ErrorCount,
    string? ErrorMessage,
    string? SourcePayloadChecksum,
    string? ProviderName = null,
    LogicalVendor? Vendor = null,
    PhysicalSource? Source = null,
    SourceMode? Mode = null,
    string? SourceDateRangeStartJalali = null,
    string? SourceDateRangeEndJalali = null);

public sealed record DataSyncProcessingResult(
    DataSyncRun Run,
    bool AlreadyProcessed);

public sealed record DerivedMetricRecalculationRequested(
    Guid Id,
    ProviderDataset SourceDataset,
    string? ExternalReference,
    string SourcePayloadChecksum,
    DateTimeOffset RequestedAt);

public interface IDataSyncRequestPublisher
{
    Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken);
}

public interface IDataSyncRequestConsumer
{
    Task ConsumeAsync(
        Func<DataSyncRequest, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}

public interface IFinancialDataSyncProcessor
{
    Task<DataSyncProcessingResult> ProcessAsync(
        DataSyncRequest request,
        CancellationToken cancellationToken);

    Task<DataSyncProcessingResult> ProcessPayloadAsync(
        DataSyncRequest request,
        ProviderRawPayload payload,
        CancellationToken cancellationToken);
}

public interface IDataSyncRunReader
{
    Task<IReadOnlyCollection<DataSyncRun>> QueryRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public interface IDerivedMetricRecalculationPublisher
{
    Task PublishAsync(
        DerivedMetricRecalculationRequested request,
        CancellationToken cancellationToken);
}

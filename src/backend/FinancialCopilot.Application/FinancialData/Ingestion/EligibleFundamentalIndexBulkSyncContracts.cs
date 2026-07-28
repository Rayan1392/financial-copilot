using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Reads eligible Noavaran current-API company references from the operator-facing
/// <c>NoavaranEligibleCompanies</c> view for batch admin workflows.
/// </summary>
public interface INoavaranEligibleCompanyReferenceReader
{
    Task<IReadOnlyCollection<string>> ReadExternalReferencesAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// DataAdmin-only batch orchestrator for the curated NADPCO fundamental-index sync. It enumerates
/// eligible companies, then publishes the same per-company <see cref="ProviderDataset.FundamentalIndexes"/>
/// requests used by the single-company admin endpoint.
/// </summary>
public interface IEligibleFundamentalIndexBulkSyncService
{
    Task<EligibleFundamentalIndexBulkSyncResult> RunAsync(
        EligibleFundamentalIndexBulkSyncRequest request,
        CancellationToken cancellationToken);
}

public sealed record EligibleFundamentalIndexBulkSyncRequest(
    string RequestedBy,
    string ProviderName,
    string? BatchIdempotencyKey,
    int? MaxItems,
    bool DryRun);

public sealed record EligibleFundamentalIndexBulkSyncItemResult(
    string ExternalReference,
    string Status,
    string IdempotencyKey,
    string? Error = null);

public sealed record EligibleFundamentalIndexBulkSyncResult(
    Guid RequestId,
    ProviderDataset Dataset,
    string Source,
    DateTimeOffset RequestedAt,
    string IdempotencyKey,
    string Status,
    int EligibleCount,
    int QueuedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyCollection<EligibleFundamentalIndexBulkSyncItemResult> Items);

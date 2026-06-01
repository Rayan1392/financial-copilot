namespace FinancialCopilot.API.Contracts;

public sealed record AdminDataSyncRequest(
    string? ExternalReference = null,
    string? IdempotencyKey = null,
    string? ProviderName = null);

public sealed record AdminDataSyncQueuedResponse(
    Guid RequestId,
    string Dataset,
    string? ExternalReference,
    DateTimeOffset RequestedAt,
    string IdempotencyKey,
    string Status);

public sealed record AdminDataSyncRunResponse(
    Guid RunId,
    string Dataset,
    string? ExternalReference,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int ProcessedRecords,
    int ErrorCount,
    string? ErrorMessage,
    string? SourcePayloadChecksum);

public sealed record AdminProviderHealthResponse(
    string ProviderName,
    string Status,
    DateTimeOffset CheckedAt,
    string? Detail);

public sealed record AdminCyclicalWavesFullSyncResponse(
    int SymbolsSynced,
    int TickersSynced,
    int TickersFailed,
    IReadOnlyCollection<string> FailedTickers,
    string Duration);

public sealed record AdminCodalDbSyncResponse(
    bool FullReload,
    int CompaniesConsidered,
    int CompaniesEnqueued,
    int FailedCompanies,
    IReadOnlyCollection<int> FailedCompanyIds,
    DateTimeOffset? AdvancedWatermark,
    string Duration);

public sealed record AdminStockMarketSyncResponse(
    string Dataset,
    int RowsRead,
    int RowsPersisted,
    DateTimeOffset? AdvancedWatermark,
    string Duration);

public sealed record AdminStockMarketSyncStateResponse(
    string Dataset,
    DateTimeOffset? Watermark,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt);

public sealed record AdminMissingAnswerFeedbackItem(
    Guid Id,
    string ActorId,
    string QueryText,
    string Classification,
    string? RequestedMetricCode,
    string? AffectedDataCodeOrName,
    int SymbolCountTotal,
    int SymbolCountMatched,
    DateTimeOffset SubmittedAt,
    int FrequencyCount,
    DateTimeOffset? ResolvedAt);

public sealed record AdminMissingAnswerFeedbackSummary(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyDictionary<string, int> CountsByClassification,
    int TotalCount);

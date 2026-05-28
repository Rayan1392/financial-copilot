namespace FinancialCopilot.API.Contracts;

public sealed record AdminDataSyncRequest(
    string? ExternalReference = null,
    string? IdempotencyKey = null);

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

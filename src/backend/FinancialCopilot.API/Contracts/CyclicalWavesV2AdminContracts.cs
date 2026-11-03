namespace FinancialCopilot.API.Contracts;

public sealed record CyclicalWavesV2RunCountsResponse(int Total, int Processed, int Updated, int Unchanged, int Failed);

public sealed record CyclicalWavesV2RunResponse(
    Guid RunId,
    string Scope,
    string? Ticker,
    string? AnalysisMode,
    string Trigger,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    CyclicalWavesV2RunCountsResponse Counts,
    string? ErrorSummary,
    string? RequestedBy);

public sealed record CyclicalWavesV2InlineRunResponse(
    Guid? RunId,
    string Status,
    CyclicalWavesV2RunCountsResponse Counts,
    string? ErrorSummary);

public sealed record CyclicalWavesV2QueuedRunResponse(Guid RunId, string Status);

public sealed record CyclicalWavesV2ConflictResponse(string Code, Guid ExistingRunId);

public sealed record CyclicalWavesV2LastSuccessResponse(string Scope, DateTimeOffset CompletedAt);

public sealed record CyclicalWavesV2StatusResponse(
    bool IngestionEnabled,
    bool DailySyncEnabled,
    string DailySyncCronUtc,
    DateTimeOffset? NextScheduledRunUtc,
    bool CredentialsConfigured,
    string ChatProvider,
    CyclicalWavesV2RunResponse? ActiveRun,
    IReadOnlyList<CyclicalWavesV2LastSuccessResponse> LastSuccessByScope,
    IReadOnlyList<CyclicalWavesV2RunResponse> RecentRuns);

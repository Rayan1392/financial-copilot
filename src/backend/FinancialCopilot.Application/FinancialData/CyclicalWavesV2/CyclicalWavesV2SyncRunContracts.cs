namespace FinancialCopilot.Application.FinancialData.CyclicalWavesV2;

public enum CyclicalWavesV2SyncScope
{
    Catalog,
    Ticker,
    Screener,
    IncomeStatements,
    Analysis,
    GaugeCatalog,
    GaugeDetails,
    EquilibriumCatalog,
    EquilibriumDetails,
    TickerGaugeEquilibrium,
    Full
}

public enum CyclicalWavesV2AnalysisMode
{
    Incremental,
    Full
}

public enum CyclicalWavesV2SyncTrigger
{
    Scheduled,
    StartupCatchUp,
    Admin
}

public enum CyclicalWavesV2SyncRunStatus
{
    Requested,
    Running,
    Succeeded,
    CompletedWithErrors,
    Failed,
    SkippedAlreadyRunning
}

public sealed record CyclicalWavesV2SyncRequest(
    CyclicalWavesV2SyncScope Scope,
    string? Ticker,
    CyclicalWavesV2AnalysisMode? AnalysisMode,
    CyclicalWavesV2SyncTrigger Trigger,
    string? RequestedBy);

public sealed record CyclicalWavesV2RunCounts(
    int TotalItems,
    int ProcessedItems,
    int UpdatedItems,
    int UnchangedItems,
    int FailedItems);

public sealed record CyclicalWavesV2SyncRunSummary(
    Guid RunId,
    CyclicalWavesV2SyncScope Scope,
    string? Ticker,
    CyclicalWavesV2AnalysisMode? AnalysisMode,
    CyclicalWavesV2SyncTrigger Trigger,
    CyclicalWavesV2SyncRunStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    CyclicalWavesV2RunCounts Counts,
    string? ErrorSummary,
    string? RequestedBy);

public sealed record CyclicalWavesV2RequestResult(Guid RunId, bool Accepted);

public sealed record CyclicalWavesV2ClaimedRun(Guid RunId, CyclicalWavesV2SyncRequest Request);

/// <summary>Run tracking with one global lease: at most one run is <c>Running</c> at a time.</summary>
public interface ICyclicalWavesV2SyncRunStore
{
    /// <summary>Queues a run, or returns the active run's id with <c>Accepted = false</c>.</summary>
    Task<CyclicalWavesV2RequestResult> RequestAsync(
        CyclicalWavesV2SyncRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Starts a run immediately; when another run holds the lease, records <c>SkippedAlreadyRunning</c> and returns <c>null</c>.</summary>
    Task<Guid?> TryStartAsync(
        CyclicalWavesV2SyncRequest request,
        string lockOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    Task<CyclicalWavesV2ClaimedRun?> ClaimNextRequestedAsync(
        string lockOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    Task HeartbeatAsync(Guid runId, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid runId,
        CyclicalWavesV2SyncRunStatus status,
        CyclicalWavesV2RunCounts counts,
        string? errorSummary,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> ExpireStaleLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetLastCompletedFullRunStartAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CyclicalWavesV2SyncRunSummary>> GetRecentRunsAsync(int count, CancellationToken cancellationToken);
}

/// <summary>Coordinator settings; the composition root maps them from the validated provider options.</summary>
public sealed record CyclicalWavesV2SyncSettings(
    int IncomeStatementLimit,
    int AnalysisPageSize,
    int AnalysisIncrementalOverlapDays,
    TimeSpan RequestDelay,
    TimeSpan Lease,
    int HeartbeatEveryItems = 25);

public sealed record CyclicalWavesV2SyncOutcome(
    Guid? RunId,
    CyclicalWavesV2SyncRunStatus Status,
    CyclicalWavesV2RunCounts Counts,
    string? ErrorSummary);

public interface ICyclicalWavesV2SyncCoordinator
{
    /// <summary>Starts and runs a sync now (inline Admin calls, scheduled runs).</summary>
    Task<CyclicalWavesV2SyncOutcome> RunAsync(
        CyclicalWavesV2SyncRequest request,
        string lockOwner,
        CancellationToken cancellationToken);

    /// <summary>Runs a run that was already claimed from the queue.</summary>
    Task<CyclicalWavesV2SyncOutcome> RunClaimedAsync(
        CyclicalWavesV2ClaimedRun run,
        CancellationToken cancellationToken);
}

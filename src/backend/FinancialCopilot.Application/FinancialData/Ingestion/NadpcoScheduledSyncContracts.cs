namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum NadpcoScheduledSyncTriggerSource
{
    Automatic = 0,
    Manual = 1,
    MissedRecovery = 2
}

public enum NadpcoScheduledSyncRunStatus
{
    Running = 0,
    Succeeded = 1,
    PartiallySucceeded = 2,
    Failed = 3,
    Cancelled = 4,
    TimedOut = 5,
    SkippedDisabled = 6,
    SkippedAlreadyRunning = 7,
    Missed = 8,
    HungRecovered = 9
}

public enum NadpcoMissedScheduleRecoveryPolicy
{
    Skip = 0,
    RunOnceImmediately = 1,
    BoundedCatchUp = 2
}

public sealed record NadpcoScheduledSyncRunRequest(
    NadpcoScheduledSyncTriggerSource TriggerSource,
    string? ManualReason = null,
    bool Force = false);

public sealed record NadpcoScheduledSyncRun(
    Guid RunId,
    NadpcoScheduledSyncTriggerSource TriggerSource,
    NadpcoScheduledSyncRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastSuccessfulExecutionAt,
    int ProcessedBatches,
    int FailedBatches,
    int RetryAttempts,
    string? Diagnostics,
    string ScheduleSnapshotJson,
    string DatasetSelectionJson,
    string? LockOwner,
    DateTimeOffset? LockLeaseExpiresAt,
    bool AlertEmitted,
    string? ManualReason);

public sealed record NadpcoScheduledSyncStatus(
    bool Enabled,
    bool Ready,
    DateTimeOffset? NextDueAt,
    DateTimeOffset? LastSuccessfulExecutionAt,
    NadpcoScheduledSyncRun? ActiveRun,
    IReadOnlyCollection<NadpcoScheduledSyncRun> RecentRuns);

public sealed record NadpcoScheduledSyncAlert(
    Guid RunId,
    NadpcoScheduledSyncRunStatus Status,
    string Severity,
    string Message,
    string? Diagnostics,
    DateTimeOffset EmittedAt);

public interface INadpcoScheduledSyncCoordinator
{
    Task<NadpcoScheduledSyncRun> RunAsync(
        NadpcoScheduledSyncRunRequest request,
        CancellationToken cancellationToken);

    Task<NadpcoScheduledSyncStatus> GetStatusAsync(
        int recentRunLimit,
        CancellationToken cancellationToken);
}

public interface INadpcoScheduledSyncRunReader
{
    Task<IReadOnlyCollection<NadpcoScheduledSyncRun>> QueryRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public interface INadpcoScheduledSyncAlertSink
{
    Task<bool> EmitAsync(
        NadpcoScheduledSyncAlert alert,
        CancellationToken cancellationToken);
}

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Manual, DataAdmin-only reverse-chronological backfill of Noavaran current-API monthly
/// production/sales activity (spec 057 Phase A). Walks Shamsi months newest-first from the
/// latest published month down to the permitted 1404/01 floor, enqueueing one bounded
/// company-month sync request per company per month through the existing ingestion pipeline.
/// Never invoked by a scheduler.
/// </summary>
public interface IMonthlyActivityBackfillCoordinator
{
    /// <summary>
    /// Starts (or resumes) the backfill. Idempotent: company-months that already completed are
    /// skipped by their deterministic idempotency keys; failed ones are retried. Starting after
    /// completion is a no-op reporting the completed state.
    /// </summary>
    Task<MonthlyActivityBackfillStartResult> StartAsync(
        MonthlyActivityBackfillRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Per-month progress computed from durable sync-run history. Also records the durable
    /// backfill-complete marker once every planned month has fully completed.
    /// </summary>
    Task<MonthlyActivityBackfillProgress> GetProgressAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Read-side gate for the steady-state scheduled refresh (spec 057 Phase B): monthly-activity
/// scheduled syncs request only the previous Shamsi month once the backfill-complete marker
/// exists, and skip monthly activity entirely while it does not.
/// </summary>
public interface IMonthlyActivityBackfillStateReader
{
    Task<bool> IsBackfillCompletedAsync(CancellationToken cancellationToken);
}

public sealed record MonthlyActivityBackfillRequest(string RequestedBy);

public sealed record MonthlyActivityBackfillStartResult(
    string Outcome,
    int MonthsPlanned,
    int CompaniesPlanned,
    int RequestsEnqueued,
    MonthlyActivityBackfillProgress Progress);

public sealed record MonthlyActivityBackfillMonthProgress(
    int ShamsiYear,
    int ShamsiMonth,
    int CompaniesPlanned,
    int CompaniesCompleted,
    int CompaniesFailed,
    string Status);

public sealed record MonthlyActivityBackfillProgress(
    bool Started,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastStartedAt,
    string? RequestedBy,
    IReadOnlyCollection<MonthlyActivityBackfillMonthProgress> Months,
    IReadOnlyDictionary<int, int>? OutputTypeCounts = null);

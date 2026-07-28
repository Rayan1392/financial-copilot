namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Outcome of a DataAdmin all-index fundamental-index coverage catch-up run (spec 050).
/// </summary>
public enum FundamentalIndexCatchUpRunStatus
{
    Running = 0,
    Succeeded = 1,
    PartiallySucceeded = 2,
    Failed = 3,
    SkippedAlreadyRunning = 4
}

/// <summary>
/// A DataAdmin request to fetch every vendor fundamental index for all local NADPCO companies over a
/// Shamsi year range, into the non-scannable coverage table (spec 050). The default range is the
/// requested 1403→1405 backfill window; an admin may widen it. Empty <c>companyIndexIds</c> is implied
/// (all indexes); this never uses the curated 041 allowlist.
/// </summary>
public sealed record FundamentalIndexCatchUpRequest(
    string RequestedBy,
    int FromShamsiYear = 1403,
    int ToShamsiYear = 1405);

public sealed record FundamentalIndexCatchUpRun(
    Guid RunId,
    FundamentalIndexCatchUpRunStatus Status,
    string RequestedBy,
    int FromShamsiYear,
    int ToShamsiYear,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int CompaniesConsidered,
    int RequestsEnqueued,
    int FailedCompanies,
    IReadOnlyCollection<int> FailedCompanyIds,
    string? Diagnostics);

/// <summary>
/// Enumerates all local NADPCO-backed company ids and enqueues bounded all-index coverage requests
/// (1403→1405), recording run history. Reuses the existing raw-payload/normalization pipeline; the
/// curated 041 promotion path is untouched.
/// </summary>
public interface IFundamentalIndexCatchUpCoordinator
{
    Task<FundamentalIndexCatchUpRun> RunAsync(
        FundamentalIndexCatchUpRequest request,
        CancellationToken cancellationToken);
}

public interface IFundamentalIndexCatchUpRunReader
{
    Task<IReadOnlyCollection<FundamentalIndexCatchUpRun>> QueryRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

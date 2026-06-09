namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Coverage gap between the frozen Noavaran archive and the Noavaran current API for one
/// (company, dataset, fiscal year), where the current API has rows the archive lacks (spec 053
/// AC #4). "Gap" is boundary-driven: periods at/after the configured Shamsi current-API boundary are
/// owned by the current API; this report surfaces where the archive is missing that coverage.
/// </summary>
public sealed record CurrentApiCoverageGap(
    string Dataset,
    string ExternalCompanyId,
    int FiscalYear,
    int CurrentApiRowCount,
    int ArchiveRowCount);

public sealed record CurrentApiGapReport(
    int CurrentApiBoundaryShamsiYear,
    int TotalGapRows,
    IReadOnlyCollection<CurrentApiCoverageGap> Gaps);

/// <summary>
/// A DataAdmin backfill of the current API. <see cref="FromShamsiYearOverride"/> lowers the
/// configured start boundary for this run only (AC #3); when null the configured boundary is used.
/// Monthly activity is always clamped to the vendor-permitted 1404 boundary regardless of override.
/// </summary>
public sealed record CurrentApiBackfillRequest(
    string RequestedBy,
    int? FromShamsiYearOverride = null);

public sealed record CurrentApiBackfillResult(
    bool FullReload,
    int? AppliedFromShamsiYear,
    int CompaniesConsidered,
    int RequestsEnqueued,
    int FailedCompanies,
    string Duration);

/// <summary>
/// Health/status of the Noavaran current API, reported separately from archive import state
/// (AC #9). Combines provider health with the latest scheduled-sync execution.
/// </summary>
public sealed record CurrentApiHealthStatus(
    string SourceName,
    string ProviderHealthStatus,
    string? ProviderHealthDetail,
    bool ScheduledSyncEnabled,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? NextDueAt,
    DateTimeOffset CheckedAt);

/// <summary>
/// Read-side reconciliation of archive vs current-API coverage. Pure read; never mutates archive
/// freeze/import state (AC #10).
/// </summary>
public interface ICurrentApiGapReader
{
    Task<CurrentApiGapReport> ReportAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Drives a current-API backfill/gap-fill through the existing bounded NADPCO/current-API
/// orchestration (single ingestion path). Failures here never touch archive freeze/import state.
/// </summary>
public interface ICurrentApiBackfillCoordinator
{
    Task<CurrentApiBackfillResult> BackfillAsync(
        CurrentApiBackfillRequest request,
        CancellationToken cancellationToken);

    Task<CurrentApiHealthStatus> GetHealthAsync(CancellationToken cancellationToken);
}

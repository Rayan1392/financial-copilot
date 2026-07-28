namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// The action a DataAdmin requests against the one-time Noavaran archive source (spec 052). The
/// archive is imported once, validated, then frozen; it is never refreshed by a recurring worker.
/// </summary>
public enum ArchiveImportAction
{
    /// <summary>Report what an import would touch without enqueuing any ingestion work.</summary>
    DryRun = 0,

    /// <summary>Enqueue the one-time archive import through the existing archive ingestion path.</summary>
    Import = 1,

    /// <summary>Validate imported coverage (company/security mapping and dataset coverage).</summary>
    Validate = 2,

    /// <summary>Mark the archive source frozen so accidental re-import is blocked.</summary>
    Freeze = 3,

    /// <summary>Explicit, reason-recorded re-import of a frozen archive (maintenance only).</summary>
    ReImport = 4
}

/// <summary>Outcome of an archive import action.</summary>
public enum ArchiveImportRunStatus
{
    Running = 0,
    Succeeded = 1,
    PartiallySucceeded = 2,
    Failed = 3,
    SkippedAlreadyRunning = 4,
    /// <summary>An import/re-import was rejected because the archive is frozen and no reason was supplied.</summary>
    RejectedFrozen = 5
}

/// <summary>
/// Datasets the archive import may be limited to (AC #8). Maps onto the archive ingestion datasets:
/// companies (symbols), statements, monthly activity, ratios, and derived growth metrics. An empty
/// selection means all archive datasets.
/// </summary>
public enum ArchiveImportDataset
{
    Companies = 0,
    FinancialStatements = 1,
    MonthlyActivity = 2,
    FinancialRatios = 3,
    DerivedMetrics = 4
}

public sealed record ArchiveImportRequest(
    ArchiveImportAction Action,
    string RequestedBy,
    IReadOnlyCollection<ArchiveImportDataset> Datasets,
    string? Reason = null);

public sealed record ArchiveImportRun(
    Guid RunId,
    ArchiveImportAction Action,
    ArchiveImportRunStatus Status,
    string RequestedBy,
    IReadOnlyCollection<ArchiveImportDataset> Datasets,
    string? Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int CompaniesConsidered,
    int RequestsEnqueued,
    int SkippedCount,
    int ConflictCount,
    int FailedCount,
    bool Frozen,
    string? Diagnostics);

/// <summary>
/// Authoritative freeze state for the Noavaran archive source (AC #3/#5). When frozen, a normal
/// <see cref="ArchiveImportAction.Import"/> is rejected; only a reason-recorded
/// <see cref="ArchiveImportAction.ReImport"/> proceeds.
/// </summary>
public sealed record ArchiveFreezeState(
    bool IsFrozen,
    DateTimeOffset? FrozenAt,
    Guid? FrozenByRunId,
    string? Reason);

/// <summary>One coverage row for the archive coverage summary (AC #9).</summary>
public sealed record ArchiveCoverageRow(
    ArchiveImportDataset Dataset,
    string ExternalCompanyId,
    int? FiscalYear,
    int RowCount);

public sealed record ArchiveCoverageSummary(
    string SourceName,
    int CompanyCount,
    IReadOnlyDictionary<string, int> RowCountByDataset,
    IReadOnlyDictionary<int, int> RowCountByFiscalYear,
    IReadOnlyCollection<ArchiveCoverageRow> Rows);

public sealed record ArchiveImportValidationResult(
    bool CompanyMappingValid,
    int CompaniesWithoutCanonicalSymbol,
    IReadOnlyCollection<string> UnmappedExternalCompanyIds,
    ArchiveCoverageSummary Coverage);

/// <summary>
/// Orchestrates the one-time archive import lifecycle. Drives the existing archive ingestion path
/// (single source of truth for fetch/normalize); it does not introduce a second ingestion pipeline.
/// </summary>
public interface IArchiveImportCoordinator
{
    Task<ArchiveImportRun> RunAsync(ArchiveImportRequest request, CancellationToken cancellationToken);

    Task<ArchiveImportValidationResult> ValidateAsync(CancellationToken cancellationToken);

    Task<ArchiveFreezeState> GetFreezeStateAsync(CancellationToken cancellationToken);
}

public interface IArchiveImportRunReader
{
    Task<IReadOnlyCollection<ArchiveImportRun>> QueryRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public interface IArchiveCoverageReader
{
    Task<ArchiveCoverageSummary> SummarizeAsync(CancellationToken cancellationToken);
}

public interface IArchiveFreezeStateStore
{
    Task<ArchiveFreezeState> GetAsync(CancellationToken cancellationToken);

    Task FreezeAsync(Guid runId, string? reason, CancellationToken cancellationToken);
}

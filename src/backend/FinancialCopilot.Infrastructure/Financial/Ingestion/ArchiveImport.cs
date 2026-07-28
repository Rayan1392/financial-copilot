using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Persistence for one-time archive import runs and the authoritative freeze marker (spec 052).
/// Mirrors the NADPCO scheduled-sync run-history pattern (lease, status transitions, recent reads)
/// but for a manually-triggered, non-recurring import.
/// </summary>
public sealed class EfCoreArchiveImportRunRepository(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : IArchiveImportRunReader
{
    private const int LeaseSeconds = 7200;

    public async Task<ArchiveImportRunRow?> TryStartAsync(
        ArchiveImportRequest request,
        string datasetSelectionJson,
        string lockOwner,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await RecoverHungRunsAsync(now, cancellationToken);

        var active = await dbContext.ArchiveImportRuns.AnyAsync(
            row => row.Status == ArchiveImportRunStatus.Running.ToString() &&
                row.LockLeaseExpiresAt != null &&
                row.LockLeaseExpiresAt > now,
            cancellationToken);
        if (active)
        {
            return null;
        }

        var row = new ArchiveImportRunRow
        {
            Id = Guid.NewGuid(),
            Action = request.Action.ToString(),
            Status = ArchiveImportRunStatus.Running.ToString(),
            RequestedBy = Limit(request.RequestedBy, 256) ?? "unknown",
            DatasetSelectionJson = datasetSelectionJson,
            Reason = Limit(request.Reason, 1000),
            StartedAt = now,
            LockOwner = lockOwner,
            LockLeaseExpiresAt = now.AddSeconds(LeaseSeconds)
        };
        dbContext.ArchiveImportRuns.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<ArchiveImportRun> RecordTerminalAsync(
        ArchiveImportRequest request,
        ArchiveImportRunStatus status,
        string datasetSelectionJson,
        string? diagnostics,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var row = new ArchiveImportRunRow
        {
            Id = Guid.NewGuid(),
            Action = request.Action.ToString(),
            Status = status.ToString(),
            RequestedBy = Limit(request.RequestedBy, 256) ?? "unknown",
            DatasetSelectionJson = datasetSelectionJson,
            Reason = Limit(request.Reason, 1000),
            StartedAt = now,
            FinishedAt = now,
            Diagnostics = Limit(diagnostics, 2000)
        };
        dbContext.ArchiveImportRuns.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<ArchiveImportRun> CompleteAsync(
        Guid runId,
        ArchiveImportRunStatus status,
        int companiesConsidered,
        int requestsEnqueued,
        int skippedCount,
        int conflictCount,
        int failedCount,
        bool frozen,
        string? diagnostics,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.ArchiveImportRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        row.Status = status.ToString();
        row.FinishedAt = timeProvider.GetUtcNow();
        row.CompaniesConsidered = companiesConsidered;
        row.RequestsEnqueued = requestsEnqueued;
        row.SkippedCount = skippedCount;
        row.ConflictCount = conflictCount;
        row.FailedCount = failedCount;
        row.Frozen = frozen;
        row.Diagnostics = Limit(diagnostics, 2000);
        row.LockOwner = null;
        row.LockLeaseExpiresAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<IReadOnlyCollection<ArchiveImportRun>> QueryRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken) =>
        await dbContext.ArchiveImportRuns.AsNoTracking()
            .OrderByDescending(row => row.StartedAt)
            .Take(Math.Clamp(maximumCount, 1, 100))
            .Select(row => Map(row))
            .ToArrayAsync(cancellationToken);

    private async Task RecoverHungRunsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hung = await dbContext.ArchiveImportRuns
            .Where(row => row.Status == ArchiveImportRunStatus.Running.ToString() &&
                row.LockLeaseExpiresAt != null &&
                row.LockLeaseExpiresAt <= now)
            .ToArrayAsync(cancellationToken);
        if (hung.Length == 0)
        {
            return;
        }

        foreach (var row in hung)
        {
            row.Status = ArchiveImportRunStatus.Failed.ToString();
            row.FinishedAt = now;
            row.LockOwner = null;
            row.LockLeaseExpiresAt = null;
            row.Diagnostics = "Recovered expired archive import lease.";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static ArchiveImportRun Map(ArchiveImportRunRow row) =>
        new(
            row.Id,
            Enum.Parse<ArchiveImportAction>(row.Action),
            Enum.Parse<ArchiveImportRunStatus>(row.Status),
            row.RequestedBy,
            ParseDatasets(row.DatasetSelectionJson),
            row.Reason,
            row.StartedAt,
            row.FinishedAt,
            row.CompaniesConsidered,
            row.RequestsEnqueued,
            row.SkippedCount,
            row.ConflictCount,
            row.FailedCount,
            row.Frozen,
            row.Diagnostics);

    internal static IReadOnlyCollection<ArchiveImportDataset> ParseDatasets(string json)
    {
        try
        {
            var names = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return names
                .Select(name => Enum.TryParse<ArchiveImportDataset>(name, out var dataset) ? dataset : (ArchiveImportDataset?)null)
                .Where(dataset => dataset is not null)
                .Select(dataset => dataset!.Value)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Limit(string? value, int length) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= length ? value : value[..length];
}

/// <summary>Single-row freeze marker store for the Noavaran archive source.</summary>
public sealed class EfCoreArchiveFreezeStateStore(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : IArchiveFreezeStateStore
{
    public async Task<ArchiveFreezeState> GetAsync(CancellationToken cancellationToken)
    {
        var row = await dbContext.ArchiveFreezeStates.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SourceName == ProviderSources.NoavaranArchiveSqlName, cancellationToken);
        return row is null
            ? new ArchiveFreezeState(false, null, null, null)
            : new ArchiveFreezeState(row.IsFrozen, row.FrozenAt, row.FrozenByRunId, row.Reason);
    }

    public async Task FreezeAsync(Guid runId, string? reason, CancellationToken cancellationToken)
    {
        var row = await dbContext.ArchiveFreezeStates
            .SingleOrDefaultAsync(item => item.SourceName == ProviderSources.NoavaranArchiveSqlName, cancellationToken);
        if (row is null)
        {
            row = new ArchiveFreezeStateRow { SourceName = ProviderSources.NoavaranArchiveSqlName };
            dbContext.ArchiveFreezeStates.Add(row);
        }

        row.IsFrozen = true;
        row.FrozenAt = timeProvider.GetUtcNow();
        row.FrozenByRunId = runId;
        row.Reason = reason is { Length: > 1000 } ? reason[..1000] : reason;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Coverage summary over the persisted archive rows (spec 052 AC #9). Counts companies, statements,
/// monthly reports, and source-marked derived metrics owned by the archive source, grouped by dataset
/// and by Gregorian fiscal year (derived from <c>PeriodEnd</c>). Read-only and provider-name-keyed.
/// </summary>
public sealed class EfCoreArchiveCoverageReader(FinancialIngestionDbContext dbContext) : IArchiveCoverageReader
{
    private const string Source = ProviderSources.NoavaranArchiveSqlName;

    public async Task<ArchiveCoverageSummary> SummarizeAsync(CancellationToken cancellationToken)
    {
        var companyCount = await dbContext.Companies.AsNoTracking()
            .CountAsync(row => row.ProviderName == Source, cancellationToken);

        var statements = await dbContext.FinancialStatements.AsNoTracking()
            .Where(row => row.ProviderName == Source)
            .GroupBy(row => new { row.ExternalCompanyId, Year = row.PeriodEnd.Year })
            .Select(group => new { group.Key.ExternalCompanyId, group.Key.Year, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var monthly = await dbContext.MonthlyReports.AsNoTracking()
            .Where(row => row.ProviderName == Source)
            .GroupBy(row => new { row.ExternalCompanyId, Year = row.PeriodEnd.Year })
            .Select(group => new { group.Key.ExternalCompanyId, group.Key.Year, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var rows = new List<ArchiveCoverageRow>(statements.Count + monthly.Count);
        rows.AddRange(statements.Select(s =>
            new ArchiveCoverageRow(ArchiveImportDataset.FinancialStatements, s.ExternalCompanyId, s.Year, s.Count)));
        rows.AddRange(monthly.Select(m =>
            new ArchiveCoverageRow(ArchiveImportDataset.MonthlyActivity, m.ExternalCompanyId, m.Year, m.Count)));

        var byDataset = rows
            .GroupBy(row => row.Dataset.ToString())
            .ToDictionary(group => group.Key, group => group.Sum(row => row.RowCount));
        byDataset[ArchiveImportDataset.Companies.ToString()] = companyCount;

        var byFiscalYear = rows
            .Where(row => row.FiscalYear is not null)
            .GroupBy(row => row.FiscalYear!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.RowCount));

        return new ArchiveCoverageSummary(Source, companyCount, byDataset, byFiscalYear, rows);
    }
}

/// <summary>
/// Orchestrates the one-time archive import lifecycle (spec 052). Imports drive the existing archive
/// ingestion path (<see cref="ICodalDbScheduledSyncService"/>) so fetch/normalize has one source of
/// truth; this class adds dry-run, validation, the freeze gate, and run history. It is invoked only
/// from the DataAdmin endpoint — never from a recurring worker.
/// </summary>
public sealed class ArchiveImportCoordinator(
    ICodalDbScheduledSyncService archiveSync,
    EfCoreArchiveImportRunRepository repository,
    IArchiveFreezeStateStore freezeStore,
    IArchiveCoverageReader coverageReader,
    FinancialIngestionDbContext dbContext,
    ILogger<ArchiveImportCoordinator> logger) : IArchiveImportCoordinator
{
    public async Task<ArchiveImportRun> RunAsync(
        ArchiveImportRequest request,
        CancellationToken cancellationToken)
    {
        var datasetsJson = JsonSerializer.Serialize(request.Datasets.Select(d => d.ToString()).ToArray());

        // Freeze gate (AC #3/#5): a normal Import against a frozen archive is rejected; a re-import
        // must carry an explicit reason.
        var freeze = await freezeStore.GetAsync(cancellationToken);
        if (request.Action == ArchiveImportAction.Import && freeze.IsFrozen)
        {
            return await repository.RecordTerminalAsync(
                request,
                ArchiveImportRunStatus.RejectedFrozen,
                datasetsJson,
                "Archive source is frozen. Use a re-import with an explicit reason to proceed.",
                cancellationToken);
        }

        if (request.Action == ArchiveImportAction.ReImport && string.IsNullOrWhiteSpace(request.Reason))
        {
            return await repository.RecordTerminalAsync(
                request,
                ArchiveImportRunStatus.RejectedFrozen,
                datasetsJson,
                "A controlled re-import requires an explicit reason.",
                cancellationToken);
        }

        return request.Action switch
        {
            ArchiveImportAction.DryRun => await ExecuteImportAsync(request, datasetsJson, dryRun: true, cancellationToken),
            ArchiveImportAction.Import => await ExecuteImportAsync(request, datasetsJson, dryRun: false, cancellationToken),
            ArchiveImportAction.ReImport => await ExecuteImportAsync(request, datasetsJson, dryRun: false, cancellationToken),
            ArchiveImportAction.Validate => await ExecuteValidateAsync(request, datasetsJson, cancellationToken),
            ArchiveImportAction.Freeze => await ExecuteFreezeAsync(request, datasetsJson, cancellationToken),
            _ => await repository.RecordTerminalAsync(
                request, ArchiveImportRunStatus.Failed, datasetsJson, "Unsupported archive import action.", cancellationToken)
        };
    }

    public async Task<ArchiveImportValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var coverage = await coverageReader.SummarizeAsync(cancellationToken);
        var (mappingValid, withoutSymbol, unmapped) = await EvaluateCompanyMappingAsync(cancellationToken);
        return new ArchiveImportValidationResult(mappingValid, withoutSymbol, unmapped, coverage);
    }

    public Task<ArchiveFreezeState> GetFreezeStateAsync(CancellationToken cancellationToken) =>
        freezeStore.GetAsync(cancellationToken);

    private async Task<ArchiveImportRun> ExecuteImportAsync(
        ArchiveImportRequest request,
        string datasetsJson,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var row = await repository.TryStartAsync(request, datasetsJson, owner, cancellationToken);
        if (row is null)
        {
            return await repository.RecordTerminalAsync(
                request,
                ArchiveImportRunStatus.SkippedAlreadyRunning,
                datasetsJson,
                "An archive import run is already active.",
                cancellationToken);
        }

        try
        {
            // Full reload: a one-time import always considers the full archive inventory.
            var result = await archiveSync.ExecuteAsync(fullReload: true, cancellationToken, dryRun);
            var status = result.FailedCompanies > 0
                ? ArchiveImportRunStatus.PartiallySucceeded
                : ArchiveImportRunStatus.Succeeded;
            var diagnostics = dryRun
                ? $"Dry-run: {result.CompaniesConsidered} companies would be imported across selected datasets."
                : $"Imported {result.CompaniesEnqueued}/{result.CompaniesConsidered} companies; failed={result.FailedCompanies}.";
            return await repository.CompleteAsync(
                row.Id,
                status,
                result.CompaniesConsidered,
                dryRun ? 0 : result.CompaniesEnqueued,
                skippedCount: 0,
                conflictCount: 0,
                failedCount: result.FailedCompanies,
                frozen: false,
                diagnostics,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Archive import failed for action {Action}.", request.Action);
            return await repository.CompleteAsync(
                row.Id,
                ArchiveImportRunStatus.Failed,
                companiesConsidered: 0,
                requestsEnqueued: 0,
                skippedCount: 0,
                conflictCount: 0,
                failedCount: 1,
                frozen: false,
                exception.Message,
                CancellationToken.None);
        }
    }

    private async Task<ArchiveImportRun> ExecuteValidateAsync(
        ArchiveImportRequest request,
        string datasetsJson,
        CancellationToken cancellationToken)
    {
        var coverage = await coverageReader.SummarizeAsync(cancellationToken);
        var (mappingValid, withoutSymbol, _) = await EvaluateCompanyMappingAsync(cancellationToken);
        var status = mappingValid ? ArchiveImportRunStatus.Succeeded : ArchiveImportRunStatus.PartiallySucceeded;
        var diagnostics =
            $"Validation: companies={coverage.CompanyCount}, companiesWithoutCanonicalSymbol={withoutSymbol}, " +
            $"statementRows={coverage.RowCountByDataset.GetValueOrDefault(ArchiveImportDataset.FinancialStatements.ToString())}, " +
            $"monthlyRows={coverage.RowCountByDataset.GetValueOrDefault(ArchiveImportDataset.MonthlyActivity.ToString())}.";
        return await repository.RecordTerminalAsync(request, status, datasetsJson, diagnostics, cancellationToken);
    }

    private async Task<ArchiveImportRun> ExecuteFreezeAsync(
        ArchiveImportRequest request,
        string datasetsJson,
        CancellationToken cancellationToken)
    {
        var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var row = await repository.TryStartAsync(request, datasetsJson, owner, cancellationToken);
        if (row is null)
        {
            return await repository.RecordTerminalAsync(
                request,
                ArchiveImportRunStatus.SkippedAlreadyRunning,
                datasetsJson,
                "An archive import run is already active.",
                cancellationToken);
        }

        await freezeStore.FreezeAsync(row.Id, request.Reason, cancellationToken);
        return await repository.CompleteAsync(
            row.Id,
            ArchiveImportRunStatus.Succeeded,
            companiesConsidered: 0,
            requestsEnqueued: 0,
            skippedCount: 0,
            conflictCount: 0,
            failedCount: 0,
            frozen: true,
            "Archive source marked frozen.",
            cancellationToken);
    }

    // AC #7: validate company/security mapping. An archive company is "mapped" when it has at least
    // one normalized symbol carrying a canonical SymbolCode.
    private async Task<(bool MappingValid, int CompaniesWithoutSymbol, IReadOnlyCollection<string> Unmapped)>
        EvaluateCompanyMappingAsync(CancellationToken cancellationToken)
    {
        const string source = ProviderSources.NoavaranArchiveSqlName;
        var companyIds = await dbContext.Companies.AsNoTracking()
            .Where(row => row.ProviderName == source)
            .Select(row => row.ExternalCompanyId)
            .ToListAsync(cancellationToken);

        // Spec 068: Symbols table removed. A company is considered "mapped" when it has a non-empty
        // TseSymbol or CompanySymbol on its Companies row (the canonical identifier fields).
        var mappedIds = await dbContext.Companies.AsNoTracking()
            .Where(company => company.ProviderName == source &&
                (company.TseSymbol != null && company.TseSymbol != "" ||
                 company.CompanySymbol != null && company.CompanySymbol != ""))
            .Select(company => company.ExternalCompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var mappedSet = mappedIds.ToHashSet(StringComparer.Ordinal);
        var unmapped = companyIds.Where(id => !mappedSet.Contains(id)).OrderBy(id => id).ToArray();
        return (unmapped.Length == 0, unmapped.Length, unmapped);
    }
}

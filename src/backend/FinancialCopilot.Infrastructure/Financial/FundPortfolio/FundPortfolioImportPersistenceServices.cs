using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioReportSourceRegistry(IEnumerable<IFundPortfolioReportSource> sources) : IFundPortfolioReportSourceRegistry
{
    private readonly IReadOnlyDictionary<string, IFundPortfolioReportSource> sourceMap = sources.ToDictionary(source => source.ProviderName, StringComparer.OrdinalIgnoreCase);
    public IFundPortfolioReportSource Get(string providerName) => sourceMap.TryGetValue(providerName, out var source) ? source : new UnavailableFundPortfolioReportSource(providerName);
}

public sealed class EfCoreFundPortfolioImportRunRepository(FinancialProviderDbContext dbContext) : IFundPortfolioImportRunRepository
{
    public async Task<Guid> CreateRunAsync(StartFundPortfolioImportRunRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var row = new FundPortfolioImportRunRow { Id = Guid.NewGuid(), TriggerType = request.TriggerType, ProviderName = request.ProviderName, RequestedByActorId = request.RequestedByActorId, StartedAtUtc = DateTimeOffset.UtcNow, Status = FundPortfolioImportRunStatus.Queued, CorrelationId = correlationId };
        dbContext.FundPortfolioImportRuns.Add(row); await dbContext.SaveChangesAsync(cancellationToken); return row.Id;
    }

    public async Task AddItemsAsync(Guid runId, IReadOnlyList<FundPortfolioReportSourceDescriptor> sources, CancellationToken cancellationToken)
    {
        var distinctSources = sources
            .Where(x => !string.IsNullOrWhiteSpace(x.StableSourceObjectId))
            .GroupBy(x => $"{x.ProviderName}\u001f{x.StableSourceObjectId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var sourceObjectIds = distinctSources.Select(x => x.StableSourceObjectId).ToHashSet(StringComparer.Ordinal);
        var existing = await dbContext.FundPortfolioImportItems
            .Where(x => x.SourceObjectId != null && sourceObjectIds.Contains(x.SourceObjectId))
            .Select(x => new { x.ProviderName, SourceObjectId = x.SourceObjectId! })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(x => $"{x.ProviderName}\u001f{x.SourceObjectId}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        dbContext.FundPortfolioImportItems.AddRange(distinctSources.Where(source => !existingKeys.Contains($"{source.ProviderName}\u001f{source.StableSourceObjectId}")).Select(source => new FundPortfolioImportItemRow
        {
            Id = Guid.NewGuid(), ImportRunId = runId, SourceObjectId = source.StableSourceObjectId, ProviderName = source.ProviderName,
            OriginalFileName = source.OriginalFileName, ObservedFundName = source.ObservedFundName, ObservedPeriodEnd = source.ObservedPeriodEnd,
            DownloadToken = source.DownloadToken, Status = FundPortfolioImportItemStatus.Queued, CorrelationId = dbContext.FundPortfolioImportRuns.Local.First(x => x.Id == runId).CorrelationId, QueuedAtUtc = DateTimeOffset.UtcNow
        }));
        var run = await dbContext.FundPortfolioImportRuns.SingleAsync(x => x.Id == runId, cancellationToken); run.DiscoveredCount = distinctSources.Length; run.Status = FundPortfolioImportRunStatus.Running;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<FundPortfolioImportRunView?> GetRunAsync(Guid runId, CancellationToken cancellationToken) => await dbContext.FundPortfolioImportRuns.AsNoTracking().Where(x => x.Id == runId).Select(x => new FundPortfolioImportRunView(x.Id, x.TriggerType, x.ProviderName, x.Status, x.DiscoveredCount, x.ImportedCount, x.DuplicateCount, x.PartialCount, x.FailedCount, x.StartedAtUtc, x.CompletedAtUtc, x.CorrelationId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<FundPortfolioImportRunPage> ListRunsAsync(FundPortfolioImportRunQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page); var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var source = dbContext.FundPortfolioImportRuns.AsNoTracking().AsQueryable();
        if (query.Status is not null) source = source.Where(x => x.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.ProviderName)) source = source.Where(x => x.ProviderName == query.ProviderName);
        var total = await source.CountAsync(cancellationToken);
        // DateTimeOffset ordering is not translated by the SQLite provider used by local acceptance tests.
        // Keep filtering server-side, then apply deterministic page ordering in memory.
        var items = (await source.Select(x => new FundPortfolioImportRunView(x.Id, x.TriggerType, x.ProviderName, x.Status, x.DiscoveredCount, x.ImportedCount, x.DuplicateCount, x.PartialCount, x.FailedCount, x.StartedAtUtc, x.CompletedAtUtc, x.CorrelationId)).ToListAsync(cancellationToken))
            .OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new(items, page, pageSize, total);
    }

    public async Task<FundPortfolioImportItemPage> ListItemsAsync(FundPortfolioImportItemQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page); var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var source = dbContext.FundPortfolioImportItems.AsNoTracking().AsQueryable();
        if (query.RunId is not null) source = source.Where(x => x.ImportRunId == query.RunId);
        if (query.Status is not null) source = source.Where(x => x.Status == query.Status);
        var total = await source.CountAsync(cancellationToken);
        var items = (await source.Select(x => new FundPortfolioImportItemView(x.Id, x.ImportRunId, x.ProviderName, x.OriginalFileName, x.ObservedFundName, x.ObservedPeriodEnd, x.SourceObjectId ?? string.Empty, x.Status, x.AttemptCount, x.ReportId, x.LastErrorCode, x.LastErrorSummary, x.StartedAtUtc, x.CompletedAtUtc)).ToListAsync(cancellationToken))
            .OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new(items, page, pageSize, total);
    }

    public async Task<int> CancelRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var changed = await dbContext.FundPortfolioImportItems.Where(x => x.ImportRunId == runId && (x.Status == FundPortfolioImportItemStatus.Queued || x.Status == FundPortfolioImportItemStatus.RetryableFailure || x.Status == FundPortfolioImportItemStatus.Running)).ExecuteUpdateAsync(updates => updates.SetProperty(x => x.Status, FundPortfolioImportItemStatus.Cancelled).SetProperty(x => x.CompletedAtUtc, DateTimeOffset.UtcNow).SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null), cancellationToken);
        await dbContext.FundPortfolioImportRuns.Where(x => x.Id == runId && x.Status != FundPortfolioImportRunStatus.Completed && x.Status != FundPortfolioImportRunStatus.CompletedWithErrors).ExecuteUpdateAsync(updates => updates.SetProperty(x => x.Status, FundPortfolioImportRunStatus.Cancelled).SetProperty(x => x.CompletedAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        return changed;
    }

    public async Task<FundPortfolioImportItemWork?> ClaimItemAsync(Guid runId, Guid itemId, int leaseDurationSeconds, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var row = await dbContext.FundPortfolioImportItems.SingleOrDefaultAsync(x => x.Id == itemId && x.ImportRunId == runId
            && (x.Status == FundPortfolioImportItemStatus.Queued || x.Status == FundPortfolioImportItemStatus.RetryableFailure || x.Status == FundPortfolioImportItemStatus.Running), cancellationToken);
        // Apply DateTimeOffset eligibility in memory for provider portability. SQLite cannot translate
        // comparisons against DateTimeOffset, while PostgreSQL and SQL Server can.
        if (row is null || row.NextAttemptAtUtc > now || row.LeaseUntilUtc > now) return null;
        row.Status = FundPortfolioImportItemStatus.Running; row.AttemptCount++; row.StartedAtUtc = now;
        row.LeaseUntilUtc = now.AddSeconds(Math.Clamp(leaseDurationSeconds, 30, 3600)); row.NextAttemptAtUtc = null;
        await dbContext.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(row.Id, row.ImportRunId, row.ProviderName, row.OriginalFileName, row.ObservedFundName, row.ObservedPeriodEnd, row.SourceObjectId ?? row.Id.ToString("N"), row.DownloadToken, row.AttemptCount, row.CorrelationId, row.QueuedAtUtc);
    }

    public async Task<IReadOnlyList<(Guid RunId, Guid ItemId)>> ListRunnableItemsAsync(int maximumItems, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await dbContext.FundPortfolioImportItems.AsNoTracking().Where(x =>
            x.Status == FundPortfolioImportItemStatus.Queued || x.Status == FundPortfolioImportItemStatus.RetryableFailure || x.Status == FundPortfolioImportItemStatus.Running).ToListAsync(cancellationToken);
        return candidates.Where(x => x.NextAttemptAtUtc is null || x.NextAttemptAtUtc <= now)
            .Where(x => x.LeaseUntilUtc is null || x.LeaseUntilUtc <= now)
            .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.StartedAtUtc).ThenBy(x => x.Id)
            .Take(Math.Clamp(maximumItems, 1, 32))
            .Select(x => (x.ImportRunId, x.Id)).ToArray();
    }

    public async Task CompleteItemAsync(Guid itemId, FundPortfolioImportItemStatus status, Guid? reportId, string? errorCode, string? errorSummary, CancellationToken cancellationToken)
    {
        var row = await dbContext.FundPortfolioImportItems.SingleAsync(x => x.Id == itemId, cancellationToken);
        row.Status = status; row.ReportId = reportId; row.LastErrorCode = errorCode; row.LastErrorSummary = errorSummary; row.CompletedAtUtc = DateTimeOffset.UtcNow; row.LeaseUntilUtc = null;
        row.NextAttemptAtUtc = status == FundPortfolioImportItemStatus.RetryableFailure
            ? DateTimeOffset.UtcNow.Add(FundPortfolioRetryPolicy.DelayForAttempt(row.AttemptCount))
            : null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<FinalizeFundPortfolioImportRunResult> FinalizeAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.FundPortfolioImportRuns.SingleAsync(x => x.Id == runId, cancellationToken);
        var statuses = await dbContext.FundPortfolioImportItems.Where(x => x.ImportRunId == runId).Select(x => x.Status).ToListAsync(cancellationToken);
        run.ImportedCount = statuses.Count(x => x is FundPortfolioImportItemStatus.Imported or FundPortfolioImportItemStatus.CorrectedRevision);
        run.DuplicateCount = statuses.Count(x => x == FundPortfolioImportItemStatus.Duplicate); run.PartialCount = statuses.Count(x => x == FundPortfolioImportItemStatus.Partial);
        run.FailedCount = statuses.Count(x => x is FundPortfolioImportItemStatus.Failed or FundPortfolioImportItemStatus.Poisoned or FundPortfolioImportItemStatus.RetryableFailure);
        run.Status = statuses.Any(x => x is FundPortfolioImportItemStatus.RetryableFailure or FundPortfolioImportItemStatus.Running or FundPortfolioImportItemStatus.Queued) ? FundPortfolioImportRunStatus.Running : run.FailedCount > 0 ? FundPortfolioImportRunStatus.CompletedWithErrors : FundPortfolioImportRunStatus.Completed; run.CompletedAtUtc = run.Status == FundPortfolioImportRunStatus.Running ? null : DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(run.Id, run.Status, run.ImportedCount, run.DuplicateCount, run.FailedCount, run.PartialCount);
    }
}

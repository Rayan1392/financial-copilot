using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioRetentionOptions
{
    public const string SectionName = "FundPortfolio:Retention";
    public bool Enabled { get; set; } = true;
    public int RunMetadataDays { get; set; } = 730;
    public int FailedItemDays { get; set; } = 90;
    public int MappingDecisionDays { get; set; } = 730;
    public int RawFileDays { get; set; } = 730;
    public int CadenceHours { get; set; } = 24;
}

public sealed class FundPortfolioRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FundPortfolioRetentionOptions> options,
    ILogger<FundPortfolioRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) { logger.LogInformation("Fund portfolio retention is disabled."); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var removed = await scope.ServiceProvider.GetRequiredService<IFundPortfolioRetentionStore>().PurgeAsync(options.Value, stoppingToken);
                await scope.ServiceProvider.GetRequiredService<Application.FinancialData.FundPortfolio.IFundPortfolioAuditSink>().WriteAsync(new("purge", "system", null, null, null, Guid.NewGuid().ToString("N"), $"Retention purge removed Reports={removed.Reports} Runs={removed.Runs} Items={removed.Items} Reviews={removed.Reviews} RawFiles={removed.RawFiles}."), stoppingToken);
                logger.LogInformation("Fund portfolio retention completed. Reports={Reports} Runs={Runs} Items={Items} Reviews={Reviews} RawFiles={RawFiles}", removed.Reports, removed.Runs, removed.Items, removed.Reviews, removed.RawFiles);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Fund portfolio retention failed."); }
            await Task.Delay(TimeSpan.FromHours(Math.Clamp(options.Value.CadenceHours, 1, 168)), stoppingToken);
        }
    }
}

public sealed record FundPortfolioRetentionResult(int Reports, int Runs, int Items, int Reviews, int RawFiles);
public interface IFundPortfolioRetentionStore
{
    Task<FundPortfolioRetentionResult> PurgeAsync(FundPortfolioRetentionOptions options, CancellationToken cancellationToken);
}

public sealed class EfCoreFundPortfolioRetentionStore(FinancialProviderDbContext dbContext, IOptions<FundPortfolioRawStorageOptions> rawStorageOptions) : IFundPortfolioRetentionStore
{
    public async Task<FundPortfolioRetentionResult> PurgeAsync(FundPortfolioRetentionOptions options, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rawCutoff = now.AddDays(-Math.Clamp(options.RawFileDays, 1, 3650));
        var runCutoff = now.AddDays(-Math.Clamp(options.RunMetadataDays, 1, 3650));
        var failedCutoff = now.AddDays(-Math.Clamp(options.FailedItemDays, 1, 3650));
        var reviewCutoff = now.AddDays(-Math.Clamp(options.MappingDecisionDays, 1, 3650));

        var rawReports = await dbContext.FundPortfolioReports.Where(x => x.ImportedAtUtc < rawCutoff).Select(x => new { x.Id, x.RawStorageKey }).ToListAsync(cancellationToken);
        var reportIds = rawReports.Select(x => x.Id).ToArray();
        var reviews = await dbContext.FundPortfolioMappingReviews.Where(x => x.Status != Application.FinancialData.FundPortfolio.FundPortfolioMappingReviewStatus.Pending && x.ResolvedAtUtc < reviewCutoff).ToListAsync(cancellationToken);
        var removedReviews = reviews.Count;
        dbContext.FundPortfolioMappingReviews.RemoveRange(reviews);
        var issues = await dbContext.FundPortfolioExtractionIssues.Where(x => reportIds.Contains(x.ReportId)).ToListAsync(cancellationToken);
        var sheets = await dbContext.FundPortfolioReportSheets.Where(x => reportIds.Contains(x.ReportId)).ToListAsync(cancellationToken);
        dbContext.FundPortfolioExtractionIssues.RemoveRange(issues);
        dbContext.FundPortfolioReportSheets.RemoveRange(sheets);
        var reports = await dbContext.FundPortfolioReports.Where(x => reportIds.Contains(x.Id)).ToListAsync(cancellationToken);
        dbContext.FundPortfolioReports.RemoveRange(reports);
        var oldItems = await dbContext.FundPortfolioImportItems.Where(x => x.CompletedAtUtc < failedCutoff && x.Status != Application.FinancialData.FundPortfolio.FundPortfolioImportItemStatus.Queued && x.Status != Application.FinancialData.FundPortfolio.FundPortfolioImportItemStatus.Running).ToListAsync(cancellationToken);
        dbContext.FundPortfolioImportItems.RemoveRange(oldItems);
        var oldRuns = await dbContext.FundPortfolioImportRuns.Where(x => x.CompletedAtUtc < runCutoff && x.Status != Application.FinancialData.FundPortfolio.FundPortfolioImportRunStatus.Running).ToListAsync(cancellationToken);
        dbContext.FundPortfolioImportRuns.RemoveRange(oldRuns);
        await dbContext.SaveChangesAsync(cancellationToken);
        var rawFiles = 0;
        foreach (var report in rawReports)
        {
            if (!report.RawStorageKey.StartsWith("fund-portfolio/", StringComparison.Ordinal)) continue;
            var root = Path.GetFullPath(rawStorageOptions.Value.RootPath);
            var path = Path.GetFullPath(Path.Combine(root, report.RawStorageKey["fund-portfolio/".Length..]));
            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) { File.Delete(path); rawFiles++; }
        }
        return new(reports.Count, oldRuns.Count, oldItems.Count, removedReviews, rawFiles);
    }
}

public sealed class FundPortfolioSourceWatermarkRow
{
    public string ProviderName { get; set; } = string.Empty;
    public DateTimeOffset? LastModifiedUtc { get; set; }
    public string? LastSourceObjectId { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
}

public sealed class EfCoreFundPortfolioSourceWatermarkStore(FinancialProviderDbContext dbContext)
{
    public async Task<(DateTimeOffset? LastModifiedUtc, string? LastSourceObjectId)?> GetAsync(string providerName, CancellationToken cancellationToken)
    {
        var row = await dbContext.FundPortfolioSourceWatermarks.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderName == providerName, cancellationToken);
        return row is null ? null : (row.LastModifiedUtc, row.LastSourceObjectId);
    }

    public async Task<bool> TryAcquireAsync(string providerName, TimeSpan lease, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await dbContext.FundPortfolioSourceWatermarks.SingleOrDefaultAsync(x => x.ProviderName == providerName, cancellationToken);
        if (row is null) { row = new() { ProviderName = providerName }; dbContext.FundPortfolioSourceWatermarks.Add(row); }
        if (row.LeaseUntilUtc > now) return false;
        row.LeaseUntilUtc = now.Add(lease); await dbContext.SaveChangesAsync(cancellationToken); return true;
    }

    public async Task AdvanceAsync(string providerName, DateTimeOffset? modifiedUtc, string? sourceObjectId, CancellationToken cancellationToken)
    {
        var row = await dbContext.FundPortfolioSourceWatermarks.SingleAsync(x => x.ProviderName == providerName, cancellationToken);
        if (modifiedUtc is not null && (row.LastModifiedUtc is null || modifiedUtc > row.LastModifiedUtc || (modifiedUtc == row.LastModifiedUtc && string.CompareOrdinal(sourceObjectId, row.LastSourceObjectId) > 0))) { row.LastModifiedUtc = modifiedUtc; row.LastSourceObjectId = sourceObjectId; }
        row.LeaseUntilUtc = null; await dbContext.SaveChangesAsync(cancellationToken);
    }
}

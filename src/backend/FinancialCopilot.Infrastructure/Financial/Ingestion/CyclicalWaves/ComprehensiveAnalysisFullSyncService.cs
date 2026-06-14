using System.Diagnostics;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class ComprehensiveAnalysisFullSyncService(
    ComprehensiveAnalysisBlogClient client,
    ComprehensiveAnalysisRepository repository,
    IOptions<ComprehensiveAnalysisBlogOptions> options,
    TimeProvider timeProvider,
    ILogger<ComprehensiveAnalysisFullSyncService> logger)
    : IComprehensiveAnalysisFullSyncService
{
    public async Task<ComprehensiveAnalysisFullSyncResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var logId = await repository.InsertSyncLogAsync("FullSync", cancellationToken);
        var totalPages = 0;
        var totalItems = 0;
        string? lastError = null;

        logger.LogInformation("ComprehensiveAnalysis full sync started.");

        try
        {
            var firstPage = await client.GetPageAsync(1, fromDate: null, toDate: null, cancellationToken);
            if (firstPage is null)
            {
                throw new InvalidOperationException("Failed to fetch page 1 from ComprehensiveAnalysis API.");
            }

            var lastPage = firstPage.Meta.LastPage;
            totalPages = lastPage;

            logger.LogInformation(
                "ComprehensiveAnalysis full sync: {Total} total pages, {Items} total items.",
                lastPage,
                firstPage.Meta.Total);

            await UpsertPageAsync(firstPage, cancellationToken);
            totalItems += firstPage.Data.Count;

            for (var page = 2; page <= lastPage; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (options.Value.RequestDelayMs > 0)
                {
                    await Task.Delay(options.Value.RequestDelayMs, cancellationToken);
                }

                try
                {
                    var pageData = await client.GetPageAsync(page, fromDate: null, toDate: null, cancellationToken);
                    if (pageData is null)
                    {
                        logger.LogWarning(
                            "ComprehensiveAnalysis full sync: page {Page} returned null, skipping.",
                            page);
                        continue;
                    }

                    await UpsertPageAsync(pageData, cancellationToken);
                    totalItems += pageData.Data.Count;

                    logger.LogInformation(
                        "ComprehensiveAnalysis full sync: page {Page}/{Last} done, {Count} items.",
                        page,
                        lastPage,
                        pageData.Data.Count);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "ComprehensiveAnalysis full sync: error on page {Page}.", page);
                    lastError = ex.Message;
                }
            }

            var status = lastError is null ? "Completed" : "CompletedWithErrors";
            await repository.UpdateSyncLogAsync(logId, totalPages, totalItems, status, lastError, cancellationToken);

            logger.LogInformation(
                "ComprehensiveAnalysis full sync finished: {Pages} pages, {Items} items, {Duration}.",
                totalPages,
                totalItems,
                sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await repository.UpdateSyncLogAsync(logId, totalPages, totalItems, "Failed", ex.Message, cancellationToken);
            logger.LogError(ex, "ComprehensiveAnalysis full sync failed.");
            throw;
        }

        return new ComprehensiveAnalysisFullSyncResult(totalPages, totalItems, sw.Elapsed);
    }

    private async Task UpsertPageAsync(
        ComprehensiveAnalysisPagedResponse page,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var mapped = page.Data.Select(dto =>
        {
            var analysis = new ComprehensiveAnalysisRow
            {
                Id = dto.Id,
                Title = dto.Title ?? string.Empty,
                Summary = dto.Summary ?? string.Empty,
                CreatedAt = DateTimeOffset.Parse(dto.CreatedAt),
                PersianCreatedAt = dto.Pcreate ?? string.Empty,
                AuthorId = dto.UserId,
                AuthorName = dto.Categories.Count > 0 ? dto.Categories[0].Name : string.Empty,
                SyncedAt = now
            };

            var tags = dto.Tags.Select(t => new ComprehensiveAnalysisTagRow
            {
                AnalysisId = dto.Id,
                TagId = t.Id,
                TagName = t.Name ?? string.Empty,
                TagSlug = t.Slug ?? string.Empty,
                TagTypeId = t.TypeId,
                IsAnalytic = t.Analytic == 1
            });

            var categories = dto.Categories.Select(c => new ComprehensiveAnalysisCategoryRow
            {
                AnalysisId = dto.Id,
                CategoryId = c.Id,
                CategoryName = c.Name ?? string.Empty
            });

            return (analysis, tags, categories);
        });

        await repository.UpsertAsync(mapped, cancellationToken);
    }
}

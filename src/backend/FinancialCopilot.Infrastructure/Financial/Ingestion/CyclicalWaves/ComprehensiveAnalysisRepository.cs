using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class ComprehensiveAnalysisRepository(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider)
    : IComprehensiveAnalysisQueryRepository, IComprehensiveAnalysisSyncRunReader
{
    // --- Write side ---

    public async Task UpsertAsync(
        IEnumerable<(ComprehensiveAnalysisRow Analysis, IEnumerable<ComprehensiveAnalysisTagRow> Tags, IEnumerable<ComprehensiveAnalysisCategoryRow> Categories)> items,
        CancellationToken cancellationToken)
    {
        foreach (var (analysis, tags, categories) in items)
        {
            var existing = await dbContext.ComprehensiveAnalyses
                .FindAsync([analysis.Id], cancellationToken);

            if (existing is null)
            {
                dbContext.ComprehensiveAnalyses.Add(analysis);
            }
            else
            {
                existing.Title = analysis.Title;
                existing.Summary = analysis.Summary;
                existing.CreatedAt = analysis.CreatedAt;
                existing.PersianCreatedAt = analysis.PersianCreatedAt;
                existing.AuthorId = analysis.AuthorId;
                existing.AuthorName = analysis.AuthorName;
                existing.SyncedAt = analysis.SyncedAt;

                var oldTags = dbContext.ComprehensiveAnalysisTags
                    .Where(t => t.AnalysisId == analysis.Id);
                dbContext.ComprehensiveAnalysisTags.RemoveRange(oldTags);

                var oldCategories = dbContext.ComprehensiveAnalysisCategories
                    .Where(c => c.AnalysisId == analysis.Id);
                dbContext.ComprehensiveAnalysisCategories.RemoveRange(oldCategories);
            }

            dbContext.ComprehensiveAnalysisTags.AddRange(tags);
            dbContext.ComprehensiveAnalysisCategories.AddRange(categories);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> InsertSyncLogAsync(string jobName, CancellationToken cancellationToken)
    {
        var row = new ComprehensiveAnalysisSyncLogRow
        {
            JobName = jobName,
            StartedAt = timeProvider.GetUtcNow(),
            Status = "Running",
            PagesTotal = 0,
            ItemsSynced = 0
        };

        dbContext.ComprehensiveAnalysisSyncLogs.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task UpdateSyncLogAsync(
        int logId,
        int pagesTotal,
        int itemsSynced,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.ComprehensiveAnalysisSyncLogs.FindAsync([logId], cancellationToken);
        if (row is null)
        {
            return;
        }

        row.PagesTotal = pagesTotal;
        row.ItemsSynced = itemsSynced;
        row.Status = status;
        row.ErrorMessage = errorMessage;
        row.FinishedAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // --- IComprehensiveAnalysisSyncRunReader ---

    public async Task<IReadOnlyList<ComprehensiveAnalysisSyncRunView>> QueryRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ComprehensiveAnalysisSyncLogs
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ComprehensiveAnalysisSyncRunView(
            r.Id,
            r.JobName,
            r.StartedAt,
            r.FinishedAt,
            r.Status,
            r.PagesTotal,
            r.ItemsSynced,
            r.ErrorMessage)).ToList();
    }

    // --- IComprehensiveAnalysisQueryRepository ---

    public async Task<IReadOnlyList<ComprehensiveAnalysisSummary>> GetLatestBySymbolAsync(
        string symbolName,
        int limit,
        CancellationToken cancellationToken)
    {
        var ids = await dbContext.ComprehensiveAnalysisTags
            .AsNoTracking()
            .Where(t => t.TagName == symbolName && t.TagTypeId == 1)
            .OrderByDescending(t => t.AnalysisId)
            .Select(t => t.AnalysisId)
            .Distinct()
            .Take(limit)
            .ToListAsync(cancellationToken);

        return await LoadSummariesAsync(ids, cancellationToken);
    }

    public async Task<IReadOnlyList<ComprehensiveAnalysisSummary>> GetBySymbolAndTopicAsync(
        string symbolName,
        string topicTagName,
        int limit,
        CancellationToken cancellationToken)
    {
        var symbolIds = dbContext.ComprehensiveAnalysisTags
            .Where(t => t.TagName == symbolName && t.TagTypeId == 1)
            .Select(t => t.AnalysisId);

        var topicIds = dbContext.ComprehensiveAnalysisTags
            .Where(t => t.TagName == topicTagName && t.IsAnalytic)
            .Select(t => t.AnalysisId);

        var ids = await symbolIds
            .Intersect(topicIds)
            .OrderDescending()
            .Take(limit)
            .ToListAsync(cancellationToken);

        return await LoadSummariesAsync(ids, cancellationToken);
    }

    public async Task<IReadOnlyList<ComprehensiveAnalysisSummary>> SearchByTagNamesAsync(
        IReadOnlyList<string> tagNames,
        int limit,
        CancellationToken cancellationToken)
    {
        var ids = await dbContext.ComprehensiveAnalysisTags
            .AsNoTracking()
            .Where(t => tagNames.Contains(t.TagName))
            .Select(t => t.AnalysisId)
            .Distinct()
            .OrderDescending()
            .Take(limit)
            .ToListAsync(cancellationToken);

        return await LoadSummariesAsync(ids, cancellationToken);
    }

    public async Task<ComprehensiveAnalysisSummary?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var summaries = await LoadSummariesAsync([id], cancellationToken);
        return summaries.Count > 0 ? summaries[0] : null;
    }

    private async Task<IReadOnlyList<ComprehensiveAnalysisSummary>> LoadSummariesAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var analyses = await dbContext.ComprehensiveAnalyses
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(cancellationToken);

        var tags = await dbContext.ComprehensiveAnalysisTags
            .AsNoTracking()
            .Where(t => ids.Contains(t.AnalysisId))
            .ToListAsync(cancellationToken);

        var tagsByAnalysis = tags.GroupBy(t => t.AnalysisId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return analyses
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ComprehensiveAnalysisSummary(
                a.Id,
                a.Title,
                StripHtml(a.Summary),
                a.CreatedAt,
                a.PersianCreatedAt,
                a.AuthorName,
                tagsByAnalysis.TryGetValue(a.Id, out var t)
                    ? t.Select(tag => new ComprehensiveAnalysisTagView(
                        tag.TagId,
                        tag.TagName,
                        tag.TagSlug,
                        tag.TagTypeId,
                        tag.IsAnalytic)).ToList()
                    : []))
            .ToList();
    }

    private static readonly Regex HtmlTagPattern = new("<[^>]*>", RegexOptions.Compiled);

    private static string StripHtml(string html) =>
        HtmlTagPattern.Replace(html, string.Empty).Trim();
}

using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class EfCoreComprehensiveAnalysisSearchRepository(
    FinancialIngestionDbContext dbContext,
    IHtmlTextStripper htmlStripper) : IComprehensiveAnalysisSearchRepository
{
    private const int SummaryCapChars = 2000;

    public Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetBySymbolNamesAsync(
        IReadOnlyList<string> symbolNames, int limit, CancellationToken ct) =>
        QueryAsync(symbolNames: symbolNames, topicTagSlugs: [], from: null, limit: limit, ct: ct);

    public Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetByTopicTagsAsync(
        IReadOnlyList<string> topicTagSlugs, int limit, CancellationToken ct) =>
        QueryAsync(symbolNames: [], topicTagSlugs: topicTagSlugs, from: null, limit: limit, ct: ct);

    public Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetByDateRangeAsync(
        DateTimeOffset from, int limit, CancellationToken ct) =>
        QueryAsync(symbolNames: [], topicTagSlugs: [], from: from, limit: limit, ct: ct);

    public Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetCombinedAsync(
        IReadOnlyList<string> symbolNames,
        IReadOnlyList<string> topicTagSlugs,
        DateTimeOffset? from,
        int limit,
        CancellationToken ct) =>
        QueryAsync(symbolNames: symbolNames, topicTagSlugs: topicTagSlugs, from: from, limit: limit, ct: ct);

    private async Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> QueryAsync(
        IReadOnlyList<string> symbolNames,
        IReadOnlyList<string> topicTagSlugs,
        DateTimeOffset? from,
        int limit,
        CancellationToken ct)
    {
        // Start from analyses and apply tag-based filters via Any() subqueries.
        // Using Any() instead of Intersect chains avoids the unfiltered base-set
        // that INTERSECT requires and generates cleaner EXISTS sub-selects.
        IQueryable<ComprehensiveAnalysisRow> query = dbContext.ComprehensiveAnalyses.AsNoTracking();

        if (symbolNames.Count > 0)
        {
            query = query.Where(a =>
                dbContext.ComprehensiveAnalysisTags.Any(t =>
                    t.AnalysisId == a.Id &&
                    t.TagTypeId == 1 &&
                    symbolNames.Contains(t.TagName)));
        }

        if (topicTagSlugs.Count > 0)
        {
            query = query.Where(a =>
                dbContext.ComprehensiveAnalysisTags.Any(t =>
                    t.AnalysisId == a.Id &&
                    t.IsAnalytic &&
                    topicTagSlugs.Contains(t.TagName)));
        }

        if (from.HasValue)
            query = query.Where(a => a.CreatedAt >= from.Value);

        var analyses = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        if (analyses.Count == 0)
            return [];

        var ids = analyses.Select(a => a.Id).ToList();

        var tags = await dbContext.ComprehensiveAnalysisTags
            .AsNoTracking()
            .Where(t => ids.Contains(t.AnalysisId))
            .ToListAsync(ct);

        var tagsByAnalysis = tags.GroupBy(t => t.AnalysisId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.TagName).ToList());

        return analyses.Select(a =>
        {
            var plain = string.IsNullOrEmpty(a.PlainTextSummary)
                ? htmlStripper.Strip(a.Summary)
                : a.PlainTextSummary;

            var capped = plain.Length > SummaryCapChars
                ? plain[..SummaryCapChars]
                : plain;

            return new ComprehensiveAnalysisSummaryItem(
                a.Id,
                a.Title,
                a.PersianCreatedAt,
                a.AuthorName,
                capped,
                tagsByAnalysis.TryGetValue(a.Id, out var t) ? t : [],
                a.SyncedAt);
        }).ToList();
    }
}

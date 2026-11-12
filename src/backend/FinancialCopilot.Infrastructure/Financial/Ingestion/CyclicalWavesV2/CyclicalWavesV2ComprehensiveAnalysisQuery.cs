using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// CyclicalWaves-mode comprehensive analysis retrieval over <c>cyclicalwaves."Analyses"</c> (design §10.4).
/// Live evidence (2026-10-31): most articles carry no tags and name their ticker as the title prefix
/// («شتوکا- گزارش عملکرد مهر 1405»); topics arrive as underscore tags («گزارش_فصلی», «P-E»).
/// Only the title, the published summary and the publication date exist; there is no full article body.
/// Feature 159 (owner, 2026-11-10): an analysis belongs to a ticker when the ticker is a whole word of its title, at
/// any position («حق تقدم شگل», «شگل، بخش سوم - نسبت P/s»); tags no longer select analyses for a ticker, because
/// daily reports such as «رصد معاملات عمده» tag many tickers. Topic tags still filter when asked.
/// The provider `summary` is the whole paid analysis text (owner, 2026-11-10), so only its partial preview leaves this
/// query (<see cref="ComprehensiveAnalysisTeaserPolicy"/>); the whole text never reaches an answer, the API or a prompt.
/// </summary>
public sealed class CyclicalWavesV2ComprehensiveAnalysisQuery(
    FinancialIngestionDbContext db,
    IOptions<ComprehensiveAnalysisPresentationOptions>? presentation = null) : IComprehensiveAnalysisQueryUseCase
{
    private readonly ComprehensiveAnalysisPresentationOptions _presentation = presentation?.Value ?? new();

    public const int DefaultLimit = 5;
    public const int MaximumLimit = 10;

    /// <summary>Upper bound of the whole text for an entitled reader (change «max-plan-full-analysis-text»).</summary>
    public const int MaximumFullTextCharacters = 20_000;
    private const int CandidateWindow = 400;
    private const int MaximumSymbolWords = 3;
    private static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);
    private static readonly string TechnicalTopic = NormalizeTopic(ComprehensiveAnalysisTopics.TechnicalAnalysis);
    private const string TechnicalWordPersian = ComprehensiveAnalysisTopics.TechnicalTitleWord;
    private static readonly string TechnicalWordArabic = TechnicalWordPersian.Replace('ی', 'ي').Replace('ک', 'ك');
    private static readonly string TechnicalWordNormalized = PersianSymbolNormalizer.Normalize(TechnicalWordPersian);

    public async Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(
        ComprehensiveAnalysisQueryRequest request,
        CancellationToken cancellationToken)
    {
        var limit = request.Limit <= 0 ? DefaultLimit : Math.Min(request.Limit, MaximumLimit);
        var symbols = request.SymbolNames
            .Select(symbol => (Raw: symbol.Trim(), Normalized: PersianSymbolNormalizer.Normalize(symbol)))
            .Where(symbol => symbol.Normalized.Length > 0)
            .DistinctBy(symbol => symbol.Normalized)
            .ToList();
        var topics = request.TopicTags.Select(NormalizeTopic).Where(topic => topic.Length > 0).Distinct().ToList();
        if (symbols.Count == 0 && topics.Count == 0)
        {
            return new ComprehensiveAnalysisQueryResponse([], []);
        }

        IQueryable<CyclicalWavesV2AnalysisRow> query = db.CyclicalWavesV2Analyses.AsNoTracking();
        if (request.FromDate is { } from)
        {
            query = query.Where(row => row.PublishedAt >= from.ToUniversalTime());
        }

        // BUG-020: most analyses carry no tags, so a technical analysis is also one whose title has «تکنیکال»
        // (Persian or Arabic letters here; the whole-word rule is applied below).
        var technical = topics.Contains(TechnicalTopic);
        if (topics.Count > 0)
        {
            query = query.Where(row => db.CyclicalWavesV2AnalysisTags.Any(tag => tag.AnalysisId == row.Id && topics.Contains(tag.NormalizedName)) ||
                                       (technical && (row.Title.Contains(TechnicalWordPersian) || row.Title.Contains(TechnicalWordArabic))));
        }

        var candidates = new List<CyclicalWavesV2AnalysisRow>();
        if (symbols.Count == 0)
        {
            candidates.AddRange(await query.OrderByDescending(row => row.PublishedAt).Take(CandidateWindow).ToListAsync(cancellationToken));
        }

        // One narrow query per symbol (title contains the ticker in Persian or Arabic letters); the whole-word rule is applied below.
        foreach (var (raw, _) in symbols.Take(MaximumLimit))
        {
            var persian = raw.Replace('ي', 'ی').Replace('ك', 'ک');
            var arabic = persian.Replace('ی', 'ي').Replace('ک', 'ك');
            candidates.AddRange(await query
                .Where(row => row.Title.Contains(persian) || row.Title.Contains(arabic))
                .OrderByDescending(row => row.PublishedAt)
                .Take(CandidateWindow)
                .ToListAsync(cancellationToken));
        }

        candidates = candidates.DistinctBy(row => row.Id).OrderByDescending(row => row.PublishedAt).ThenByDescending(row => row.Id).ToList();
        var ids = candidates.Select(row => row.Id).ToArray();
        var tags = (await db.CyclicalWavesV2AnalysisTags.AsNoTracking().Where(tag => ids.Contains(tag.AnalysisId)).ToListAsync(cancellationToken))
            .ToLookup(tag => tag.AnalysisId);
        var categories = (await db.CyclicalWavesV2AnalysisCategories.AsNoTracking().Where(category => ids.Contains(category.AnalysisId)).ToListAsync(cancellationToken))
            .ToLookup(category => category.AnalysisId);

        var matchedSymbols = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<ComprehensiveAnalysisSummaryItem>();
        foreach (var row in candidates)
        {
            if (topics.Count > 0 && !tags[row.Id].Any(tag => topics.Contains(tag.NormalizedName)) &&
                !(technical && TitleWords(row.Title).Contains(TechnicalWordNormalized)))
            {
                continue;
            }

            if (symbols.Count > 0)
            {
                var matches = symbols.Where(symbol => MatchesTicker(row.Title, symbol.Normalized)).ToList();
                if (matches.Count == 0)
                {
                    continue;
                }

                matchedSymbols.UnionWith(matches.Select(symbol => symbol.Raw));
            }

            items.Add(ToItem(row, tags[row.Id], categories[row.Id], request.IncludeFullText));
            if (items.Count == limit)
            {
                break;
            }
        }

        var unresolved = symbols.Select(symbol => symbol.Raw).Where(symbol => !matchedSymbols.Contains(symbol)).ToList();
        return new ComprehensiveAnalysisQueryResponse(items, unresolved,
            FullTextIncluded: request.IncludeFullText && items.Count > 0);
    }

    /// <summary>
    /// An article belongs to a ticker when the ticker is a whole word of the title (one to three consecutive words for
    /// tickers written with a space), compared in normalized form. «فرابورس» does not match «بورس».
    /// </summary>
    internal static bool MatchesTicker(string title, string normalizedTicker)
    {
        if (normalizedTicker.Length == 0)
        {
            return false;
        }

        var words = TitleWords(title);
        for (var start = 0; start < words.Count; start++)
        {
            var joined = string.Empty;
            for (var count = 1; count <= MaximumSymbolWords && start + count <= words.Count; count++)
            {
                joined += words[start + count - 1];
                if (joined == normalizedTicker)
                {
                    return true;
                }

                if (joined.Length >= normalizedTicker.Length)
                {
                    break;
                }
            }
        }

        return false;
    }

    /// <summary>Normalized title words, split on white space, punctuation («، ؛ - : / ( )») and symbols («|»).</summary>
    internal static IReadOnlyList<string> TitleWords(string title)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in title)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch) || char.IsSeparator(ch))
            {
                Flush();
                continue;
            }

            current.Append(ch);
        }

        Flush();
        return words;

        void Flush()
        {
            var word = PersianSymbolNormalizer.Normalize(current.ToString());
            if (word.Length > 0)
            {
                words.Add(word);
            }

            current.Clear();
        }
    }


    /// <summary>Topic slugs («نمودار_P_S», «تحلیل تکنیکال») map to stored tag names («P-S», «تحلیل_تکنیکال») by normalized form.</summary>
    internal static string NormalizeTopic(string topic)
    {
        var normalized = PersianSymbolNormalizer.Normalize(topic);
        const string chartPrefix = "نمودار";
        return normalized.StartsWith(chartPrefix, StringComparison.Ordinal) ? normalized[chartPrefix.Length..] : normalized;
    }

    private ComprehensiveAnalysisSummaryItem ToItem(
        CyclicalWavesV2AnalysisRow row,
        IEnumerable<CyclicalWavesV2AnalysisTagRow> tags,
        IEnumerable<CyclicalWavesV2AnalysisCategoryRow> categories,
        bool includeFullText)
    {
        // Change «max-plan-full-analysis-text»: the whole text only for an entitled reader (bounded, the longest seen is
        // about 4,000 characters); everyone else gets the preview, so the whole text never leaves this query for them.
        var text = includeFullText
            ? ComprehensiveAnalysisTeaserPolicy.FitTo(
                ComprehensiveAnalysisTeaserPolicy.FullText(row.Title, row.PlainTextSummary), MaximumFullTextCharacters)
            : ComprehensiveAnalysisTeaserPolicy.Teaser(row.Title, row.PlainTextSummary, _presentation);
        return new ComprehensiveAnalysisSummaryItem(
            row.Id,
            row.Title,
            PersianDate(row.PublishedAt),
            string.Join("، ", categories.Select(category => category.Title)),
            text,
            tags.Select(tag => tag.Name).ToList(),
            row.LastSyncedAt);
    }

    private static string PersianDate(DateTimeOffset publishedAt)
    {
        var calendar = new PersianCalendar();
        var local = publishedAt.ToOffset(TehranOffset).DateTime;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}");
    }
}

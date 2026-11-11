namespace FinancialCopilot.Application.FinancialData.Ingestion;

// --- Parser output ---

public enum ComprehensiveAnalysisParseStatus { Parsed, ClarificationRequired }

public sealed record ComprehensiveAnalysisParseResult(
    ComprehensiveAnalysisParseStatus Status,
    IReadOnlyList<string> SymbolNames,
    IReadOnlyList<string> TopicTags,
    DateTimeOffset? FromDate,
    int Limit,
    string? ClarificationPrompt = null);

// --- Use case input ---

/// <param name="IncludeFullText">
/// Feature 162 change «max-plan-full-analysis-text»: true only for a reader the entitlement port granted; the items then
/// carry the whole text instead of the preview. Default false: the preview, cut at the source.
/// </param>
public sealed record ComprehensiveAnalysisQueryRequest(
    IReadOnlyList<string> SymbolNames,
    IReadOnlyList<string> TopicTags,
    DateTimeOffset? FromDate,
    int Limit,
    bool IncludeFullText = false);

// --- Single analysis item returned to AI ---

public sealed record ComprehensiveAnalysisSummaryItem(
    long AnalysisId,
    string Title,
    string PersianCreatedAt,
    string AuthorName,
    string PlainTextSummary,
    IReadOnlyList<string> TagNames,
    DateTimeOffset SyncedAt);

// --- Use case result ---

/// <param name="TechnicalAnalysisNotFoundSymbols">
/// BUG-020: symbols asked for a technical analysis that has none; <see cref="Items"/> then holds their latest analysis.
/// </param>
/// <param name="FullTextIncluded">True when <see cref="Items"/> carry the whole text (entitled reader), not the preview.</param>
public sealed record ComprehensiveAnalysisQueryResponse(
    IReadOnlyList<ComprehensiveAnalysisSummaryItem> Items,
    IReadOnlyList<string> UnresolvedSymbols,
    IReadOnlyList<string>? TechnicalAnalysisNotFoundSymbols = null,
    bool FullTextIncluded = false)
{
    public bool HasResults => Items.Count > 0;
}

/// <summary>Topic tags of TahlilApp analyses that the chat asks for (stored tag names, underscore form).</summary>
public static class ComprehensiveAnalysisTopics
{
    public const string TechnicalAnalysis = "تحلیل_تکنیکال";

    /// <summary>BUG-020: most analyses carry no tags, so a technical analysis is also one whose title has this word.</summary>
    public const string TechnicalTitleWord = "تکنیکال";
}

// --- Interfaces ---

public interface IComprehensiveAnalysisQueryParser
{
    Task<ComprehensiveAnalysisParseResult> ParseAsync(
        string userMessage,
        CancellationToken cancellationToken);
}

public interface IComprehensiveAnalysisQueryUseCase
{
    Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(
        ComprehensiveAnalysisQueryRequest request,
        CancellationToken cancellationToken);
}

public interface IComprehensiveAnalysisSearchRepository
{
    Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetBySymbolNamesAsync(
        IReadOnlyList<string> symbolNames, int limit, CancellationToken ct);

    Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetByTopicTagsAsync(
        IReadOnlyList<string> topicTagSlugs, int limit, CancellationToken ct);

    Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetByDateRangeAsync(
        DateTimeOffset from, int limit, CancellationToken ct);

    Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetCombinedAsync(
        IReadOnlyList<string> symbolNames,
        IReadOnlyList<string> topicTagSlugs,
        DateTimeOffset? from,
        int limit,
        CancellationToken ct);
}

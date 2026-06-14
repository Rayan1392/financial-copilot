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

public sealed record ComprehensiveAnalysisQueryRequest(
    IReadOnlyList<string> SymbolNames,
    IReadOnlyList<string> TopicTags,
    DateTimeOffset? FromDate,
    int Limit);

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

public sealed record ComprehensiveAnalysisQueryResponse(
    IReadOnlyList<ComprehensiveAnalysisSummaryItem> Items,
    IReadOnlyList<string> UnresolvedSymbols)
{
    public bool HasResults => Items.Count > 0;
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

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed record ComprehensiveAnalysisTagView(
    int TagId,
    string TagName,
    string TagSlug,
    int TagTypeId,
    bool IsAnalytic);

public sealed record ComprehensiveAnalysisSummary(
    long Id,
    string Title,
    string PlainTextSummary,
    DateTimeOffset CreatedAt,
    string PersianCreatedAt,
    string AuthorName,
    IReadOnlyList<ComprehensiveAnalysisTagView> Tags);

// --- Sync service contracts ---

public sealed record ComprehensiveAnalysisFullSyncResult(
    int PagesTotal,
    int ItemsSynced,
    TimeSpan Duration);

public sealed record ComprehensiveAnalysisDailySyncResult(
    int PagesTotal,
    int ItemsSynced,
    TimeSpan Duration);

public sealed record ComprehensiveAnalysisSyncRunView(
    int Id,
    string JobName,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Status,
    int PagesTotal,
    int ItemsSynced,
    string? ErrorMessage);

public sealed record ComprehensiveAnalysisBackfillResult(int RowsUpdated);

public interface IComprehensiveAnalysisPlainTextBackfillService
{
    Task<ComprehensiveAnalysisBackfillResult> ExecuteAsync(CancellationToken cancellationToken);
}

public interface IComprehensiveAnalysisFullSyncService
{
    Task<ComprehensiveAnalysisFullSyncResult> ExecuteAsync(CancellationToken cancellationToken);
}

public interface IComprehensiveAnalysisDailySyncService
{
    Task<ComprehensiveAnalysisDailySyncResult> ExecuteAsync(CancellationToken cancellationToken);
}

public interface IComprehensiveAnalysisSyncRunReader
{
    Task<IReadOnlyList<ComprehensiveAnalysisSyncRunView>> QueryRecentAsync(
        int limit,
        CancellationToken cancellationToken);
}

// --- AI query contracts ---

public sealed record ComprehensiveAnalysisQuery(
    string? SymbolName,
    IReadOnlyList<string>? TopicTags,
    int Limit = 5);

public sealed record ComprehensiveAnalysisQueryResult(
    IReadOnlyList<ComprehensiveAnalysisSummary> Items);

public interface IComprehensiveAnalysisQueryRepository
{
    Task<IReadOnlyList<ComprehensiveAnalysisSummary>> GetLatestBySymbolAsync(
        string symbolName,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ComprehensiveAnalysisSummary>> GetBySymbolAndTopicAsync(
        string symbolName,
        string topicTagName,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ComprehensiveAnalysisSummary>> SearchByTagNamesAsync(
        IReadOnlyList<string> tagNames,
        int limit,
        CancellationToken cancellationToken);

    Task<ComprehensiveAnalysisSummary?> GetByIdAsync(long id, CancellationToken cancellationToken);
}

public interface IQueryComprehensiveAnalysisUseCase
{
    Task<ComprehensiveAnalysisQueryResult> ExecuteAsync(
        ComprehensiveAnalysisQuery query,
        CancellationToken cancellationToken);
}

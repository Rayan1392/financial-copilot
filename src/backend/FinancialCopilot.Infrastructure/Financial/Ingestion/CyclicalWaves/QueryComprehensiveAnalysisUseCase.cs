using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class QueryComprehensiveAnalysisUseCase(
    IComprehensiveAnalysisQueryRepository repository)
    : IQueryComprehensiveAnalysisUseCase
{
    public async Task<ComprehensiveAnalysisQueryResult> ExecuteAsync(
        ComprehensiveAnalysisQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 20);

        IReadOnlyList<ComprehensiveAnalysisSummary> items;

        if (!string.IsNullOrWhiteSpace(query.SymbolName) &&
            query.TopicTags is { Count: > 0 })
        {
            // Symbol + one or more topic tags: use the first topic tag to intersect
            items = await repository.GetBySymbolAndTopicAsync(
                query.SymbolName.Trim(),
                query.TopicTags[0].Trim(),
                limit,
                cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(query.SymbolName))
        {
            items = await repository.GetLatestBySymbolAsync(
                query.SymbolName.Trim(),
                limit,
                cancellationToken);
        }
        else if (query.TopicTags is { Count: > 0 })
        {
            items = await repository.SearchByTagNamesAsync(
                query.TopicTags.Select(t => t.Trim()).ToList(),
                limit,
                cancellationToken);
        }
        else
        {
            items = [];
        }

        return new ComprehensiveAnalysisQueryResult(items);
    }
}

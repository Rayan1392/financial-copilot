using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class ComprehensiveAnalysisQueryUseCase(
    IComprehensiveAnalysisSearchRepository repository) : IComprehensiveAnalysisQueryUseCase
{
    public async Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(
        ComprehensiveAnalysisQueryRequest request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 5);
        var hasSymbols = request.SymbolNames.Count > 0;
        var hasTags = request.TopicTags.Count > 0;
        var hasDate = request.FromDate.HasValue;

        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items;

        if (hasSymbols || hasTags || hasDate)
        {
            items = await repository.GetCombinedAsync(
                request.SymbolNames,
                request.TopicTags,
                request.FromDate,
                limit,
                cancellationToken);
        }
        else
        {
            items = [];
        }

        // Detect which requested symbols produced zero results
        var unresolvedSymbols = new List<string>();
        if (hasSymbols)
        {
            var foundSymbols = items
                .SelectMany(i => i.TagNames)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var sym in request.SymbolNames)
            {
                if (!foundSymbols.Contains(sym))
                    unresolvedSymbols.Add(sym);
            }
        }

        return new ComprehensiveAnalysisQueryResponse(items, unresolvedSymbols);
    }
}

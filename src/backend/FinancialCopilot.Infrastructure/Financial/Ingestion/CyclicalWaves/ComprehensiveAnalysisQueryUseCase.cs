using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class ComprehensiveAnalysisQueryUseCase(
    IComprehensiveAnalysisSearchRepository repository,
    TimeProvider timeProvider) : IComprehensiveAnalysisQueryUseCase
{
    // Analyses older than 30 days are considered stale; use that as the default window.
    // The parser overrides this when the user explicitly names a date or range.
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    public async Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(
        ComprehensiveAnalysisQueryRequest request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 5);
        var hasSymbols = request.SymbolNames.Count > 0;
        var hasTags = request.TopicTags.Count > 0;

        // Default to 30-day window unless the parser resolved an explicit date.
        var effectiveFrom = request.FromDate ?? timeProvider.GetUtcNow() - DefaultWindow;

        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items;

        if (hasSymbols || hasTags)
        {
            items = await repository.GetCombinedAsync(
                request.SymbolNames,
                request.TopicTags,
                effectiveFrom,
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

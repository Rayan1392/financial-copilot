using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class ComprehensiveAnalysisQueryUseCase(
    IComprehensiveAnalysisSearchRepository repository,
    TimeProvider timeProvider) : IComprehensiveAnalysisQueryUseCase
{
    // Analysis posts older than 3 months are rarely actionable; cap the window
    // unless the user explicitly requested an earlier date via the parser.
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    public async Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(
        ComprehensiveAnalysisQueryRequest request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 5);
        var hasSymbols = request.SymbolNames.Count > 0;
        var hasTags = request.TopicTags.Count > 0;

        // Apply a default 3-month window when the user did not specify a date.
        // If the parser already resolved a date (e.g. "این ماه", "هفته گذشته", ISO date),
        // use that value as-is — it may be earlier or later than the default.
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

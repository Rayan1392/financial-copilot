namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Change «analysis-recency-window» (Feature 159, owner, 2026-11-16): a TahlilApp analysis is valid only for
/// <see cref="ComprehensiveAnalysisPresentationOptions.MaxAgeDays"/> days after publication. This decorator is the one
/// owner of that rule: it raises the search's lower bound to «now − MaxAgeDays», so an older analysis is never returned
/// to any caller, and a period the user names can narrow the window but never widen it. The response records the
/// window so that the "no analysis found" sentence can say it searched only recent analyses.
/// </summary>
public sealed class RecentComprehensiveAnalysisQueryUseCase(
    IComprehensiveAnalysisQueryUseCase inner,
    ComprehensiveAnalysisPresentationOptions options,
    TimeProvider timeProvider) : IComprehensiveAnalysisQueryUseCase
{
    public async Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(
        ComprehensiveAnalysisQueryRequest request,
        CancellationToken cancellationToken)
    {
        var windowStart = timeProvider.GetUtcNow() - TimeSpan.FromDays(options.MaxAgeDays);
        var from = request.FromDate is { } asked && asked > windowStart ? asked : windowStart;
        var response = await inner.ExecuteAsync(request with { FromDate = from }, cancellationToken);
        return response with { RecentWindowDays = options.MaxAgeDays };
    }
}

using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// BUG-032 (owner, 2026-11-17): when the agent's analysis tool finds nothing, its card still names the recency window
/// (OQ-099) instead of the old «تحلیلی در تحلیل‌اپ پیدا نشد» sentence.
/// </summary>
public sealed class Bug032AnalysisToolCardTests
{
    private sealed class EmptyUseCase(int? recentWindowDays) : IComprehensiveAnalysisQueryUseCase
    {
        public Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(ComprehensiveAnalysisQueryRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ComprehensiveAnalysisQueryResponse([], request.SymbolNames, RecentWindowDays: recentWindowDays));
    }

    private static Task<ComprehensiveAnalysisToolResult> QueryAsync(int? recentWindowDays) =>
        new ComprehensiveAnalysisToolAdapter(
                new EmptyUseCase(recentWindowDays),
                Options.Create(new ComprehensiveAnalysisPresentationOptions()))
            .QueryAsync(["شکبیر"], null, null, 1, CancellationToken.None);

    [Fact]
    public async Task NothingFound_InTheWindow_TheCardNamesTheWindow()
    {
        var result = await QueryAsync(30);

        Assert.False(result.Succeeded);
        Assert.Null(result.QueryResponse);
        Assert.Contains("برای نماد شکبیر تحلیل تازه‌ای در ۳۰ روز اخیر در تحلیل‌اپ پیدا نشد.", result.AgentSummary);
    }

    [Fact]
    public async Task NothingFound_WithoutAWindow_KeepsTheGeneralSentence()
    {
        var result = await QueryAsync(null);

        Assert.Contains("برای نماد شکبیر تحلیلی در تحلیل‌اپ پیدا نشد.", result.AgentSummary);
    }
}

using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 159 change «analysis-recency-window»: the 30-day rule, its card sentence and its hand-off to Feature 163.</summary>
public sealed class AnalysisRecencyWindow159Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingUseCase : IComprehensiveAnalysisQueryUseCase
    {
        public ComprehensiveAnalysisQueryRequest? Last { get; private set; }

        public Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(ComprehensiveAnalysisQueryRequest request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(new ComprehensiveAnalysisQueryResponse([], request.SymbolNames));
        }
    }

    private static async Task<(ComprehensiveAnalysisQueryRequest Sent, ComprehensiveAnalysisQueryResponse Response)> Run(
        DateTimeOffset? asked, int maxAgeDays = 30)
    {
        var inner = new RecordingUseCase();
        var decorator = new RecentComprehensiveAnalysisQueryUseCase(
            inner, new ComprehensiveAnalysisPresentationOptions { MaxAgeDays = maxAgeDays }, new FixedTimeProvider(Now));
        var response = await decorator.ExecuteAsync(new ComprehensiveAnalysisQueryRequest(["چدن"], [], asked, 1), CancellationToken.None);
        return (inner.Last!, response);
    }

    [Fact]
    public async Task NoPeriod_SearchesOnlyTheLast30Days()
    {
        var (sent, response) = await Run(null);

        Assert.Equal(Now.AddDays(-30), sent.FromDate);
        Assert.Equal(30, response.RecentWindowDays);
    }

    [Fact]
    public async Task AnOlderPeriod_CannotWidenTheWindow()
    {
        var (sent, _) = await Run(new DateTimeOffset(2023, 11, 12, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(Now.AddDays(-30), sent.FromDate);
    }

    [Fact]
    public async Task ANewerPeriod_NarrowsTheWindow()
    {
        var asked = Now.AddDays(-3);

        var (sent, _) = await Run(asked);

        Assert.Equal(asked, sent.FromDate);
    }

    [Fact]
    public async Task TheWindowFollowsTheOption()
    {
        var (sent, response) = await Run(null, maxAgeDays: 7);

        Assert.Equal(Now.AddDays(-7), sent.FromDate);
        Assert.Equal(7, response.RecentWindowDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void MaxAgeDays_OutOfRange_IsInvalid(int days) =>
        Assert.False(new ComprehensiveAnalysisPresentationOptions { MaxAgeDays = days }.IsValid(out _));

    [Fact] // OQ-099 text.
    public void NoRecentAnalysis_SentenceSaysTheWindow()
    {
        Assert.Equal("برای نماد چدن تحلیل تازه‌ای در ۳۰ روز اخیر در تحلیل‌اپ پیدا نشد.",
            ComprehensiveAnalysisCardComposer.NoResultSentence(["چدن"], 30));
        Assert.Equal("تحلیل تازه‌ای در ۷ روز اخیر در تحلیل‌اپ پیدا نشد.",
            ComprehensiveAnalysisCardComposer.NoResultSentence([], 7));
    }

    [Theory] // D4: only this no-data answer reaches Feature 163; every other no-data answer keeps its message (OQ-092).
    [InlineData(DialogueOutcome.NoData, DialogueOutcomeReasonCodes.ComprehensiveAnalysisNotRecent, true)]
    [InlineData(DialogueOutcome.NoData, DialogueOutcomeReasonCodes.SupportedButNoRows, false)]
    [InlineData(DialogueOutcome.Unsupported, DialogueOutcomeReasonCodes.CapabilityNotRecognized, true)]
    [InlineData(DialogueOutcome.ClarificationNeeded, DialogueOutcomeReasonCodes.ComprehensiveAnalysisNotRecent, false)]
    public void FallbackReplaces_OnlyTheOwnerApprovedOutcomes(DialogueOutcome outcome, string reason, bool expected) =>
        Assert.Equal(expected, SymbolGroundedFallbackEligibility.MayReplace(outcome, reason));
}

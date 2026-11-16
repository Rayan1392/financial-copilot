using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 162 change «report-interpretation-suggestion» (owner, 2026-11-16): an interpretation suggestion goes first
/// under a shown quarterly income statement, balance sheet or monthly sales trend, and at most five suggestions remain.
/// </summary>
public sealed class ReportInterpretationSuggestion162Tests
{
    [Fact]
    public void Texts_MatchTheOwnersWording()
    {
        Assert.Equal("صورت سود و زیان فاذر را تفسیر کن", ReportInterpretationFollowUp.Message(ReportFamily.QuarterlyIncomeStatement, "فاذر"));
        Assert.Equal("ترازنامه فاذر را تفسیر کن", ReportInterpretationFollowUp.Message(ReportFamily.BalanceSheet, "فاذر"));
        Assert.Equal("روند فروش ماهانه فاذر را تفسیر کن", ReportInterpretationFollowUp.Message(ReportFamily.MonthlySales, "فاذر"));
    }

    public static TheoryData<string, ReportFamily> Reports() => new()
    {
        { "quarterly", ReportFamily.QuarterlyIncomeStatement },
        { "balance", ReportFamily.BalanceSheet },
        { "monthly", ReportFamily.MonthlySales }
    };

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Pipeline_PutsTheInterpretationFirst_ThenTheExistingSuggestionsInOrder(string report, ReportFamily family)
    {
        var context = Context(report);
        var result = await Pipeline(enabled: true, "a", "b").BuildAsync(context, default);

        Assert.True(result.Applied);
        var first = result.Actions!.First();
        Assert.Equal(SuggestedActionKind.DiscussReport, first.Kind);
        Assert.Equal(ReportInterpretationFollowUp.CapabilityCode, first.CapabilityCode);
        Assert.StartsWith(ReportInterpretationFollowUp.IdPrefix, first.Id, StringComparison.Ordinal);
        Assert.Equal(ReportInterpretationFollowUp.Message(family, Symbol(report)), first.Message);
        Assert.Equal(["a", "b"], result.Actions!.Skip(1).Select(action => action.Id));
    }

    [Fact]
    public async Task Pipeline_KeepsAtMostFive_CuttingExistingOnesAfterTheFourth()
    {
        var result = await Pipeline(enabled: true, "a", "b", "c", "d", "e").BuildAsync(Context("balance"), default);

        Assert.Equal(FollowUpSuggestionPipeline.MaximumPresentedActions, result.Actions!.Count);
        Assert.Equal(SuggestedActionKind.DiscussReport, result.Actions.First().Kind);
        Assert.Equal(["a", "b", "c", "d"], result.Actions.Skip(1).Select(action => action.Id));
    }

    [Fact]
    public async Task Pipeline_ReportDiscussionDisabled_SuggestionsAreUnchanged()
    {
        var result = await Pipeline(enabled: false, "a", "b", "c", "d", "e").BuildAsync(Context("quarterly"), default);
        Assert.Equal(["a", "b", "c", "d", "e"], result.Actions!.Select(action => action.Id));
    }

    [Theory]
    [InlineData(DialogueOutcome.NoData)]
    [InlineData(DialogueOutcome.ClarificationNeeded)]
    public async Task Pipeline_NotAnswered_SuggestionsAreUnchanged(DialogueOutcome outcome)
    {
        var result = await Pipeline(enabled: true, "a").BuildAsync(Context("quarterly", outcome), default);
        Assert.Equal(["a"], result.Actions!.Select(action => action.Id));
    }

    [Fact]
    public async Task Pipeline_AnswerWithoutADiscussableReport_IsUnchanged()
    {
        var context = new FollowUpSuggestionContext(Request(), DetectedIntent.SymbolLookup, DialogueOutcome.Answered, "fa");
        var result = await Pipeline(enabled: true, "a").BuildAsync(context, default);
        Assert.Equal(["a"], result.Actions!.Select(action => action.Id));
    }

    [Fact]
    public async Task Pipeline_NoProviderOwnsTheAnswer_IsStillNotApplied()
    {
        var pipeline = new FollowUpSuggestionPipeline([new Fixed(false, [])], new ReportInterpretationFollowUp(true));
        var result = await pipeline.BuildAsync(Context("balance"), default);
        Assert.False(result.Applied);
        Assert.Null(result.Actions);
    }

    [Fact]
    public void Suggestion_IdIsStablePerReportAndSymbol()
    {
        var follow = new ReportInterpretationFollowUp(true);
        Assert.Equal(follow.TryCreate(Context("quarterly"))!.Id, follow.TryCreate(Context("quarterly"))!.Id);
        Assert.NotEqual(follow.TryCreate(Context("quarterly"))!.Id, follow.TryCreate(Context("balance"))!.Id);
        Assert.True(follow.TryCreate(Context("quarterly"))!.Id.Length <= 160);
    }

    // ---- helpers ----

    private static FollowUpSuggestionPipeline Pipeline(bool enabled, params string[] existingIds) =>
        new([new Fixed(true, existingIds)], new ReportInterpretationFollowUp(enabled));

    private static AiQueryRequest Request() => new("پرسش", Guid.NewGuid(), Guid.NewGuid(), "corr");

    private static FollowUpSuggestionContext Context(string report, DialogueOutcome outcome = DialogueOutcome.Answered) => report switch
    {
        "quarterly" => new(Request(), DetectedIntent.FinancialStatementTableLookup, outcome, "fa",
            FinancialStatementTable: ReportDiscussion162Tests.Table(ReportDiscussion162Tests.Quarterly())),
        "balance" => new(Request(), DetectedIntent.FinancialStatementTableLookup, outcome, "fa",
            FinancialStatementTable: ReportDiscussion162Tests.Table(balance: ReportDiscussion162Tests.Balance())),
        _ => new(Request(), DetectedIntent.MonthlyActivityTrend, outcome, "fa", MonthlyActivityTrend: ReportDiscussion162Tests.Monthly())
    };

    private static string Symbol(string report) => report switch
    {
        "quarterly" => ReportDiscussion162Tests.Quarterly().CompanySymbol,
        "balance" => ReportDiscussion162Tests.Balance().Company.Symbol,
        _ => ReportDiscussion162Tests.Monthly().CompanySymbol
    };

    private sealed class Fixed(bool canHandle, string[] ids) : IFollowUpSuggestionProvider
    {
        public int Order => 10;
        public bool CanHandle(FollowUpSuggestionContext context) => canHandle;
        public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SuggestedAction>>(ids.Select(id => new SuggestedAction(
                id, SuggestedActionKind.RunRelatedCapability, id, id, "x", new Dictionary<string, string>(), "test", 1)).ToArray());
    }
}

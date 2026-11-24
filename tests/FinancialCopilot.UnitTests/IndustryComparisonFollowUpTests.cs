using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryComparisonFollowUpTests
{
    private const string OwnIndustry = ChatCapabilityAvailability.PeerComparisonCapabilityCode;
    private static readonly ConversationalCapabilityRegistry Registry = new(InitialConversationalCapabilityCatalog.Create());
    private static readonly RelatedFollowUpActionFactory Factory = new(Registry, new DeterministicCapabilityInterpreter(Registry));
    private static readonly IndustryComparisonFollowUpProvider Provider = new(Factory, new ChatCapabilityAvailability(
        FinancialDataChatProvider.NoavaranAmin, ChatCapabilitySourceMap.Empty, cyclicalWavesV2RelativeValuationEnabled: true));

    [Fact]
    public async Task UsesTheFirstThreeDisplayedSymbolsEvenWhenTheRequestedSymbolIsAppended()
    {
        var context = Context(OwnIndustry, DialogueOutcome.Answered, "fa", "قشیر", "قجام", "قچار", "قهکمت");
        var pipeline = new FollowUpSuggestionPipeline([new SingleSymbolFollowUpProvider(Factory), Provider]);

        var result = await pipeline.BuildAsync(context, default);

        Assert.True(result.Applied);
        string[] expected = ["قشیر را با صنعت خودش مقایسه کن", "قجام را با صنعت خودش مقایسه کن", "قچار را با صنعت خودش مقایسه کن"];
        Assert.Equal(expected, result.Actions!.Select(action => action.LocalizedLabel));
        Assert.Equal(expected, result.Actions!.Select(action => action.Message));
        Assert.All(result.Actions!, action => Assert.Equal(OwnIndustry, action.CapabilityCode));
    }

    [Fact]
    public async Task UsesTheAvailableRowsAndLocalizesTheAction()
    {
        var context = Context(OwnIndustry, DialogueOutcome.Answered, "en", "فولاد");

        var action = Assert.Single(await Provider.BuildAsync(context, default));

        Assert.Equal("Compare فولاد with its industry", action.LocalizedLabel);
        Assert.Equal("Compare فولاد with its industry", action.Message);
        Assert.Equal("فولاد", action.PresetSlots["symbol"]);
    }

    [Fact]
    public async Task SkipsDuplicateAndUnusableSymbolsInsideTheFirstThreeRows()
    {
        var context = Context(OwnIndustry, DialogueOutcome.Answered, "fa", "کگهر", "كگهر", Guid.NewGuid().ToString(), "شگل");

        var action = Assert.Single(await Provider.BuildAsync(context, default));

        Assert.Equal("کگهر", action.PresetSlots["symbol"]);
    }

    [Theory]
    [InlineData(OwnIndustry, DialogueOutcome.NoData)]
    [InlineData(OwnIndustry, DialogueOutcome.Failed)]
    [InlineData("industry_relative_valuation_ranking", DialogueOutcome.Answered)]
    [InlineData("symbol_pair_within_industry", DialogueOutcome.Answered)]
    public void LeavesOtherOutcomesAndComparisonTypesToTheExistingPipeline(string capability, DialogueOutcome outcome) =>
        Assert.False(Provider.CanHandle(Context(capability, outcome, "fa", "کگهر")));

    [Fact]
    public async Task SkipsComparisonActionsWhenTheActiveProviderCannotAnswerThem()
    {
        var unavailable = new IndustryComparisonFollowUpProvider(
            Factory, new ChatCapabilityAvailability(FinancialDataChatProvider.CyclicalWaves));

        Assert.Empty(await unavailable.BuildAsync(Context(OwnIndustry, DialogueOutcome.Answered, "fa", "کگهر"), default));
    }

    private static FollowUpSuggestionContext Context(string capability, DialogueOutcome outcome, string language, params string[] symbols)
    {
        var metric = new RelativeValuationMetricReadModel(50m, 60m, "Green", false, "", "Available");
        var rows = symbols.Select((symbol, index) =>
            new RelativeValuationMemberReadModel(Guid.NewGuid(), symbol, symbol, index + 1, symbols.Length, metric, metric, metric))
            .ToArray();
        var model = new IndustryRelativeValuationReadModel(
            capability, Guid.NewGuid(), "97", "Industry", new DateOnly(2026, 11, 24), Guid.NewGuid(), 1,
            "Published", null, "IQR-v1", "rank-v1", rows.Length, rows.Length, rows, [metric], "Published");
        return new(new("قهکمت را با صنعت خودش مقایسه کن", Guid.NewGuid(), Guid.NewGuid(), "test"),
            DetectedIntent.ComprehensiveAnalysis, outcome, language, IndustryComparison: model);
    }
}

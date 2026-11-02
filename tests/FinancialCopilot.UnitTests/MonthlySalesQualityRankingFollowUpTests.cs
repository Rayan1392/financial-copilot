using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlySalesQualityRankingFollowUpTests
{
    [Fact]
    public void Build_ReturnsTrendActionsForTheFirstThreeRankedSymbolsInOrder()
    {
        var result = Result("\u06a9\u06af\u0647\u0631", "\u0641\u0648\u0644\u0627\u062f", "\u06a9\u0686\u0627\u062f", "\u0641\u0627\u0631\u0633");
        var service = Create();

        var actions = service.Build(result).ToArray();

        Assert.Equal(3, actions.Length);
        Assert.Equal(["\u06a9\u06af\u0647\u0631", "\u0641\u0648\u0644\u0627\u062f", "\u06a9\u0686\u0627\u062f"], actions.Select(action => action.PresetSlots["symbol"]));
        Assert.All(actions, action =>
        {
            var expected = $"\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 {action.PresetSlots["symbol"]} \u0631\u0627 \u0646\u0634\u0627\u0646 \u0628\u062f\u0647";
            Assert.Equal(expected, action.LocalizedLabel);
            Assert.Equal(expected, action.Message);
            Assert.Equal("monthly_activity_trend", action.CapabilityCode);
            Assert.Equal(SuggestedActionKind.RunRelatedCapability, action.Kind);
            Assert.Equal(MonthlySalesQualityRankingFollowUpSuggestionService.RelevanceReason, action.RelevanceReason);
            Assert.StartsWith("monthly-ranking:", action.Id);
            Assert.True(MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(action.Message));
            Assert.Equal(action.PresetSlots["symbol"], MonthlyActivityTrendIntentRules.ExtractCompanySymbol(action.Message));
        });

        Assert.Equal(actions.Select(action => action.Id), service.Build(result).Select(action => action.Id));
    }

    [Fact]
    public void Build_ReturnsNoActionsWhenTrendCapabilityIsDisabledOrRankingIsEmpty()
    {
        var service = Create(disableMonthlyActivityTrend: true);

        Assert.Empty(service.Build(Result("\u06a9\u06af\u0647\u0631", "\u06a9\u0686\u0627\u062f")));
        Assert.Empty(Create().Build(Result()));
    }

    private static MonthlySalesQualityRankingFollowUpSuggestionService Create(bool disableMonthlyActivityTrend = false)
    {
        var definitions = InitialConversationalCapabilityCatalog.Create()
            .Select(definition => disableMonthlyActivityTrend && definition.Code == "monthly_activity_trend"
                ? definition with { Enabled = false }
                : definition)
            .ToArray();
        var registry = new ConversationalCapabilityRegistry(definitions, version: 3);
        return new MonthlySalesQualityRankingFollowUpSuggestionService(
            registry, new DeterministicCapabilityInterpreter(registry));
    }

    private static MonthlySalesQualityRankingResponse Result(params string[] symbols) => new(
        1405,
        6,
        MonthlySalesQualityScope.Market,
        MonthlySalesQualityDirection.Top,
        symbols.Length,
        DateTimeOffset.Parse("2026-10-06T08:00:00Z"),
        symbols.Select((symbol, index) => new MonthlySalesQualityRankingItem(
            index + 1,
            symbol,
            symbol,
            null,
            90m - index,
            "Strong",
            90m,
            100m,
            80m,
            25m,
            10m,
            15m,
            null,
            [],
            [],
            new MonthlySalesQualityDataCoverage(12, true, true, 8),
            "Test",
            DateTimeOffset.Parse("2026-10-06T08:00:00Z"))).ToArray());
}

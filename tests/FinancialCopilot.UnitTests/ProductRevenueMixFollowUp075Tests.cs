using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class ProductRevenueMixFollowUp075Tests
{
    [Fact]
    public void FourValidProducts_ReturnsFirstTwoInTypedOrderThenCompanyTrend()
    {
        var actions = Create().Build(Result("فخوز", "اسلب", "بلوم", "گندله", "آهن اسفنجی")).ToArray();

        Assert.Equal(3, actions.Length);
        Assert.Equal(["روند فروش اسلب فخوز", "روند فروش بلوم فخوز", "روند فروش ماهانه فخوز"],
            actions.Select(action => action.Message));
        Assert.Equal(["monthly_product_trend", "monthly_product_trend", "monthly_activity_trend"],
            actions.Select(action => action.CapabilityCode));
    }

    [Fact]
    public void OneValidProduct_ReturnsProductThenCompanyTrend()
    {
        var actions = Create().Build(Result("فخوز", "اسلب")).ToArray();

        Assert.Equal(["روند فروش اسلب فخوز", "روند فروش ماهانه فخوز"], actions.Select(action => action.Message));
    }

    [Fact]
    public void ParserUnsafeFirstProduct_IsSkippedAndNextTwoAreUsed()
    {
        var actions = Create().Build(Result("فخوز", "فروش", "اسلب", "بلوم")).ToArray();

        Assert.Equal(["روند فروش اسلب فخوز", "روند فروش بلوم فخوز", "روند فروش ماهانه فخوز"],
            actions.Select(action => action.Message));
    }

    [Fact]
    public void DuplicateNormalizedTitles_AreAllExcludedAsAmbiguous()
    {
        var actions = Create().Build(Result("فخوز", "اسلب", " اسلب ", "گندله")).ToArray();

        Assert.Equal(["روند فروش گندله فخوز", "روند فروش ماهانه فخوز"], actions.Select(action => action.Message));
    }

    [Fact]
    public void NoQueryableProducts_ReturnsCompanyTrendOnly()
    {
        var actions = Create().Build(Result("فخوز", "سایر", "جمع", "فروش")).ToArray();

        var action = Assert.Single(actions);
        Assert.Equal("monthly_activity_trend", action.CapabilityCode);
    }

    [Fact]
    public void CompanyTrendUnavailableAndNoQueryableProducts_ReturnsExplicitEmptyCollection()
    {
        var actions = Create(disabled: ["monthly_activity_trend"]).Build(Result("فخوز", "سایر", "جمع"));

        Assert.NotNull(actions);
        Assert.Empty(actions);
    }

    [Fact]
    public void MissingCanonicalSymbol_ReturnsExplicitEmptyCollection()
    {
        Assert.Empty(Create().Build(Result(" ", "اسلب")));
    }

    [Fact]
    public void TypedOrderWinsOverSalesShareAndRankRecalculation()
    {
        var result = Result("فخوز", "اسلب", "بلوم", "گندله") with
        {
            Products =
            [
                Item("اسلب", rank: 3, sales: 1m, share: 1m),
                Item("بلوم", rank: 2, sales: 20m, share: 20m),
                Item("گندله", rank: 1, sales: 79m, share: 79m)
            ]
        };

        var actions = Create().Build(result).ToArray();

        Assert.Equal(["روند فروش اسلب فخوز", "روند فروش بلوم فخوز", "روند فروش ماهانه فخوز"],
            actions.Select(action => action.Message));
    }

    [Fact]
    public void IdsAndOrderingAreStableAndRegistryVersionChangesIds()
    {
        var first = Create().Build(Result("فخوز", "اسلب", "بلوم")).ToArray();
        var repeated = Create().Build(Result("فخوز", "اسلب", "بلوم")).ToArray();
        var versioned = Create(version: 2).Build(Result("فخوز", "اسلب", "بلوم")).ToArray();

        Assert.Equal(first.Select(action => action.Id), repeated.Select(action => action.Id));
        Assert.All(first, action => Assert.StartsWith("feature075:", action.Id));
        Assert.All(first.Zip(versioned), pair => Assert.NotEqual(pair.First.Id, pair.Second.Id));
        Assert.All(versioned, action => Assert.Equal(2, action.RegistryVersion));
    }

    [Fact]
    public void ProductActionsHaveExactCompanySymbolAndProductSlots()
    {
        var action = Create().Build(Result("فخوز", "اسلب")).First();

        Assert.Equal(SuggestedActionKind.RunRelatedCapability, action.Kind);
        Assert.Equal(action.Message, action.LocalizedLabel);
        Assert.Equal("فخوز", action.PresetSlots["company"]);
        Assert.Equal("فخوز", action.PresetSlots["symbol"]);
        Assert.Equal("اسلب", action.PresetSlots["product"]);
        Assert.Equal(ProductRevenueMixFollowUpSuggestionService.RelevanceReason, action.RelevanceReason);
    }

    [Fact]
    public void EveryReturnedActionRoundTripsAndNeverSuggestsRevenueMixItself()
    {
        var registry = Registry();
        var interpreter = new DeterministicCapabilityInterpreter(registry);
        var actions = new ProductRevenueMixFollowUpSuggestionService(registry, interpreter)
            .Build(Result("فخوز", "اسلب", "بلوم"));

        Assert.DoesNotContain(actions, action => action.CapabilityCode == "product_revenue_mix");
        foreach (var action in actions)
        {
            var interpretation = interpreter.Interpret(action.Message);
            var expected = action.CapabilityCode == "monthly_product_trend"
                ? "product_sales_trend"
                : action.CapabilityCode;
            Assert.Equal(expected, interpretation.CapabilityCandidates[0].CapabilityCode);
            if (action.CapabilityCode == "monthly_product_trend")
            {
                var parsed = MonthlyProductTrendIntentRules.BuildQuery(action.Message);
                Assert.Equal("فخوز", parsed.CompanyText);
                Assert.Equal(action.PresetSlots["product"], parsed.ProductText);
            }
            else
            {
                Assert.Equal("فخوز", MonthlyActivityTrendIntentRules.ExtractCompanySymbol(action.Message));
            }
        }
    }

    [Fact]
    public void DisabledProductTrendCapability_ReturnsCompanyTrendOnly()
    {
        var action = Assert.Single(Create(disabled: ["product_sales_trend"]).Build(Result("فخوز", "اسلب")));
        Assert.Equal("monthly_activity_trend", action.CapabilityCode);
    }

    private static ProductRevenueMixFollowUpSuggestionService Create(
        IReadOnlyCollection<string>? disabled = null,
        int version = 1)
    {
        var registry = Registry(disabled, version);
        return new ProductRevenueMixFollowUpSuggestionService(registry, new DeterministicCapabilityInterpreter(registry));
    }

    private static ConversationalCapabilityRegistry Registry(
        IReadOnlyCollection<string>? disabled = null,
        int version = 1) =>
        new(
            InitialConversationalCapabilityCatalog.Create()
                .Select(definition => disabled?.Contains(definition.Code) == true
                    ? definition with { Enabled = false }
                    : definition)
                .ToArray(),
            version);

    private static ProductRevenueMixResponse Result(string symbol, params string[] products) =>
        new(symbol, "شرکت", 1405, 6, products.Length * 10m, "fixture",
            products.Select((product, index) => Item(product, index + 1, (products.Length - index) * 10m,
                products.Length == 0 ? 0m : 100m / products.Length)).ToArray());

    private static ProductRevenueMixProductItem Item(
        string title,
        int rank,
        decimal sales,
        decimal share) =>
        new(title, sales, share, rank, rank == 1, null, null, null);
}

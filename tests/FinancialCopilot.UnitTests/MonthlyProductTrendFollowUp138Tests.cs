using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlyProductTrendFollowUp138Tests
{
    [Theory]
    [InlineData("کگهر", "گندله")]
    [InlineData("کچاد", "کنسانتره آهن")]
    [InlineData("فولاد", "ورق گرم")]
    public void SuccessfulProductTrend_ReturnsMixThenCompanyTrend_ForAnyCompanyAndProduct(string symbol, string product)
    {
        var actions = Create().Build(Result(symbol, product)).ToArray();

        Assert.Equal(["product_revenue_mix", "monthly_activity_trend"], actions.Select(a => a.CapabilityCode));
        Assert.Equal([$"ترکیب فروش محصولات {symbol}", $"روند فروش ماهانه {symbol}"], actions.Select(a => a.Message));
        Assert.All(actions, action =>
        {
            Assert.Equal(SuggestedActionKind.RunRelatedCapability, action.Kind);
            Assert.Equal(action.Message, action.LocalizedLabel);
            Assert.Equal("monthly_product_trend_follow_up", action.RelevanceReason);
            Assert.Equal(symbol, action.PresetSlots["symbol"]);
            Assert.StartsWith("feature138:", action.Id);
            Assert.DoesNotContain(product, action.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NeverSuggestsTheSameProductTrendOrItsAliases()
    {
        var actions = Create().Build(Result("کگهر", "گندله"));

        Assert.DoesNotContain(actions, a => a.CapabilityCode is "monthly_product_trend" or "product_sales_trend" or "product_sales_value");
        Assert.All(actions, a => Assert.False(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(a.Message)));
    }

    [Fact]
    public void MixUnavailable_ReturnsCompanyTrendOnly()
    {
        var action = Assert.Single(Create(disabled: ["product_revenue_mix"]).Build(Result("کگهر", "گندله")));
        Assert.Equal("monthly_activity_trend", action.CapabilityCode);
    }

    [Fact]
    public void CompanyTrendUnavailable_ReturnsMixOnly()
    {
        var action = Assert.Single(Create(disabled: ["monthly_activity_trend"]).Build(Result("کگهر", "گندله")));
        Assert.Equal("product_revenue_mix", action.CapabilityCode);
    }

    [Fact]
    public void BothUnavailable_ReturnsExplicitEmptyCollection()
    {
        var actions = Create(disabled: ["product_revenue_mix", "monthly_activity_trend"]).Build(Result("کگهر", "گندله"));
        Assert.NotNull(actions);
        Assert.Empty(actions);
    }

    [Fact]
    public void SymbolLongerThanMixParserLimit_StillReturnsCompanyTrend()
    {
        var actions = Create().Build(Result("وبصادر", "اتیلن")).ToArray();

        Assert.DoesNotContain(actions, a => a.CapabilityCode == "product_revenue_mix");
        Assert.Contains(actions, a => a.CapabilityCode == "monthly_activity_trend" && a.Message == "روند فروش ماهانه وبصادر");
    }

    [Fact]
    public void UnresolvedOrSymbollessResult_ReturnsNoActions()
    {
        var service = Create();
        Assert.Empty(service.Build(Result("کگهر", "گندله") with { ResolutionState = MonthlyProductTrendResolutionState.NotFound }));
        Assert.Empty(service.Build(Result("کگهر", "گندله") with { ResolutionState = MonthlyProductTrendResolutionState.Ambiguous }));
        Assert.Empty(service.Build(Result("کگهر", "گندله") with { CompanySymbol = null }));
        Assert.Empty(service.Build(Result("کگهر", "گندله") with { CompanySymbol = "  " }));
    }

    [Fact]
    public void OrderingAndIds_AreStableAndVersionedAndContextual()
    {
        var first = Create().Build(Result("کگهر", "گندله")).ToArray();
        var repeated = Create().Build(Result("کگهر", "گندله")).ToArray();
        var otherVersion = Create(version: 9).Build(Result("کگهر", "گندله")).ToArray();
        var otherProduct = Create().Build(Result("کگهر", "آهن اسفنجی")).ToArray();

        Assert.Equal(first.Select(a => a.Id), repeated.Select(a => a.Id));
        Assert.Equal(first.Length, first.Select(a => a.Id).Distinct().Count());
        Assert.All(first.Zip(otherVersion), pair => Assert.NotEqual(pair.First.Id, pair.Second.Id));
        Assert.All(otherVersion, a => Assert.Equal(9, a.RegistryVersion));
        Assert.All(first.Zip(otherProduct), pair => Assert.NotEqual(pair.First.Id, pair.Second.Id));
    }

    [Fact]
    public void ImplicitAndExplicitTwelveMonthPhrasings_ResolveSameContextAndSameActions()
    {
        var implicitQuery = MonthlyProductTrendIntentRules.BuildQuery("روند فروش گندله کگهر");
        var explicitQuery = MonthlyProductTrendIntentRules.BuildQuery("روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟");

        Assert.Equal(implicitQuery.ProductText, explicitQuery.ProductText);
        Assert.Equal(implicitQuery.CompanyText, explicitQuery.CompanyText);
        Assert.False(explicitQuery.UnsupportedTimeWindow);

        // Both phrasings yield the same typed result, and the policy only reads that result.
        var service = Create();
        var fromImplicit = service.Build(Result(implicitQuery.CompanyText, implicitQuery.ProductText));
        var fromExplicit = service.Build(Result(explicitQuery.CompanyText, explicitQuery.ProductText));
        Assert.Equal(fromImplicit, fromExplicit, SuggestedActionComparer.Instance);
        Assert.Equal(2, fromImplicit.Count);
    }

    private sealed class SuggestedActionComparer : IEqualityComparer<SuggestedAction>
    {
        public static readonly SuggestedActionComparer Instance = new();
        public bool Equals(SuggestedAction? x, SuggestedAction? y) =>
            x is not null && y is not null && x.Id == y.Id && x.Message == y.Message && x.CapabilityCode == y.CapabilityCode &&
            x.LocalizedLabel == y.LocalizedLabel && x.RelevanceReason == y.RelevanceReason && x.RegistryVersion == y.RegistryVersion &&
            x.PresetSlots.OrderBy(p => p.Key).SequenceEqual(y.PresetSlots.OrderBy(p => p.Key));
        public int GetHashCode(SuggestedAction obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }

    private static MonthlyProductTrendFollowUpSuggestionService Create(
        IReadOnlyCollection<string>? disabled = null, int version = 1)
    {
        var definitions = InitialConversationalCapabilityCatalog.Create()
            .Select(d => disabled?.Contains(d.Code) == true ? d with { Enabled = false } : d)
            .ToArray();
        var registry = new ConversationalCapabilityRegistry(definitions, version);
        return new MonthlyProductTrendFollowUpSuggestionService(registry, new DeterministicCapabilityInterpreter(registry));
    }

    private static MonthlyProductTrendResult Result(string symbol, string product) => new(
        MonthlyProductTrendResult.Discriminator, MonthlyProductTrendResult.CurrentVersion,
        MonthlyProductTrendResolutionState.Resolved, symbol, $"company-{symbol}", "شرکت", symbol,
        $"KEY:{product}", null, null, product, "تن", [], [], []);
}

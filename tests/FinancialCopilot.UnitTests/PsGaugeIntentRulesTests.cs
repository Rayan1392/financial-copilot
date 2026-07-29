using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class PsGaugeIntentRulesTests
{
    [Theory]
    [InlineData("گیج P/S شراز؟", "شراز")]
    [InlineData("گیج ps شراز", "شراز")]
    [InlineData("عقربه نسبت فروش به قیمت غگلپا", "غگلپا")]
    public void ExplicitGaugeQueriesRouteToGauge(string query, string symbol)
    {
        Assert.True(PsGaugeIntentRules.LooksLikeQuery(query));
        Assert.Equal(symbol, PsGaugeIntentRules.ExtractCompanySymbol(query));
    }

    [Theory]
    [InlineData("P/S شراز")]
    [InlineData("آخرین P/S شراز چقدر است؟")]
    public void PlainMetricQueriesRemainSymbolLookup(string query) =>
        Assert.False(PsGaugeIntentRules.LooksLikeQuery(query));
}

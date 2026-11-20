using FinancialCopilot.Application.AI.GroundedAnalysis;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» T03 (FR-2, FR-3, AC-1, AC-3): prices are a function of depth and web switch
/// only; technical profiles never change a price; the pricing version changes only when an amount changes.
/// </summary>
public sealed class GroundedAnalysisPricingTests
{
    private static GroundedAnalysisPricingOptions ApprovedPrices() => new()
    {
        Credits = new GroundedAnalysisCreditsOptions { SimpleNoWeb = 8, SimpleWeb = 12, DeepNoWeb = 16, DeepWeb = 25 }
    };

    [Theory]
    [InlineData(AnalysisDepth.Simple, false, 8)]
    [InlineData(AnalysisDepth.Simple, true, 12)]
    [InlineData(AnalysisDepth.Deep, false, 16)]
    [InlineData(AnalysisDepth.Deep, true, 25)]
    public void CreditsFor_ReturnsTheApprovedAmount(AnalysisDepth depth, bool web, int expected)
    {
        var pricing = new GroundedAnalysisPricing(ApprovedPrices());

        Assert.Equal(expected, pricing.CreditsFor(depth, web));
        Assert.Equal(expected, pricing.Prices.Single(price => price.Depth == depth && price.WebSearch == web).Credits);
    }

    [Fact]
    public void ProfileChanges_NeverChangeAPriceOrTheVersion()
    {
        var pricing = new GroundedAnalysisPricing(ApprovedPrices());
        var before = AnalysisChoice.All.Select(pricing.CreditsFor).ToArray();
        var version = pricing.PricingVersion;
        var random = new Random(162);

        for (var iteration = 0; iteration < 50; iteration++)
        {
            var options = new GroundedAnalysisOptions();
            options.Profiles.Simple.MaxOutputTokens = random.Next(500, 32000);
            options.Profiles.Deep.MaxOutputTokens = random.Next(500, 32000);
            options.Profiles.Simple.InputTokenTarget = random.Next(4000, 30000);
            options.Profiles.Deep.MaxWebSearchCalls = random.Next(1, 6);
            options.Profiles.Deep.ReasoningEffort = GroundedAnalysisOptions.ReasoningEfforts.ElementAt(random.Next(0, 5));
            options.Validate();
            _ = new GroundedAnalysisProfiles(options).For(AnalysisDepth.Deep);

            // Prices are built from their own section only; the profile options are not an input.
            var rebuilt = new GroundedAnalysisPricing(ApprovedPrices());
            Assert.Equal(before, AnalysisChoice.All.Select(rebuilt.CreditsFor).ToArray());
            Assert.Equal(version, rebuilt.PricingVersion);
        }
    }

    [Fact]
    public void PricingVersion_ChangesOnlyWhenAnAmountChanges()
    {
        var first = new GroundedAnalysisPricing(ApprovedPrices()).PricingVersion;
        var same = new GroundedAnalysisPricing(ApprovedPrices()).PricingVersion;
        var changedOptions = ApprovedPrices();
        changedOptions.Credits.DeepWeb = 26;
        var changed = new GroundedAnalysisPricing(changedOptions).PricingVersion;

        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
        Assert.Equal(12, first.Length);
    }

    [Fact]
    public void IsCurrent_AcceptsTheCurrentVersionOrNone_AndRejectsAStaleOne()
    {
        var pricing = new GroundedAnalysisPricing(ApprovedPrices());

        Assert.True(pricing.IsCurrent(pricing.PricingVersion));
        Assert.True(pricing.IsCurrent(null));
        Assert.False(pricing.IsCurrent("000000000000"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8.5)]
    [InlineData(1001)]
    public void InvalidPrice_FailsValidation(double value)
    {
        var options = ApprovedPrices();
        options.Credits.SimpleWeb = (decimal)value;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void DefaultOptions_AreValid_AndMatchTheApprovedBudgets()
    {
        var options = new GroundedAnalysisOptions();
        options.Validate();
        var profiles = new GroundedAnalysisProfiles(options);

        var simple = profiles.For(AnalysisDepth.Simple);
        var deep = profiles.For(AnalysisDepth.Deep);
        Assert.Equal(("low", 3000, 6000, 45, 1), (simple.ReasoningEffort, simple.MaxOutputTokens, simple.InputTokenTarget, simple.TimeoutSeconds, simple.MaxWebSearchCalls));
        Assert.Equal(("medium", 8000, 10000, 90, 2), (deep.ReasoningEffort, deep.MaxOutputTokens, deep.InputTokenTarget, deep.TimeoutSeconds, deep.MaxWebSearchCalls));
        Assert.Equal("gpt-6.2-sol", deep.Model);
        Assert.Equal(new AnalysisChoice(AnalysisDepth.Deep, true), options.DefaultChoice);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("")]
    public void ReasoningEffortNone_FailsValidation(string effort)
    {
        var options = new GroundedAnalysisOptions();
        options.Profiles.Simple.ReasoningEffort = effort;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(0)]
    public void ModelTimeoutAtOrAboveTheProviderTimeout_FailsValidation(int seconds)
    {
        var options = new GroundedAnalysisOptions();
        options.Profiles.Deep.TimeoutSeconds = seconds;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void InvalidSearchContextAndCountry_FailValidation()
    {
        var context = new GroundedAnalysisOptions();
        context.WebSearch.SearchContextSize = "huge";
        var country = new GroundedAnalysisOptions();
        country.WebSearch.UserLocationCountry = "Iran";

        Assert.Throws<InvalidOperationException>(context.Validate);
        Assert.Throws<InvalidOperationException>(country.Validate);
    }

    [Fact]
    public void BillingPolicy_ParsesTheFourStatusesOnly()
    {
        var pricing = new GroundedAnalysisPricing(ApprovedPrices());

        Assert.Equal(
            ["GroundedAnalysisAnswered.SimpleNoWeb", "GroundedAnalysisAnswered.SimpleWeb", "GroundedAnalysisAnswered.DeepNoWeb", "GroundedAnalysisAnswered.DeepWeb"],
            GroundedAnalysisBillingPolicy.AnsweredStatuses);
        Assert.Equal(25m, GroundedAnalysisBillingPolicy.FixedCredits("GroundedAnalysisAnswered.DeepWeb", pricing));
        Assert.Null(GroundedAnalysisBillingPolicy.FixedCredits("ReportDiscussionAnswered", pricing));
        Assert.Null(GroundedAnalysisBillingPolicy.FixedCredits("NoData", pricing));
        Assert.Null(GroundedAnalysisBillingPolicy.FixedCredits(null, pricing));
    }
}

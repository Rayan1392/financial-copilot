using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Infrastructure.Financial.MarketViews;

namespace FinancialCopilot.UnitTests;

public sealed class MarketPulse095Tests
{
    [Fact]
    public void TransactionValue_SumsCanonicalTotalCapitalAndPreservesZero()
    {
        Assert.Equal(350m, MarketPulseCalculator.CalculateTransactionValue([100m, 250m]));
        Assert.Equal(0m, MarketPulseCalculator.CalculateTransactionValue([0m]));
        Assert.Null(MarketPulseCalculator.CalculateTransactionValue([]));
    }

    [Theory]
    [InlineData("2026-07-11T08:00:00+03:30", MarketPulseSessionState.PreOpen, "pre-open")]
    [InlineData("2026-07-11T10:00:00+03:30", MarketPulseSessionState.Open, "open-012")]
    [InlineData("2026-07-11T13:00:00+03:30", MarketPulseSessionState.Intermission, "post-close-settlement")]
    [InlineData("2026-07-11T17:00:00+03:30", MarketPulseSessionState.Closed, "final")]
    [InlineData("2026-07-16T10:00:00+03:30", MarketPulseSessionState.Holiday, "holiday")]
    public void SessionState_IsDeterministic(string timestamp, MarketPulseSessionState state, string slot)
    {
        var result = MarketPulseCalculator.ResolveSession(DateTimeOffset.Parse(timestamp), 5);

        Assert.Equal(state, result.State);
        Assert.Equal(slot, result.CadenceSlot);
    }

    [Fact]
    public void Breadth_ExcludesStaleQuotesAndDoesNotConvertMissingToZero()
    {
        var now = DateTimeOffset.Parse("2026-07-11T10:00:00Z");
        MarketPulseCalculator.Quote[] quotes =
        [
            new(2m, now, "10", "فلزات"),
            new(-1m, now, "20", "بانک"),
            new(0m, now, "10", "فلزات"),
            new(4m, now.AddHours(-1), "20", "بانک")
        ];

        var result = MarketPulseCalculator.CalculateBreadth(quotes, now.AddMinutes(-15), 1);

        Assert.Equal(1, result.Advancing);
        Assert.Equal(1, result.Declining);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(3, result.IncludedInstruments);
        Assert.Equal(2, result.ExcludedInstruments);
        Assert.Equal(MarketPulseFactStatus.Partial, result.Status);
    }

    [Fact]
    public void IndustryDrivers_AverageConstituentsAndBreakTiesByCode()
    {
        var now = DateTimeOffset.Parse("2026-07-11T10:00:00Z");
        MarketPulseCalculator.Quote[] quotes =
        [
            new(3m, now, "20", "بانک"),
            new(1m, now, "20", "بانک"),
            new(2m, now, "10", "فلزات"),
            new(-4m, now, "30", "خودرو")
        ];

        var result = MarketPulseCalculator.CalculateIndustryDrivers(quotes, now.AddMinutes(-15), 3);

        Assert.Equal(new[] { "10", "20", "30" }, result.Leading.Select(item => item.IndustryCode));
        Assert.Equal(new[] { "30", "10", "20" }, result.Lagging.Select(item => item.IndustryCode));
        Assert.Equal(2m, result.Leading.First().ChangePercent);
    }

    [Fact]
    public void Comparison_UsesOnlyRequestedCompletedSessions()
    {
        var result = MarketPulseCalculator.CalculateComparison(
            "weekly", 5, 3, 150m, [100m, 100m, 100m, 100m, 100m, 1m]);

        Assert.Equal(MarketPulseFactStatus.Available, result.Status);
        Assert.Equal(5, result.AvailableSessions);
        Assert.Equal(100m, result.BaselineAverage);
        Assert.Equal(50m, result.ChangePercent);
    }

    [Fact]
    public void Comparison_ReportsUnavailableWhenMinimumSampleIsMissing()
    {
        var result = MarketPulseCalculator.CalculateComparison("monthly", 20, 10, 150m, [100m, 120m]);

        Assert.Equal(MarketPulseFactStatus.Unavailable, result.Status);
        Assert.Null(result.BaselineAverage);
        Assert.Null(result.ChangePercent);
    }

    [Fact]
    public void Comparison_ReportsUnavailableForZeroBaseline()
    {
        var result = MarketPulseCalculator.CalculateComparison(
            "weekly", 5, 3, 100m, [0m, 0m, 0m, 0m, 0m]);

        Assert.Equal(MarketPulseFactStatus.Unavailable, result.Status);
        Assert.Null(result.ChangePercent);
    }
}

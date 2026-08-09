using System.Text.Json;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthCommonEvaluationPeriodSelectorTests
{
    [Fact]
    public void EvaluationPeriod_JsonRoundTrip_PreservesValidatedYearAndMonth()
    {
        var expected = new SalesGrowthEvaluationPeriod(2026, 6);

        var json = JsonSerializer.Serialize(expected);
        var actual = JsonSerializer.Deserialize<SalesGrowthEvaluationPeriod>(json);

        Assert.Equal(expected, actual);
        Assert.Equal(2026, actual.Year);
        Assert.Equal(6, actual.Month);
        Assert.DoesNotContain("FirstDay", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IsValid", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectsNewestPeriodThatMeetsCoveragePolicy()
    {
        var selector = CreateSelector(70m);
        SalesGrowthPeriodObservation[] observations =
        [
            Observation(2026, 7, "A", true),
            Observation(2026, 7, "B", true),
            Observation(2026, 7, "C", true),
            Observation(2026, 6, "A", true),
            Observation(2026, 6, "B", true),
            Observation(2026, 6, "C", true),
            Observation(2026, 6, "D", true),
            Observation(2026, 6, "E", true)
        ];

        var result = selector.Select(observations, eligibleUniverseSymbolCount: 5);

        Assert.Equal(SalesGrowthCommonPeriodSelectionStatus.Available, result.Status);
        Assert.Equal(new SalesGrowthEvaluationPeriod(2026, 6), result.TargetPeriod);
        Assert.Equal(5, result.CoverageNumerator);
        Assert.Equal(5, result.CoverageDenominator);
        Assert.Equal(100m, result.CoveragePercent);
        Assert.Equal("sales-growth-target-period-v1", result.PolicyVersion.Value);
    }

    [Fact]
    public void CountsDistinctCompleteEligibleSymbolsOnly()
    {
        var selector = CreateSelector(50m);
        SalesGrowthPeriodObservation[] observations =
        [
            Observation(2026, 6, "A", true),
            Observation(2026, 6, "A", true),
            Observation(2026, 6, " B ", true),
            Observation(2026, 6, "C", false),
            Observation(2026, 6, "", true),
            Observation(2026, 6, "D", true)
        ];

        var result = selector.Select(observations, eligibleUniverseSymbolCount: 4);

        Assert.Equal(SalesGrowthCommonPeriodSelectionStatus.Available, result.Status);
        Assert.Equal(3, result.CoverageNumerator);
        Assert.Equal(4, result.CoverageDenominator);
        Assert.Equal(75m, result.CoveragePercent);
    }

    [Fact]
    public void ReturnsPartialWhenNoPeriodMeetsCoverageInsteadOfSilentlyFallingBack()
    {
        var selector = CreateSelector(70m);
        var result = selector.Select(
            [
                Observation(2026, 7, "A", true),
                Observation(2026, 7, "B", true),
                Observation(2026, 6, "A", true)
            ],
            eligibleUniverseSymbolCount: 5);

        Assert.Equal(SalesGrowthCommonPeriodSelectionStatus.Partial, result.Status);
        Assert.False(result.IsUsable);
        Assert.Equal(new SalesGrowthEvaluationPeriod(2026, 7), result.TargetPeriod);
        Assert.Equal(2, result.CoverageNumerator);
        Assert.Contains("minimum coverage", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReturnsUnavailableWhenThereAreNoCompleteObservations()
    {
        var selector = CreateSelector(70m);
        var result = selector.Select(
            [Observation(2026, 7, "A", false)],
            eligibleUniverseSymbolCount: 5);

        Assert.Equal(SalesGrowthCommonPeriodSelectionStatus.Unavailable, result.Status);
        Assert.Null(result.TargetPeriod);
        Assert.Contains("complete", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IgnoresDefaultStructPeriodsInsteadOfSelectingYearZero()
    {
        var selector = CreateSelector(10m);
        SalesGrowthPeriodObservation[] observations =
        [
            new(default, "INVALID", true),
            Observation(2026, 6, "VALID", true)
        ];

        var result = selector.Select(observations, eligibleUniverseSymbolCount: 2);

        Assert.Equal(SalesGrowthCommonPeriodSelectionStatus.Available, result.Status);
        Assert.Equal(new SalesGrowthEvaluationPeriod(2026, 6), result.TargetPeriod);
        Assert.Equal(1, result.CoverageNumerator);
    }

    [Fact]
    public void DisclosesMixedPeriodPolicyInSelectionResult()
    {
        var options = new SalesGrowthScannerOptions
        {
            AllowMixedLatestPeriods = true,
            MinimumCommonPeriodCoveragePercent = 50m
        };
        var result = new SalesGrowthCommonEvaluationPeriodSelector(options).Select(
            [Observation(2026, 7, "A", true)],
            eligibleUniverseSymbolCount: 1);

        Assert.True(result.MixedPeriodsAllowed);
    }

    [Theory]
    [InlineData(-1, 20, 100)]
    [InlineData(101, 20, 100)]
    [InlineData(70, 0, 100)]
    [InlineData(70, 101, 100)]
    [InlineData(70, 20, 0)]
    [InlineData(70, 20, 101)]
    [InlineData(70, 20, 10)]
    public void RejectsInvalidScannerOptions(
        decimal coverage,
        int defaultPageSize,
        int maximumPageSize)
    {
        var options = new SalesGrowthScannerOptions
        {
            MinimumCommonPeriodCoveragePercent = coverage,
            DefaultPageSize = defaultPageSize,
            MaximumPageSize = maximumPageSize
        };

        var validation = SalesGrowthScannerOptionsValidation.Validate(options);

        Assert.NotEmpty(validation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void RejectsInvalidEvaluationMonth(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalesGrowthEvaluationPeriod(2026, month));
    }

    private static SalesGrowthCommonEvaluationPeriodSelector CreateSelector(decimal coverage) =>
        new(new SalesGrowthScannerOptions { MinimumCommonPeriodCoveragePercent = coverage });

    private static SalesGrowthPeriodObservation Observation(
        int year,
        int month,
        string symbol,
        bool isComplete) =>
        new(new SalesGrowthEvaluationPeriod(year, month), symbol, isComplete);
}

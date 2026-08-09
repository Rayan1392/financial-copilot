using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthComparisonCalculatorTests
{
    private readonly SalesGrowthComparisonCalculator _calculator = new();

    [Fact]
    public void CalculatesPreviousMonthDifferencePercentAndMultipleDeterministically()
    {
        var result = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.PreviousMonth,
            [
                Observation("A", 2026, 5, 80m, "mom-previous"),
                Observation("A", 2026, 6, 120m, "mom-current")
            ]);

        Assert.True(result.IsUsable);
        Assert.Equal(120m, result.Current.Amount);
        Assert.Equal(80m, result.BaselineValue.Amount);
        Assert.Equal(40m, result.GrowthDifference);
        Assert.Equal(50m, result.GrowthPercent);
        Assert.Equal(1.5m, result.GrowthMultiple);
        Assert.Equal(["mom-previous", "mom-current"], result.Evidence.Select(e => e.EvidenceId).ToArray());
        Assert.Equal("sales-growth-calculation-v1", result.Policies.Calculation.Value);
    }

    [Fact]
    public void UsesSameMonthPreviousYearPolicyAndRecordsPeriods()
    {
        var result = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.SameMonthPreviousYear,
            [
                Observation("A", 2025, 6, 100m, "yoy-baseline"),
                Observation("A", 2026, 6, 125m, "yoy-current")
            ]);

        Assert.Equal(Period(2026, 6), result.Current.Period);
        Assert.Equal(Period(2025, 6), result.BaselineValue.Period);
        Assert.Equal(25m, result.GrowthPercent);
    }

    [Fact]
    public void AverageBaselineUsesExactlyTheTwelvePriorMonthsAndExcludesCurrent()
    {
        var observations = Enumerable.Range(1, 12)
            .Select(offset =>
            {
                var period = Period(2026, 6).FirstDay.AddMonths(-offset);
                return Observation("A", period.Year, period.Month, 100m, $"average-{offset}");
            })
            .Append(Observation("A", 2026, 6, 200m, "average-current"))
            .ToArray();

        var result = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.AveragePrevious12Months,
            observations);

        Assert.Equal(100m, result.BaselineValue.Amount);
        Assert.Equal(100m, result.GrowthDifference);
        Assert.Equal(100m, result.GrowthPercent);
        Assert.Equal(12, result.BaselineValue.WindowPeriods.Count);
        Assert.DoesNotContain(result.BaselineValue.WindowPeriods, period => period == Period(2026, 6));
    }

    [Fact]
    public void TwoTimesBaselineIsEquivalentToOneHundredPercentGrowth()
    {
        var result = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.SameMonthPreviousYear,
            [
                Observation("A", 2025, 6, 100m, "multiple-baseline"),
                Observation("A", 2026, 6, 200m, "multiple-current")
            ]);

        Assert.Equal(2m, result.GrowthMultiple);
        Assert.Equal(100m, result.GrowthPercent);
    }

    [Fact]
    public void AverageBaselineWithFewerThanTwelvePriorObservationsIsMissing()
    {
        var observations = Enumerable.Range(1, 11)
            .Select(offset =>
            {
                var period = Period(2026, 6).FirstDay.AddMonths(-offset);
                return Observation("A", period.Year, period.Month, 100m, $"short-average-{offset}");
            })
            .Append(Observation("A", 2026, 6, 200m, "short-average-current"))
            .ToArray();

        var result = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.AveragePrevious12Months,
            observations);

        Assert.Equal(SalesGrowthValueState.Missing, result.BaselineValue.State);
        Assert.Null(result.GrowthPercent);
        Assert.False(result.IsUsable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BaselineNonPositiveNeverProducesGrowthValues(decimal baseline)
    {
        var result = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.PreviousMonth,
            [
                Observation("A", 2026, 5, baseline, "invalid-baseline"),
                Observation("A", 2026, 6, 100m, "valid-current")
            ]);

        Assert.NotEqual(SalesGrowthValueState.Available, result.BaselineValue.State);
        Assert.Null(result.GrowthDifference);
        Assert.Null(result.GrowthPercent);
        Assert.Null(result.GrowthMultiple);
        Assert.False(result.IsUsable);
    }

    [Fact]
    public void MissingBaselineAndNegativeCurrentAreExplicitlyRepresented()
    {
        var missing = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.PreviousMonth,
            [Observation("A", 2026, 6, 100m, "current-only")]);
        var invalid = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.PreviousMonth,
            [
                Observation("A", 2026, 5, 100m, "baseline"),
                Observation("A", 2026, 6, -10m, "negative-current")
            ]);

        Assert.Equal(SalesGrowthValueState.Missing, missing.BaselineValue.State);
        Assert.Equal(SalesGrowthValueState.Invalid, invalid.Current.State);
        Assert.Null(invalid.GrowthPercent);
    }

    [Fact]
    public void DuplicateInputPeriodsAreInvalidRatherThanArbitrarilySelected()
    {
        var result = _calculator.Calculate(
            "A",
            Period(2026, 6),
            SalesGrowthComparisonBaseline.PreviousMonth,
            [
                Observation("A", 2026, 5, 80m, "duplicate-one"),
                Observation("A", 2026, 5, 90m, "duplicate-two"),
                Observation("A", 2026, 6, 120m, "current")
            ]);

        Assert.Equal(SalesGrowthValueState.Invalid, result.BaselineValue.State);
        Assert.Equal(3, result.Evidence.Count);
    }

    [Fact]
    public void RepeatedExecutionOnSameEvidenceSnapshotReturnsSameResult()
    {
        var observations = new[]
        {
            Observation("A", 2026, 5, 80m, "same-baseline", new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)),
            Observation("A", 2026, 6, 120m, "same-current", new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero))
        };

        var first = _calculator.Calculate("A", Period(2026, 6), SalesGrowthComparisonBaseline.PreviousMonth, observations);
        var second = _calculator.Calculate("A", Period(2026, 6), SalesGrowthComparisonBaseline.PreviousMonth, observations);

        Assert.Equal(first.ExternalCompanyId, second.ExternalCompanyId);
        Assert.Equal(first.Current.Amount, second.Current.Amount);
        Assert.Equal(first.Current.State, second.Current.State);
        Assert.Equal(first.Current.Period, second.Current.Period);
        Assert.Equal(first.Current.WindowPeriods, second.Current.WindowPeriods);
        Assert.Equal(first.BaselineValue.Amount, second.BaselineValue.Amount);
        Assert.Equal(first.BaselineValue.State, second.BaselineValue.State);
        Assert.Equal(first.BaselineValue.Period, second.BaselineValue.Period);
        Assert.Equal(first.BaselineValue.WindowPeriods, second.BaselineValue.WindowPeriods);
        Assert.Equal(first.GrowthDifference, second.GrowthDifference);
        Assert.Equal(first.GrowthPercent, second.GrowthPercent);
        Assert.Equal(first.GrowthMultiple, second.GrowthMultiple);
        Assert.Equal(first.Evidence.Select(e => e.EvidenceId), second.Evidence.Select(e => e.EvidenceId));
        Assert.Equal(first.LatestObservedAtUtc, second.LatestObservedAtUtc);
    }

    private static SalesGrowthEvaluationPeriod Period(int year, int month) =>
        new(year, month);

    private static SalesGrowthSalesObservation Observation(
        string company,
        int year,
        int month,
        decimal amount,
        string evidenceId,
        DateTimeOffset? observedAtUtc = null) =>
        new(company, Period(year, month), amount, "persisted-monthly-sales", evidenceId, observedAtUtc);
}

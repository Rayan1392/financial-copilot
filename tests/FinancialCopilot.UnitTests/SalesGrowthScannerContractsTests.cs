using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthScannerContractsTests
{
    [Fact]
    public void Contract_ExposesCanonicalFeature116Identity()
    {
        Assert.Equal("SalesGrowthSymbolScanner", SalesGrowthSymbolScanner.Intent);
        Assert.Equal("MonthlySales", SalesGrowthSymbolScanner.MetricFamily);
        Assert.Equal("ListMatchingSymbols", SalesGrowthSymbolScanner.Objective);
    }

    [Fact]
    public void PositiveGrowth_UsesStrictCurrentGreaterThanBaselineRule()
    {
        var semantics = new SalesGrowthScannerSemantics(
            SalesGrowthComparisonBaseline.PreviousMonth,
            SalesGrowthThresholdKind.Positive,
            ConditionOperator.GreaterThan,
            thresholdValue: null,
            FilterOrigin.InferredDefault,
            SalesGrowthPolicyVersions.V1);

        Assert.Equal("CurrentSales > BaselineSales", SalesGrowthScannerSemantics.PositiveRule);
        Assert.Null(semantics.ThresholdValue);
        Assert.Equal(FilterOrigin.InferredDefault, semantics.Origin);
    }

    [Fact]
    public void PercentageAndMultipleFormulas_AreCanonicalAndVersioned()
    {
        var percentage = new SalesGrowthScannerSemantics(
            SalesGrowthComparisonBaseline.SameMonthPreviousYear,
            SalesGrowthThresholdKind.Percent,
            ConditionOperator.GreaterThanOrEqual,
            30m,
            FilterOrigin.Explicit,
            SalesGrowthPolicyVersions.V1);
        var multiple = new SalesGrowthScannerSemantics(
            SalesGrowthComparisonBaseline.AveragePrevious12Months,
            SalesGrowthThresholdKind.Multiple,
            ConditionOperator.GreaterThanOrEqual,
            2m,
            FilterOrigin.Explicit,
            SalesGrowthPolicyVersions.V1);

        Assert.Equal("((CurrentSales - BaselineSales) / BaselineSales) * 100", SalesGrowthScannerSemantics.PercentageFormula);
        Assert.Equal("CurrentSales / BaselineSales", SalesGrowthScannerSemantics.MultipleFormula);
        Assert.Equal("sales-growth-target-period-v1", percentage.Policies.TargetPeriod.Value);
        Assert.Equal("sales-growth-calculation-v1", multiple.Policies.Calculation.Value);
        Assert.Equal(2m, multiple.ThresholdValue);
    }

    [Fact]
    public void Contract_RejectsInvalidThresholdShapes()
    {
        Assert.Throws<ArgumentException>(() => new SalesGrowthScannerSemantics(
            SalesGrowthComparisonBaseline.PreviousMonth,
            SalesGrowthThresholdKind.Positive,
            ConditionOperator.GreaterThan,
            0m,
            FilterOrigin.Explicit,
            SalesGrowthPolicyVersions.V1));

        Assert.Throws<ArgumentNullException>(() => new SalesGrowthScannerSemantics(
            SalesGrowthComparisonBaseline.PreviousMonth,
            SalesGrowthThresholdKind.Percent,
            ConditionOperator.GreaterThan,
            null,
            FilterOrigin.Explicit,
            SalesGrowthPolicyVersions.V1));

        Assert.Throws<ArgumentOutOfRangeException>(() => new SalesGrowthScannerSemantics(
            SalesGrowthComparisonBaseline.PreviousMonth,
            SalesGrowthThresholdKind.Multiple,
            ConditionOperator.GreaterThan,
            0m,
            FilterOrigin.Explicit,
            SalesGrowthPolicyVersions.V1));
    }
}

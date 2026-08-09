using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthStructuredResultTableTests
{
    private readonly ScannerResultColumnPolicy _policy = new();

    [Fact]
    public void SalesGrowthDefaultsContainOnlyIdentityCurrentBaselineAndGrowthColumns()
    {
        var plan = CreatePlan(new SalesGrowthScannerPlan(
            new SalesGrowthScannerSemantics(
                SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                SalesGrowthThresholdKind.Positive,
                ConditionOperator.GreaterThan,
                null,
                FilterOrigin.InferredDefault,
                SalesGrowthPolicyVersions.V1)));

        var columns = _policy.BuildColumns(plan);

        Assert.Equal(
            [
                "SYMBOL",
                "COMPANY",
                "MONTHLY_SALES",
                "MONTHLY_SALES_BASELINE_SAME_MONTH_PREVIOUS_YEAR",
                "MONTHLY_SALES_GROWTH_PERCENT"
            ],
            columns.Select(column => column.Identifier).ToArray());
        Assert.DoesNotContain(columns, column =>
            column.Identifier is "LATEST_PRICE" or "DAILY_CHANGE_PCT" or "MARKET_CAP");
    }

    [Fact]
    public void BaselineColumnChangesWithPreviousMonthAndAveragePolicies()
    {
        var previousColumns = _policy.BuildColumns(CreatePlan(CreateSalesPlan(
            SalesGrowthComparisonBaseline.PreviousMonth)));
        var previousMonth = previousColumns.Select(column => column.Identifier).ToArray();
        var average = _policy.BuildColumns(CreatePlan(CreateSalesPlan(
            SalesGrowthComparisonBaseline.AveragePrevious12Months))).Select(column => column.Identifier).ToArray();

        Assert.Contains("MONTHLY_SALES_BASELINE_PREVIOUS_MONTH", previousMonth);
        Assert.Contains("MONTHLY_SALES_BASELINE_AVERAGE_PREVIOUS_12_MONTHS", average);
        Assert.Equal(
            "Previous Month Sales",
            previousColumns.Single(column => column.Identifier == "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH").DisplayName);
    }

    [Fact]
    public void MultipleSemanticsExposeSalesMultipleAndDoNotExposeRawGrowthMetricColumn()
    {
        var plan = CreatePlan(new SalesGrowthScannerPlan(
            new SalesGrowthScannerSemantics(
                SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                SalesGrowthThresholdKind.Multiple,
                ConditionOperator.GreaterThanOrEqual,
                1.5m,
                FilterOrigin.Explicit,
                SalesGrowthPolicyVersions.V1)));

        var columns = _policy.BuildColumns(plan);

        Assert.Contains(columns, column => column.Identifier == "MONTHLY_SALES_GROWTH_MULTIPLE");
        Assert.DoesNotContain(columns, column => column.Identifier == "MONTHLY_SALES_GROWTH_YOY");
    }

    [Fact]
    public void ExplicitSalesMultipleColumnRequestIsHonored()
    {
        var plan = CreatePlan(CreateSalesPlan(SalesGrowthComparisonBaseline.SameMonthPreviousYear) with
        {
            RequestedDisplayColumns = [new ScannerColumnRequest("sales multiple", true)]
        });

        var columns = _policy.BuildColumns(plan);

        Assert.Contains(columns, column => column.Identifier == "MONTHLY_SALES_GROWTH_MULTIPLE");
    }

    [Fact]
    public void OtherConditionsRemainVisibleAndAutomaticQuoteColumnsRemainAbsent()
    {
        var plan = CreatePlan(
            CreateSalesPlan(SalesGrowthComparisonBaseline.SameMonthPreviousYear),
            new ScannerCondition(
                new ScannerMetricReference(
                    "P/E",
                    new MetricCode("PE_TTM"),
                    new MetricVersion("v1"),
                    new CalculationPolicyVersion("pe-v1"),
                    FiscalPeriodType.TwelveMonths,
                    null),
                ConditionOperator.LessThan,
                5m,
                FilterOrigin.Explicit));

        var columns = _policy.BuildColumns(plan);

        Assert.Contains(columns, column => column.Identifier == "PE_TTM");
        Assert.DoesNotContain(columns, column => column.Identifier == "LATEST_PRICE");
        Assert.DoesNotContain(columns, column => column.Identifier == "MARKET_CAP");
    }

    private static SalesGrowthScannerPlan CreateSalesPlan(SalesGrowthComparisonBaseline baseline) =>
        new(new SalesGrowthScannerSemantics(
            baseline,
            SalesGrowthThresholdKind.Positive,
            ConditionOperator.GreaterThan,
            null,
            FilterOrigin.InferredDefault,
            SalesGrowthPolicyVersions.V1));

    private static ScannerQueryPlan CreatePlan(
        SalesGrowthScannerPlan salesGrowth,
        params ScannerCondition[] conditions) =>
        new(
            Guid.NewGuid(),
            "sales growth scanner",
            "en",
            conditions,
            [],
            false,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            "v1",
            salesGrowth);
}

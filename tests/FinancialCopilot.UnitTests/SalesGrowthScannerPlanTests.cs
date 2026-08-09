using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthScannerPlanTests
{
    private static readonly ScannerQueryPlan BasePlan = CreatePlan(
        new SalesGrowthScannerPlan(
            new SalesGrowthScannerSemantics(
                SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                SalesGrowthThresholdKind.Percent,
                ConditionOperator.GreaterThanOrEqual,
                30m,
                FilterOrigin.Explicit,
                SalesGrowthPolicyVersions.V1)));

    [Fact]
    public void Validator_AcceptsGovernedSalesGrowthPlan()
    {
        var error = new ScannerQueryPlanValidator().Validate(BasePlan);

        Assert.Null(error);
        Assert.Equal(SalesGrowthCurrentObservationSelector.LatestEligibleCompleteMonthlySales,
            BasePlan.SalesGrowth!.CurrentObservationSelector);
        Assert.Equal(SalesGrowthSortKey.GrowthPercent, BasePlan.SalesGrowth.EffectiveSort.Key);
        Assert.Equal(SalesGrowthSortDirection.Descending, BasePlan.SalesGrowth.EffectiveSort.Direction);
    }

    [Fact]
    public void InferredDefault_UsesPositiveGrowthAgainstSameMonthPreviousYear()
    {
        var plan = SalesGrowthScannerPlan.CreateInferredDefault();

        Assert.Equal(SalesGrowthComparisonBaseline.SameMonthPreviousYear, plan.Semantics.Baseline);
        Assert.Equal(SalesGrowthThresholdKind.Positive, plan.Semantics.ThresholdKind);
        Assert.Equal(ConditionOperator.GreaterThan, plan.Semantics.ComparisonOperator);
        Assert.Equal(FilterOrigin.InferredDefault, plan.Semantics.BaselineOrigin);
        Assert.Equal(FilterOrigin.InferredDefault, plan.Semantics.ThresholdOrigin);
        Assert.Null(plan.Semantics.ThresholdValue);
        Assert.Null(new ScannerQueryPlanValidator().Validate(CreatePlan(plan)));
    }

    [Fact]
    public void Validator_AllowsSalesGrowthPlanWithoutGenericMetricCondition()
    {
        var plan = BasePlan with { Conditions = [] };

        Assert.Null(new ScannerQueryPlanValidator().Validate(plan));
    }

    [Fact]
    public void Validator_RejectsNonStrictPositiveOperator()
    {
        var semantics = new SalesGrowthScannerSemantics(
            SalesGrowthComparisonBaseline.PreviousMonth,
            SalesGrowthThresholdKind.Positive,
            ConditionOperator.GreaterThanOrEqual,
            null,
            FilterOrigin.InferredDefault,
            SalesGrowthPolicyVersions.V1);
        var plan = CreatePlan(new SalesGrowthScannerPlan(semantics));

        Assert.Contains("strict GreaterThan", new ScannerQueryPlanValidator().Validate(plan));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(1, 0)]
    public void Validator_RejectsInvalidPagination(int page, int pageSize)
    {
        var plan = CreatePlan(BasePlan.SalesGrowth! with { Page = page, PageSize = pageSize });

        Assert.Contains("pagination", new ScannerQueryPlanValidator().Validate(plan));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public void Validator_RejectsInvalidMarketUniverse(int maximumSymbols)
    {
        var plan = CreatePlan(BasePlan.SalesGrowth! with
        {
            MarketUniverse = new ScannerUniverseScope(MaximumSymbols: maximumSymbols)
        });

        Assert.Contains("market universe", new ScannerQueryPlanValidator().Validate(plan));
    }

    [Fact]
    public void Validator_RejectsUnsupportedDisplayColumnCount()
    {
        var columns = Enumerable.Range(0, ScannerQueryPlan.MaxDisplayColumns + 1)
            .Select(i => new ScannerColumnRequest($"column_{i}", true))
            .ToArray();
        var plan = CreatePlan(BasePlan.SalesGrowth! with { RequestedDisplayColumns = columns });

        Assert.Contains("display columns", new ScannerQueryPlanValidator().Validate(plan));
    }

    private static ScannerQueryPlan CreatePlan(SalesGrowthScannerPlan salesGrowth) =>
        new(
            Guid.NewGuid(),
            "list stocks with monthly sales growth",
            "en",
            [new ScannerCondition(
                new ScannerMetricReference(
                    "sales growth",
                    new MetricCode("MONTHLY_SALES_GROWTH_YOY"),
                    new MetricVersion("v1"),
                    new CalculationPolicyVersion("yoy-monthly-sales-v1"),
                    FiscalPeriodType.Monthly,
                    GrowthComparison.YearOverYear),
                ConditionOperator.GreaterThan,
                0m,
                FilterOrigin.InferredDefault)],
            [],
            false,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            "v1",
            salesGrowth);
}

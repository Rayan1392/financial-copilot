using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthTask14Tests
{
    [Fact]
    public void StrictAndInclusiveOperatorsPreserveEqualitySemantics()
    {
        Assert.False(EfCoreScannerExecutionService.PassesCondition(30m, ConditionOperator.GreaterThan, 30m));
        Assert.True(EfCoreScannerExecutionService.PassesCondition(30m, ConditionOperator.GreaterThanOrEqual, 30m));
        Assert.False(EfCoreScannerExecutionService.PassesCondition(30m, ConditionOperator.LessThan, 30m));
        Assert.True(EfCoreScannerExecutionService.PassesCondition(30m, ConditionOperator.LessThanOrEqual, 30m));
    }

    [Fact]
    public void SalesGrowthTieBreakIsDeterministicBySymbol()
    {
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(),
            "sales growth",
            "en",
            [new ScannerCondition(
                new ScannerMetricReference(
                    "growth",
                    new MetricCode("MONTHLY_SALES_GROWTH_PERCENT"),
                    new MetricVersion("v1"),
                    new CalculationPolicyVersion("growth-v1"),
                    FiscalPeriodType.Monthly,
                    null),
                ConditionOperator.GreaterThanOrEqual,
                30m,
                FilterOrigin.Explicit)],
            [],
            false,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            "v1");
        var rows = new[]
        {
            Row("BBB", 50m),
            Row("AAA", 50m)
        };

        var ranked = new ScannerResultRanker().Rank(rows, plan);

        Assert.Equal(["AAA", "BBB"], ranked.Select(row => row.SymbolCode).ToArray());
    }

    private static ScannerTableRow Row(string symbol, decimal growth) =>
        new(
            symbol,
            null,
            new Dictionary<string, ScannerTableCell>
            {
                ["MONTHLY_SALES_GROWTH_PERCENT"] = new(growth, growth.ToString("0.##"), CellFreshnessStatus.Persisted, null)
            },
            0,
            []);
}

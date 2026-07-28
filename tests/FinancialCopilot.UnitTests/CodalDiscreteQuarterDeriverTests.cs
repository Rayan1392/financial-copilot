using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDiscreteQuarterDeriverTests
{
    // Iranian fiscal year Apr 1, 2024 – Mar 31, 2025 (cumulative observations).
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    // Q1 cumulative (3-month): Apr 1 – Jun 30, value=300
    private static readonly FiscalPeriod CumQ1 = FiscalPeriod.Closed(
        FiscalPeriodType.ThreeMonths, FyStart, new DateOnly(2024, 6, 30));

    // Q2 cumulative (6-month): Apr 1 – Sep 30, value=700 → discrete Q2=400
    private static readonly FiscalPeriod CumQ2 = FiscalPeriod.Closed(
        FiscalPeriodType.SixMonths, FyStart, new DateOnly(2024, 9, 30));

    // Q3 cumulative (9-month): Apr 1 – Dec 31, value=1200 → discrete Q3=500
    private static readonly FiscalPeriod CumQ3 = FiscalPeriod.Closed(
        FiscalPeriodType.NineMonths, FyStart, new DateOnly(2024, 12, 31));

    // Q4 cumulative (12-month): Apr 1, 2024 – Mar 31, 2025, value=1800 → discrete Q4=600
    private static readonly FiscalPeriod CumQ4 = FiscalPeriod.Closed(
        FiscalPeriodType.TwelveMonths, FyStart, new DateOnly(2025, 3, 31));

    private static MetricInputObservation Obs(FiscalPeriod period, decimal? value) =>
        new(new MetricCode("REVENUE"), new MetricVersion("v1"),
            new CalculationPolicyVersion("src"), period, value,
            [new FinancialSourceEvidence("CodalDb", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);

    [Fact]
    public void Derive_Q1Only_ReturnedUnchanged()
    {
        var obs = new[] { Obs(CumQ1, 300m) };
        var result = CodalDiscreteQuarterDeriver.Derive(obs);

        Assert.Single(result);
        Assert.Equal(FiscalPeriodType.ThreeMonths, result[0].Period.Type);
        Assert.Equal(300m, result[0].Value);
        Assert.Equal(CumQ1.StartDate, result[0].Period.StartDate);
        Assert.Equal(CumQ1.EndDate, result[0].Period.EndDate);
    }

    [Fact]
    public void Derive_Q1AndQ2Cumulative_ProducesCorrectDiscreteQ2()
    {
        var obs = new[] { Obs(CumQ1, 300m), Obs(CumQ2, 700m) };
        var result = CodalDiscreteQuarterDeriver.Derive(obs)
            .OrderBy(o => o.Period.EndDate)
            .ToList();

        Assert.Equal(2, result.Count);
        // Q1 unchanged
        Assert.Equal(300m, result[0].Value);
        Assert.Equal(FiscalPeriodType.ThreeMonths, result[0].Period.Type);
        // Q2 discrete: 700 - 300 = 400, period Jul 1 – Sep 30
        Assert.Equal(400m, result[1].Value);
        Assert.Equal(FiscalPeriodType.ThreeMonths, result[1].Period.Type);
        Assert.Equal(new DateOnly(2024, 7, 1), result[1].Period.StartDate);
        Assert.Equal(new DateOnly(2024, 9, 30), result[1].Period.EndDate);
    }

    [Fact]
    public void Derive_FullFiscalYear_ProducesFourDiscreteQuarters()
    {
        var obs = new[]
        {
            Obs(CumQ1, 300m),
            Obs(CumQ2, 700m),
            Obs(CumQ3, 1200m),
            Obs(CumQ4, 1800m)
        };
        var result = CodalDiscreteQuarterDeriver.Derive(obs)
            .OrderBy(o => o.Period.EndDate)
            .ToList();

        Assert.Equal(4, result.Count);
        Assert.All(result, r => Assert.Equal(FiscalPeriodType.ThreeMonths, r.Period.Type));
        Assert.Equal(300m, result[0].Value);  // Q1 = 300
        Assert.Equal(400m, result[1].Value);  // Q2 = 700 - 300
        Assert.Equal(500m, result[2].Value);  // Q3 = 1200 - 700
        Assert.Equal(600m, result[3].Value);  // Q4 = 1800 - 1200
    }

    [Fact]
    public void Derive_TwoFiscalYears_EachGroupedIndependently()
    {
        var fy2Start = new DateOnly(2025, 4, 1);
        var fy2Q1 = FiscalPeriod.Closed(FiscalPeriodType.ThreeMonths, fy2Start, new DateOnly(2025, 6, 30));
        var fy2Q2 = FiscalPeriod.Closed(FiscalPeriodType.SixMonths, fy2Start, new DateOnly(2025, 9, 30));

        var obs = new[]
        {
            Obs(CumQ1, 300m),  // FY1
            Obs(CumQ2, 700m),  // FY1
            Obs(fy2Q1, 400m),  // FY2
            Obs(fy2Q2, 900m),  // FY2
        };
        var result = CodalDiscreteQuarterDeriver.Derive(obs);

        Assert.Equal(4, result.Count);
        // FY1 Q2 discrete: 400; FY2 Q2 discrete: 500
        var discreteQ2s = result
            .Where(r => r.Period.Type == FiscalPeriodType.ThreeMonths && r.Period.EndDate > new DateOnly(2024, 7, 1))
            .ToList();
        Assert.Contains(discreteQ2s, r => r.Value == 400m); // FY1 Q2
        Assert.Contains(discreteQ2s, r => r.Value == 500m); // FY2 Q2
    }

    [Fact]
    public void Derive_NullValueInCumulative_DiscreteValueIsNull()
    {
        var obs = new[] { Obs(CumQ1, 300m), Obs(CumQ2, null) };
        var result = CodalDiscreteQuarterDeriver.Derive(obs)
            .OrderBy(o => o.Period.EndDate)
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(300m, result[0].Value);
        Assert.Null(result[1].Value); // null cumulative → null discrete
    }
}

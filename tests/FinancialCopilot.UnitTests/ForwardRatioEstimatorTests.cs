using System.Text.Json;
using FinancialCopilot.Application.FinancialData.ValuationGauges;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «valuation-gauge-forward-ratio»: the pure estimator (T1) and the research reference fixtures (T2, AC-03).
/// The fixture file is a byte copy of <c>tools/research/forward-pe-reverse-engineering/results/estimator-fixtures.json</c>;
/// its <c>expected</c> members are the research script's own results. The <c>tahlilapp_*</c> members are information
/// only and are never asserted.
/// </summary>
public sealed class ForwardRatioEstimatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);
    private static readonly DateOnly LastMonth = new(2026, 10, 22);
    private static readonly ForwardRatioThresholds Thresholds = new(0.5m, 4m, 75, new HashSet<string> { "31" });

    // ---- T1: formula on a hand-computed case -----------------------------------------------------------------

    // Months 1-4 sell 100 each, months 5-12 100 each, months 13-16 150 each: growth 600 / 400 = 1.5, last 12 months
    // 1 400, forecast 2 100. Eight quarters of revenue 500 and net profit 50: margin 0.1, revenue 2 000, profit 200.
    // Forward P/S = 2 × 2 000 / 2 100 = 1.9048; forward P/E = 20 × 200 / (2 100 × 0.1) = 19.0476.
    [Fact]
    public void TheFormulaMatchesAHandComputedCase()
    {
        var estimate = ForwardRatioEstimator.Estimate(Inputs(), 20m, 2m, Thresholds, Today);

        Assert.Equal(ForwardRatioReasonCodes.None, estimate.PsReasonCode);
        Assert.Equal(ForwardRatioReasonCodes.None, estimate.PeReasonCode);
        Assert.Equal(1.5m, estimate.GrowthFactor);
        Assert.Equal(2100m, estimate.ForecastSales);
        Assert.Equal(0.1m, estimate.Margin);
        Assert.Equal(1.90m, Round2(estimate.ForwardPs));
        Assert.Equal(19.05m, Round2(estimate.ForwardPe));
        Assert.Equal(LastMonth, estimate.LatestSalesMonthEnd);
        Assert.Equal(Quarters()[^1].PeriodEnd, estimate.LatestQuarterEnd);
    }

    [Fact]
    public void TheMarginIsTheMedianSoOneOffQuartersDoNotMoveIt()
    {
        var quarters = Quarters().ToList();
        quarters[2] = quarters[2] with { NetProfit = 450m }; // a one-off quarter with a 90% margin
        var estimate = ForwardRatioEstimator.Estimate(Inputs(quarters: quarters), 20m, 2m, Thresholds, Today);
        Assert.Equal(0.1m, estimate.Margin);
    }

    // ---- T1: every guard has its own reason code ----------------------------------------------------------------

    [Fact]
    public void AnExcludedIndustryGetsNoEstimate()
    {
        var estimate = ForwardRatioEstimator.Estimate(Inputs(industry: "31"), 20m, 2m, Thresholds, Today);
        AssertNone(estimate, ForwardRatioReasonCodes.ExcludedIndustry);
    }

    [Fact]
    public void FewerThanSixteenMonthsGetNoEstimate()
    {
        var estimate = ForwardRatioEstimator.Estimate(Inputs(months: Months().Skip(1).ToArray()), 20m, 2m, Thresholds, Today);
        AssertNone(estimate, ForwardRatioReasonCodes.InsufficientMonthlySales);
    }

    [Fact]
    public void AGapBetweenMonthsGetsNoEstimate()
    {
        // The month ending 2026-04-22 is moved 6 days earlier, so it is 36 days before the month ending 2026-05-22.
        var months = Months().ToArray();
        months[9] = months[9] with { MonthEnd = months[9].MonthEnd.AddDays(-6) };
        var estimate = ForwardRatioEstimator.Estimate(Inputs(months: months), 20m, 2m, Thresholds, Today);
        AssertNone(estimate, ForwardRatioReasonCodes.InsufficientMonthlySales);
    }

    [Fact]
    public void OlderMonthsBeforeTheLastSixteenDoNotMatter()
    {
        var months = new[] { new ForwardMonthlySales(LastMonth.AddMonths(-30), 1m) }.Concat(Months()).ToArray();
        var estimate = ForwardRatioEstimator.Estimate(Inputs(months: months), 20m, 2m, Thresholds, Today);
        Assert.Equal(ForwardRatioReasonCodes.None, estimate.PsReasonCode);
    }

    [Theory]
    [InlineData(75, ForwardRatioReasonCodes.None)]
    [InlineData(76, ForwardRatioReasonCodes.StaleMonthlySales)]
    public void TheLatestMonthMayBeAtMostTheConfiguredAgeOld(int ageDays, string reason)
    {
        var estimate = ForwardRatioEstimator.Estimate(Inputs(), 20m, 2m, Thresholds, LastMonth.AddDays(ageDays));
        Assert.Equal(reason, estimate.PsReasonCode);
        Assert.Equal(reason, estimate.PeReasonCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void NoComparisonSalesGetNoEstimate(decimal sales)
    {
        var months = Months().Select((month, index) => index < 4 ? month with { Sales = sales } : month).ToArray();
        var estimate = ForwardRatioEstimator.Estimate(Inputs(months: months), 20m, 2m, Thresholds, Today);
        AssertNone(estimate, ForwardRatioReasonCodes.NoComparisonSales);
    }

    [Theory]
    [InlineData(400, ForwardRatioReasonCodes.None)]                // growth 4.00, the upper bound
    [InlineData(401, ForwardRatioReasonCodes.GrowthOutOfRange)]    // growth 4.01
    [InlineData(50, ForwardRatioReasonCodes.None)]                 // growth 0.50, the lower bound
    [InlineData(49, ForwardRatioReasonCodes.GrowthOutOfRange)]     // growth 0.49
    public void TheGrowthFactorMustBeInsideTheConfiguredRange(decimal lastMonthsSales, string reason)
    {
        var months = Months().Select((month, index) => index >= 12 ? month with { Sales = lastMonthsSales } : month).ToArray();
        var estimate = ForwardRatioEstimator.Estimate(Inputs(months: months), 20m, 2m, Thresholds, Today);
        Assert.Equal(reason, estimate.PsReasonCode);
        Assert.Equal(reason, estimate.PeReasonCode);
        Assert.Equal(lastMonthsSales / 100m, estimate.GrowthFactor);
        if (reason != ForwardRatioReasonCodes.None)
            Assert.Null(estimate.ForecastSales);
    }

    [Fact]
    public void FewerThanEightQuartersGetNoEstimate()
    {
        var estimate = ForwardRatioEstimator.Estimate(Inputs(quarters: Quarters().Skip(1).ToArray()), 20m, 2m, Thresholds, Today);
        AssertNone(estimate, ForwardRatioReasonCodes.InsufficientQuarters, growth: 1.5m);
    }

    [Fact]
    public void AGapBetweenQuartersGetsNoEstimate()
    {
        var quarters = Quarters().ToArray();
        quarters[0] = quarters[0] with { PeriodEnd = quarters[0].PeriodEnd.AddDays(-20) }; // 111 days before the next
        var estimate = ForwardRatioEstimator.Estimate(Inputs(quarters: quarters), 20m, 2m, Thresholds, Today);
        AssertNone(estimate, ForwardRatioReasonCodes.InsufficientQuarters, growth: 1.5m);
    }

    // A missing discrete quarter is never filled from year-to-date figures.
    [Theory]
    [InlineData(3, null, 50)]
    [InlineData(3, 500, null)]
    [InlineData(3, 0, 50)]      // zero revenue: the quarter's margin has no denominator
    [InlineData(3, -500, 50)]
    [InlineData(7, null, null)] // the latest statement itself
    public void AQuarterWithoutUsableRevenueOrProfitGetsNoEstimate(int index, int? revenue, int? profit)
    {
        var quarters = Quarters().ToArray();
        quarters[index] = quarters[index] with { Revenue = revenue, NetProfit = profit };
        var estimate = ForwardRatioEstimator.Estimate(Inputs(quarters: quarters), 20m, 2m, Thresholds, Today);
        AssertNone(estimate, ForwardRatioReasonCodes.InsufficientQuarters, growth: 1.5m);
    }

    [Fact]
    public void ANonPositiveMarginHidesTheForwardPeButKeepsTheForwardPs()
    {
        // Margins -0.1 (×4), -0.02, 0.06 (×3): median -0.06, while the last four quarters still earn 80.
        var quarters = Quarters().Select((quarter, index) => quarter with
        {
            NetProfit = index < 4 ? -50m : index == 4 ? -10m : 30m
        }).ToArray();
        var estimate = ForwardRatioEstimator.Estimate(Inputs(quarters: quarters), 20m, 2m, Thresholds, Today);

        Assert.Null(estimate.ForwardPe);
        Assert.Equal(ForwardRatioReasonCodes.NonPositiveMargin, estimate.PeReasonCode);
        Assert.Equal(1.90m, Round2(estimate.ForwardPs));
        Assert.Equal(ForwardRatioReasonCodes.None, estimate.PsReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-4)]
    public void NoPositiveCurrentPeHidesTheForwardPe(int? currentPe)
    {
        var estimate = ForwardRatioEstimator.Estimate(Inputs(), currentPe, 2m, Thresholds, Today);
        Assert.Null(estimate.ForwardPe);
        Assert.Equal(ForwardRatioReasonCodes.NoPositiveEarnings, estimate.PeReasonCode);
        Assert.NotNull(estimate.ForwardPs);
    }

    [Fact]
    public void NoPositiveTrailingProfitHidesTheForwardPe()
    {
        var quarters = Quarters().Select((quarter, index) => index >= 4 ? quarter with { NetProfit = index == 7 ? -200m : 50m } : quarter).ToArray();
        var estimate = ForwardRatioEstimator.Estimate(Inputs(quarters: quarters), 20m, 2m, Thresholds, Today);
        Assert.Null(estimate.ForwardPe);
        Assert.Equal(ForwardRatioReasonCodes.NoPositiveEarnings, estimate.PeReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void NoCurrentPsHidesOnlyTheForwardPs(int? currentPs)
    {
        var estimate = ForwardRatioEstimator.Estimate(Inputs(), 20m, currentPs, Thresholds, Today);
        Assert.Null(estimate.ForwardPs);
        Assert.Equal(ForwardRatioReasonCodes.NoCurrentValue, estimate.PsReasonCode);
        Assert.Equal(19.05m, Round2(estimate.ForwardPe));
    }

    // ---- T2: the research reference fixtures (AC-03) -------------------------------------------------------------

    public static TheoryData<string> FixtureSymbols()
    {
        var data = new TheoryData<string>();
        foreach (var item in Fixture().RootElement.GetProperty("cases").EnumerateArray())
            data.Add(item.GetProperty("symbol").GetString()!);
        return data;
    }

    [Fact]
    public void TheFixtureFileHasAllTwentyFiveSymbolsAndTheApprovedThresholds()
    {
        var root = Fixture().RootElement;
        Assert.Equal(25, root.GetProperty("cases").GetArrayLength());
        var thresholds = FixtureThresholds(root);
        Assert.Equal(Thresholds.MinGrowthFactor, thresholds.MinGrowthFactor);
        Assert.Equal(Thresholds.MaxGrowthFactor, thresholds.MaxGrowthFactor);
        Assert.Equal(Thresholds.MaxMonthlyReportAgeDays, thresholds.MaxMonthlyReportAgeDays);
        Assert.Equal(Thresholds.ExcludedIndustryExternalIds, thresholds.ExcludedIndustryExternalIds);
        // The defaults the product ships with are the same thresholds.
        var defaults = new ValuationGaugeForwardOptions().Thresholds();
        Assert.Equal(thresholds.MinGrowthFactor, defaults.MinGrowthFactor);
        Assert.Equal(thresholds.MaxGrowthFactor, defaults.MaxGrowthFactor);
        Assert.Equal(thresholds.MaxMonthlyReportAgeDays, defaults.MaxMonthlyReportAgeDays);
        Assert.Equal(thresholds.ExcludedIndustryExternalIds, defaults.ExcludedIndustryExternalIds);
        // The fixed gaps of the estimator are the fixture's.
        Assert.Equal(35, root.GetProperty("thresholds").GetProperty("max_month_gap_days").GetInt32());
        Assert.Equal(100, root.GetProperty("thresholds").GetProperty("max_quarter_gap_days").GetInt32());
    }

    [Theory]
    [MemberData(nameof(FixtureSymbols))]
    public void TheEstimatorReproducesTheResearchFixture(string symbol)
    {
        var root = Fixture().RootElement;
        var item = root.GetProperty("cases").EnumerateArray().Single(entry => entry.GetProperty("symbol").GetString() == symbol);
        var inputs = new ForwardRatioInputs(
            item.GetProperty("monthly_sales").EnumerateArray()
                .Select(month => new ForwardMonthlySales(Date(month, "month_end"), month.GetProperty("sales").GetDecimal()))
                .ToArray(),
            item.GetProperty("quarters").EnumerateArray()
                .Select(quarter => new ForwardQuarter(Date(quarter, "period_end"), Number(quarter, "revenue"), Number(quarter, "net_profit")))
                .ToArray(),
            item.GetProperty("industry_external_id").GetString());
        var today = DateOnly.Parse(root.GetProperty("as_of").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        var estimate = ForwardRatioEstimator.Estimate(
            inputs, Number(item, "current_pe"), Number(item, "current_ps"), FixtureThresholds(root), today);

        var expected = item.GetProperty("expected");
        Assert.Equal(expected.GetProperty("ps_reason").GetString(), estimate.PsReasonCode);
        Assert.Equal(expected.GetProperty("pe_reason").GetString(), estimate.PeReasonCode);
        Assert.Equal(Number(expected, "forward_ps"), Round2(estimate.ForwardPs));
        Assert.Equal(Number(expected, "forward_pe"), Round2(estimate.ForwardPe));
        Assert.Equal(Optional(expected, "growth_factor"), Round(estimate.GrowthFactor, 6));
        Assert.Equal(Optional(expected, "margin"), Round(estimate.Margin, 6));
        AssertForecastSales(Optional(expected, "forecast_sales"), estimate.ForecastSales);
    }

    /// <summary>
    /// The research script computes the forecast in binary floating point and rounds it with Python's round-half-even;
    /// at 10^15-10^16 rials that is up to one rial from the exact value (خزامیا, فملی, فولاد, فخوز). The estimator is
    /// exact, so the forecast is compared at the reference's own float precision, a relative 1e-15 (owner, 2026-11-27).
    /// Every other member is compared exactly.
    /// </summary>
    private static void AssertForecastSales(decimal? expected, decimal? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        // The fixture stores whole rials.
        var relative = Math.Abs(Round(actual, 0)!.Value - expected.Value) / expected.Value;
        Assert.True(relative <= 1e-15m, $"forecast sales {actual} vs {expected}: relative difference {relative}");
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------

    private static ForwardRatioInputs Inputs(
        IReadOnlyList<ForwardMonthlySales>? months = null,
        IReadOnlyList<ForwardQuarter>? quarters = null,
        string? industry = "1") =>
        new(months ?? Months(), quarters ?? Quarters(), industry);

    /// <summary>16 consecutive month ends ending at <see cref="LastMonth"/>; months 13-16 sell 150, the others 100.</summary>
    private static ForwardMonthlySales[] Months() =>
        Enumerable.Range(0, 16)
            .Select(index => new ForwardMonthlySales(LastMonth.AddMonths(index - 15), index >= 12 ? 150m : 100m))
            .ToArray();

    /// <summary>8 consecutive quarter ends ending 2026-09-22, each with revenue 500 and net profit 50.</summary>
    private static ForwardQuarter[] Quarters() =>
        Enumerable.Range(0, 8)
            .Select(index => new ForwardQuarter(new DateOnly(2026, 9, 22).AddMonths(3 * (index - 7)), 500m, 50m))
            .ToArray();

    private static void AssertNone(ForwardRatioEstimate estimate, string reason, decimal? growth = null)
    {
        Assert.Null(estimate.ForwardPs);
        Assert.Null(estimate.ForwardPe);
        Assert.Equal(reason, estimate.PsReasonCode);
        Assert.Equal(reason, estimate.PeReasonCode);
        Assert.Equal(growth, estimate.GrowthFactor);
        Assert.Null(estimate.ForecastSales);
        Assert.Null(estimate.Margin);
    }

    private static decimal? Round2(decimal? value) => Round(value, 2);

    private static decimal? Round(decimal? value, int decimals) =>
        value is { } number ? decimal.Round(number, decimals, MidpointRounding.AwayFromZero) : null;

    private static JsonDocument Fixture() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ValuationGauges", "estimator-fixtures.json")));

    private static ForwardRatioThresholds FixtureThresholds(JsonElement root)
    {
        var thresholds = root.GetProperty("thresholds");
        return new ForwardRatioThresholds(
            thresholds.GetProperty("min_growth_factor").GetDecimal(),
            thresholds.GetProperty("max_growth_factor").GetDecimal(),
            thresholds.GetProperty("max_monthly_report_age_days").GetInt32(),
            thresholds.GetProperty("excluded_industry_external_ids").EnumerateArray().Select(id => id.GetString()!).ToHashSet());
    }

    private static DateOnly Date(JsonElement element, string name) =>
        DateOnly.Parse(element.GetProperty(name).GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static decimal? Number(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetDecimal();

    /// <summary>A member the fixture leaves out when the estimator stops before computing it.</summary>
    private static decimal? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetDecimal() : null;
}

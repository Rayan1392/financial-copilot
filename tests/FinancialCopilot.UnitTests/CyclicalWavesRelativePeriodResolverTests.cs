using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using static FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves.CyclicalWavesRelativePeriodResolver;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesRelativePeriodResolverTests
{
    // asOf in Q2 (Jul 15, 2025) → last completed quarter = Q1 (Mar 21 – Jun 21, 2025)
    [Fact]
    public void ResolveQuarter_Q0_WhenAsOfInQ2_ReturnsQ1()
    {
        var asOf = new DateTimeOffset(2025, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveQuarter(asOf, QuarterOffset.Q0);
        Assert.Equal(new DateOnly(2025, 3, 21), start);
        Assert.Equal(new DateOnly(2025, 6, 21), end);
    }

    // asOf in Q3 (Oct 10, 2025) → last completed quarter = Q2 (Jun 22 – Sep 22, 2025)
    [Fact]
    public void ResolveQuarter_Q0_WhenAsOfInQ3_ReturnsQ2()
    {
        var asOf = new DateTimeOffset(2025, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveQuarter(asOf, QuarterOffset.Q0);
        Assert.Equal(new DateOnly(2025, 6, 22), start);
        Assert.Equal(new DateOnly(2025, 9, 22), end);
    }

    // asOf in Q4 (Jan 10, 2026) → last completed quarter = Q3 (Sep 23 – Dec 22, 2025)
    [Fact]
    public void ResolveQuarter_Q0_WhenAsOfInQ4_ReturnsQ3()
    {
        var asOf = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveQuarter(asOf, QuarterOffset.Q0);
        Assert.Equal(new DateOnly(2025, 9, 23), start);
        Assert.Equal(new DateOnly(2025, 12, 22), end);
    }

    // asOf in Q1 (Apr 5, 2025) → last completed quarter = Q4 of FY2024 (Dec 23, 2024 – Mar 20, 2025)
    [Fact]
    public void ResolveQuarter_Q0_WhenAsOfInQ1_ReturnsPreviousFiscalYearQ4()
    {
        var asOf = new DateTimeOffset(2025, 4, 5, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveQuarter(asOf, QuarterOffset.Q0);
        Assert.Equal(new DateOnly(2024, 12, 23), start);
        Assert.Equal(new DateOnly(2025, 3, 20), end);
    }

    // Q1 offset from Q2 → penultimate = Q4 of previous FY
    [Fact]
    public void ResolveQuarter_Q1_WhenAsOfInQ2_ReturnsPreviousQ4()
    {
        var asOf = new DateTimeOffset(2025, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveQuarter(asOf, QuarterOffset.Q1);
        Assert.Equal(new DateOnly(2024, 12, 23), start);
        Assert.Equal(new DateOnly(2025, 3, 20), end);
    }

    // Q4 offset from Q2 (same quarter, previous year)
    [Fact]
    public void ResolveQuarter_Q4_ReturnsOneYearBackSameQuarter()
    {
        var asOf = new DateTimeOffset(2025, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveQuarter(asOf, QuarterOffset.Q4);
        Assert.Equal(new DateOnly(2024, 3, 21), start);
        Assert.Equal(new DateOnly(2024, 6, 21), end);
    }

    // M-0: last completed month
    [Fact]
    public void ResolveMonth_M0_ReturnsPreviousCalendarMonth()
    {
        var asOf = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveMonth(asOf, MonthOffset.M0);
        Assert.Equal(new DateOnly(2026, 4, 1), start);
        Assert.Equal(new DateOnly(2026, 4, 30), end);
    }

    // M-1: penultimate month
    [Fact]
    public void ResolveMonth_M1_ReturnsTwoMonthsBack()
    {
        var asOf = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveMonth(asOf, MonthOffset.M1);
        Assert.Equal(new DateOnly(2026, 3, 1), start);
        Assert.Equal(new DateOnly(2026, 3, 31), end);
    }

    // M-12: last year same month
    [Fact]
    public void ResolveMonth_M12_ReturnsSameMonthPreviousYear()
    {
        var asOf = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);
        var (start, end) = ResolveMonth(asOf, MonthOffset.M12);
        Assert.Equal(new DateOnly(2025, 4, 1), start);
        Assert.Equal(new DateOnly(2025, 4, 30), end);
    }
}

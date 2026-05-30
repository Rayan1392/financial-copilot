using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbFiscalPeriodMapperTests
{
    // FY 1403: ends 2025-03-20; starts 2024-03-21
    private static readonly DateTimeOffset FyEnd1403 = new(2025, 3, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Map_ThreeMonthPeriod_ProducesThreeMonthsTypeWithFiscalYearStart()
    {
        var periodEnd = new DateTimeOffset(2024, 6, 22, 0, 0, 0, TimeSpan.Zero);
        var result = CodalDbFiscalPeriodMapper.Map(FyEnd1403, periodEnd, 3);

        Assert.Equal(FiscalPeriodType.ThreeMonths, result.FiscalPeriodType);
        Assert.Equal(new DateOnly(2024, 3, 21), result.PeriodStart); // fiscal year start
        Assert.Equal(new DateOnly(2024, 6, 22), result.PeriodEnd);
    }

    [Fact]
    public void Map_SixMonthPeriod_ProducesSixMonthsType()
    {
        var periodEnd = new DateTimeOffset(2024, 9, 21, 0, 0, 0, TimeSpan.Zero);
        var result = CodalDbFiscalPeriodMapper.Map(FyEnd1403, periodEnd, 6);

        Assert.Equal(FiscalPeriodType.SixMonths, result.FiscalPeriodType);
        Assert.Equal(new DateOnly(2024, 3, 21), result.PeriodStart);
        Assert.Equal(new DateOnly(2024, 9, 21), result.PeriodEnd);
    }

    [Fact]
    public void Map_TwelveMonthFullYear_PeriodEndEqualsFiscalYearEnd()
    {
        // Full-year: PeriodEnd = FiscalYearEnd
        var result = CodalDbFiscalPeriodMapper.Map(FyEnd1403, FyEnd1403, 12);

        Assert.Equal(FiscalPeriodType.TwelveMonths, result.FiscalPeriodType);
        Assert.Equal(new DateOnly(2024, 3, 21), result.PeriodStart);
        Assert.Equal(new DateOnly(2025, 3, 20), result.PeriodEnd);
    }

    [Fact]
    public void Map_JalaliStringsRetainedAsEvidence()
    {
        var periodEnd = new DateTimeOffset(2024, 6, 22, 0, 0, 0, TimeSpan.Zero);
        var result = CodalDbFiscalPeriodMapper.Map(
            FyEnd1403, periodEnd, 3,
            periodEndJalali: "1403/03/31",
            fiscalYearEndJalali: "1403/12/29");

        Assert.Equal("1403/03/31", result.PeriodEndJalali);
        Assert.Equal("1403/12/29", result.FiscalYearEndJalali);
    }
}

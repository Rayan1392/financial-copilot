using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

namespace FinancialCopilot.UnitTests;

public sealed class QuarterlyFinancialHeader144Tests
{
    [Theory]
    [InlineData("2026-09-22", "دوره منتهی به ۳۱ شهریور ۱۴۰۵")]
    [InlineData("2026-06-21", "دوره منتهی به ۳۱ خرداد ۱۴۰۵")]
    [InlineData("2026-03-20", "دوره منتهی به ۲۹ اسفند ۱۴۰۴")]
    public void UsesPersistedCurrentReportPeriodEndAsDateOnly(string periodEnd, string expected)
    {
        var end = DateOnly.Parse(periodEnd);
        var period = new QuarterlyFiscalPeriod(1405, 2, end.AddMonths(-3).AddDays(1), end);
        var result = Result(period, [Report(end, 6)]);

        Assert.Equal(expected, FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
        Assert.DoesNotContain('0', FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
    }

    [Fact]
    public void UsesSixMonthReportNotThreeMonthPredecessor()
    {
        var predecessor = new DateOnly(2026, 6, 21);
        var current = new DateOnly(2026, 9, 22);
        var period = new QuarterlyFiscalPeriod(1405, 2, predecessor.AddDays(1), current);
        var result = Result(period, [Report(predecessor, 3), Report(current, 6)]);

        Assert.Equal("دوره منتهی به ۳۱ شهریور ۱۴۰۵",
            FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
        var rendered = FinancialStatementTableQueryUseCase.RenderQuarterly(result);
        Assert.Contains("| شرح | دوره منتهی به ۳۱ شهریور ۱۴۰۵ |", rendered);
        Assert.DoesNotContain("سال مالی 1405، فصل 2", rendered);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 6)]
    [InlineData(3, 9)]
    [InlineData(4, 12)]
    public void MapsEachQuarterToItsCurrentCumulativeReport(int quarter, int months)
    {
        var end = new DateOnly(2026, 9, 22);
        var period = new QuarterlyFiscalPeriod(1405, quarter, end.AddMonths(-3).AddDays(1), end);
        var selected = new[] { Report(end.AddDays(-1), months), Report(end, months) };

        Assert.Equal("دوره منتهی به ۳۱ شهریور ۱۴۰۵",
            FinancialStatementTableQueryUseCase.FormatQuarterHeader(Result(period, selected), period));
    }

    [Fact]
    public void MissingCurrentReportKeepsFiscalQuarterFallback()
    {
        var predecessor = new DateOnly(2026, 6, 21);
        var period = new QuarterlyFiscalPeriod(1405, 2, predecessor.AddDays(1), new DateOnly(2026, 9, 22));
        var result = Result(period, [Report(predecessor, 3), Report(period.End, 3)]);

        Assert.Equal("سال مالی 1405، فصل 2",
            FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
    }

    private static QuarterlyFinancialReportTableResult Result(
        QuarterlyFiscalPeriod period, IReadOnlyList<QuarterlySelectedReportReference> selected) =>
        new("نماد", null, "issuer-1", "NoavaranCurrentApi", "IncomeStatement", false,
            1, [period], [], [], null, "Available", null, DateTimeOffset.UtcNow, selected);

    private static QuarterlySelectedReportReference Report(DateOnly end, int months) =>
        new(Guid.NewGuid(), "report", "NoavaranCurrentApi", "IncomeStatement",
            end.AddMonths(-months).AddDays(1), end, months, false, false, false,
            "checksum", DateTimeOffset.UtcNow, "MillionRials", "N/A");
}

using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

namespace FinancialCopilot.UnitTests;

public sealed class QuarterlyFinancialHeader144Tests
{
    [Theory]
    [InlineData("2026-09-22", 3, "\u0641\u0635\u0644 \u0627\u0648\u0644", "\u06f3\u06f1 \u0634\u0647\u0631\u06cc\u0648\u0631 \u06f1\u06f4\u06f0\u06f5")]
    [InlineData("2026-06-21", 6, "\u0641\u0635\u0644 \u062f\u0648\u0645", "\u06f3\u06f1 \u062e\u0631\u062f\u0627\u062f \u06f1\u06f4\u06f0\u06f5")]
    [InlineData("2026-09-22", 9, "\u0641\u0635\u0644 \u0633\u0648\u0645", "\u06f3\u06f1 \u0634\u0647\u0631\u06cc\u0648\u0631 \u06f1\u06f4\u06f0\u06f5")]
    [InlineData("2026-12-21", 12, "\u0641\u0635\u0644 \u0686\u0647\u0627\u0631\u0645", "\u06f3\u06f0 \u0622\u0630\u0631 \u06f1\u06f4\u06f0\u06f5")]
    public void UsesPersistedPeriodTypeAndPeriodEnd(string periodEnd, int months, string quarterName, string date)
    {
        var end = DateOnly.Parse(periodEnd);
        var period = new QuarterlyFiscalPeriod(1405, months / 3, end.AddMonths(-3).AddDays(1), end);
        var result = Result(period, [Report(end, months)]);

        Assert.Equal($"{quarterName}<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>{date}",
            FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
        Assert.DoesNotContain("\u0645\u0627\u0647\u0647", FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
    }

    [Fact]
    public void UsesCurrentSixMonthReportNotThreeMonthPredecessor()
    {
        var predecessor = new DateOnly(2026, 6, 21);
        var current = new DateOnly(2026, 9, 22);
        var period = new QuarterlyFiscalPeriod(1405, 2, predecessor.AddDays(1), current);
        var result = Result(period, [Report(predecessor, 3), Report(current, 6)]);

        Assert.Equal("\u0641\u0635\u0644 \u062f\u0648\u0645<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>\u06f3\u06f1 \u0634\u0647\u0631\u06cc\u0648\u0631 \u06f1\u06f4\u06f0\u06f5",
            FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
        Assert.Contains("<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>", FinancialStatementTableQueryUseCase.RenderQuarterly(result));
    }

    [Fact]
    public void UsesNineMonthReportForNonEsfandFiscalYearCompany()
    {
        var predecessor = new DateOnly(2026, 6, 21);
        var current = new DateOnly(2026, 9, 22); // 1405/06/31; fiscal year ends 1405/09/30.
        var period = new QuarterlyFiscalPeriod(1405, 3, predecessor.AddDays(1), current);
        var result = Result(period, [Report(predecessor, 6), Report(current, 9)]);

        Assert.Equal("\u0641\u0635\u0644 \u0633\u0648\u0645<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>\u06f3\u06f1 \u0634\u0647\u0631\u06cc\u0648\u0631 \u06f1\u06f4\u06f0\u06f5",
            FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
    }

    [Fact]
    public void UsesCurrentTwelveMonthReportEndForFourthQuarter()
    {
        var predecessor = new DateOnly(2026, 12, 21);
        var current = new DateOnly(2027, 3, 20);
        var period = new QuarterlyFiscalPeriod(1405, 4, predecessor.AddDays(1), current);
        var result = Result(period, [Report(predecessor, 9), Report(current, 12)]);

        Assert.Equal("\u0641\u0635\u0644 \u0686\u0647\u0627\u0631\u0645<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>\u06f2\u06f9 \u0627\u0633\u0641\u0646\u062f \u06f1\u06f4\u06f0\u06f5",
            FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
    }

    [Fact]
    public void MissingCurrentReportRetainsSafeFiscalQuarterFallback()
    {
        var end = new DateOnly(2026, 9, 22);
        var period = new QuarterlyFiscalPeriod(1405, 2, end.AddMonths(-3).AddDays(1), end);
        var result = Result(period, [Report(end.AddDays(-1), 6)]);

        Assert.Equal("\u0633\u0627\u0644 \u0645\u0627\u0644\u06cc ۱۴۰۵\u060c \u0641\u0635\u0644 ۲",
            FinancialStatementTableQueryUseCase.FormatQuarterHeader(result, period));
    }

    private static QuarterlyFinancialReportTableResult Result(
        QuarterlyFiscalPeriod period, IReadOnlyList<QuarterlySelectedReportReference> selected) =>
        new("\u0646\u0645\u0627\u062f", null, "issuer-1", "NoavaranCurrentApi", "IncomeStatement", false,
            1, [period], [], [], null, "Available", null, DateTimeOffset.UtcNow, selected);

    private static QuarterlySelectedReportReference Report(DateOnly end, int months) =>
        new(Guid.NewGuid(), "report", "NoavaranCurrentApi", "IncomeStatement",
            end.AddMonths(-months).AddDays(1), end, months, false, false, false,
            "checksum", DateTimeOffset.UtcNow, "MillionRials", "N/A", PeriodType: months switch
            {
                3 => "ThreeMonths",
                6 => "SixMonths",
                9 => "NineMonths",
                12 => "TwelveMonths",
                _ => null
            });
}

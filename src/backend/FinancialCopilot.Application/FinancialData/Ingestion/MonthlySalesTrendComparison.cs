namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>One month's company-level sales on the monthly trend basis (ProductSales type 0 or ServiceSales).</summary>
public readonly record struct MonthlySalesPoint(int ReportYear, int ReportMonth, decimal SalesAmount);

public sealed record MonthlySalesComparison(
    decimal? PreviousMonthSalesAmount,
    decimal? MonthOverMonthGrowthPercent,
    decimal? SameMonthPreviousYearSalesAmount,
    decimal? YearOverYearGrowthPercent,
    decimal Average12MonthSalesAmount,
    int Average12MonthPeriodCount)
{
    public bool IsAverage12MonthComplete => Average12MonthPeriodCount >= MonthlySalesTrendComparison.TrailingAverageMonths;
}

/// <summary>
/// Single owner of the monthly trend comparison rules (spec 076 bug-001). The snapshot calculator persists these
/// values and the trend query recomputes them from the same snapshots that feed the chart, so the two cannot drift.
/// </summary>
public static class MonthlySalesTrendComparison
{
    public const int TrailingAverageMonths = 12;

    /// <summary>"Previous month" is the immediately preceding Jalali calendar month, never the previous stored row.</summary>
    public static (int Year, byte Month) PreviousMonth(int year, int month) =>
        month == 1 ? (year - 1, (byte)12) : (year, (byte)(month - 1));

    /// <summary>Returns null when there is no baseline or the baseline is zero; a percent is never invented.</summary>
    public static decimal? GrowthPercent(decimal current, decimal? baseline) =>
        baseline is { } value && value != 0
            ? Math.Round((current - value) / value * 100m, 4)
            : null;

    /// <summary>
    /// Compares one month with its neighbours. <paramref name="series"/> may contain the current month and any other
    /// months; the trailing average uses the current month plus up to 11 most recent earlier months present.
    /// </summary>
    public static MonthlySalesComparison Compare(MonthlySalesPoint current, IEnumerable<MonthlySalesPoint> series)
    {
        var currentKey = Key(current.ReportYear, current.ReportMonth);
        var byKey = new Dictionary<int, decimal>();
        foreach (var point in series)
            byKey[Key(point.ReportYear, point.ReportMonth)] = point.SalesAmount;

        var (previousYear, previousMonth) = PreviousMonth(current.ReportYear, current.ReportMonth);
        decimal? previousMonthSales = byKey.TryGetValue(Key(previousYear, previousMonth), out var previous) ? previous : null;
        decimal? previousYearSales = byKey.TryGetValue(Key(current.ReportYear - 1, current.ReportMonth), out var lastYear) ? lastYear : null;

        var trailing = byKey
            .Where(entry => entry.Key < currentKey)
            .OrderByDescending(entry => entry.Key)
            .Take(TrailingAverageMonths - 1)
            .Select(entry => entry.Value)
            .Prepend(current.SalesAmount)
            .ToArray();

        return new MonthlySalesComparison(
            previousMonthSales,
            GrowthPercent(current.SalesAmount, previousMonthSales),
            previousYearSales,
            GrowthPercent(current.SalesAmount, previousYearSales),
            trailing.Average(),
            trailing.Length);
    }

    private static int Key(int year, int month) => year * 12 + month;
}

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

/// <summary>
/// Converts CyclicalWaves relative period labels to Gregorian date ranges using Iranian fiscal-year
/// calendar approximations. Quarters use Iranian fiscal year boundaries (starts ~March 21).
/// Months use Gregorian calendar months. All resolved dates are estimates.
/// </summary>
public static class CyclicalWavesRelativePeriodResolver
{
    // Iranian fiscal quarters in Gregorian approximation:
    // Q1: Mar 21 – Jun 21
    // Q2: Jun 22 – Sep 22
    // Q3: Sep 23 – Dec 22
    // Q4: Dec 23 – Mar 20 (next calendar year)

    public enum QuarterOffset { Q0 = 0, Q1 = 1, Q4 = 4 }

    public enum MonthOffset { M0 = 0, M1 = 1, M12 = 12 }

    public static (DateOnly Start, DateOnly End) ResolveQuarter(DateTimeOffset asOf, QuarterOffset offset)
    {
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        var lastCompleted = GetLastCompletedFiscalQuarter(today);

        return offset switch
        {
            QuarterOffset.Q0 => lastCompleted,
            QuarterOffset.Q1 => GetPreviousFiscalQuarter(lastCompleted.Start),
            QuarterOffset.Q4 => ShiftFiscalYearBack(lastCompleted, years: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(offset))
        };
    }

    public static (DateOnly Start, DateOnly End) ResolveQuarter(
        DateOnly? latestQuarterEnd,
        DateTimeOffset fallbackAsOf,
        QuarterOffset offset)
    {
        if (latestQuarterEnd is null)
        {
            return ResolveQuarter(fallbackAsOf, offset);
        }

        var latest = GetFiscalQuarterContaining(latestQuarterEnd.Value);
        return offset switch
        {
            QuarterOffset.Q0 => latest,
            QuarterOffset.Q1 => GetPreviousFiscalQuarter(latest.Start),
            QuarterOffset.Q4 => ShiftFiscalYearBack(latest, years: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(offset))
        };
    }

    public static (DateOnly Start, DateOnly End) ResolveMonth(DateTimeOffset asOf, MonthOffset offset)
    {
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        var lastMonth = GetLastCompletedMonth(today);

        return offset switch
        {
            MonthOffset.M0 => lastMonth,
            MonthOffset.M1 => AddMonths(lastMonth.Start, -1),
            MonthOffset.M12 => AddMonths(lastMonth.Start, -12),
            _ => throw new ArgumentOutOfRangeException(nameof(offset))
        };
    }

    public static (DateOnly Start, DateOnly End) ResolveMonth(
        DateOnly? latestMonthEnd,
        DateTimeOffset fallbackAsOf,
        MonthOffset offset)
    {
        if (latestMonthEnd is null)
        {
            return ResolveMonth(fallbackAsOf, offset);
        }

        var latest = GetMonthContaining(latestMonthEnd.Value);
        return offset switch
        {
            MonthOffset.M0 => latest,
            MonthOffset.M1 => AddMonths(latest.Start, -1),
            MonthOffset.M12 => AddMonths(latest.Start, -12),
            _ => throw new ArgumentOutOfRangeException(nameof(offset))
        };
    }

    private static (DateOnly Start, DateOnly End) GetLastCompletedFiscalQuarter(DateOnly today)
    {
        // Find which fiscal quarter 'today' is in, then go back one quarter.
        var current = GetFiscalQuarterContaining(today);
        return GetPreviousFiscalQuarter(current.Start);
    }

    private static (DateOnly Start, DateOnly End) GetFiscalQuarterContaining(DateOnly date)
    {
        int y = date.Year;

        if (date >= new DateOnly(y, 3, 21) && date <= new DateOnly(y, 6, 21))
            return (new DateOnly(y, 3, 21), new DateOnly(y, 6, 21));

        if (date >= new DateOnly(y, 6, 22) && date <= new DateOnly(y, 9, 22))
            return (new DateOnly(y, 6, 22), new DateOnly(y, 9, 22));

        if (date >= new DateOnly(y, 9, 23) && date <= new DateOnly(y, 12, 22))
            return (new DateOnly(y, 9, 23), new DateOnly(y, 12, 22));

        // Q4 spans two calendar years: Dec 23 of year Y to Mar 20 of year Y+1
        if (date >= new DateOnly(y, 12, 23))
            return (new DateOnly(y, 12, 23), new DateOnly(y + 1, 3, 20));

        // Jan 1 – Mar 20: belongs to Q4 of the previous fiscal year
        return (new DateOnly(y - 1, 12, 23), new DateOnly(y, 3, 20));
    }

    private static (DateOnly Start, DateOnly End) GetPreviousFiscalQuarter(DateOnly currentQuarterStart)
    {
        // Identify which quarter currentQuarterStart is and return the one before it.
        int m = currentQuarterStart.Month;
        int d = currentQuarterStart.Day;
        int y = currentQuarterStart.Year;

        if (m == 3 && d == 21) // Q1 start → previous is Q4
            return (new DateOnly(y - 1, 12, 23), new DateOnly(y, 3, 20));

        if (m == 6 && d == 22) // Q2 start → previous is Q1
            return (new DateOnly(y, 3, 21), new DateOnly(y, 6, 21));

        if (m == 9 && d == 23) // Q3 start → previous is Q2
            return (new DateOnly(y, 6, 22), new DateOnly(y, 9, 22));

        if (m == 12 && d == 23) // Q4 start → previous is Q3
            return (new DateOnly(y, 9, 23), new DateOnly(y, 12, 22));

        // Jan 1–Mar 20 dates are within Q4 — treat the start as Dec 23 of previous year
        if (m <= 3)
        {
            int fiscalY = y - 1;
            return (new DateOnly(fiscalY, 9, 23), new DateOnly(fiscalY, 12, 22));
        }

        throw new InvalidOperationException(
            $"Cannot determine previous fiscal quarter for start date {currentQuarterStart}.");
    }

    private static (DateOnly Start, DateOnly End) ShiftFiscalYearBack((DateOnly Start, DateOnly End) quarter, int years)
    {
        var start = quarter.Start.AddYears(-years);
        var end = quarter.End.AddYears(-years);
        return (start, end);
    }

    private static (DateOnly Start, DateOnly End) GetLastCompletedMonth(DateOnly today)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstOfLastMonth = firstOfThisMonth.AddMonths(-1);
        var lastOfLastMonth = firstOfThisMonth.AddDays(-1);
        return (firstOfLastMonth, lastOfLastMonth);
    }

    private static (DateOnly Start, DateOnly End) GetMonthContaining(DateOnly date)
    {
        var start = new DateOnly(date.Year, date.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    private static (DateOnly Start, DateOnly End) AddMonths(DateOnly monthStart, int months)
    {
        var shifted = monthStart.AddMonths(months);
        var start = new DateOnly(shifted.Year, shifted.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return (start, end);
    }
}

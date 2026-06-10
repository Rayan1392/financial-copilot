using System.Globalization;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>One Shamsi (Jalali) calendar month, e.g. 1405/02 = Ordibehesht 1405.</summary>
public readonly record struct ShamsiMonth(int Year, int Month)
{
    /// <summary>Jalali date string for the first day of this month, e.g. "1405/02/01".</summary>
    public string FirstDayJalali => string.Create(
        CultureInfo.InvariantCulture, $"{Year:D4}/{Month:D2}/01");

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"{Year:D4}/{Month:D2}");

    public ShamsiMonth Previous() => Month == 1 ? new ShamsiMonth(Year - 1, 12) : new ShamsiMonth(Year, Month - 1);

    public static bool operator <(ShamsiMonth left, ShamsiMonth right) =>
        left.Year < right.Year || (left.Year == right.Year && left.Month < right.Month);

    public static bool operator >(ShamsiMonth left, ShamsiMonth right) => right < left;

    public static bool operator <=(ShamsiMonth left, ShamsiMonth right) => !(right < left);

    public static bool operator >=(ShamsiMonth left, ShamsiMonth right) => !(left < right);
}

/// <summary>
/// Pure Shamsi-calendar arithmetic for the Noavaran monthly-activity publication cadence
/// (spec 057). Vendors publish a month's production/sales report from the 1st of the following
/// Shamsi month, so "latest published month" is always the previous Shamsi month relative to now,
/// with year rollover (Farvardin 1405 → 1404/12). The current-API permission floor is 1404/01.
/// </summary>
public static class ShamsiMonthCalculator
{
    /// <summary>Earliest Shamsi month the Noavaran current API permits for monthly activity.</summary>
    public static readonly ShamsiMonth MonthlyActivityFloor = new(1404, 1);

    private static readonly PersianCalendar Calendar = new();

    /// <summary>
    /// The latest fully published Shamsi month: the month before the one containing
    /// <paramref name="utcNow"/>, never earlier than <see cref="MonthlyActivityFloor"/>.
    /// </summary>
    public static ShamsiMonth LatestPublishedMonth(DateTimeOffset utcNow)
    {
        var now = utcNow.UtcDateTime;
        var current = new ShamsiMonth(Calendar.GetYear(now), Calendar.GetMonth(now));
        var previous = current.Previous();
        return previous < MonthlyActivityFloor ? MonthlyActivityFloor : previous;
    }

    /// <summary>
    /// Descending month sequence from <paramref name="newest"/> down to <paramref name="floor"/>
    /// inclusive (e.g. 1405/02, 1405/01, 1404/12, …, 1404/01). Empty when newest precedes floor.
    /// </summary>
    public static IReadOnlyList<ShamsiMonth> DescendingMonths(ShamsiMonth newest, ShamsiMonth floor)
    {
        var months = new List<ShamsiMonth>();
        for (var month = newest; month >= floor; month = month.Previous())
        {
            months.Add(month);
        }

        return months;
    }

    /// <summary>Jalali date string for the last day of the month (29/30/31 per the Persian calendar).</summary>
    public static string LastDayJalali(ShamsiMonth month)
    {
        var days = Calendar.GetDaysInMonth(month.Year, month.Month);
        return string.Create(
            CultureInfo.InvariantCulture, $"{month.Year:D4}/{month.Month:D2}/{days:D2}");
    }
}

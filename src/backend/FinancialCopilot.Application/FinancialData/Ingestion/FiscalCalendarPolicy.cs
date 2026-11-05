using System.Globalization;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Single owner of issuer fiscal-calendar knowledge derived from persisted <c>JalaliFiscalYearEnd</c>
/// evidence (Feature 144 R-41, extracted unchanged for Feature 154). <c>PeriodStart</c> is only a
/// fallback because the Noavaran normalizer derives it in the Gregorian calendar.
/// </summary>
public static class FiscalCalendarPolicy
{
    private static readonly PersianCalendar Jalali = new();

    /// <summary>The last day of the Jalali month reached <paramref name="months"/> months after a fiscal start.</summary>
    public static DateOnly JalaliEndAfterMonths(DateOnly fiscalStart, int months) =>
        DateOnly.FromDateTime(Jalali.AddMonths(fiscalStart.ToDateTime(TimeOnly.MinValue), months).AddDays(-1));

    /// <summary>
    /// An Esfand year end starts the fiscal year on 1 Farvardin of the same Jalali year; any other year end
    /// starts it on the first day of the following month of the previous Jalali year.
    /// </summary>
    public static DateOnly ResolveFiscalStart(string? jalaliFiscalYearEnd, DateOnly periodStartFallback)
    {
        if (TryParseJalaliDate(jalaliFiscalYearEnd, out var fiscalYearEnd))
            return FiscalStartFromYearEnd(fiscalYearEnd);
        return periodStartFallback;
    }

    public static DateOnly FiscalStartFromYearEnd(DateOnly fiscalYearEnd)
    {
        var endYear = Jalali.GetYear(fiscalYearEnd.ToDateTime(TimeOnly.MinValue));
        var endMonth = Jalali.GetMonth(fiscalYearEnd.ToDateTime(TimeOnly.MinValue));
        return endMonth == 12
            ? DateOnly.FromDateTime(Jalali.ToDateTime(endYear, 1, 1, 0, 0, 0, 0))
            : DateOnly.FromDateTime(Jalali.ToDateTime(endYear - 1, endMonth + 1, 1, 0, 0, 0, 0));
    }

    /// <summary>A fiscal year is named by the Jalali year in which it ends.</summary>
    public static int FiscalYearLabel(DateOnly fiscalStart) =>
        Jalali.GetYear(JalaliEndAfterMonths(fiscalStart, 12).ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// Maps each provider-derived fiscal start to the start its periods are measured from. Fiscal years are
    /// visited oldest first; one that begins on or before the last report of an accepted fiscal year is a
    /// transitional year and is rebased to the day after that report. Rebased years never push the accepted
    /// boundary, so a regular fiscal year sharing the rebased start is still accepted.
    /// </summary>
    public static Dictionary<DateOnly, DateOnly> ResolveEffectiveFiscalStarts<T>(
        IEnumerable<T> reports,
        Func<T, DateOnly> fiscalStart,
        Func<T, DateOnly> periodEnd)
    {
        var effective = new Dictionary<DateOnly, DateOnly>();
        var latestAcceptedEnd = DateOnly.MinValue;
        foreach (var fiscalYear in reports.GroupBy(fiscalStart).OrderBy(group => group.Key))
        {
            if (latestAcceptedEnd >= fiscalYear.Key)
            {
                effective[fiscalYear.Key] = latestAcceptedEnd.AddDays(1);
                continue;
            }

            effective[fiscalYear.Key] = fiscalYear.Key;
            var fiscalYearEnd = fiscalYear.Max(periodEnd);
            if (fiscalYearEnd > latestAcceptedEnd) latestAcceptedEnd = fiscalYearEnd;
        }

        return effective;
    }

    public static bool TryParseJalaliDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
            return false;

        try
        {
            date = DateOnly.FromDateTime(Jalali.ToDateTime(year, month, day, 0, 0, 0, 0));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Formats a Gregorian date as a Jalali yyyy/MM/dd string with ASCII digits.</summary>
    public static string ToJalali(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Jalali.GetYear(value):0000}/{Jalali.GetMonth(value):00}/{Jalali.GetDayOfMonth(value):00}");
    }

    public static int JalaliMonth(DateOnly date) => Jalali.GetMonth(date.ToDateTime(TimeOnly.MinValue));
}

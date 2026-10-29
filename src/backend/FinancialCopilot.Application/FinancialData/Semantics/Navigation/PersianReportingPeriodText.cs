using System.Globalization;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>
/// Jalali (Solar Hijri) text for reporting dates in user-visible Feature 141 answers, converted deterministically with
/// <see cref="PersianCalendar"/>. Inputs stay Gregorian; queries, parameters, evidence and traces keep their ISO dates.
/// </summary>
internal static class PersianReportingPeriodText
{
    private static readonly PersianCalendar Calendar = new();

    private static readonly string[] MonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    /// <summary>"۳۱ خرداد ۱۴۰۵".</summary>
    public static string Date(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        return PersianStructuredQueryResponseGenerator.Digits(string.Create(CultureInfo.InvariantCulture,
            $"{Calendar.GetDayOfMonth(value)} {MonthNames[Calendar.GetMonth(value) - 1]} {Calendar.GetYear(value)}"));
    }

    /// <summary>A whole Jalali month is "شهریور ۱۴۰۵"; any other window is "بازهٔ {start} تا {end}".</summary>
    public static string Window(DateOnly start, DateOnly end)
    {
        if (CoversWholeMonths(start, end, 1))
        {
            var value = start.ToDateTime(TimeOnly.MinValue);
            return PersianStructuredQueryResponseGenerator.Digits(string.Create(CultureInfo.InvariantCulture,
                $"{MonthNames[Calendar.GetMonth(value) - 1]} {Calendar.GetYear(value)}"));
        }
        return $"بازهٔ {Date(start)} تا {Date(end)}";
    }

    /// <summary>
    /// A financial statement period from its reporting metadata: "دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵", "دوره شش‌ماهه …",
    /// "دوره نه‌ماهه …" or "سال مالی منتهی به ۲۹ اسفند ۱۴۰۴". When the period type is unknown, or the start does not span
    /// exactly that many whole Jalali months, both boundaries are stated so no period information is lost.
    /// </summary>
    public static string StatementPeriod(string? periodType, DateOnly? start, DateOnly? end)
    {
        if (end is not { } periodEnd) return "دوره گزارش نامشخص";
        var (months, label) = periodType switch
        {
            "ThreeMonths" => (3, "دوره سه‌ماهه"),
            "SixMonths" => (6, "دوره شش‌ماهه"),
            "NineMonths" => (9, "دوره نه‌ماهه"),
            "TwelveMonths" => (12, "سال مالی"),
            _ => (0, "دوره")
        };
        if (start is { } periodStart && (months == 0 || !CoversWholeMonths(periodStart, periodEnd, months)))
            return $"{label} از {Date(periodStart)} تا {Date(periodEnd)}";
        return $"{label} منتهی به {Date(periodEnd)}";
    }

    /// <summary>Reads a reporting date as returned by the executor (DateOnly, DateTime or an ISO "yyyy-MM-dd" string).</summary>
    public static DateOnly? Parse(object? value) => value switch
    {
        DateOnly date => date,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        DateTimeOffset offset => DateOnly.FromDateTime(offset.UtcDateTime),
        string text when DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => date,
        _ => null
    };

    /// <summary>True when the window starts on the first day of a Jalali month and ends on the last day of the
    /// <paramref name="months"/>-th Jalali month counted from it (Esfand 30 in leap years included).</summary>
    private static bool CoversWholeMonths(DateOnly start, DateOnly end, int months)
    {
        if (end < start) return false;
        var first = start.ToDateTime(TimeOnly.MinValue);
        var last = end.ToDateTime(TimeOnly.MinValue);
        if (Calendar.GetDayOfMonth(first) != 1 || Calendar.GetDayOfMonth(last.AddDays(1)) != 1) return false;
        var span = Calendar.GetYear(last) * 12 + Calendar.GetMonth(last) - (Calendar.GetYear(first) * 12 + Calendar.GetMonth(first)) + 1;
        return span == months;
    }
}

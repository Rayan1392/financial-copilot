using System.Globalization;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.FinancialData.Semantics.Intent;

public enum PersianReportingPeriodStatus { Normalized, NotApplicable, Unparseable, Unsupported }

/// <summary>
/// Deterministic Gregorian bounds of one Persian (Jalali) report month. The model only names the
/// year and month; the calendar arithmetic is done here and model-provided Gregorian dates are never
/// authoritative.
/// </summary>
public sealed record PersianReportingPeriod(
    PersianReportingPeriodStatus Status,
    int? PersianYear,
    int? PersianMonth,
    DateOnly? Start,
    DateOnly? End,
    string? DiagnosticCode,
    string? SourceLabel)
{
    public string? PersianLabel => PersianYear is { } y && PersianMonth is { } m ? $"{y:0000}-{m:00}" : null;
}

public static class PersianReportingPeriodNormalizer
{
    private static readonly PersianCalendar Calendar = new();
    private static readonly IReadOnlyDictionary<string, int> MonthNames = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["فروردین"] = 1, ["اردیبهشت"] = 2, ["خرداد"] = 3, ["تیر"] = 4, ["مرداد"] = 5, ["امرداد"] = 5, ["شهریور"] = 6,
        ["مهر"] = 7, ["آبان"] = 8, ["ابان"] = 8, ["آذر"] = 9, ["اذر"] = 9, ["دی"] = 10, ["بهمن"] = 11, ["اسفند"] = 12,
        ["farvardin"] = 1, ["ordibehesht"] = 2, ["khordad"] = 3, ["tir"] = 4, ["mordad"] = 5, ["shahrivar"] = 6,
        ["mehr"] = 7, ["aban"] = 8, ["azar"] = 9, ["dey"] = 10, ["bahman"] = 11, ["esfand"] = 12
    };
    private static readonly Regex NumericMonth = new(@"^(?<year>\d{4})[-/.](?<month>\d{1,2})$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Normalizes an explicit-month intent; other period kinds are NotApplicable.</summary>
    public static PersianReportingPeriod Normalize(ReportingPeriodIntent period)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (period.Kind != ReportingPeriodKind.ExplicitMonth)
            return new(PersianReportingPeriodStatus.NotApplicable, null, null, null, null, "period_kind_not_explicit_month", null);
        foreach (var label in new[] { period.UserPeriodLabel, period.StartLabel, period.EndLabel })
        {
            if (string.IsNullOrWhiteSpace(label)) continue;
            var parsed = Parse(label);
            if (parsed.Status == PersianReportingPeriodStatus.Normalized) return parsed;
        }
        var first = new[] { period.UserPeriodLabel, period.StartLabel, period.EndLabel }.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        return first is null
            ? new(PersianReportingPeriodStatus.Unparseable, null, null, null, null, "persian_month_label_missing", null)
            : Parse(first);
    }

    /// <summary>
    /// Normalizes a month-range intent such as «از فروردین تا شهریور ۱۴۰۵» to its first and last report months.
    /// A start month named without a year takes the end month's year (the previous year when it would
    /// otherwise follow the end month). Null when either bound cannot be normalized or the range is inverted.
    /// </summary>
    public static (PersianReportingPeriod First, PersianReportingPeriod Last)? NormalizeRange(ReportingPeriodIntent period)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (period.Kind != ReportingPeriodKind.DateRange || string.IsNullOrWhiteSpace(period.StartLabel)
            || string.IsNullOrWhiteSpace(period.EndLabel))
            return null;
        var last = Parse(period.EndLabel);
        if (last.Status != PersianReportingPeriodStatus.Normalized) return null;
        var first = Parse(period.StartLabel);
        if (first.Status == PersianReportingPeriodStatus.Unparseable)
        {
            first = Parse($"{period.StartLabel} {last.PersianYear}");
            if (first.Status == PersianReportingPeriodStatus.Normalized && first.Start > last.Start)
                first = Parse($"{period.StartLabel} {last.PersianYear - 1}");
        }
        return first.Status == PersianReportingPeriodStatus.Normalized && first.Start <= last.Start ? (first, last) : null;
    }

    /// <summary>Parses a Persian month label such as «شهریور ۱۴۰۵», «1405/06» or «شهریور ماه ۱۴۰۵».</summary>
    public static PersianReportingPeriod Parse(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var text = NormalizeDigits(label).Replace('‌', ' ').Trim();
        int year, month;
        var numeric = NumericMonth.Match(text);
        if (numeric.Success)
        {
            year = int.Parse(numeric.Groups["year"].Value, CultureInfo.InvariantCulture);
            month = int.Parse(numeric.Groups["month"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var tokens = text.Split([' ', '\t', ',', '،', '/', '-'], StringSplitOptions.RemoveEmptyEntries);
            var monthToken = tokens.Select(t => t.Trim()).FirstOrDefault(t => MonthNames.ContainsKey(t.ToLowerInvariant()));
            var yearToken = tokens.FirstOrDefault(t => t.All(char.IsDigit) && t.Length == 4);
            if (monthToken is null || yearToken is null)
                return new(PersianReportingPeriodStatus.Unparseable, null, null, null, null, "persian_month_label_unparseable", label);
            month = MonthNames[monthToken.ToLowerInvariant()];
            year = int.Parse(yearToken, CultureInfo.InvariantCulture);
        }
        if (month is < 1 or > 12 || year is < 1300 or > 1500)
            return new(PersianReportingPeriodStatus.Unsupported, year, month, null, null, "persian_month_out_of_supported_range", label);
        var days = Calendar.GetDaysInMonth(year, month, PersianCalendar.PersianEra);
        var start = DateOnly.FromDateTime(Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0));
        var end = DateOnly.FromDateTime(Calendar.ToDateTime(year, month, days, 0, 0, 0, 0));
        return new(PersianReportingPeriodStatus.Normalized, year, month, start, end, null, label);
    }

    /// <summary>
    /// True when free text names a reporting period: a Jalali month («تیر», «تیرماه», «tir») or a Jalali year
    /// («۱۴۰۵», «1405/04»). Detection only; use <see cref="Parse"/> for the period itself.
    /// </summary>
    public static bool MentionsReportingPeriod(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (Match token in Regex.Matches(NormalizeDigits(text).Replace('‌', ' '), @"[\p{L}\p{Nd}]+"))
        {
            var value = token.Value.ToLowerInvariant();
            if (MonthNames.ContainsKey(value) ||
                (value.EndsWith("ماه", StringComparison.Ordinal) && MonthNames.ContainsKey(value[..^3])))
                return true;
            if (value.Length == 4 && value.All(char.IsAsciiDigit) &&
                int.Parse(value, CultureInfo.InvariantCulture) is >= 1300 and <= 1500)
                return true;
        }
        return false;
    }

    private static string NormalizeDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= '۰' and <= '۹') chars[i] = (char)('0' + (c - '۰'));
            else if (c is >= '٠' and <= '٩') chars[i] = (char)('0' + (c - '٠'));
        }
        return new string(chars);
    }
}

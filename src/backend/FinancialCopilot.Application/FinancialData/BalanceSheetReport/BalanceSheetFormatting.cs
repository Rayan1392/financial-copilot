using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.FinancialData.BalanceSheetReport;

/// <summary>
/// Display rules shared by the text rendering and the typed outcome messages (spec §6.5, §7): «۰» for a
/// reported zero, «—» for missing, accounting parentheses for negatives, Persian digits, and percentages to
/// two decimals rounded half away from zero.
/// </summary>
public static class BalanceSheetFormatting
{
    public const string Missing = "—";

    public static string PersianDigits(string value) =>
        string.Concat(value.Select(character => character is >= '0' and <= '9' ? (char)('۰' + character - '0') : character));

    public static string JalaliDate(DateOnly date) => PersianDigits(FiscalCalendarPolicy.ToJalali(date));

    public static string Amount(decimal? value)
    {
        if (value is not { } amount) return Missing;
        if (amount == 0m) return "۰";
        var formatted = PersianDigits(Math.Abs(amount).ToString("#,##0.##", CultureInfo.InvariantCulture)).Replace(',', '٬');
        return amount < 0 ? $"({formatted})" : formatted;
    }

    public static string Percent(decimal? value, bool signed = false)
    {
        if (value is not { } percent) return Missing;
        var rounded = Math.Round(percent, 2, MidpointRounding.AwayFromZero);
        var text = PersianDigits(Math.Abs(rounded).ToString("0.00", CultureInfo.InvariantCulture)).Replace('.', '٫');
        var sign = rounded < 0 ? "−" : signed && rounded > 0 ? "+" : string.Empty;
        return $"{sign}{text}٪";
    }

    public static string PercentagePoints(decimal? value)
    {
        if (value is not { } points) return Missing;
        var rounded = Math.Round(points, 2, MidpointRounding.AwayFromZero);
        var text = PersianDigits(Math.Abs(rounded).ToString("0.00", CultureInfo.InvariantCulture)).Replace('.', '٫');
        var sign = rounded < 0 ? "−" : rounded > 0 ? "+" : string.Empty;
        return $"{sign}{text} واحد درصد";
    }

    public static string PeriodLength(int months) => months > 0 ? $"گزارش {PersianDigits(months.ToString(CultureInfo.InvariantCulture))} ماهه" : string.Empty;

    public static string BaselineLabel(BalanceSheetBaselineCandidate candidate) =>
        candidate.BaselineDate is { } date ? $"{candidate.Label} ({JalaliDate(date)})" : candidate.Label;
}

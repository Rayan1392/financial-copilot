using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.Telegram;

/// <summary>Bounded, deterministic recognition of one-month Persian market reports.</summary>
public sealed class TelegramMonthlyReportRecognizer : ITelegramMonthlyReportRecognizer
{
    private const int MinimumYear = 1404;
    private const int MaximumYear = 1500;
    private static readonly string[] Months = [
        "\u0641\u0631\u0648\u0631\u062f\u06cc\u0646", "\u0627\u0631\u062f\u06cc\u0628\u0647\u0634\u062a", "\u062e\u0631\u062f\u0627\u062f",
        "\u062a\u06cc\u0631", "\u0645\u0631\u062f\u0627\u062f", "\u0634\u0647\u0631\u06cc\u0648\u0631",
        "\u0645\u0647\u0631", "\u0622\u0628\u0627\u0646", "\u0622\u0630\u0631", "\u062f\u06cc", "\u0628\u0647\u0645\u0646", "\u0627\u0633\u0641\u0646\u062f"];
    private static readonly Regex Hashtag = new("#(?<value>[\\p{L}\\p{Nd}_\\u200c-]{2,80})", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex NumericPeriod = new("(?<!\\d)(?<year>[0-9۰-۹٠-٩]{4})\\s*[/\\-.]\\s*(?<month>[0-9۰-۹٠-٩]{1,2})(?:\\s*[/\\-.]\\s*(?<day>[0-9۰-۹٠-٩]{1,2}))?", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public TelegramMonthlyReportRecognition? Recognize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) return null;
        var normalized = Normalize(text);
        var hashtags = Hashtag.Matches(normalized).Select(m => m.Groups["value"].Value.Trim('_', '-')).ToArray();
        var hasMonthlyMarker = hashtags.Any(IsMonthlyMarker) ||
            normalized.Contains("فعالیت ماهانه", StringComparison.Ordinal) ||
            normalized.Contains("گزارش تولید و فروش ماهانه", StringComparison.Ordinal) ||
            normalized.Contains("گزارش ماهانه تولید و فروش", StringComparison.Ordinal);
        if (!hasMonthlyMarker || normalized.Contains("سه ماهه", StringComparison.Ordinal) || normalized.Contains("دوازده ماهه", StringComparison.Ordinal)) return null;

        var periods = new HashSet<(int Year, int Month)>();
        foreach (Match match in NumericPeriod.Matches(normalized))
        {
            if (IsFiscalYearContext(normalized, match.Index)) continue;
            if (!int.TryParse(NormalizeDigits(match.Groups["year"].Value), CultureInfo.InvariantCulture, out var year) ||
                !int.TryParse(NormalizeDigits(match.Groups["month"].Value), CultureInfo.InvariantCulture, out var month) ||
                year is < MinimumYear or > MaximumYear || month is < 1 or > 12) return null;
            periods.Add((year, month));
        }

        foreach (var hashtag in hashtags)
        {
            var value = hashtag.Replace('_', ' ').Replace('\u200c', ' ').Trim();
            var month = Array.FindIndex(Months, m => value.StartsWith(m, StringComparison.Ordinal)) + 1;
            if (month <= 0) continue;
            var yearMatch = Regex.Match(value[Months[month - 1].Length..], "(?<year>[0-9۰-۹٠-٩]{4})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            if (yearMatch.Success && int.TryParse(NormalizeDigits(yearMatch.Groups["year"].Value), out var year) && year is >= MinimumYear and <= MaximumYear) periods.Add((year, month));
        }
        if (periods.Count != 1) return null;

        var candidates = hashtags.Where(h => !IsMonthlyMarker(h) && !IsMonthOrDate(h)).ToArray();
        if (candidates.Length != 1) return null;
        var symbol = NormalizeSymbol(candidates[0]);
        return symbol.Length == 0 ? null : new TelegramMonthlyReportRecognition(symbol, periods.First().Year, periods.First().Month);
    }

    private static bool IsMonthlyMarker(string value) => value.Replace("_", "", StringComparison.Ordinal).Replace("\u200c", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal) is "فعالیتماهانه" or "فعالیت" or "گزارشماهانه";
    private static bool IsMonthOrDate(string value) => Months.Any(m => value.Contains(m, StringComparison.Ordinal)) || NumericPeriod.IsMatch(value);
    private static bool IsFiscalYearContext(string text, int index) => text[Math.Max(0, index - 24)..index].Contains("سال مالی منتهی به", StringComparison.Ordinal);
    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormC).Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9').Replace('\u200c', ' ');
    private static string NormalizeDigits(string value) => string.Concat(value.Select(c => c switch { >= '\u06F0' and <= '\u06F9' => (char)('0' + c - '\u06F0'), >= '\u0660' and <= '\u0669' => (char)('0' + c - '\u0660'), _ => c }));
    private static string NormalizeSymbol(string value) => value.Replace('_', ' ').Replace('-', ' ').Replace('\u200c', ' ').Replace('\u064A', '\u06CC').Replace('\u0649', '\u06CC').Replace('\u0643', '\u06A9').Trim();
}

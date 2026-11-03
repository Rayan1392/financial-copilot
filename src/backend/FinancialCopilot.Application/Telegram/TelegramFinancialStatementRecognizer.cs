using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.Telegram;

public sealed record TelegramFinancialStatementRecognition(
    string Symbol,
    string Category,
    int PeriodYear,
    int PeriodMonth,
    int PeriodDay,
    int PeriodMonths,
    bool IsAnnualFiscalYearHeader,
    bool? IsAudited,
    bool IsAmendment,
    bool? IsConsolidated);

public interface ITelegramFinancialStatementRecognizer
{
    TelegramFinancialStatementRecognition? Recognize(string? text);
}

/// <summary>Deterministic parser for CodalMirror's financial-statement announcement header.</summary>
public sealed class TelegramFinancialStatementRecognizer : ITelegramFinancialStatementRecognizer
{
    private static readonly Regex Hashtag = new("#(?<ticker>[^()\\s#]+(?:\\s+[^()\\s#]+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Date = new("(?<![0-9])(?<year>[0-9]{4})\\s*[/.-]\\s*(?<month>[0-9]{1,2})\\s*[/.-]\\s*(?<day>[0-9]{1,2})(?![0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Duration = new("(?:دوره\\s*)?(?<months>3|6|9|12)\\s*ماهه", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public TelegramFinancialStatementRecognition? Recognize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) return null;
        var normalized = Normalize(text);
        var headerLines = normalized.Split('\n').Take(3).ToArray();
        var header = headerLines.FirstOrDefault(line =>
            line.Contains("صورت های مالی", StringComparison.Ordinal) ||
            line.Contains("صورتهای مالی", StringComparison.Ordinal));
        if (header is null) return null;

        // Mixed statement and monthly activity posts fail closed, preventing both pipelines from firing.
        if (normalized.Contains("فعالیت ماهانه", StringComparison.Ordinal) ||
            normalized.Contains("گزارش تولید و فروش ماهانه", StringComparison.Ordinal)) return null;

        var tickers = Hashtag.Matches(string.Join('\n', headerLines)).Select(x => NormalizeSymbol(x.Groups["ticker"].Value)).Distinct(StringComparer.Ordinal).ToArray();
        if (tickers.Length != 1 || tickers[0].Length == 0) return null;

        var durationMatches = Duration.Matches(header);
        var durations = durationMatches.Select(x => int.Parse(x.Groups["months"].Value, CultureInfo.InvariantCulture)).Distinct().ToArray();
        if (durations.Length == 0 && header.Contains("سال مالی", StringComparison.Ordinal)) durations = [12];
        if (durations.Length != 1) return null;
        var periodMarker = header.Contains("منتهی به", StringComparison.Ordinal);
        if (!periodMarker) return null;
        var dateMatches = Date.Matches(header);
        if (dateMatches.Count != 1) return null;
        var match = dateMatches[0];
        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        try { _ = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0); }
        catch (ArgumentOutOfRangeException) { return null; }

        bool? audited = header.Contains("حسابرسی نشده", StringComparison.Ordinal) ? false :
            header.Contains("حسابرسی شده", StringComparison.Ordinal) ? true : null;
        bool? consolidated = header.Contains("غیر تلفیقی", StringComparison.Ordinal) ||
            header.Contains("شرکت اصلی", StringComparison.Ordinal) || header.Contains("جداگانه", StringComparison.Ordinal) ? false :
            header.Contains("تلفیقی", StringComparison.Ordinal) ? true : null;
        var category = durations[0] == 12 ? "Annual" : "Interim";
        return new TelegramFinancialStatementRecognition(tickers[0], category, year, month, day, durations[0],
            durations[0] == 12 && header.Contains("سال مالی", StringComparison.Ordinal), audited,
            header.Contains("اصلاحیه", StringComparison.Ordinal), consolidated);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC)
            .Replace('\u064a', '\u06cc').Replace('\u0649', '\u06cc').Replace('\u0643', '\u06a9')
            .Replace('\u200c', ' ').Replace('\u200f', ' ').Replace('\u200e', ' ');
        normalized = string.Concat(normalized.Select(c => c switch
        {
            >= '\u06f0' and <= '\u06f9' => (char)('0' + c - '\u06f0'),
            >= '\u0660' and <= '\u0669' => (char)('0' + c - '\u0660'),
            _ => c
        }));
        normalized = normalized.Replace("صورت‌های", "صورت های", StringComparison.Ordinal)
            .Replace("صورتهای", "صورت های", StringComparison.Ordinal)
            .Replace("میاندوره‌ای", "میان دوره ای", StringComparison.Ordinal)
            .Replace("میاندوره ای", "میان دوره ای", StringComparison.Ordinal);
        return Regex.Replace(normalized, @"[ \t]+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }

    private static string NormalizeSymbol(string value) => value.Replace('_', ' ').Replace('-', ' ').Trim();
}

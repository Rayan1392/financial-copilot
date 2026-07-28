using FinancialCopilot.Application.FinancialData.Ingestion;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.AI.Orchestration;

public static class MonthlySalesQualityRankingIntentRules
{
    private static readonly string[] RankingPhrases =
    [
        "بهترین گزارش های ماهانه",
        "بهترین گزارش‌های ماهانه",
        "بهترین گزارش تولید و فروش",
        "بهترین گزارش‌های تولید و فروش",
        "رتبه بندی گزارش ماهانه",
        "رتبه‌بندی گزارش ماهانه",
        "رتبه بندی گزارش تولید و فروش",
        "رتبه‌بندی گزارش تولید و فروش",
        "گزارش های فروش قوی",
        "گزارش‌های فروش قوی",
        "گزارش های فروش ضعیف",
        "گزارش‌های فروش ضعیف",
        "گزارش ماهانه خوبی داشتند",
        "رشد باکیفیت فروش",
        "رشد با کیفیت فروش",
        "رشد فروش باکیفیت",
        "رشد فروش با کیفیت",
        "رشد فروش فقط از نرخ",
        "رکورد فروش ماهانه",
        "بالاتر از میانگین ۱۲ ماهه",
        "بالاتر از میانگین 12 ماهه",
        "گزارش برتر تولید و فروش",
        "گزارش های ماهانه بازار",
        "گزارش‌های ماهانه بازار",
        "monthly sales quality",
        "monthly production sales quality",
        "monthly reports ranking"
    ];

    private static readonly string[] NormalizedRankingPhrases = RankingPhrases
        .Select(NormalizeText)
        .ToArray();

    public static bool LooksLikeMonthlySalesQualityRankingQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var normalized = NormalizeText(query);
        return NormalizedRankingPhrases.Any(p => normalized.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public static MonthlySalesQualityRankingQuery BuildQuery(string query)
    {
        var normalized = NormalizeText(query);
        var direction = ContainsAny(normalized, "ضعیف", "بدترین", "پایین ترین", "پایین‌ترین", "bottom", "weak")
            ? MonthlySalesQualityDirection.Bottom
            : MonthlySalesQualityDirection.Top;
        var industryTitle = ExtractIndustryTitle(query);
        var scope = !string.IsNullOrWhiteSpace(industryTitle)
            ? MonthlySalesQualityScope.Industry
            : MonthlySalesQualityScope.Market;

        return new MonthlySalesQualityRankingQuery(
            IndustryTitle: industryTitle,
            Scope: scope,
            Direction: direction,
            Limit: ExtractLimit(normalized) ?? 10,
            IncludeExplanation: true,
            IncludeDimensionScores: false,
            OnlyEligibleRows: true);
    }

    public static string NormalizeText(string text) =>
        text.Trim()
            .Replace('ك', 'ک')
            .Replace('ي', 'ی')
            .Replace('‌', ' ')
            .ToLowerInvariant();

    private static bool ContainsAny(string normalized, params string[] phrases) =>
        phrases.Any(p => normalized.Contains(NormalizeText(p), StringComparison.OrdinalIgnoreCase));

    private static int? ExtractLimit(string normalized)
    {
        var digits = new string(normalized
            .Select(ToAsciiDigitOrNull)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .ToArray());

        if (int.TryParse(digits, out var parsed))
            return Math.Clamp(parsed, 1, 50);

        if (normalized.Contains("ده", StringComparison.OrdinalIgnoreCase)) return 10;
        if (normalized.Contains("پنج", StringComparison.OrdinalIgnoreCase)) return 5;
        return null;
    }

    private static string? ExtractIndustryTitle(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var normalized = NormalizeText(query);
        var match = IndustryPattern.Match(normalized);
        if (!match.Success)
            return null;

        var value = match.Groups["industry"].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static char? ToAsciiDigitOrNull(char c) => c switch
    {
        >= '0' and <= '9' => c,
        '۰' => '0',
        '۱' => '1',
        '۲' => '2',
        '۳' => '3',
        '۴' => '4',
        '۵' => '5',
        '۶' => '6',
        '۷' => '7',
        '۸' => '8',
        '۹' => '9',
        '٠' => '0',
        '١' => '1',
        '٢' => '2',
        '٣' => '3',
        '٤' => '4',
        '٥' => '5',
        '٦' => '6',
        '٧' => '7',
        '٨' => '8',
        '٩' => '9',
        _ => null
    };

    private static readonly Regex IndustryPattern = new(
        @"(?:در\s+)?صنعت\s+(?<industry>.+?)(?:\s+(?:کدام|چه|بهترین|ضعیف|رتبه|گزارش|شرکت|نماد|داشتند|بودند|را|است|هستند)\b|[؟?]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

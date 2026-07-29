namespace FinancialCopilot.Application.AI.Orchestration;

public static class MonthlyActivityTrendIntentRules
{
    // Feature 113 canonical aliases. Each describes the same persisted monthly sales-trend result;
    // production wording is an established alias, not a request for a new production series.
    private static readonly string[] CanonicalMonthlySalesTrendPhrases =
    [
        "روند فروش ماهانه",
        "چارت فروش ماهانه",
        "روند فروش",
        "روند تولید و فروش",
        "نمودار تولید و فروش ماهانه",
        "نمودار فروش",
        "نمودار فروش ماهانه"
    ];

    // Existing supported comparison/report phrases remain part of the same governed intent.
    private static readonly string[] SupportedTrendPhrases =
    [
        "روند تولید",
        "نمودار تولید",
        "مقایسه فروش سال جاری و سال گذشته",
        "مقایسه فروش سال جاری",
        "مقایسه سال جاری و سال قبل",
        "فروش امسال نسبت به پارسال",
        "فروش امسال نسبت به سال قبل",
        "فروش سال جاری نسبت به سال قبل",
        "فروش نسبت به میانگین ۱۲ ماهه",
        "فروش نسبت به میانگین دوازده ماهه",
        "میانگین ۱۲ ماهه فروش",
        "میانگین دوازده ماهه فروش",
        "گزارش تولید و فروش با نمودار",
        "گزارش فروش با نمودار",
        "تولید و فروش نسبت به سال قبل",
        "تولید و فروش در سال جاری",
        "monthly sales trend",
        "monthly production trend",
        "sales chart",
        "production sales chart"
    ];

    private static readonly string[] NormalizedTrendPhrases =
        CanonicalMonthlySalesTrendPhrases
            .Concat(SupportedTrendPhrases)
            .Select(NormalizeText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static bool LooksLikeMonthlyActivityTrendQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var normalized = NormalizeText(query);
        return NormalizedTrendPhrases.Any(phrase =>
            normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ExtractCompanySymbol(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var normalized = NormalizeText(query);

        // Strip the matched trend phrase to reduce noise before symbol extraction.
        var matchedPhrase = NormalizedTrendPhrases
            .OrderByDescending(p => p.Length)
            .FirstOrDefault(p => normalized.Contains(p, StringComparison.OrdinalIgnoreCase));

        var stripped = matchedPhrase is null
            ? normalized
            : RemoveFirst(normalized, matchedPhrase);

        // NormalizeText maps ZWNJ to a space for phrase matching. For a symbol
        // containing a ZWNJ, preserve the original token first so it is not
        // truncated into two plausible-looking candidates.
        if (query.Contains('\u200C'))
        {
            var joinedCandidates = ExtractCandidateTokens(query);
            if (joinedCandidates.Count > 0) return joinedCandidates[0];
        }

        var candidates = ExtractCandidateTokens(stripped);
        if (candidates.Count > 0) return candidates[0];

        // Fallback to original if stripping removed too much context.
        candidates = ExtractCandidateTokens(normalized);
        if (candidates.Count > 0) return candidates[0];

        return null;
    }

    public static string NormalizeText(string text) =>
        text.Trim()
            .Replace('ك', 'ک')
            .Replace('ي', 'ی')
            .Replace('‌', ' ')
            .ToLowerInvariant();

    private static string RemoveFirst(string source, string value)
    {
        var index = source.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? source : source.Remove(index, value.Length);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "از", "به", "در", "با", "که", "را", "تا", "یا", "هم", "هر", "این", "آن", "اگر",
        "چه", "کی", "کو", "اما", "ولی", "پس", "نه", "بله", "خیر",
        "چیست", "چیه", "هست", "است", "بود", "شد", "کرد", "داد", "برای",
        "دارد", "دارم", "دارن", "دارند", "ندارد",
        "بده", "بگو", "بگیر", "بزن", "نشان", "نده",
        "می", "نمی", "فقط", "اول", "آخر", "کجا", "کدام",
        "روند", "نمودار", "فروش", "تولید", "مقایسه", "میانگین", "گزارش",
        "سال", "جاری", "قبل", "پارسال", "امسال", "ماهانه", "ماه",
        "نسبت", "دوازده", "ماهه"
    };

    private static List<string> ExtractCandidateTokens(string normalized)
    {
        var candidates = new List<string>();
        var i = 0;
        while (i < normalized.Length)
        {
            var c = normalized[i];
            var isSymbolCharacter = IsSymbolCharacter(c);
            if (isSymbolCharacter)
            {
                var start = i;
                while (i < normalized.Length && IsSymbolCharacter(normalized[i]))
                    i++;
                var len = i - start;
                var token = NormalizeSymbolToken(normalized.Substring(start, len));
                if (token.Length is >= 2 and <= 6 && !StopWords.Contains(token))
                    candidates.Add(token);
            }
            else
            {
                i++;
            }
        }
        return candidates;
    }

    private static bool IsSymbolCharacter(char value) =>
        char.IsLetter(value)
        || value is '\u200C' or '\u200D' or '\u0640';

    private static string NormalizeSymbolToken(string value) =>
        value
            .Replace('ك', 'ک')
            .Replace('ي', 'ی')
            .Replace('\u200C', '\0')
            .Replace('\u200D', '\0')
            .Replace('\u0640', '\0')
            .Replace("\0", string.Empty)
            .ToLowerInvariant();
}

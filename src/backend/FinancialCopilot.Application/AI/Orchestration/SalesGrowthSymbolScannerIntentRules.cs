namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Governed lexical coverage for Feature 116. Routing precedence is owned by
/// Task 4; this class only answers whether a normalized message expresses the
/// list + monthly-sales + growth shape.
/// </summary>
public static class SalesGrowthSymbolScannerIntentRules
{
    private static readonly string[] DiscoveryTerms =
    [
        "list", "which stocks", "which symbols", "which companies", "find stocks",
        "find symbols", "stocks", "symbols", "companies", "shares",
        "لیست", "فهرست", "کدام سهم", "چه نماد", "شرکت هایی", "شرکت های",
        "سهم ها", "نمادها", "سهام", "کدوما", "کدوم سهم"
    ];

    private static readonly string[] SalesTerms =
    [
        "sales", "monthly sales", "sales revenue", "فروش", "فروش ماهانه",
        "فروش این ماه", "فروش ماه جاری", "درآمد فروش"
    ];

    private static readonly string[] GrowthTerms =
    [
        "sales growth", "growth", "grew", "increased", "increase", "improved",
        "higher", "times", "رشد", "افزایش", "افزایش داشته",
        "بیشتر شده", "بهتر شده", "بهتر", "بهبود فروش", "بالاتر رفته", "چند برابر شده"
    ];

    private static readonly string[] ComparisonTerms =
    [
        "previous month", "last month", "month over month", "mom", "same month last year",
        "last year", "year over year", "yoy", "previous 12 months", "12 month average",
        "12-month average", "average of the previous twelve months", "ماه قبل", "دوره قبل",
        "سال گذشته", "پارسال", "ماه مشابه سال قبل", "دوره مشابه سال قبل", "میانگین 12 ماهه",
        "متوسط 12 ماه", "میانگین دوازده ماه", "میانگین فروش 12 ماهه"
    ];

    private static readonly string[] ThresholdTerms =
    [
        "above", "over", "at least", "minimum", "below", "under", "more than", "less than",
        "بالای", "حداقل", "بیش از", "زیر", "کمتر از", "درصد", "برابر"
    ];

    private static readonly string[] NormalizedDiscoveryTerms = DiscoveryTerms.Select(Normalize).ToArray();
    private static readonly string[] NormalizedSalesTerms = SalesTerms.Select(Normalize).ToArray();
    private static readonly string[] NormalizedGrowthTerms = GrowthTerms.Select(Normalize).ToArray();
    private static readonly string[] NormalizedComparisonTerms = ComparisonTerms.Select(Normalize).ToArray();
    private static readonly string[] NormalizedThresholdTerms = ThresholdTerms.Select(Normalize).ToArray();

    private static readonly HashSet<string> LookupStopWords =
    [
        "monthly", "sales", "growth", "grew", "increased", "increase", "improved", "higher",
        "previous", "last", "same", "month", "year", "over", "average", "of", "for", "with",
        "the", "and", "by", "to", "from", "versus", "above", "below", "percent", "times",
        "فروش", "ماهانه", "ماه", "رشد", "افزایش", "بهبود", "بیشتر", "بالاتر", "نسبت", "به",
        "سال", "قبل", "گذشته", "مشابه", "میانگین", "متوسط", "درصد", "برابر", "شرکت", "سهام"
    ];

    public static bool LooksLikeSalesGrowthScannerQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var normalized = Normalize(query);
        var hasDiscoveryShape = ContainsAny(normalized, NormalizedDiscoveryTerms);
        var hasNumericThreshold = System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\d")
            && ContainsAny(normalized, NormalizedThresholdTerms);
        return (hasDiscoveryShape || hasNumericThreshold)
            && ContainsAny(normalized, NormalizedSalesTerms)
            && ContainsAny(normalized, NormalizedGrowthTerms);
    }

    public static bool ContainsComparisonPhrase(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        return ContainsAny(Normalize(query), NormalizedComparisonTerms);
    }

    /// <summary>
    /// Identifies the single-symbol sales-growth lookup shape so it can remain
    /// on the existing SymbolLookup route. This intentionally does not resolve
    /// a company; the authoritative symbol/company resolver remains downstream.
    /// </summary>
    public static bool LooksLikeSingleSymbolSalesGrowthLookup(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var normalized = Normalize(query);
        if (ContainsAny(normalized, NormalizedDiscoveryTerms)
            || !ContainsAny(normalized, NormalizedSalesTerms)
            || !ContainsAny(normalized, NormalizedGrowthTerms)
            || (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\d")
                && ContainsAny(normalized, NormalizedThresholdTerms))
            || ContainsAny(normalized, ["trend", "chart", "نمودار", "روند", "ترکیب", "محصول"]))
        {
            return false;
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Length is >= 2 and <= 20 && !LookupStopWords.Contains(token));
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim()
            .Replace('ي', 'ی')
            .Replace('ى', 'ی')
            .Replace('ك', 'ک')
            .Replace('\u200C', ' ')
            .Replace('\u200D', ' ')
            .Replace('\uFEFF', ' ')
            .Replace('٪', '%')
            .Replace('×', 'x')
            .Replace('✕', 'x')
            .Replace('٫', '.')
            .Replace('٬', ',');

        for (var i = 0; i <= 9; i++)
        {
            normalized = normalized
                .Replace((char)('\u06F0' + i), (char)('0' + i))
                .Replace((char)('\u0660' + i), (char)('0' + i));
        }

        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized, "(?<=\\d),(?=\\d)", ".");
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized, "[^\\p{L}\\p{N}%x.]+", " ");

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        return normalized.Trim().ToLowerInvariant();
    }

    private static bool ContainsAny(string value, IEnumerable<string> phrases) =>
        phrases.Any(phrase => value.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}

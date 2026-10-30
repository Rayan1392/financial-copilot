using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Generic product-scope extraction for semantic routing. It does not resolve identity.</summary>
public static class ProductSemanticIntentRules
{
    private static readonly string[] ProductMarkers =
    [
        "product", "products", "\u0645\u062d\u0635\u0648\u0644", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a"
    ];

    private static readonly string[] TrendMarkers =
    [
        "trend", "chart", "graph", "\u0631\u0648\u0646\u062f", "\u0646\u0645\u0648\u062f\u0627\u0631", "\u0686\u0627\u0631\u062a", "\u0645\u0627\u0647\u0627\u0646\u0647"
    ];

    private static readonly string[] SalesMarkers =
    [
        "sales", "sale", "revenue", "sold", "\u0641\u0631\u0648\u0634", "\u0641\u0631\u0648\u062e\u062a\u0647", "\u0641\u0631\u0648\u062e\u062a", "\u062f\u0631\u0622\u0645\u062f"
    ];

    private static readonly string[] CompositionMarkers =
    [
        "mix", "composition", "\u062a\u0631\u06a9\u06cc\u0628", "\u0631\u06a9\u06cc\u0628", "\u0633\u0647\u0645", "\u0628\u06cc\u0634\u062a\u0631\u06cc\u0646"
    ];

    private static readonly string[] ComparisonMarkers =
    [
        "compare", "comparison", "versus", "between", "change", "\u0645\u0642\u0627\u06cc\u0633\u0647", "\u062a\u063a\u06cc\u06cc\u0631", "\u0628\u06cc\u0646", "\u062a\u0648\u0644\u06cc\u062f"
    ];

    private static readonly HashSet<string> CandidateStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sales", "sale", "revenue", "sold", "trend", "chart", "graph", "monthly", "latest", "company",
        "\u0641\u0631\u0648\u0634", "\u0641\u0631\u0648\u062e\u062a\u0647", "\u0641\u0631\u0648\u062e\u062a", "\u062f\u0631\u0622\u0645\u062f", "\u0631\u0648\u0646\u062f", "\u0646\u0645\u0648\u062f\u0627\u0631", "\u0686\u0627\u0631\u062a",
        "\u0645\u0627\u0647\u0627\u0646\u0647", "\u0622\u062e\u0631\u06cc\u0646", "\u0686\u0642\u062f\u0631", "\u0628\u0648\u062f\u0647", "\u0627\u0633\u062a",
        "\u0631\u0627", "\u0631\u0648", "\u0646\u0634\u0627\u0646", "\u0646\u0645\u0627\u06cc\u0634", "\u0628\u062f\u0647", "\u0634\u0631\u06a9\u062a",
        "\u0628\u06cc\u0634\u062a\u0631\u06cc\u0646", "\u06a9\u062f\u0627\u0645", "\u06a9\u062f\u0627\u0645\u06cc\u0646"
    };

    // Physical-volume wording: «مقدار/میزان/حجم فروش» is SalesQuantity and «مقدار/میزان تولید» ProductionQuantity, never SalesAmount.
    private static readonly string[] PhysicalQuantityMarkers =
    [
        "مقدار فروش", "میزان فروش", "حجم فروش",
        "مقدار تولید", "میزان تولید", "تناژ",
        "چند تن", "sales quantity", "sales volume", "sold volume", "production quantity", "tonnage"
    ];

    /// <summary>True when the question asks for a physical sold or produced volume (tons, units), not a monetary amount.</summary>
    public static bool RequestsPhysicalQuantity(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (ContainsAny(text, PhysicalQuantityMarkers) || ContainsAny(QueryNormalization.Normalize(text), PhysicalQuantityMarkers));

    // Measure, period, unit and question words of a product value question. What remains of
    // «مبلغ فروش گندله کگهر در تیر ۱۴۰۵ چقدر بود؟» is «گندله کگهر»: the product, then the company.
    private static readonly HashSet<string> ValueQuestionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "مبلغ", "ارزش", "ریالی", "فروش", "فروخته", "فروخت", "درآمد", "خالص", "ناخالص", "کل", "کلی", "مجموع",
        "در", "طی", "ماه", "سال", "دوره", "آخرین", "اخیر", "گذشته", "جاری", "گزارش", "شده",
        "چقدر", "چه", "قدر", "چند", "است", "بود", "بوده", "هست", "شد", "شرکت", "نماد", "سهم",
        "را", "رو", "از", "به", "و", "با", "برای", "میلیارد", "میلیون", "تومان", "ریال",
        "لطفا", "لطفاً", "بگو", "بگید", "بده", "بدید", "نشان", "نمایش", "کن", "کنید", "ها", "های", "بر", "اساس"
    };

    // A ranking across companies is never one company's product value.
    private static readonly string[] RankingMarkers =
    [
        "رتبه", "بیشترین", "بالاترین", "کمترین", "پایین‌ترین", "کدام شرکت", "rank", "top"
    ];

    // A question whose answer is a set of companies (discovery or ranking), never one company's value.
    private static readonly string[] CompanyPopulationMarkers =
    [
        "کدام شرکت", "چه شرکت", "کدام نماد", "چه نماد", "شرکت‌ها", "شرکتها", "شرکت ها", "شرکت‌های", "شرکتهای",
        "تولیدکنندگان", "تولید کنندگان", "رتبه‌بندی", "رتبه بندی", "which compan", "rank"
    ];

    /// <summary>
    /// True when the question asks for a set of companies («کدام شرکت‌ها … گندله تولید کرده‌اند؟», «شرکت‌ها را … رتبه‌بندی کن»).
    /// Such a question names its population itself; a company remembered from an earlier turn must not narrow it.
    /// </summary>
    public static bool AsksForCompanyPopulation(string? text) =>
        !string.IsNullOrWhiteSpace(text) && ContainsAny(text, CompanyPopulationMarkers);

    /// <summary>A product's monetary sales value (SalesAmount); a physical-quantity question is never this capability.</summary>
    public static bool LooksLikeProductSalesValue(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        !RequestsPhysicalQuantity(text) &&
        (ExtractProductMention(text) ?? ExtractUnmarkedProductMention(text)) is not null &&
        ContainsAny(text, SalesMarkers) &&
        !ContainsAny(text, TrendMarkers) &&
        !ContainsAny(text, ComparisonMarkers) &&
        !ContainsAny(text, RankingMarkers);

    /// <summary>
    /// The product of a value question written without the «محصول» marker («مبلغ فروش گندله کگهر چقدر است؟»): after
    /// measure, period and question words are removed, the last word is the company and the words before it are the
    /// product. Null when only the company remains or a Latin word occurs (Persian wording only). Recognition only;
    /// the company-scoped resolver decides identity.
    /// </summary>
    public static string? ExtractUnmarkedProductMention(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Regex.IsMatch(text, "[A-Za-z]")) return null;
        var remaining = Regex.Matches(text, @"[\p{L}\p{Nd}][\p{L}\p{Nd}_-]*")
            .Select(match => match.Value)
            .Where(token => token.Length > 1 &&
                            !token.All(char.IsDigit) &&
                            !ValueQuestionWords.Contains(token) &&
                            !CandidateStopWords.Contains(token) &&
                            !ProductMarkers.Contains(token, StringComparer.OrdinalIgnoreCase) &&
                            !AnalysisVocabulary.IsMetaTerm(token) &&
                            !PersianReportingPeriodNormalizer.MentionsReportingPeriod(token))
            .ToArray();
        return remaining.Length < 2 ? null : string.Join(' ', remaining[..^1]);
    }

    /// <summary>«محصول گندله» → «گندله»: the mention without its generic product marker, or null when it has none.</summary>
    public static string? StripProductMarker(string? mention)
    {
        if (string.IsNullOrWhiteSpace(mention)) return null;
        var parts = mention.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && ProductMarkers.Contains(NormalizeProductSurface(parts[0]), StringComparer.OrdinalIgnoreCase)
            ? parts[1].Trim()
            : null;
    }

    public static bool LooksLikeProductSalesTrend(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        ExtractProductMention(text) is not null &&
        ContainsAny(text, SalesMarkers) &&
        ContainsAny(text, TrendMarkers) &&
        !ContainsAny(text, ComparisonMarkers);

    public static bool LooksLikeProductRevenueComposition(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (ContainsAny(text, CompositionMarkers) ||
         ContainsAny(QueryNormalization.Normalize(text), CompositionMarkers)) &&
        (ContainsAny(text, SalesMarkers) ||
         ContainsAny(QueryNormalization.Normalize(text), SalesMarkers));

    public static string? ExtractProductMention(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (LooksLikeProductRevenueComposition(text) ||
            LooksLikeProductRevenueComposition(QueryNormalization.Normalize(text))) return null;
        var tokens = Regex.Matches(text, @"[\p{L}\p{Nd}][\p{L}\p{Nd}_\u200c-]*")
            .Select(match => match.Value)
            .Where(token => NormalizeProductSurface(token).Length > 1)
            .ToArray();
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            var marker = NormalizeProductSurface(tokens[index]);
            if (!ProductMarkers.Any(item => string.Equals(marker, NormalizeProductSurface(item), StringComparison.OrdinalIgnoreCase)))
                continue;
            var candidate = NormalizeProductSurface(tokens[index + 1]);
            if (CandidateStopWords.Contains(candidate) || ProductMarkers.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                continue;

            // Keep the marker with the following token. A short token such as
            // "hot"/"گرم" is not an identity: the product registry can contain
            // both the exact title and several longer titles containing it.
            return $"{marker} {candidate}";
        }

        // Persian possessive forms often omit the generic product marker (for
        // example, "کاتدش"). Strip only the grammatical suffix; identity
        // resolution remains company-scoped and authoritative.
        foreach (var token in tokens)
        {
            var normalized = NormalizeProductSurface(token);
            var hasPossessiveSuffix = token.Replace("\u200c", string.Empty, StringComparison.Ordinal)
                .EndsWith("\u0634", StringComparison.Ordinal);
            if (!hasPossessiveSuffix ||
                CandidateStopWords.Contains(token) ||
                CandidateStopWords.Contains(normalized) ||
                AnalysisVocabulary.IsMetaTerm(token) ||
                ProductMarkers.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                continue;
            return normalized;
        }
        return null;
    }

    public static string NormalizeProductSurface(string value)
    {
        var normalized = value.Replace("\u200c", string.Empty, StringComparison.Ordinal).Trim();
        if (normalized.EndsWith("\u0634", StringComparison.Ordinal) && normalized.Length > 3)
            normalized = normalized[..^1];
        return normalized;
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}

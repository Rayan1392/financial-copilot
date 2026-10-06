using System.Text.RegularExpressions;

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
        "\u0631\u0627", "\u0631\u0648", "\u0646\u0634\u0627\u0646", "\u0628\u062f\u0647", "\u0634\u0631\u06a9\u062a",
        "\u0628\u06cc\u0634\u062a\u0631\u06cc\u0646", "\u06a9\u062f\u0627\u0645", "\u06a9\u062f\u0627\u0645\u06cc\u0646"
    };

    public static bool LooksLikeProductSalesValue(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        ExtractProductMention(text) is not null &&
        ContainsAny(text, SalesMarkers) &&
        !ContainsAny(text, TrendMarkers) &&
        !ContainsAny(text, ComparisonMarkers);

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

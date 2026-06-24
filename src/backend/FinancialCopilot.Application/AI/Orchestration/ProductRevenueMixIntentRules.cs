using System.Text;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.AI.Orchestration;

public static class ProductRevenueMixIntentRules
{
    private static readonly string[] ProductRevenuePhrases =
    [
        "مهم‌ترین محصول", "مهم ترین محصول",
        "پرفروش‌ترین محصول", "پرفروش ترین محصول", "پرفروشترین محصول",
        "پرفروش‌ترین محصولات", "پرفروش ترین محصولات", "پرفروشترین محصولات",
        "محصول پرفروش", "محصولات پرفروش",
        "محصول اصلی", "محصولات اصلی",
        "بیشترین فروش محصول", "بیشترین فروش محصولات",
        "بیشترین درآمد از چه محصول", "بیشترین درآمد از چه محصولی",
        "بیشتر از چه محصول", "بیشتر از چه محصولی",
        "بیشتر از چه محصول درآمد دارد", "بیشتر از چه محصولی درآمد دارد",
        "ترکیب فروش محصول", "ترکیب فروش محصولات",
        "ترکیب درآمد محصول", "ترکیب درآمد محصولات",
        "سهم فروش محصول", "سهم فروش محصولات",
        "سهم درآمد محصول", "سهم درآمد محصولات",
        "کدام محصول بیشترین فروش", "کدام محصول بیشترین درآمد",
        "بالاترین فروش محصول", "بالاترین درآمد محصول",
        "revenue mix", "product revenue",
        "most important product", "top products",
        "product composition", "product concentration"
    ];

    public static bool LooksLikeProductRevenueMixQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var normalized = NormalizeLookupText(query);
        return GetMatchedPhrase(normalized) is not null;
    }

    public static string? ExtractCompanySymbol(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var normalized = NormalizeLookupText(query);
        var matchedPhrase = GetMatchedPhrase(normalized);
        var stripped = matchedPhrase is null
            ? normalized
            : RemoveFirst(normalized, matchedPhrase);

        var candidateTokens = ExtractCandidateTokens(stripped);
        if (candidateTokens.Count > 0)
            return candidateTokens[0];

        // Fall back to the original text if phrase stripping removed too much context.
        candidateTokens = ExtractCandidateTokens(normalized);
        return candidateTokens.Count > 0 ? candidateTokens[0] : null;
    }

    public static string NormalizeLookupText(string text) =>
        text.Trim()
            .Replace('ك', 'ک')
            .Replace('ي', 'ی')
            .Replace('‌', ' ')
            .ToLowerInvariant();

    private static string? GetMatchedPhrase(string normalizedQuery) =>
        ProductRevenuePhrases
            .Select(NormalizeLookupText)
            .OrderByDescending(phrase => phrase.Length)
            .FirstOrDefault(phrase => normalizedQuery.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static string RemoveFirst(string source, string value)
    {
        var index = source.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? source : source.Remove(index, value.Length);
    }

    private static List<string> ExtractCandidateTokens(string normalized)
    {
        var candidates = new List<string>();
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "از", "به", "در", "با", "که", "را", "تا", "یا", "هم", "هر", "این", "آن", "اگر",
            "چه", "کی", "کو", "اما", "ولی", "پس", "نه", "بله", "خیر",
            "چیست", "چیه", "هست", "است", "بود", "شد", "کرد", "داد", "برای",
            "دارد", "دارم", "دارن", "دارند", "ندارد",
            "بده", "بگو", "بگیر", "بزن", "نشان", "نده",
            "می", "نمی", "فقط", "اول", "آخر", "کجا", "کدام",
            "مهم", "اصلی", "ترین", "بیشتر", "بیشترین", "کمتر", "بالا", "پایین",
            "محصول", "محصولات", "محصولی", "فروش", "درآمد", "سهم", "ترکیب",
        };

        foreach (Match match in Regex.Matches(normalized, @"[\p{L}\p{Nd}]+"))
        {
            var token = match.Value;
            if (token.Length is < 2 or > 5)
                continue;

            if (!stopWords.Contains(token))
                candidates.Add(token);
        }

        return candidates;
    }
}

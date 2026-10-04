using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Deterministic V2 gate/parser for a named product monthly trend.</summary>
public static class MonthlyProductTrendIntentRules
{
    private static readonly string[] TrendTerms =
    [
        "monthly product sales", "product sales trend", "product sale rate", "sales rate",
        "product production", "product trend", "monthly sales rate",
        "روند فروش", "روند تولید", "نرخ فروش", "فروش ماهانه", "تولید ماهانه"
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "trend", "monthly", "sale", "sales", "product", "products", "production", "rate",
        "the", "of", "for", "company", "و", "از", "به", "را", "در", "ماهانه", "ماه", "روند",
        "فروش", "تولید", "نرخ", "محصول", "محصولات", "شرکت", "سهم", "چارت", "نمودار", "کن", "کنید"
    };

    private static readonly HashSet<string> CommandStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "را", "نشان", "بده", "کن", "کنید", "ده", "دهید", "نمایش", "لطفا", "لطفاً",
        "Ø±Ø§", "Ù†Ø´Ø§Ù†", "Ø¨Ø¯Ù‡", "Ú©Ù†", "Ú©Ù†ÛŒØ¯", "Ø¯Ù‡", "Ø¯Ù‡ÛŒØ¯", "Ù†Ù…Ø§ÛŒØ´"
    };

    // Keep the legacy encoded aliases above for backward compatibility, but ensure real
    // Unicode Persian requests are recognized by the V2 deterministic gate as well.
    private static readonly string[] UnicodeTrendTerms =
    [
        "\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634", "\u0631\u0648\u0646\u062f \u062a\u0648\u0644\u06cc\u062f",
        "\u0646\u0631\u062e \u0641\u0631\u0648\u0634", "\u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647",
        "\u062a\u0648\u0644\u06cc\u062f \u0645\u0627\u0647\u0627\u0646\u0647"
    ];

    private static readonly HashSet<string> UnicodeStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u0648", "\u0627\u0632", "\u0628\u0647", "\u0631\u0627", "\u062f\u0631", "\u0645\u0627\u0647\u0627\u0646\u0647", "\u0645\u0627\u0647", "\u0631\u0648\u0646\u062f",
        "\u0641\u0631\u0648\u0634", "\u062a\u0648\u0644\u06cc\u062f", "\u0646\u0631\u062e", "\u0645\u062d\u0635\u0648\u0644", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a",
        "\u0634\u0631\u06a9\u062a", "\u0633\u0647\u0645", "\u0686\u0627\u0631\u062a", "\u0646\u0645\u0648\u062f\u0627\u0631", "\u06a9\u0646", "\u06a9\u0646\u06cc\u062f"
    };

    private static readonly HashSet<string> UnicodeCommandStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u0631\u0627", "\u0646\u0634\u0627\u0646", "\u0628\u062f\u0647", "\u06a9\u0646", "\u06a9\u0646\u06cc\u062f", "\u0628\u062f\u0647\u06cc\u062f", "\u0646\u0645\u0627\u06cc\u0634", "\u0644\u0637\u0641\u0627"
    };

    private static readonly HashSet<string> UnicodeGenericTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u0631\u0648\u0646\u062f", "\u0641\u0631\u0648\u0634", "\u0645\u0627\u0647\u0627\u0646\u0647", "\u062a\u0648\u0644\u06cc\u062f", "\u0646\u0645\u0648\u062f\u0627\u0631", "\u0686\u0627\u0631\u062a", "\u0633\u0627\u0644", "\u062c\u0627\u0631\u06cc", "\u0642\u0628\u0644"
    };

    public static bool LooksLikeMonthlyProductTrendQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var text = query.Trim();
        if (ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery(text) ||
            MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery(text)) return false;
        var parsed = ExtractProductAndCompany(text);
        return TrendTerms.Concat(UnicodeTrendTerms).Any(text.Contains) && parsed.Product is not null && HasProductEvidence(parsed.Product);
    }

    public static MonthlyProductTrendQuery BuildQuery(string query)
    {
        var parsed = ExtractProductAndCompany(query);
        return new(
            parsed.Company ?? string.Empty,
            parsed.Product ?? string.Empty,
            TryPeriod(query, "from"),
            TryPeriod(query, "to"),
            query.Contains("نرخ", StringComparison.Ordinal) || query.Contains("\u0646\u0631\u062e", StringComparison.Ordinal) || query.Contains("rate", StringComparison.OrdinalIgnoreCase)
                ? MonthlyProductComparisonFocus.Rate
                : MonthlyProductComparisonFocus.Sales);
    }

    public static (string? Product, string? Company) ExtractProductAndCompany(string query)
    {
        var tokens = Regex.Matches(query, @"[\u0600-\u06ffA-Za-z][\u0600-\u06ffA-Za-z0-9_\u200c-]*")
            .Select(match => match.Value.Trim('_', '-'))
            .Where(token => token.Length > 1)
            .ToList();
        if (tokens.Count < 2) return (null, null);

        var companyIndex = -1;
        for (var index = tokens.Count - 1; index >= 0; index--)
        {
            if (!StopWords.Contains(tokens[index]) && !UnicodeStopWords.Contains(tokens[index]) && !CommandStopWords.Contains(tokens[index]) && !UnicodeCommandStopWords.Contains(tokens[index]) && !Regex.IsMatch(tokens[index], @"^\d+$"))
            {
                companyIndex = index;
                break;
            }
        }
        if (companyIndex <= 0) return (null, null);

        var productTokens = tokens
            .Take(companyIndex)
            .Where(token => !StopWords.Contains(token) && !UnicodeStopWords.Contains(token) && !CommandStopWords.Contains(token) && !UnicodeCommandStopWords.Contains(token) && !Regex.IsMatch(token, @"^\d+$"))
            .ToArray();
        return productTokens.Length == 0 ? (null, null) : (string.Join(' ', productTokens), tokens[companyIndex]);
    }

    private static bool HasProductEvidence(string product)
    {
        var candidate = product.Trim();
        if (candidate.Length == 0) return false;

        // A company-only trend such as "روند فروش کچاد" must remain on the
        // company-trend route.  Product titles may be one or more tokens, so
        // accept any token left after removing only generic trend vocabulary.
        var genericTerms = new[]
        {
            "روند", "فروش", "ماهانه", "تولید", "نمودار", "چارت", "سال", "جاری", "قبل",
            "Ø±ÙˆÙ†Ø¯", "ÙØ±ÙˆØ´", "Ù…Ø§Ù‡Ø§Ù†Ù‡", "ØªÙˆÙ„ÛŒØ¯", "Ù†Ù…ÙˆØ¯Ø§Ø±", "Ú†Ø§Ø±Øª"
        };

        var meaningful = Regex.Matches(candidate, @"[\u0600-\u06ffA-Za-z][\u0600-\u06ffA-Za-z0-9_\u200c-]*")
            .Select(match => match.Value)
            .Where(token => !genericTerms.Contains(token, StringComparer.OrdinalIgnoreCase) && !UnicodeGenericTerms.Contains(token))
            .ToArray();
        return meaningful.Length > 0;
    }

    private static JalaliPeriod? TryPeriod(string query, string qualifier)
    {
        var expression = qualifier == "from"
            ? @"(?:از|from)[^0-9۰-۹]{0,12}([0-9۰-۹]{4})[/\-]([0-9۰-۹]{1,2})"
            : @"(?:تا|to)[^0-9۰-۹]{0,12}([0-9۰-۹]{4})[/\-]([0-9۰-۹]{1,2})";
        var match = Regex.Match(query, expression, RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        static int Number(string value) => int.Parse(value
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9'));
        return JalaliPeriod.TryCreate(Number(match.Groups[1].Value), Number(match.Groups[2].Value), out var period)
            ? period : null;
    }
}

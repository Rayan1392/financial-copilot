using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Deterministic V2 gate/parser for a named product monthly trend.</summary>
public static class MonthlyProductTrendIntentRules
{
    private static readonly Regex DefaultRecentWindow = new(
        @"(?<![\u0600-\u06ffA-Za-z0-9])(?:(?:\u062f\u0631|\u0637\u06cc)\s*)?(?:12|\u06f1\u06f2|\u06f1\u0662|\u0661\u06f2|\u0661\u0662|\u062f\u0648\u0627\u0632\u062f\u0647)\s*\u0645\u0627\u0647(?:\u0647{0,2})?(?:\s*(?:\u0627\u062e\u06cc\u0631|\u06af\u0630\u0634\u062a\u0647))?(?![\u0600-\u06ffA-Za-z0-9])|(?<![\u0600-\u06ffA-Za-z])(?:\u062f\u0631\s*)?(?:\u06cc\u06a9|\u06cc\u06a9\u06cc)\s*\u0633\u0627\u0644\s*(?:\u0627\u062e\u06cc\u0631|\u06af\u0630\u0634\u062a\u0647)(?![\u0600-\u06ffA-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ConversationalSuffix = new(
        @"(?:\s+(?:\u0686\u0637\u0648\u0631|\u0686\u06af\u0648\u0646\u0647)\s+\u0628\u0648\u062f\u0647|\s+\u0686\u0647\s+\u0631\u0648\u0646\u062f\u06cc\s+\u062f\u0627\u0634\u062a\u0647|\s+\u0686\u0647\s+\u0648\u0636\u0639\u06cc\u062a\u06cc\s+\u062f\u0627\u0634\u062a\u0647)(?:\s+\u0627\u0633\u062a)?\s*[?؟.!]*\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex UnsupportedWindowPhrase = new(
        @"(?:(?:\u062f\u0631|\u0637\u06cc)\s*)?(?:[0-9\u06f0-\u06f9\u0660-\u0669]+|\u0633\u0647)\s*\u0645\u0627\u0647(?:\u0647)?(?:\s+\u0633\u0648\u0645)?(?:\s*(?:\u0627\u062e\u06cc\u0631|\u06af\u0630\u0634\u062a\u0647))?|(?:\u062f\u0631\s*)?\u0633\u0627\u0644\s*[0-9\u06f0-\u06f9\u0660-\u0669]{4}|\u0627\u0632\s+(?:\u0641\u0631\u0648\u0631\u062f\u06cc\u0646|\u0627\u0631\u062f\u06cc\u0628\u0647\u0634\u062a|\u062e\u0631\u062f\u0627\u062f|\u062a\u06cc\u0631|\u0645\u0631\u062f\u0627\u062f|\u0634\u0647\u0631\u06cc\u0648\u0631|\u0645\u0647\u0631|\u0622\u0628\u0627\u0646|\u0622\u0630\u0631|\u062f\u06cc|\u0628\u0647\u0645\u0646|\u0627\u0633\u0641\u0646\u062f)\s+\u062a\u0627\s+(?:\u0641\u0631\u0648\u0631\u062f\u06cc\u0646|\u0627\u0631\u062f\u06cc\u0628\u0647\u0634\u062a|\u062e\u0631\u062f\u0627\u062f|\u062a\u06cc\u0631|\u0645\u0631\u062f\u0627\u062f|\u0634\u0647\u0631\u06cc\u0648\u0631|\u0645\u0647\u0631|\u0622\u0628\u0627\u0646|\u0622\u0630\u0631|\u062f\u06cc|\u0628\u0647\u0645\u0646|\u0627\u0633\u0641\u0646\u062f)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
        var text = NormalizeQuery(query);
        if (ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery(text) ||
            MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery(text)) return false;
        var parsed = BuildQuery(query);
        return TrendTerms.Concat(UnicodeTrendTerms).Any(text.Contains) &&
               parsed.ProductText.Length > 0 && HasProductEvidence(parsed.ProductText);
    }

    public static MonthlyProductTrendQuery BuildQuery(string query)
    {
        var hasUnsupportedWindow = HasUnsupportedWindow(query);
        query = NormalizeForSlotExtraction(query);
        var parsed = ExtractProductAndCompany(query);
        return new(
            parsed.Company ?? string.Empty,
            parsed.Product ?? string.Empty,
            TryPeriod(query, "from"),
            TryPeriod(query, "to"),
            query.Contains("نرخ", StringComparison.Ordinal) || query.Contains("\u0646\u0631\u062e", StringComparison.Ordinal) || query.Contains("rate", StringComparison.OrdinalIgnoreCase)
                ? MonthlyProductComparisonFocus.Rate
                : MonthlyProductComparisonFocus.Sales,
            UnsupportedTimeWindow: hasUnsupportedWindow);
    }

    public static (string? Product, string? Company) ExtractProductAndCompany(string query)
    {
        query = NormalizeQuery(query);
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

    /// <summary>
    /// Removes only wording that denotes the capability's existing default
    /// 12-position history and a small set of trailing conversational asks.
    /// Other requested periods remain in the text for the existing parser path.
    /// </summary>
    public static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;
        var normalized = DefaultRecentWindow.Replace(query, " ");
        normalized = ConversationalSuffix.Replace(normalized, " ");
        return Regex.Replace(normalized, @"[?؟.!\s]+$", string.Empty, RegexOptions.CultureInvariant).Trim();
    }

    private static bool HasUnsupportedWindow(string query)
    {
        foreach (Match match in UnsupportedWindowPhrase.Matches(query))
        {
            if (!match.Value.Contains("\u0645\u0627\u0647", StringComparison.Ordinal)) return true;
            var digits = Regex.Match(match.Value, @"[0-9\u06f0-\u06f9\u0660-\u0669]+").Value
                .Replace('\u06f0', '0').Replace('\u06f1', '1').Replace('\u06f2', '2')
                .Replace('\u06f3', '3').Replace('\u06f4', '4').Replace('\u06f5', '5')
                .Replace('\u06f6', '6').Replace('\u06f7', '7').Replace('\u06f8', '8')
                .Replace('\u06f9', '9').Replace('\u0660', '0').Replace('\u0661', '1')
                .Replace('\u0662', '2').Replace('\u0663', '3').Replace('\u0664', '4')
                .Replace('\u0665', '5').Replace('\u0666', '6').Replace('\u0667', '7')
                .Replace('\u0668', '8').Replace('\u0669', '9');
            if (digits != "12") return true;
        }
        return false;
    }

    private static string NormalizeForSlotExtraction(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;
        return NormalizeQuery(UnsupportedWindowPhrase.Replace(query, " "));
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

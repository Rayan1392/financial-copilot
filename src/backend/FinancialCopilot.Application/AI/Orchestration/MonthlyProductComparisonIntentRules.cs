using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Lightweight semantic gate for Feature 129. It identifies the intent only;
/// periods, company and all financial values are validated/calculated by the application.</summary>
public static class MonthlyProductComparisonIntentRules
{
    private static readonly string[] ComparisonTerms =
    [
        "product sales comparison", "product sales change", "production versus sales",
        "compare products", "largest increase in sales", "product-level sales",
        "\u0641\u0631\u0648\u0634 \u0645\u062d\u0635\u0648\u0644\u0627\u062a", "\u062a\u063a\u06cc\u06cc\u0631 \u0641\u0631\u0648\u0634",
        "\u0645\u0642\u0627\u06cc\u0633\u0647 \u0645\u062d\u0635\u0648\u0644", "\u0628\u06cc\u0634\u062a\u0631\u06cc\u0646 \u0627\u0641\u0632\u0627\u06cc\u0634 \u0641\u0631\u0648\u0634",
        "\u062a\u0648\u0644\u06cc\u062f \u0648 \u0641\u0631\u0648\u0634"
    ];

    public static bool LooksLikeMonthlyProductComparisonQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var text = query.Trim().ToLowerInvariant();
        var asksForRevenueMix = text.Contains("revenue mix", StringComparison.Ordinal) ||
            (text.Contains("\u0641\u0631\u0648\u0634 \u0645\u062d\u0635\u0648\u0644", StringComparison.Ordinal) &&
             (text.Contains("\u062a\u0631\u06a9\u06cc\u0628", StringComparison.Ordinal) || text.Contains("\u0631\u06a9\u06cc\u0628", StringComparison.Ordinal)) &&
             !text.Contains("\u0645\u0642\u0627\u06cc\u0633\u0647", StringComparison.Ordinal) &&
             !text.Contains("\u062a\u063a\u06cc\u06cc\u0631", StringComparison.Ordinal));
        if (asksForRevenueMix) return false;
        if (text.Contains("monthly sales", StringComparison.Ordinal) && !text.Contains("product", StringComparison.Ordinal)) return false;
        return ComparisonTerms.Any(text.Contains);
    }

    public static MonthlyProductComparisonQuery BuildQuery(string query)
    {
        var normalized = query.Trim();
        var current = TryPeriod(normalized, "(?:جاری|فعلی|current)");
        var comparison = TryPeriod(normalized, "(?:قبلی|مقایسه|comparison|previous)");
        var company = ExtractCompanyText(normalized);
        var focus = normalized.Contains("تولید", StringComparison.OrdinalIgnoreCase) ? MonthlyProductComparisonFocus.Production : MonthlyProductComparisonFocus.All;
        return new MonthlyProductComparisonQuery(company ?? normalized, current, comparison, null, focus);
    }

    private static string? ExtractCompanyText(string query)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "فروش", "محصول", "محصولات", "تولید", "شرکت", "ماه", "ماهانه", "جاری", "قبلی", "مقایسه", "بیشترین", "افزایش", "تغییر", "نسبت", "به", "را", "کن", "کرد", "چگونه", "دارشت" };
        return System.Text.RegularExpressions.Regex.Matches(query, @"[\u0600-\u06ffA-Za-z][\u0600-\u06ffA-Za-z0-9‌_-]{1,11}")
            .Select(m => m.Value.Trim('_', '-')).FirstOrDefault(x => !stop.Contains(x));
    }

    private static JalaliPeriod? TryPeriod(string query, string qualifier)
    {
        var match = System.Text.RegularExpressions.Regex.Match(query, $"{qualifier}[^0-9۰-۹]{{0,12}}([0-9۰-۹]{{4}})[/\\-]([0-9۰-۹]{{1,2}})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        static int Number(string value) => int.Parse(value.Replace('۰','0').Replace('۱','1').Replace('۲','2').Replace('۳','3').Replace('۴','4').Replace('۵','5').Replace('۶','6').Replace('۷','7').Replace('۸','8').Replace('۹','9'));
        return JalaliPeriod.TryCreate(Number(match.Groups[1].Value), Number(match.Groups[2].Value), out var period) ? period : null;
    }
}

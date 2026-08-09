using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Recognises explicit requests for the persisted P/S gauge.</summary>
public static class PsGaugeIntentRules
{
    private static readonly string[] GaugeAliases = ["گیج", "گيج", "gauge", "عقربه"];
    private static readonly string[] PsAliases = ["p/s", "ps", "نسبت فروش به قیمت"];
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "گیج", "گيج", "gauge", "عقربه", "p", "s", "ps", "نسبت", "فروش", "به", "قیمت",
        "را", "رو", "برای", "نماد", "سهم", "شرکت", "آخرین", "نمایش", "نشان", "بده", "بدهید"
    };

    public static bool LooksLikeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var normalized = Normalize(query);
        return GaugeAliases.Any(normalized.Contains) && PsAliases.Any(normalized.Contains);
    }

    public static string? ExtractCompanySymbol(string? query)
    {
        if (!LooksLikeQuery(query)) return null;
        var tokens = Regex.Matches(Normalize(query!), @"[\p{L}\p{Nd}]+")
            .Select(x => x.Value)
            .Where(x => x.Length is >= 2 and <= 8 && !StopWords.Contains(x))
            .ToArray();
        return tokens.LastOrDefault();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant().Replace('ي', 'ی').Replace('ك', 'ک');
}

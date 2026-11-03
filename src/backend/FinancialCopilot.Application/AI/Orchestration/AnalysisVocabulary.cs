namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Single owner of analysis / reporting / ranking vocabulary that describes a kind of analysis rather than a
/// product. Product-evidence heuristics (product trend gate, Persian possessive-suffix fallback in
/// <see cref="ProductSemanticIntentRules"/>) must not treat these words as product identity, regardless of
/// morphology such as a trailing "ش".
/// </summary>
internal static class AnalysisVocabulary
{
    private static readonly HashSet<string> MetaTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        // رتبه، رتبه‌بندی، بندی، کیفیت، گزارش، بهترین، بدترین، ضعیف، قوی، برتر
        "رتبه", "رتبهبندی", "بندی",
        "کیفیت", "گزارش", "بهترین",
        "بدترین", "ضعیف", "قوی", "برتر",
        // other common analysis nouns that end in the possessive-looking "ش": ارزش، افزایش، کاهش، بخش، گرایش
        "ارزش", "افزایش", "کاهش",
        "بخش", "گرایش",
        "ranking", "rank", "quality", "report", "reports"
    };

    public static bool IsMetaTerm(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        MetaTerms.Contains(token.Replace("‌", string.Empty, StringComparison.Ordinal).Trim());
}

using FinancialCopilot.Application.FinancialData.ValuationGauges;

namespace FinancialCopilot.Application.AI.Orchestration;

public static class ValuationGaugeCapability
{
    public const string Code = "valuation_gauges";
}

/// <summary>An explicit standalone gauge request: the requested kinds and the company text (null when missing).</summary>
public sealed record ValuationGaugeRequest(IReadOnlyList<ValuationGaugeKind> Kinds, string? CompanyText)
{
    public string KindsSlotValue => ValuationGaugeIntentRules.FormatKinds(Kinds);
}

/// <summary>
/// Recognizes only explicit, single-purpose, single-symbol gauge requests: «گیج P/E X», «گیج P/S X»,
/// «گیج‌های ارزش‌گذاری X», «گیج/بازهٔ قیمت تعادلی X» (OQ-130: «گیج» or «بازه» is required, so the bare
/// «قیمت تعادلی X» keeps its route). Thresholds, screening, comparison, advice, analysis, trend or several
/// symbols are never matched here. Whether the company exists is the resolver's job.
/// </summary>
public static class ValuationGaugeIntentRules
{
    public const int MaxMessageLength = 80;
    private const int MaxCompanyTokens = 3;

    private static readonly HashSet<string> GaugeWords = new(StringComparer.Ordinal) { "گیج", "گیجهای", "gauge", "gauges" };
    private static readonly HashSet<string> RangeWords = new(StringComparer.Ordinal) { "بازه", "بازهی", "range" };

    /// <summary>Words that make the message something other than a plain gauge request.</summary>
    private static readonly HashSet<string> RejectWords = new(StringComparer.Ordinal)
    {
        "و", "یا", "با", "vs", "and", "or", "مقایسه", "مقایسهی", "compare", "در مقابل",
        "کمتر", "بیشتر", "زیر", "بالای", "بالاتر", "پایینتر", "کمترین", "بیشترین", "less", "more", "below", "above",
        "سهمهایی", "سهامی", "نمادهایی", "شرکتهایی", "سهمهای", "نمادهای", "شرکتهای", "فیلتر", "اسکنر", "screen",
        "بخرم", "بفروشم", "خرید", "فروش", "توصیه", "بخریم", "بفروشیم", "سیگنال", "buy", "sell",
        "تحلیل", "تکنیکال", "بنیادی", "روند", "تاریخچه", "تغییرات", "صنعت", "گروه", "analysis", "trend", "history",
        "ذاتی", "هدف", "پیش", "بینی", "forward", "آینده"
    };

    /// <summary>Politeness and command words removed around the company text.</summary>
    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "نماد", "سهم", "شرکت", "برای", "را", "رو", "از", "به", "در", "of", "for", "the", "show", "me",
        "نشان", "نمایش", "بده", "بدهید", "بدین", "بده؟", "کن", "کنید", "بگو", "بگید", "ببینم", "میخواهم", "میخوام",
        "چیست", "چیه", "چه", "چطور", "است", "هست", "لطفا", "لطفاً", "please", "دارد", "داره", "ی", "های", "ها"
    };

    public static ValuationGaugeRequest? TryMatch(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return null;
        var tokens = Tokens(message);
        if (tokens.Count == 0 || tokens.Any(token => token.Any(char.IsAsciiDigit) || RejectWords.Contains(token)))
            return null;

        var hasGauge = tokens.Any(GaugeWords.Contains);
        var hasRange = tokens.Any(RangeWords.Contains);
        var kinds = new SortedSet<ValuationGaugeKind>();
        var consumed = new bool[tokens.Count];
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (GaugeWords.Contains(token) || RangeWords.Contains(token))
            {
                consumed[index] = true;
                continue;
            }

            if (Ratio(tokens, index) is { } ratio)
            {
                kinds.Add(ratio.Kind);
                for (var offset = 0; offset < ratio.Length; offset++) consumed[index + offset] = true;
                index += ratio.Length - 1;
                continue;
            }

            // «ارزش‌گذاری» (normalized «ارزش گذاری» or «ارزشگذاری») and «valuation»: every kind.
            if (token is "ارزشگذاری" or "valuation")
            {
                AddAll(kinds);
                consumed[index] = true;
                continue;
            }

            if (token == "ارزش" && index + 1 < tokens.Count && tokens[index + 1] is "گذاری" or "گزاری")
            {
                AddAll(kinds);
                consumed[index] = consumed[index + 1] = true;
                index++;
                continue;
            }

            if (token is "تعادلی" or "equilibrium")
            {
                kinds.Add(ValuationGaugeKind.Equilibrium);
                consumed[index] = true;
                if (index > 0 && tokens[index - 1] is "قیمت" or "price")
                    consumed[index - 1] = true;
                continue;
            }
        }

        if (kinds.Count == 0)
            return null;
        // OQ-130: a gauge word is required; the equilibrium card also accepts «بازه».
        var onlyEquilibrium = kinds.Count == 1 && kinds.Contains(ValuationGaugeKind.Equilibrium);
        if (!hasGauge && !(hasRange && onlyEquilibrium))
            return null;
        // «بازه» with a ratio (for example «بازه P/E») is not a gauge request of this feature.
        if (!hasGauge && kinds.Any(kind => kind != ValuationGaugeKind.Equilibrium))
            return null;

        var company = tokens.Where((token, index) => !consumed[index] && !Fillers.Contains(token) && token is not "قیمت" and not "price").ToArray();
        if (company.Length > MaxCompanyTokens)
            return null;
        return new ValuationGaugeRequest(kinds.ToArray(), company.Length == 0 ? null : string.Join(' ', company));
    }

    public static string FormatKinds(IEnumerable<ValuationGaugeKind> kinds) =>
        string.Join(',', kinds.Distinct().Order().Select(kind => kind switch
        {
            ValuationGaugeKind.Pe => "pe",
            ValuationGaugeKind.Ps => "ps",
            _ => "equilibrium"
        }));

    /// <summary>Parses the persisted kinds slot; unknown entries are ignored and an empty result means all kinds.</summary>
    public static IReadOnlyList<ValuationGaugeKind> ParseKinds(string? value)
    {
        var kinds = new SortedSet<ValuationGaugeKind>();
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "pe": kinds.Add(ValuationGaugeKind.Pe); break;
                case "ps": kinds.Add(ValuationGaugeKind.Ps); break;
                case "equilibrium": kinds.Add(ValuationGaugeKind.Equilibrium); break;
            }
        }

        return kinds.Count == 0 ? ValuationGaugeCardsUseCase.Normalize(null) : kinds.ToArray();
    }

    public static QueryInterpretation BuildInterpretation(string message, ValuationGaugeRequest request, int registryVersion)
    {
        const decimal confidence = 1m;
        var evidence = new InterpretationEvidence(ValuationGaugeCapability.Code, "explicit-valuation-gauge", QueryValueProvenance.UserExplicit);
        var mentions = request.CompanyText is null
            ? Array.Empty<EntityMention>()
            : [new EntityMention(request.CompanyText, 0, request.CompanyText.Length)];
        return new QueryInterpretation(
            message,
            QueryNormalization.Normalize(message),
            "fa",
            [new CapabilityCandidate(ValuationGaugeCapability.Code, registryVersion, confidence, [evidence])],
            mentions,
            [],
            null,
            null,
            null,
            [],
            [],
            confidence,
            [evidence],
            registryVersion,
            InterpretationConfidencePolicy.Band(confidence));
    }

    private static void AddAll(SortedSet<ValuationGaugeKind> kinds)
    {
        kinds.Add(ValuationGaugeKind.Pe);
        kinds.Add(ValuationGaugeKind.Ps);
        kinds.Add(ValuationGaugeKind.Equilibrium);
    }

    /// <summary>P/E or P/S as «p e», «pe», «p/e» (normalized), or Persian «پی ای» / «پی اس».</summary>
    private static (ValuationGaugeKind Kind, int Length)? Ratio(IReadOnlyList<string> tokens, int index)
    {
        var token = tokens[index];
        if (token == "pe") return (ValuationGaugeKind.Pe, 1);
        if (token == "ps") return (ValuationGaugeKind.Ps, 1);
        if (index + 1 >= tokens.Count) return null;
        var next = tokens[index + 1];
        return token switch
        {
            "p" when next == "e" => (ValuationGaugeKind.Pe, 2),
            "p" when next == "s" => (ValuationGaugeKind.Ps, 2),
            "پی" when next == "ای" => (ValuationGaugeKind.Pe, 2),
            "پی" when next == "اس" => (ValuationGaugeKind.Ps, 2),
            _ => null
        };
    }

    private static List<string> Tokens(string message)
    {
        var text = QueryNormalization.Normalize(message.Replace('ۀ', 'ه').Replace("ٔ", string.Empty, StringComparison.Ordinal))
            .ToLowerInvariant();
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}

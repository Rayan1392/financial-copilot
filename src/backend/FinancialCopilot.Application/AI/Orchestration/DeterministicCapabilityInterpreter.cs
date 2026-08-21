using System.Diagnostics;

namespace FinancialCopilot.Application.AI.Orchestration;

public interface ICapabilityInterpreter
{
    QueryInterpretation Interpret(string message);
}

public sealed class DeterministicCapabilityInterpreter(
    IConversationalCapabilityRegistry registry,
    IQueryInterpretationTelemetrySink? telemetrySink = null) : ICapabilityInterpreter
{
    private static readonly string[] ScreeningWords = ["screen", "filter", "stocks", "سهام", "فیلتر", "شرط"];
    private static readonly string[] TrendWords = ["trend", "chart", "graph", "روند", "چارت", "نمودار"];
    private static readonly string[] AnalysisWords = ["analysis", "analyze", "review", "تحلیل", "بررسی", "ارزیابی", "وضعیت"];
    private static readonly string[] GaugeWords = ["gauge", "گیج"];
    private static readonly string[] ProductWords = ["product mix", "product revenue", "ترکیب فروش", "محصول"];
    private static readonly string[] StatementWords = ["statement", "صورت مالی", "سود و زیان", "ترازنامه"];
    private static readonly string[] StatementAnalysisWords = ["financial statement analysis", "analyze financial statement", "تحلیل صورت مالی", "تحلیل ترازنامه", "تحلیل سود و زیان"];
    private static readonly string[] DisclosureWords = ["disclosure", "اطلاعیه", "کدال"];
    private static readonly string[] RankingWords = ["ranking", "rank", "رتبه", "رتبه‌بندی", "کیفیت فروش"];
    private static readonly string[] MetricWords = ["p/e", "p/s", "eps", "roe", "roa", "فروش", "درآمد", "سود", "قیمت", "نسبت"];
    private static readonly string[] RelativeWords = ["relative valuation", "industry relative", "ارزش گذاری نسبی", "ارزش‌گذاری نسبی", "با صنعت", "در صنعت", "داخل صنعت"];
    private static readonly string[] IndustryWords = ["industry", "group", "صنعت", "گروه"];
    private static readonly HashSet<string> NonEntityWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "show", "the", "for", "with", "below", "above", "monthly", "sales", "product", "products", "mix", "revenue", "stock", "stocks", "screen",
        "filter", "analysis", "analyze", "review", "fundamental", "technical", "p", "e", "s", "eps", "چارت", "نمودار", "روند", "فروش",
        "ماهانه", "سهام", "با", "زیر", "بالای", "تحلیل", "بنیادی", "تکنیکال", "بررسی", "آخرین", "جدول", "صورت", "مالی", "قیمت", "محصول", "محصولات", "ترکیب", "رکیب",
        "ytd", "چقدر", "بوده", "است", "هست", "چیست", "چیه", "را", "کن", "بده", "نشان", "نمایش", "لطفا", "لطفاً",
        "month", "quarter", "year", "week", "previous", "prior", "last", "latest", "same", "before", "current", "and", "or",
        "ماه", "فصل", "سال", "هفته", "قبل", "قبلی", "گذشته", "اخیر", "مشابه", "جاری", "امسال", "پارسال", "و", "یا", "برای", "از", "به",
        "خود", "خودش", "همان", "مقایسه"
    };

    static DeterministicCapabilityInterpreter()
    {
        NonEntityWords.UnionWith(["industry", "group", "صنعت", "گروه", "با", "در", "داخل", "compare", "rank", "ranking", "pair", "دو", "نمادها", "symbol", "symbols", "its", "relative", "valuation"]);
    }

    public QueryInterpretation Interpret(string message)
    {
        var started = Stopwatch.GetTimestamp();
        var original = message ?? string.Empty;
        var normalized = QueryNormalization.Normalize(original);
        var language = AiDialogueOutcomePolicy.DetectReplyLanguage(original);
        var evidence = new List<InterpretationEvidence>();
        var scores = new Dictionary<string, decimal>(StringComparer.Ordinal);

        // Registry aliases are the governed recognition source. Hand-authored keyword
        // rules below only add common paraphrases and conflict-specific evidence.
        foreach (var catalogDefinition in registry.GetEnabled())
        {
            var alias = catalogDefinition.Aliases.FirstOrDefault(item =>
                normalized.Contains(QueryNormalization.Normalize(item.Value), StringComparison.OrdinalIgnoreCase));
            if (alias is null) continue;
            scores[catalogDefinition.Code] = Math.Max(scores.GetValueOrDefault(catalogDefinition.Code), 0.97m);
            evidence.Add(new InterpretationEvidence(catalogDefinition.Code, $"registry-alias:{alias.Language}", QueryValueProvenance.UserExplicit));
        }

        AddScore("stock_screening", ScreeningWords, 0.9m, normalized, scores, evidence, "screening-keyword");
        AddScore("monthly_activity_trend", TrendWords, 0.9m, normalized, scores, evidence, "trend-keyword");
        AddScore("comprehensive_analysis", AnalysisWords, 0.75m, normalized, scores, evidence, "analysis-keyword");
        AddScore("ps_gauge_visualization", GaugeWords, 0.95m, normalized, scores, evidence, "gauge-keyword");
        AddScore("product_revenue_mix", ProductWords, 0.9m, normalized, scores, evidence, "product-keyword");
        AddScore("financial_statement_table", StatementWords, 0.8m, normalized, scores, evidence, "statement-keyword");
        AddScore("financial_statement_period_analysis", StatementAnalysisWords, 0.98m, normalized, scores, evidence, "statement-analysis-keyword");
        AddScore("disclosure_listing", DisclosureWords, 0.9m, normalized, scores, evidence, "disclosure-keyword");
        AddScore("monthly_sales_quality_ranking", RankingWords, 0.9m, normalized, scores, evidence, "ranking-keyword");
        var entities = ExtractEntities(original, normalized);

        if (ContainsAny(normalized, RelativeWords) ||
            ContainsAny(normalized, IndustryWords) && ContainsAny(normalized, ["compare", "rank", "ranking", "analysis", "analyze", "review", "تحلیل", "بررسی", "مقایسه", "رتبه"]))
        {
            var pair = ContainsAny(normalized, ["pair", "two symbols", "دو نماد"]);
            var ranking = ContainsAny(normalized, RankingWords);
            var summary = !ranking && !pair && !ContainsAny(normalized, ["compare", "مقایسه"]);
            var code = pair ? "symbol_pair_within_industry" : ranking ? "industry_relative_valuation_ranking" : summary ? "industry_relative_valuation_summary" : "symbol_vs_industry_relative_valuation";
            scores[code] = Math.Max(scores.GetValueOrDefault(code), 0.98m);
            evidence.Add(new InterpretationEvidence(code, "feature-125-relative-valuation", QueryValueProvenance.UserExplicit));
        }

        // This only enters the Feature 125 comparison family. The dialogue gate
        // promotes it to the pair capability only after two canonical companies resolve.
        if (ContainsAny(normalized, ["compare", "مقایسه"]) && HasPairConjunction(normalized))
        {
            var hasIndustryReference = ContainsAny(normalized, IndustryWords);
            var code = entities.Count > 1 && !hasIndustryReference
                ? "symbol_pair_within_industry"
                : "symbol_vs_industry_relative_valuation";
            scores[code] = Math.Max(scores.GetValueOrDefault(code), 0.98m);
            evidence.Add(new InterpretationEvidence(code, "feature-125-canonical-pair-candidate", QueryValueProvenance.UserExplicit));
        }

        if (ContainsAny(normalized, MetricWords) && ContainsAny(normalized,
                ["below", "above", "under", "over", "زیر", "بالای", "کمتر از", "بیشتر از", "حداقل", "حداکثر"]))
        {
            scores["stock_screening"] = Math.Max(scores.GetValueOrDefault("stock_screening"), 0.96m);
            evidence.Add(new InterpretationEvidence("stock_screening", "metric-threshold", QueryValueProvenance.UserExplicit));
        }

        if (ContainsAny(normalized, StatementWords) && ContainsAny(normalized, AnalysisWords))
        {
            scores["financial_statement_period_analysis"] = Math.Max(scores.GetValueOrDefault("financial_statement_period_analysis"), 0.98m);
            evidence.Add(new InterpretationEvidence("financial_statement_period_analysis", "statement-with-analysis", QueryValueProvenance.UserExplicit));
        }

        if (ContainsAny(normalized, MetricWords))
        {
            scores["symbol_metric_lookup"] = Math.Max(
                scores.TryGetValue("symbol_metric_lookup", out var existing) ? existing : 0m,
                entities.Count > 0 ? 0.88m : 0.86m);
            evidence.Add(new InterpretationEvidence(
                "symbol_metric_lookup",
                "metric-and-entity",
                QueryValueProvenance.UserExplicit));
        }

        if (entities.Count > 0 && ContainsAny(normalized, AnalysisWords))
            scores["comprehensive_analysis"] = Math.Max(scores.GetValueOrDefault("comprehensive_analysis"), 0.92m);

        if (entities.Count > 0 && ContainsAny(normalized, TrendWords) && ContainsAny(normalized, ["sales", "فروش", "monthly", "ماهانه"]))
            scores["monthly_activity_trend"] = Math.Max(scores.GetValueOrDefault("monthly_activity_trend"), 0.95m);

        if (scores.TryGetValue("ps_gauge_visualization", out var gaugeScore) && ContainsAny(normalized, ["p/s", "ps"]))
            scores["ps_gauge_visualization"] = Math.Max(gaugeScore, 0.98m);

        var candidates = scores
            .Where(item => registry.Find(item.Key)?.Enabled == true)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new CapabilityCandidate(
                item.Key,
                registry.Version,
                Math.Min(item.Value, 1m),
                evidence.Where(e => e.Value.Equals(item.Key, StringComparison.OrdinalIgnoreCase) || e.Category.Contains(item.Key, StringComparison.OrdinalIgnoreCase)).ToArray()))
            .ToArray();

        var presentation = DetectPresentation(normalized);
        var metrics = ContainsAny(normalized, MetricWords)
            ? [new MetricSelection(ExtractMetric(normalized), null, QueryValueProvenance.UserExplicit)]
            : Array.Empty<MetricSelection>();
        var missingSlots = candidates.FirstOrDefault() is { } winner &&
                           registry.Find(winner.CapabilityCode) is { } definition
            ? definition.RequiredSlots
                .Where(slot => slot.Name == "symbol" && entities.Count == 0 || slot.Name == "metric" && metrics.Length == 0)
                .Select(slot => slot.Name)
                .ToArray()
            : Array.Empty<string>();
        var confidence = candidates.FirstOrDefault()?.Confidence ?? 0m;

        var preliminary = new QueryInterpretation(
            original,
            normalized,
            language,
            candidates,
            entities,
            metrics,
            Period: null,
            Comparison: null,
            presentation,
            missingSlots,
            [],
            confidence,
            evidence,
            registry.Version,
            InterpretationConfidencePolicy.Band(confidence));
        var ordered = CapabilityRoutingPrecedence.Order(preliminary, preliminary.CapabilityCandidates);
        var interpretation = preliminary with
        {
            CapabilityCandidates = ordered,
            Confidence = ordered.FirstOrDefault()?.Confidence ?? 0m,
            ConfidenceBand = InterpretationConfidencePolicy.Band(ordered.FirstOrDefault()?.Confidence ?? 0m)
        };
        try
        {
            new QueryInterpretationValidator(registry).Validate(interpretation);
            telemetrySink?.Record(new QueryInterpretationTelemetry(
                registry.Version,
                interpretation.CapabilityCandidates.Count,
                interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
                interpretation.CapabilityCandidates.FirstOrDefault()?.Confidence ?? 0m,
                interpretation.ConfidenceBand,
                interpretation.Evidence.Select(item => item.Category).Distinct(StringComparer.Ordinal).Take(10).ToArray(),
                Stopwatch.GetElapsedTime(started)));
            return interpretation;
        }
        catch
        {
            telemetrySink?.Record(new QueryInterpretationTelemetry(
                registry.Version, interpretation.CapabilityCandidates.Count,
                null, 0m, InterpretationConfidenceBand.Low, [],
                Stopwatch.GetElapsedTime(started), ValidationFailed: true));
            throw;
        }
    }

    private static void AddScore(
        string code,
        IEnumerable<string> words,
        decimal score,
        string normalized,
        IDictionary<string, decimal> scores,
        ICollection<InterpretationEvidence> evidence,
        string category)
    {
        var matched = words.FirstOrDefault(word => normalized.Contains(QueryNormalization.Normalize(word), StringComparison.OrdinalIgnoreCase));
        if (matched is null) return;
        scores[code] = Math.Max(scores.TryGetValue(code, out var existing) ? existing : 0m, score);
        evidence.Add(new InterpretationEvidence(code, matched, QueryValueProvenance.UserExplicit));
    }

    private static IReadOnlyList<EntityMention> ExtractEntities(string original, string normalized)
    {
        var result = new List<EntityMention>();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.Length < 2 || NonEntityWords.Contains(token) || QueryNormalization.IsEntityDistractor(token) || token.All(char.IsDigit))
                continue;
            if (!token.Any(character => char.IsLetter(character))) continue;
            var start = normalized.IndexOf(token, StringComparison.Ordinal);
            result.Add(new EntityMention(token, Math.Max(start, 0), token.Length));
        }
        return result.DistinctBy(item => item.Text, StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
    }

    private static PresentationPreference? DetectPresentation(string normalized) =>
        normalized.Contains("chart", StringComparison.OrdinalIgnoreCase) || normalized.Contains("چارت", StringComparison.Ordinal) || normalized.Contains("نمودار", StringComparison.Ordinal)
            ? new PresentationPreference(PresentationKind.Chart, QueryValueProvenance.UserExplicit)
            : normalized.Contains("table", StringComparison.OrdinalIgnoreCase) || normalized.Contains("جدول", StringComparison.Ordinal)
                ? new PresentationPreference(PresentationKind.Table, QueryValueProvenance.UserExplicit)
                : normalized.Contains("gauge", StringComparison.OrdinalIgnoreCase) || normalized.Contains("گیج", StringComparison.Ordinal)
                    ? new PresentationPreference(PresentationKind.Gauge, QueryValueProvenance.UserExplicit)
                    : null;

    private static string ExtractMetric(string normalized) =>
        new[] { "p/e", "p/s", "eps", "roe", "roa", "فروش", "درآمد", "سود", "قیمت" }
            .FirstOrDefault(metric => normalized.Contains(metric, StringComparison.OrdinalIgnoreCase)) ?? "unknown";

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(QueryNormalization.Normalize(candidate), StringComparison.OrdinalIgnoreCase));

    private static bool HasPairConjunction(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Equals("and", StringComparison.OrdinalIgnoreCase) || token is "و" or "با");
}

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
    private static readonly string[] TrendWords = ["trend", "chart", "graph", "monthly sales", "روند", "چارت", "نمودار", "فروش ماهانه"];
    private static readonly string[] AnalysisWords = ["analysis", "analyze", "review", "تحلیل", "بررسی", "ارزیابی", "وضعیت"];
    private static readonly string[] GaugeWords = ["gauge", "گیج"];
    private static readonly string[] ProductWords = ["product mix", "product revenue", "ترکیب فروش", "محصول"];
    private static readonly string[] StatementWords = ["statement", "صورت مالی", "سود و زیان", "ترازنامه"];
    private static readonly string[] DisclosureWords = ["disclosure", "اطلاعیه", "کدال"];
    private static readonly string[] RankingWords = ["ranking", "rank", "رتبه", "رتبه‌بندی", "کیفیت فروش"];
    private static readonly string[] MetricWords = ["p/e", "p/s", "eps", "roe", "roa", "فروش", "درآمد", "سود", "قیمت", "نسبت"];
    private static readonly HashSet<string> NonEntityWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "show", "the", "for", "with", "below", "above", "monthly", "sales", "stock", "stocks", "screen",
        "filter", "analysis", "analyze", "review", "p", "e", "s", "eps", "چارت", "نمودار", "روند", "فروش",
        "ماهانه", "سهام", "با", "زیر", "بالای", "تحلیل", "بررسی", "آخرین", "جدول", "صورت", "مالی", "قیمت"
    };

    public QueryInterpretation Interpret(string message)
    {
        var started = Stopwatch.GetTimestamp();
        var original = message ?? string.Empty;
        var normalized = QueryNormalization.Normalize(original);
        var language = AiDialogueOutcomePolicy.DetectReplyLanguage(original);
        var evidence = new List<InterpretationEvidence>();
        var scores = new Dictionary<string, decimal>(StringComparer.Ordinal);

        AddScore("stock_screening", ScreeningWords, 0.9m, normalized, scores, evidence, "screening-keyword");
        AddScore("monthly_activity_trend", TrendWords, 0.9m, normalized, scores, evidence, "trend-keyword");
        AddScore("comprehensive_analysis", AnalysisWords, 0.75m, normalized, scores, evidence, "analysis-keyword");
        AddScore("ps_gauge_visualization", GaugeWords, 0.95m, normalized, scores, evidence, "gauge-keyword");
        AddScore("product_revenue_mix", ProductWords, 0.9m, normalized, scores, evidence, "product-keyword");
        AddScore("financial_statement_table", StatementWords, 0.8m, normalized, scores, evidence, "statement-keyword");
        AddScore("disclosure_listing", DisclosureWords, 0.9m, normalized, scores, evidence, "disclosure-keyword");
        AddScore("monthly_sales_quality_ranking", RankingWords, 0.9m, normalized, scores, evidence, "ranking-keyword");

        var entities = ExtractEntities(original, normalized);
        if (entities.Count > 0 && ContainsAny(normalized, MetricWords))
        {
            scores["symbol_metric_lookup"] = Math.Max(
                scores.TryGetValue("symbol_metric_lookup", out var existing) ? existing : 0m,
                0.88m);
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
            if (token.Length < 2 || NonEntityWords.Contains(token) || QueryNormalization.IsPresentationWord(token) || token.All(char.IsDigit))
                continue;
            if (!token.Any(character => char.IsLetter(character))) continue;
            var start = normalized.IndexOf(token, StringComparison.Ordinal);
            result.Add(new EntityMention(token, Math.Max(start, 0), token.Length));
        }
        return result.DistinctBy(item => item.Text, StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
    }

    private static PresentationPreference? DetectPresentation(string normalized) =>
        normalized.Contains("chart", StringComparison.OrdinalIgnoreCase) || normalized.Contains("چارت", StringComparison.Ordinal)
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
}

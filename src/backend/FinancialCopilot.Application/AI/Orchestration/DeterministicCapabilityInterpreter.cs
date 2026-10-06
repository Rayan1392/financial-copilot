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
    private static readonly string[] MetricWords = ["p/e", "p/s", "eps", "roe", "roa", "revenue", "profit", "gross profit", "net profit", "income", "فروش", "درآمد", "سود", "قیمت", "نسبت"];
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
        // Keep conversational Persian glue words out of the company/entity set.
        // These are escaped deliberately because this source file contains legacy
        // mojibake literals in the surrounding keyword tables.
        NonEntityWords.UnionWith([
            "\u0648", "\u0631\u0648", "\u06a9\u0646\u0627\u0631", "\u0647\u0645",
            "\u0628\u0630\u0627\u0631", "\u0628\u0628\u06cc\u0646", "\u06a9\u062f\u0648\u0645", "\u0628\u0647\u062a\u0631\u0647",
            "\u0641\u0631\u0648\u0634", "\u0641\u0631\u0648\u062e\u062a\u0647", "\u0686\u0642\u062f\u0631", "\u0686\u0642\u062f\u0631\u0647",
            "\u062f\u0627\u0634\u062a", "\u062f\u0627\u0634\u062a\u0647", "\u0645\u0627\u0647\u0627\u0646\u0647", "\u0631\u0648\u0646\u062f",
            "\u0645\u062d\u0635\u0648\u0644", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a", "\u0634\u0631\u06a9\u062a", "\u0646\u0634\u0627\u0646",
            "\u0628\u062f\u0647", "\u0628\u0631\u0627\u06cc", "\u0627\u0632", "\u0628\u0647", "\u0631\u0627", "\u0627\u0633\u062a",
            "\u0628\u0648\u062f", "\u062f\u0627\u0631\u062f", "\u0686\u06cc\u0647", "\u0686\u06cc", "\u06a9\u0646", "\u06a9\u0646\u06cc\u062f",
            "\u06a9\u062f\u0627\u0645", "\u062a\u062d\u0644\u06cc\u0644", "\u0628\u0631\u0631\u0633\u06cc", "\u062a\u0631\u06a9\u06cc\u0628", "\u0628\u06cc\u0634\u062a\u0631\u06cc\u0646"
            , "\u062e\u0648\u062f", "\u062f\u0631", "\u0628\u0627"
        ]);
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
        var entities = ExtractEntities(original, normalized).ToList();
        var isProductRevenueComposition =
            ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery(original) ||
            ProductSemanticIntentRules.LooksLikeProductRevenueComposition(original) ||
            ProductSemanticIntentRules.LooksLikeProductRevenueComposition(normalized) ||
            normalized.Contains("\u062a\u0631\u06a9\u06cc\u0628", StringComparison.Ordinal) &&
            normalized.Contains("\u0641\u0631\u0648\u0634", StringComparison.Ordinal) &&
            normalized.Contains("\u0645\u062d\u0635\u0648\u0644", StringComparison.Ordinal);
        var productMention = isProductRevenueComposition
            ? null
            : ProductSemanticIntentRules.ExtractProductMention(original);
        if (productMention is not null)
        {
            var productParts = productMention
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(ProductSemanticIntentRules.NormalizeProductSurface)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            entities.RemoveAll(entity =>
            {
                var normalizedEntity = ProductSemanticIntentRules.NormalizeProductSurface(entity.Text);
                return normalizedEntity.Equals(productMention, StringComparison.OrdinalIgnoreCase) ||
                       productParts.Contains(normalizedEntity);
            });
            var productStart = normalized.IndexOf(productMention, StringComparison.OrdinalIgnoreCase);
            entities.Add(new EntityMention(
                productMention,
                Math.Max(productStart, 0),
                productMention.Length,
                QueryValueProvenance.UserExplicit,
                "product",
                "product"));

            if (ProductSemanticIntentRules.LooksLikeProductSalesTrend(original))
            {
                scores["product_sales_trend"] = Math.Max(scores.GetValueOrDefault("product_sales_trend"), 0.99m);
                evidence.Add(new InterpretationEvidence("product_sales_trend", "product-sales-trend", QueryValueProvenance.UserExplicit));
            }
            else if (ProductSemanticIntentRules.LooksLikeProductSalesValue(original))
            {
                scores["product_sales_value"] = Math.Max(scores.GetValueOrDefault("product_sales_value"), 0.99m);
                evidence.Add(new InterpretationEvidence("product_sales_value", "product-sales-value", QueryValueProvenance.UserExplicit));
            }
        }
        else if (isProductRevenueComposition)
        {
            entities.RemoveAll(entity => ProductSemanticIntentRules.NormalizeProductSurface(entity.Text) is "محصول" or "محصولات");
            for (var entityIndex = entities.Count - 1; entityIndex >= 0; entityIndex--)
            {
                var entityText = ProductSemanticIntentRules.NormalizeProductSurface(entities[entityIndex].Text);
                var marker = entityText.StartsWith("\u0645\u062d\u0635\u0648\u0644\u0627\u062a ", StringComparison.OrdinalIgnoreCase)
                    ? "\u0645\u062d\u0635\u0648\u0644\u0627\u062a "
                    : entityText.StartsWith("\u0645\u062d\u0635\u0648\u0644 ", StringComparison.OrdinalIgnoreCase)
                        ? "\u0645\u062d\u0635\u0648\u0644 "
                        : null;
                if (marker is null)
                {
                    if (entityText is "\u0645\u062d\u0635\u0648\u0644" or "\u0645\u062d\u0635\u0648\u0644\u0627\u062a")
                        entities.RemoveAt(entityIndex);
                    continue;
                }

                var cleaned = entityText[marker.Length..].Trim();
                entities[entityIndex] = entities[entityIndex] with { Text = cleaned, Length = cleaned.Length };
            }
            scores["product_revenue_mix"] = Math.Max(scores.GetValueOrDefault("product_revenue_mix"), 0.99m);
            evidence.Add(new InterpretationEvidence("product_revenue_mix", "product-revenue-composition", QueryValueProvenance.UserExplicit));
        }
        entities.RemoveAll(entity => entity.Text.Contains("\u062e\u0648\u062f", StringComparison.Ordinal) ||
                                     entity.EntityType is not "product" &&
                                     (entity.Text.Equals("\u0645\u062d\u0635\u0648\u0644", StringComparison.Ordinal) ||
                                      entity.Text.Equals("\u0645\u062d\u0635\u0648\u0644\u0627\u062a", StringComparison.Ordinal)));
        for (var entityIndex = entities.Count - 1; entityIndex >= 0; entityIndex--)
        {
            if (entities[entityIndex].EntityType is "product")
                continue;
            var entityText = ProductSemanticIntentRules.NormalizeProductSurface(entities[entityIndex].Text);
            var marker = entityText.StartsWith("\u0645\u062d\u0635\u0648\u0644\u0627\u062a ", StringComparison.OrdinalIgnoreCase)
                ? "\u0645\u062d\u0635\u0648\u0644\u0627\u062a "
                : entityText.StartsWith("\u0645\u062d\u0635\u0648\u0644 ", StringComparison.OrdinalIgnoreCase)
                    ? "\u0645\u062d\u0635\u0648\u0644 "
                    : null;
            if (marker is not null)
            {
                var cleaned = entityText[marker.Length..].Trim();
                entities[entityIndex] = entities[entityIndex] with { Text = cleaned, Length = cleaned.Length };
            }
        }
        if (QueryNormalization.TryParseFinancialStatementClues(original, out _, out _) &&
            ContainsAny(normalized, MetricWords) &&
            !ContainsAny(normalized, ["below", "above", "under", "over", "زیر", "بالای", "کمتر از", "بیشتر از", "growth"]))
        {
            scores["financial_statement_value_search"] = Math.Max(scores.GetValueOrDefault("financial_statement_value_search"), 0.94m);
            evidence.Add(new InterpretationEvidence("financial_statement_value_search", "exact-value-identification", QueryValueProvenance.UserExplicit));
        }

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
        if ((ContainsAny(normalized, ["compare", "مقایسه", "\u06a9\u0646\u0627\u0631", "\u06a9\u062f\u0648\u0645"]) ||
             entities.Count > 1 && ContainsAny(normalized, ["\u06a9\u0646\u0627\u0631", "\u06a9\u062f\u0648\u0645", "\u0628\u0647\062a\u0631\u0647"])) &&
            HasPairConjunction(normalized))
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
            if (token.Length < 2 || NonEntityWords.Contains(token) || token is "\u062e\u0648\u062f" or "\u062f\u0631" or "\u0628\u0627" || QueryNormalization.IsEntityDistractor(token) || token.All(char.IsDigit))
                continue;
            if (!token.Any(character => char.IsLetter(character))) continue;
            var tokenForMarker = QueryNormalization.Normalize(token);
            var mention = tokenForMarker.StartsWith("\u0645\u062d\u0635\u0648\u0644\u0627\u062a ", StringComparison.OrdinalIgnoreCase)
                ? tokenForMarker["\u0645\u062d\u0635\u0648\u0644\u0627\u062a ".Length..].Trim()
                : tokenForMarker.StartsWith("\u0645\u062d\u0635\u0648\u0644 ", StringComparison.OrdinalIgnoreCase)
                    ? tokenForMarker["\u0645\u062d\u0635\u0648\u0644 ".Length..].Trim()
                    : token;
            var start = normalized.IndexOf(mention, StringComparison.Ordinal);
            result.Add(new EntityMention(mention, Math.Max(start, 0), mention.Length));
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
            .Any(token => token.Equals("and", StringComparison.OrdinalIgnoreCase) ||
                         token is "و" or "با" or "\u0648" or "\u0628\u0627");
}

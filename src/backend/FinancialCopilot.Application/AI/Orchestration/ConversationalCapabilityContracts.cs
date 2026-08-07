namespace FinancialCopilot.Application.AI.Orchestration;

public enum QueryValueProvenance
{
    UserExplicit,
    ConversationInferred,
    PolicyDefaulted,
    ModelProposed
}

public enum PresentationKind
{
    Table,
    Chart,
    Gauge,
    Summary,
    List
}

public sealed record LocalizedAlias(string Language, string Value);

public sealed record LocalizedExample(string Language, string Text);

public sealed record SlotDefinition(
    string Name,
    string ValueType,
    bool Required,
    IReadOnlyCollection<string> AllowedValues,
    string? DefaultValue = null);

public sealed record SuggestionPolicy(
    bool IncludeInGuidance,
    int MaxSuggestions = 3);

public sealed record CapabilityDefinition(
    string Code,
    int Version,
    bool Enabled,
    IReadOnlyList<LocalizedAlias> Aliases,
    IReadOnlyList<LocalizedExample> Examples,
    IReadOnlyList<SlotDefinition> RequiredSlots,
    IReadOnlyList<SlotDefinition> OptionalSlots,
    string ExecutionRoute,
    string OutputType,
    IReadOnlyList<string> DataRequirements,
    string PrecedenceGroup,
    SuggestionPolicy SuggestionPolicy);

public static class CapabilityExecutionRoutes
{
    public const string Scanner = "scanner";
    public const string SymbolLookup = "symbol_lookup";
    public const string ComprehensiveAnalysis = "comprehensive_analysis";
    public const string MonthlyActivityTrend = "monthly_activity_trend";
    public const string ProductRevenueMix = "product_revenue_mix";
    public const string FinancialStatementTable = "financial_statement_table";
    public const string FinancialStatementPeriodAnalysis = "financial_statement_period_analysis";
    public const string DisclosureListing = "disclosure_listing";
    public const string MonthlySalesQualityRanking = "monthly_sales_quality_ranking";
    public const string PsGaugeVisualization = "ps_gauge_visualization";
    public const string PersonalizedInsightExplanation = "personalized_insight_explanation";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Scanner, SymbolLookup, ComprehensiveAnalysis, MonthlyActivityTrend, ProductRevenueMix,
        FinancialStatementTable, FinancialStatementPeriodAnalysis, DisclosureListing,
        MonthlySalesQualityRanking, PsGaugeVisualization, PersonalizedInsightExplanation
    };
}

public interface IConversationalCapabilityRegistry
{
    int Version { get; }

    IReadOnlyCollection<CapabilityDefinition> GetAll();

    IReadOnlyCollection<CapabilityDefinition> GetEnabled();

    CapabilityDefinition? Find(string code);
}

public sealed class ConversationalCapabilityRegistry : IConversationalCapabilityRegistry
{
    private readonly IReadOnlyDictionary<string, CapabilityDefinition> definitions;

    public ConversationalCapabilityRegistry(
        IReadOnlyCollection<CapabilityDefinition> definitions,
        int version = 1)
    {
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));

        Validate(definitions);
        Version = version;
        this.definitions = definitions.ToDictionary(item => item.Code, StringComparer.Ordinal);
    }

    public int Version { get; }

    public IReadOnlyCollection<CapabilityDefinition> GetAll() => definitions.Values.ToArray();

    public IReadOnlyCollection<CapabilityDefinition> GetEnabled() =>
        definitions.Values.Where(item => item.Enabled).ToArray();

    public CapabilityDefinition? Find(string code) =>
        definitions.TryGetValue(code, out var definition) ? definition : null;

    public static void Validate(IReadOnlyCollection<CapabilityDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var duplicateCodes = definitions
            .GroupBy(item => item.Code, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateCodes.Length > 0)
            throw new InvalidOperationException($"Duplicate capability codes: {string.Join(", ", duplicateCodes)}.");

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Code) || definition.Code.Any(char.IsWhiteSpace))
                throw new InvalidOperationException("Capability codes must be non-empty and whitespace-free.");
            if (definition.Version < 1)
                throw new InvalidOperationException($"Capability '{definition.Code}' has an invalid version.");
            if (!CapabilityExecutionRoutes.All.Contains(definition.ExecutionRoute))
                throw new InvalidOperationException($"Capability '{definition.Code}' references unknown route '{definition.ExecutionRoute}'.");
            if (string.IsNullOrWhiteSpace(definition.OutputType) || string.IsNullOrWhiteSpace(definition.PrecedenceGroup))
                throw new InvalidOperationException($"Capability '{definition.Code}' is missing output or precedence metadata.");
            if (definition.Aliases.Count == 0 || definition.Examples.Count == 0)
                throw new InvalidOperationException($"Capability '{definition.Code}' must define aliases and examples.");

            foreach (var localized in definition.Aliases)
            {
                if (localized.Language is not ("fa" or "en") || string.IsNullOrWhiteSpace(localized.Value))
                    throw new InvalidOperationException($"Capability '{definition.Code}' contains incomplete localization metadata.");
                var aliasKey = $"{localized.Language}:{localized.Value.Trim()}";
                if (!aliases.Add(aliasKey))
                    throw new InvalidOperationException($"Duplicate localized capability alias/example '{aliasKey}'.");
            }

            foreach (var example in definition.Examples)
            {
                if (example.Language is not ("fa" or "en") || string.IsNullOrWhiteSpace(example.Text))
                    throw new InvalidOperationException($"Capability '{definition.Code}' contains incomplete localization metadata.");
            }

            var slotNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in definition.RequiredSlots.Concat(definition.OptionalSlots))
            {
                if (string.IsNullOrWhiteSpace(slot.Name) || !slotNames.Add(slot.Name))
                    throw new InvalidOperationException($"Capability '{definition.Code}' has duplicate or empty slot names.");
                if (slot.Required != definition.RequiredSlots.Contains(slot))
                    throw new InvalidOperationException($"Capability '{definition.Code}' has inconsistent required-slot metadata.");
            }
        }
    }
}

public static class InitialConversationalCapabilityCatalog
{
    public static IReadOnlyCollection<CapabilityDefinition> Create() =>
    [
        Definition("stock_screening", CapabilityExecutionRoutes.Scanner, "table", "screening",
            [Alias("en", "screen stocks"), Alias("fa", "فیلتر سهام")],
            [Example("en", "stocks with P/E below 5"), Example("fa", "سهام با P/E زیر ۵")],
            [Slot("conditions", "condition-list", true)], []),
        Definition("symbol_metric_lookup", CapabilityExecutionRoutes.SymbolLookup, "table", "symbol-query",
            [Alias("en", "symbol metrics"), Alias("fa", "شاخص‌های نماد")],
            [Example("en", "show فولاد P/E"), Example("fa", "P/E فولاد چقدر است؟")],
            [Slot("symbol", "symbol", true), Slot("metric", "metric", true)],
            [Slot("period", "period", false)]),
        Definition("comprehensive_analysis", CapabilityExecutionRoutes.ComprehensiveAnalysis, "summary", "analysis",
            [Alias("en", "stock analysis"), Alias("fa", "تحلیل سهم")],
            [Example("en", "analyze فولاد"), Example("fa", "فولاد را بررسی کن")],
            [Slot("symbol", "symbol", true)], [Slot("topic", "topic", false), Slot("period", "period", false)]),
        Definition("monthly_activity_trend", CapabilityExecutionRoutes.MonthlyActivityTrend, "chart", "trend",
            [Alias("en", "monthly sales trend"), Alias("fa", "روند فروش ماهانه")],
            [Example("en", "chart monthly sales for فولاد"), Example("fa", "چارت روند فروش فولاد")],
            [Slot("symbol", "symbol", true)], [Slot("period", "period", false), Slot("presentation", "presentation", false)]),
        Definition("product_revenue_mix", CapabilityExecutionRoutes.ProductRevenueMix, "table", "product",
            [Alias("en", "product revenue mix"), Alias("fa", "ترکیب فروش محصولات")],
            [Example("en", "product mix for فولاد"), Example("fa", "ترکیب فروش محصولات فولاد")],
            [Slot("symbol", "symbol", true)], []),
        Definition("financial_statement_table", CapabilityExecutionRoutes.FinancialStatementTable, "table", "statement",
            [Alias("en", "financial statement table"), Alias("fa", "جدول صورت مالی")],
            [Example("en", "show فولاد income statement"), Example("fa", "جدول صورت سود و زیان فولاد")],
            [Slot("symbol", "symbol", true)], [Slot("period", "period", false)]),
        Definition("financial_statement_period_analysis", CapabilityExecutionRoutes.FinancialStatementPeriodAnalysis, "summary", "statement",
            [Alias("en", "financial statement analysis"), Alias("fa", "تحلیل صورت مالی")],
            [Example("en", "analyze فولاد financial statements"), Example("fa", "تحلیل صورت مالی فولاد")],
            [Slot("symbol", "symbol", true)], [Slot("period", "period", false)]),
        Definition("disclosure_listing", CapabilityExecutionRoutes.DisclosureListing, "list", "disclosure",
            [Alias("en", "company disclosures"), Alias("fa", "اطلاعیه‌های شرکت")],
            [Example("en", "latest disclosures for فولاد"), Example("fa", "آخرین اطلاعیه‌های فولاد")],
            [], [Slot("symbol", "symbol", false), Slot("period", "period", false)]),
        Definition("monthly_sales_quality_ranking", CapabilityExecutionRoutes.MonthlySalesQualityRanking, "table", "ranking",
            [Alias("en", "monthly sales ranking"), Alias("fa", "رتبه‌بندی کیفیت فروش ماهانه")],
            [Example("en", "rank monthly sales quality"), Example("fa", "رتبه‌بندی کیفیت فروش ماهانه")],
            [], [Slot("industry", "industry", false), Slot("period", "period", false)]),
        Definition("ps_gauge_visualization", CapabilityExecutionRoutes.PsGaugeVisualization, "gauge", "valuation",
            [Alias("en", "P/S gauge"), Alias("fa", "گیج P/S")],
            [Example("en", "show the P/S gauge for فولاد"), Example("fa", "گیج P/S فولاد")],
            [Slot("symbol", "symbol", true)], [Slot("presentation", "presentation", false)]),
        Definition("personalized_insight_explanation", CapabilityExecutionRoutes.PersonalizedInsightExplanation, "summary", "insight",
            [Alias("en", "explain this alert"), Alias("fa", "این هشدار را توضیح بده")],
            [Example("en", "explain this alert"), Example("fa", "این هشدار را توضیح بده")],
            [Slot("insight", "insight", true)], [])
    ];

    private static CapabilityDefinition Definition(
        string code,
        string route,
        string output,
        string precedence,
        IReadOnlyList<LocalizedAlias> aliases,
        IReadOnlyList<LocalizedExample> examples,
        IReadOnlyList<SlotDefinition> required,
        IReadOnlyList<SlotDefinition> optional) =>
        new(code, 1, true, aliases, examples, required, optional, route, output, [], precedence, new SuggestionPolicy(true));

    private static LocalizedAlias Alias(string language, string value) => new(language, value);

    private static LocalizedExample Example(string language, string value) => new(language, value);

    private static SlotDefinition Slot(string name, string type, bool required) =>
        new(name, type, required, []);
}

public sealed record CapabilityCandidate(
    string CapabilityCode,
    int RegistryVersion,
    decimal Confidence,
    IReadOnlyCollection<InterpretationEvidence> Evidence);

public sealed record EntityMention(
    string Text,
    int Start,
    int Length,
    QueryValueProvenance Provenance = QueryValueProvenance.UserExplicit);

public sealed record MetricSelection(
    string MetricCode,
    string? UserText,
    QueryValueProvenance Provenance);

public sealed record PeriodSelection(string Value, QueryValueProvenance Provenance);

public sealed record ComparisonSelection(string Value, QueryValueProvenance Provenance);

public sealed record PresentationPreference(PresentationKind Kind, QueryValueProvenance Provenance);

public sealed record InterpretationEvidence(string Category, string Value, QueryValueProvenance Provenance);

public sealed record QueryInterpretation(
    string OriginalText,
    string NormalizedText,
    string ReplyLanguage,
    IReadOnlyList<CapabilityCandidate> CapabilityCandidates,
    IReadOnlyList<EntityMention> EntityMentions,
    IReadOnlyList<MetricSelection> Metrics,
    PeriodSelection? Period,
    ComparisonSelection? Comparison,
    PresentationPreference? Presentation,
    IReadOnlyList<string> MissingSlots,
    IReadOnlyList<string> UnsupportedParts,
    decimal Confidence,
    IReadOnlyList<InterpretationEvidence> Evidence,
    int RegistryVersion,
    InterpretationConfidenceBand ConfidenceBand = InterpretationConfidenceBand.Low);

public sealed class QueryInterpretationValidator(IConversationalCapabilityRegistry registry)
{
    public void Validate(QueryInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(interpretation);
        if (interpretation.OriginalText.Length > 4000 || interpretation.NormalizedText.Length > 4000)
            throw new InvalidOperationException("Query interpretation text exceeds the allowed limit.");
        if (interpretation.ReplyLanguage is not ("fa" or "en"))
            throw new InvalidOperationException("Query interpretation language is invalid.");
        if (interpretation.RegistryVersion != registry.Version)
            throw new InvalidOperationException("Query interpretation registry version is not current.");
        if (interpretation.CapabilityCandidates.Count > 20 || interpretation.EntityMentions.Count > 20 || interpretation.Metrics.Count > 20)
            throw new InvalidOperationException("Query interpretation collection exceeds the allowed limit.");
        if (interpretation.Confidence is < 0 or > 1)
            throw new InvalidOperationException("Query interpretation confidence must be between 0 and 1.");
        if (interpretation.ConfidenceBand != InterpretationConfidencePolicy.Band(interpretation.Confidence))
            throw new InvalidOperationException("Query interpretation confidence band is inconsistent.");

        foreach (var candidate in interpretation.CapabilityCandidates)
        {
            if (registry.Find(candidate.CapabilityCode) is not { Enabled: true } ||
                candidate.RegistryVersion != registry.Version || candidate.Confidence is < 0 or > 1)
                throw new InvalidOperationException($"Query interpretation contains an invalid capability candidate '{candidate.CapabilityCode}'.");
        }

        foreach (var metric in interpretation.Metrics)
        {
            if (string.IsNullOrWhiteSpace(metric.MetricCode))
                throw new InvalidOperationException("Query interpretation contains an empty metric code.");
        }
    }
}

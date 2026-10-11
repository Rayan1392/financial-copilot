using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FinancialCopilot.Application.FinancialData.Semantics.Intent;

public sealed record CanonicalBusinessMeasureConcept(
    string Id,
    string DisplayName,
    BusinessMeasureDimension Dimension,
    MeasureConceptKind Kind,
    IReadOnlyList<string> ReviewedPersianTerms,
    SemanticUnit? ImpliedRequestedUnit = null);

public sealed record MeasureConceptResolution(
    MeasureCanonicalizationStatus Status,
    string? ConceptId,
    IReadOnlyList<string> CandidateConceptIds,
    BusinessMeasureDimension Dimension,
    MeasureConceptKind Kind,
    SemanticUnit? ImpliedRequestedUnit);

/// <summary>
/// Governed business meaning only. Definitions intentionally have no MetricCode, catalog ID,
/// SourceItemId, provider, table, column, or availability/authorization field.
/// </summary>
public sealed class CanonicalBusinessMeasureConceptRegistry
{
    private readonly IReadOnlyDictionary<string, CanonicalBusinessMeasureConcept> _concepts;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _terms;
    private readonly IReadOnlyDictionary<string, SemanticUnit> _termImpliedUnits;

    public const string ProductionQuantity = "productionQuantity";
    public const string SalesQuantity = "salesQuantity";
    public const string SalesAmount = "salesAmount";
    public const string SalesRate = "salesRate";
    public const string OperatingProfit = "operatingProfit";
    public const string OperatingProfitPerShare = "operatingProfitPerShare";
    public const string NetProfit = "netProfit";
    public const string EarningsPerShare = "earningsPerShare";
    public const string CashBalance = "cashBalance";
    public const string DepreciationExpense = "depreciationExpense";
    public const string NetProfitMargin = "netProfitMargin";
    public const string GrossProfitMargin = "grossProfitMargin";
    public const string PriceEarningsRatio = "priceEarningsRatio";
    public const string LatestPrice = "latestPrice";

    public static CanonicalBusinessMeasureConceptRegistry Default { get; } = new();

    public IReadOnlyCollection<CanonicalBusinessMeasureConcept> Concepts => _concepts.Values.ToArray();

    public CanonicalBusinessMeasureConceptRegistry()
    {
        var concepts = new[]
        {
            Define(ProductionQuantity, "Production quantity", BusinessMeasureDimension.PhysicalQuantity, MeasureConceptKind.ProductionQuantity,
                "مقدار تولید", "تولید", "production quantity"),
            Define(SalesQuantity, "Sales quantity", BusinessMeasureDimension.PhysicalQuantity, MeasureConceptKind.SalesQuantity,
                "مقدار فروش", "مقدار فروش ماهانه", "sales quantity"),
            Define(SalesAmount, "Sales amount", BusinessMeasureDimension.CurrencyAmount, MeasureConceptKind.SalesAmount,
                "مبلغ فروش", "مبالغ فروش", "مبلغ فروش ماهانه", "فروش ریالی", "مبلغ فروش ریالی", "sales amount", "monthly sales amount"),
            Define(SalesRate, "Sales rate", BusinessMeasureDimension.CurrencyPerUnit, MeasureConceptKind.SalesRate,
                "نرخ فروش", "نرخ فروش ماهانه", "sales rate"),
            Define(OperatingProfit, "Operating profit", BusinessMeasureDimension.CurrencyAmount, MeasureConceptKind.FinancialStatementItem,
                "سود عملیاتی", "operating profit"),
            Define(OperatingProfitPerShare, "Operating profit per share", BusinessMeasureDimension.CurrencyPerShare, MeasureConceptKind.FinancialStatementItem,
                "سود عملیاتی هر سهم", "سود عملیاتی به ازای هر سهم", "operating profit per share", impliedUnit: SemanticUnit.IRRPerShare),
            Define(NetProfit, "Net profit", BusinessMeasureDimension.CurrencyAmount, MeasureConceptKind.FinancialStatementItem,
                "سود خالص", "سود خالص کل", "net profit"),
            Define(EarningsPerShare, "Earnings per share", BusinessMeasureDimension.CurrencyPerShare, MeasureConceptKind.FinancialStatementItem,
                "سود هر سهم", "سود خالص هر سهم", "eps", "earnings per share", impliedUnit: SemanticUnit.IRRPerShare),
            Define(CashBalance, "Cash balance", BusinessMeasureDimension.CurrencyAmount, MeasureConceptKind.FinancialStatementItem,
                "موجودی نقد", "موجودی وجه نقد", "وجه نقد", "مانده وجه نقد", "cash balance"),
            Define(DepreciationExpense, "Depreciation expense", BusinessMeasureDimension.CurrencyAmount, MeasureConceptKind.FinancialStatementItem,
                "هزینه استهلاک", "depreciation expense"),
            Define(NetProfitMargin, "Net profit margin", BusinessMeasureDimension.Percentage, MeasureConceptKind.ProviderReportedIndicator,
                "حاشیه سود خالص", "net profit margin"),
            Define(GrossProfitMargin, "Gross profit margin", BusinessMeasureDimension.Percentage, MeasureConceptKind.ProviderReportedIndicator,
                "حاشیه سود ناخالص", "gross profit margin"),
            Define(PriceEarningsRatio, "Price-to-earnings ratio", BusinessMeasureDimension.Ratio, MeasureConceptKind.ProviderReportedIndicator,
                "نسبت قیمت به درآمد", "نسبت پی به ای", "p/e", "نسبت p/e", "pe ratio", "price earnings ratio"),
            Define(LatestPrice, "Latest observed price", BusinessMeasureDimension.CurrencyAmount, MeasureConceptKind.ProviderReportedIndicator,
                "قیمت پایانی", "قیمت پایانی سهم", "آخرین قیمت", "closing price", "latest price")
        };
        _concepts = concepts.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        var termMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var concept in concepts)
        foreach (var term in concept.ReviewedPersianTerms)
        {
            var normalized = NormalizeTerm(term);
            if (!termMap.TryGetValue(normalized, out var ids)) termMap[normalized] = ids = [];
            if (!ids.Contains(concept.Id, StringComparer.OrdinalIgnoreCase)) ids.Add(concept.Id);
        }

        // Bare "فروش" intentionally remains ambiguous between physical quantity and amount.
        termMap[NormalizeTerm("فروش")] = [SalesAmount, SalesQuantity];
        _terms = termMap.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        _termImpliedUnits = new Dictionary<string, SemanticUnit>(StringComparer.Ordinal)
        {
            [NormalizeTerm("فروش ریالی")] = SemanticUnit.IRR,
            [NormalizeTerm("مبلغ فروش ریالی")] = SemanticUnit.IRR
        };
    }

    public bool TryGet(string? conceptId, out CanonicalBusinessMeasureConcept concept)
    {
        if (conceptId is not null && _concepts.TryGetValue(conceptId, out concept!)) return true;
        concept = null!;
        return false;
    }

    public MeasureConceptResolution ResolveReviewedTerm(string? phrase)
    {
        var normalized = NormalizeTerm(phrase ?? string.Empty);
        if (!_terms.TryGetValue(normalized, out var ids))
            return new(MeasureCanonicalizationStatus.Unrecognized, null, [], BusinessMeasureDimension.Unknown,
                MeasureConceptKind.OtherBusinessMeasure, null);
        if (ids.Count != 1)
            return new(MeasureCanonicalizationStatus.Ambiguous, null, ids, BusinessMeasureDimension.Unknown,
                MeasureConceptKind.OtherBusinessMeasure, null);
        var concept = _concepts[ids[0]];
        return new(MeasureCanonicalizationStatus.Canonicalized, concept.Id, [concept.Id], concept.Dimension,
            concept.Kind, _termImpliedUnits.TryGetValue(normalized, out var impliedUnit) ? impliedUnit : concept.ImpliedRequestedUnit);
    }

    public static string NormalizeTerm(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant().Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u0640', ' ')
            .Replace('\u200c', ' ').Replace('\u200f', ' ').Replace('\u200e', ' ');
        var builder = new StringBuilder(normalized.Length);
        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            if (Rune.IsLetterOrDigit(rune)) builder.Append(rune.ToString());
            else if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
        }
        return Regex.Replace(builder.ToString().Trim(), @"\s+", " ", RegexOptions.CultureInvariant);
    }

    private static CanonicalBusinessMeasureConcept Define(string id, string displayName, BusinessMeasureDimension dimension,
        MeasureConceptKind kind, params string[] terms) => new(id, displayName, dimension, kind, terms);

    private static CanonicalBusinessMeasureConcept Define(string id, string displayName, BusinessMeasureDimension dimension,
        MeasureConceptKind kind, string term1, string term2, string term3, SemanticUnit impliedUnit) =>
        new(id, displayName, dimension, kind, [term1, term2, term3], impliedUnit);

    private static CanonicalBusinessMeasureConcept Define(string id, string displayName, BusinessMeasureDimension dimension,
        MeasureConceptKind kind, string term1, string term2, string term3, string term4, SemanticUnit impliedUnit) =>
        new(id, displayName, dimension, kind, [term1, term2, term3, term4], impliedUnit);
}

/// <summary>Maps only exact reviewed measure phrases; unknown phrases survive unchanged.</summary>
public sealed class CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry registry)
{
    public CanonicalSemanticIntent Canonicalize(CanonicalSemanticIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var productLabels = (intent.Products ?? []).Select(product => CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(product.UserLabel))
            .Where(label => label.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var measures = (intent.Measures ?? []).Select(measure => Canonicalize(measure, productLabels)).ToArray();
        var populationComparison = intent.Operation?.Kind == AnalyticalOperationKind.Compare
            && intent.EntityScope?.Kind == EntityScopeKind.UnboundCompanyPopulation
            && intent.Output?.Kind == RequestedOutputKind.Table
            && intent.Comparison?.Mode is (ComparisonMode.MonthOverMonth or ComparisonMode.YearOverYear or ComparisonMode.PeriodOverPeriod)
            && (intent.Constraints ?? []).Any(constraint =>
                string.Equals(constraint.Name, "comparisonCondition", StringComparison.OrdinalIgnoreCase));
        var operation = populationComparison ? new AnalyticalOperation(AnalyticalOperationKind.DiscoverPopulation) : intent.Operation;
        var canonicalByPhrase = measures.Where(measure => measure.ConceptId is not null)
            .GroupBy(measure => CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(measure.UserPhrase), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ConceptId!, StringComparer.Ordinal);
        string ResolveReference(string reference)
        {
            if (registry.TryGet(reference, out var concept)) return concept.Id;
            var normalized = CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(reference);
            return canonicalByPhrase.GetValueOrDefault(normalized, reference);
        }

        FilterExpression? CanonicalizeFilter(FilterExpression? filter) => filter switch
        {
            MeasureThresholdFilter threshold => threshold with { MeasureConcept = ResolveReference(threshold.MeasureConcept) },
            FilterGroup group => group with { Children = (group.Children ?? []).Select(child => CanonicalizeFilter(child)!).ToArray() },
            _ => filter
        };
        var ambiguity = intent.Ambiguity;
        var ambiguous = measures.Where(item => item.CanonicalizationStatus == MeasureCanonicalizationStatus.Ambiguous).ToArray();
        if (ambiguous.Length > 0)
        {
            var questions = (ambiguity?.Questions ?? []).ToList();
            foreach (var measure in ambiguous)
                questions.Add($"منظورتان از «{measure.UserPhrase}» مبلغ فروش است یا مقدار فروش؟");
            ambiguity = new AmbiguityAssessment(true, questions.Distinct(StringComparer.Ordinal).ToArray(),
                (ambiguity?.UnresolvedPoints ?? []).Append("ambiguous_reviewed_measure_term").Distinct(StringComparer.Ordinal).ToArray());
        }

        return intent with
        {
            Measures = measures,
            Operation = operation!,
            Filters = CanonicalizeFilter(intent.Filters),
            Ranking = intent.Ranking is null ? null : intent.Ranking with { MeasureConcept = ResolveReference(intent.Ranking.MeasureConcept) },
            Ambiguity = ambiguity!
        };
    }

    private RequestedMeasure Canonicalize(RequestedMeasure measure, IReadOnlyList<string> productLabels)
    {
        var resolutionPhrase = CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(measure.UserPhrase);
        foreach (var productLabel in productLabels)
        {
            resolutionPhrase = Regex.Replace(resolutionPhrase,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(productLabel)}(?![\p{{L}}\p{{N}}])", " ",
                RegexOptions.CultureInvariant);
        }
        resolutionPhrase = RemoveExplicitQualifierTerms(resolutionPhrase, measure.Qualifiers);
        resolutionPhrase = Regex.Replace(resolutionPhrase, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        var resolution = registry.ResolveReviewedTerm(resolutionPhrase);
        if (resolution.Status != MeasureCanonicalizationStatus.Canonicalized)
            return measure with
            {
                ConceptId = null,
                CanonicalizationStatus = resolution.Status,
                CandidateConceptIds = resolution.CandidateConceptIds,
                Dimension = BusinessMeasureDimension.Unknown
            };

        var requestedUnit = measure.Qualifiers?.ValueForm == MeasureValueForm.PercentageChange
            ? SemanticUnit.Percent
            : measure.RequestedUnit == SemanticUnit.Unspecified && resolution.ImpliedRequestedUnit.HasValue
                ? resolution.ImpliedRequestedUnit.Value
                : measure.RequestedUnit;
        return measure with
        {
            ConceptId = resolution.ConceptId,
            CanonicalizationStatus = resolution.Status,
            CandidateConceptIds = resolution.CandidateConceptIds,
            Kind = resolution.Kind,
            Dimension = resolution.Dimension,
            RequestedUnit = requestedUnit,
        };
    }

    private static string RemoveExplicitQualifierTerms(string phrase, MeasureQualifiers? qualifiers)
    {
        if (qualifiers is null) return phrase;
        var terms = new List<string>();
        if (qualifiers.ValueForm == MeasureValueForm.PercentageChange)
            terms.AddRange(["رشد", "افزایش", "کاهش", "درصد تغییر", "تغییر درصدی", "درصدی", "درصد"]);
        if (qualifiers.StatementScope == FinancialStatementScope.Standalone)
            terms.AddRange(["جداگانه", "مستقل", "standalone"]);
        if (qualifiers.StatementScope == FinancialStatementScope.Consolidated)
            terms.AddRange(["تلفیقی", "consolidated"]);
        if (qualifiers.ReportingBasis is MeasureReportingBasis.MonthlyDiscrete or MeasureReportingBasis.MonthlyCumulative)
            terms.AddRange(["ماهانه", "monthly"]);
        if (qualifiers.ReportingBasis == MeasureReportingBasis.MonthlyCumulative)
            terms.AddRange(["تجمعی", "cumulative"]);
        if (qualifiers.ReportingBasis is MeasureReportingBasis.QuarterlyDiscrete or MeasureReportingBasis.QuarterlyCumulative)
            terms.AddRange(["فصلی", "quarterly"]);
        if (qualifiers.ReportingBasis == MeasureReportingBasis.Annual)
            terms.AddRange(["سالانه", "annual"]);

        foreach (var term in terms)
            phrase = Regex.Replace(phrase,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(term))}(?![\p{{L}}\p{{N}}])",
                " ", RegexOptions.CultureInvariant);
        return Regex.Replace(phrase, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }
}

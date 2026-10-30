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
        // A canonical measure repeated with identical qualifiers and units (observed live once per compared
        // company) is one measure; entries that differ in any qualifier, such as two statement scopes, are kept.
        var measures = (intent.Measures ?? []).Select(measure => Canonicalize(measure, productLabels))
            .DistinctBy(measure => measure.ConceptId is null
                ? (object)measure
                : (measure.ConceptId, measure.Kind, measure.Dimension, measure.Qualifiers, measure.RequestedUnit,
                    measure.EffectiveSourceUnit, measure.EffectiveSourceUnitEvidence))
            .ToArray();
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

        // A threshold stated in rials on a per-share measure ("EPS بالای ۲۰۰۰ ریال") is rials per share, the
        // reviewed implied unit of per-share terms; the magnitude is unchanged. Every other unit is left as stated.
        MeasureThresholdFilter CanonicalizeThreshold(MeasureThresholdFilter threshold)
        {
            var concept = ResolveReference(threshold.MeasureConcept);
            var perShare = measures.Any(measure => measure.ConceptId == concept && measure.Dimension == BusinessMeasureDimension.CurrencyPerShare);
            return threshold with
            {
                MeasureConcept = concept,
                Threshold = perShare && threshold.Threshold is { Unit: SemanticUnit.IRR } value
                    ? value with { Unit = SemanticUnit.IRRPerShare } : threshold.Threshold
            };
        }

        FilterExpression? CanonicalizeFilter(FilterExpression? filter) => filter switch
        {
            MeasureThresholdFilter threshold => CanonicalizeThreshold(threshold),
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

        // A requested output field that only restates a requested measure (its phrase or its concept)
        // adds no semantics; models emit it inconsistently, so it is removed deterministically here.
        var measureTerms = measures.SelectMany(measure => new[] { CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(measure.UserPhrase), measure.ConceptId ?? string.Empty })
            .Where(term => term.Length > 0).ToHashSet(StringComparer.Ordinal);
        // Likewise a generic placeholder for the scalar result itself ("value", "مقدار") names no
        // additional column; it is removed so the retrieval slice is not rejected over it.
        // In a multi-company comparison or ranking the company identity is part of every result row, so a
        // requested "company"/"symbol" field, or a field that repeats a named company, restates the output
        // shape and entity scope; elsewhere such a field is preserved.
        var multiCompanyResult = intent.Output?.Kind is RequestedOutputKind.RankedList or RequestedOutputKind.Comparison
            || intent.Operation?.Kind is AnalyticalOperationKind.Compare or AnalyticalOperationKind.Rank
            || intent.EntityScope?.Kind == EntityScopeKind.UnboundCompanyPopulation
            || (intent.EntityScope?.CompanyLabels ?? []).Count > 1;
        // A ranking returns every row with its rank, so a requested "rank" field restates the output shape.
        var rankedResult = intent.Ranking is not null || intent.Output?.Kind == RequestedOutputKind.RankedList;
        var companyLabelTerms = (intent.EntityScope?.CompanyLabels ?? [])
            .Select(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm).Where(term => term.Length > 0).ToHashSet(StringComparer.Ordinal);
        var statementItemsOnly = measures.Length > 0 && measures.All(measure =>
            measure.CanonicalizationStatus == MeasureCanonicalizationStatus.Canonicalized
            && measure.Kind == MeasureConceptKind.FinancialStatementItem);
        var requestedFields = (intent.Output?.RequestedFields ?? [])
            .Where(field => !string.IsNullOrWhiteSpace(field)
                && !(multiCompanyResult && companyLabelTerms.Contains(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(field)))
                && !GenericValuePlaceholders.Contains(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(field))
                && !(statementItemsOnly && PeriodPlaceholders.Contains(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(field)))
                && !(multiCompanyResult && CompanyIdentityPlaceholders.Contains(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(field)))
                && !(rankedResult && RankPlaceholders.Contains(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(field)))
                && !measureTerms.Contains(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(field))
                && !measureTerms.Contains(ResolveReference(field)))
            .Distinct(StringComparer.Ordinal).ToArray();

        // A financial statement line item is the company total by definition (see the qualifier rule in
        // Canonicalize(measure)); an *inferred* constraint that only restates that grain carries no
        // semantics and is removed. Explicit constraints and every other constraint are preserved. The
        // model names it "valueGrain" or, per requested measure, "<conceptId>ValueGrain".
        var grainNames = measures.Select(measure => measure.ConceptId + "ValueGrain").Append("valueGrain").ToArray();
        var constraints = (intent.Constraints ?? []).Where(constraint => !(statementItemsOnly
                && constraint.Provenance == ConstraintProvenance.Inferred
                && grainNames.Contains(constraint.Name?.Trim(), StringComparer.OrdinalIgnoreCase)
                && string.Equals(constraint.Value?.Trim(), "companyTotal", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        // A statement basis stated as a "statementScope" constraint («بر اساس صورت‌های مالی جداگانه») is the measures'
        // own statementScope qualifier: it is applied where the qualifier is unspecified and the constraint removed. A
        // measure with a different explicit scope keeps the constraint, so the request is refused rather than merged.
        if (statementItemsOnly && StatedStatementScope(constraints) is { } statedScope
            && measures.All(measure => measure.Qualifiers.StatementScope is FinancialStatementScope.Unspecified
                || measure.Qualifiers.StatementScope == statedScope))
        {
            measures = measures.Select(measure => measure with
            {
                Qualifiers = measure.Qualifiers with { StatementScope = statedScope }
            }).ToArray();
            constraints = constraints.Where(constraint => StatementBasisOf(constraint) is null).ToArray();
        }
        // A statement period named by its end only («دوره شش‌ماهه منتهی به شهریور ۱۴۰۵») arrives either as an explicit
        // month or as a date range without a start; for statement items both mean the period ending in that month, so
        // the end-only range becomes that explicit month (monthly measures keep their range semantics untouched).
        var period = intent.Period;
        if (statementItemsOnly && period is { Kind: ReportingPeriodKind.DateRange } && string.IsNullOrWhiteSpace(period.StartLabel)
            && !string.IsNullOrWhiteSpace(period.EndLabel))
            period = period with { Kind = ReportingPeriodKind.ExplicitMonth, Granularity = ReportingPeriodGranularity.Month };
        // «در دوره مشترک» on a statement comparison of named companies restates the engine's documented policy (the
        // latest reporting period every named company reports), so the "commonPeriod" constraint is removed.
        if (statementItemsOnly && (intent.EntityScope?.CompanyLabels ?? []).Count > 1
            && intent.Period?.Kind is ReportingPeriodKind.Unspecified or ReportingPeriodKind.LatestAvailable)
            constraints = constraints.Where(constraint => !IsCommonPeriodConstraint(constraint)).ToArray();

        return intent with
        {
            Measures = measures,
            Period = period!,
            Constraints = constraints,
            Operation = operation!,
            Filters = CanonicalizeFilter(intent.Filters),
            Ranking = intent.Ranking is null ? null : intent.Ranking with { MeasureConcept = ResolveReference(intent.Ranking.MeasureConcept) },
            Output = intent.Output is null ? intent.Output! : intent.Output with { RequestedFields = requestedFields },
            Ambiguity = ambiguity!
        };
    }

    // Constraint names are model-written free text (observed for one question: "commonPeriod", "commonReportingPeriod",
    // "periodAlignment=commonPeriod", "دوره=مشترک"; "statementScope", "statementBasis"), so they are recognized by the
    // meaning of name and value together; a statement basis must be named by the value.
    private static bool IsStatementScopeConstraint(UserConstraint constraint)
    {
        var text = $"{constraint.Name} {constraint.Value}";
        return new[] { "scope", "basis", "consolidat", "statement", "صورت", "مبنا" }
            .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCommonPeriodConstraint(UserConstraint constraint)
    {
        var text = $"{constraint.Name} {constraint.Value}";
        return (text.Contains("common", StringComparison.OrdinalIgnoreCase) || text.Contains("مشترک", StringComparison.Ordinal))
            && new[] { "period", "alignment", "دوره" }.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The one basis named by the statement-basis constraints, or null when none or conflicting.</summary>
    private static FinancialStatementScope? StatedStatementScope(IEnumerable<UserConstraint> constraints)
    {
        var scopes = constraints.Select(StatementBasisOf).OfType<FinancialStatementScope>().Distinct().ToArray();
        return scopes.Length == 1 ? scopes[0] : null;
    }

    /// <summary>The statement basis a basis-named constraint states in its value; null for any other constraint.</summary>
    private static FinancialStatementScope? StatementBasisOf(UserConstraint constraint)
    {
        if (!IsStatementScopeConstraint(constraint)) return null;
        var value = CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(constraint.Value ?? string.Empty);
        return value.Contains("غیرتلفیقی", StringComparison.Ordinal) || value.Contains("غیر تلفیقی", StringComparison.Ordinal)
            || value.Contains("جداگانه", StringComparison.Ordinal) || value.Contains("مستقل", StringComparison.Ordinal)
            || value.Contains("standalone", StringComparison.OrdinalIgnoreCase) || value.Contains("separate", StringComparison.OrdinalIgnoreCase)
                ? FinancialStatementScope.Standalone
            : value.Contains("تلفیقی", StringComparison.Ordinal) || value.Contains("consolidated", StringComparison.OrdinalIgnoreCase)
                ? FinancialStatementScope.Consolidated
            : null;
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

        // Rials stated for a per-share measure are rials per share (same magnitude, no conversion).
        var requestedUnit = measure.Qualifiers?.ValueForm == MeasureValueForm.PercentageChange
            ? SemanticUnit.Percent
            : measure.RequestedUnit == SemanticUnit.Unspecified && resolution.ImpliedRequestedUnit.HasValue
                ? resolution.ImpliedRequestedUnit.Value
                : measure.RequestedUnit == SemanticUnit.IRR && resolution.Dimension == BusinessMeasureDimension.CurrencyPerShare
                    ? SemanticUnit.IRRPerShare
                    : measure.RequestedUnit;
        // A financial statement line item is by definition the company total; a model-emitted
        // CompanyTotal grain restates that and is normalized away so it cannot block the retrieval
        // slice. ProductLevel on a statement item remains a contradiction and is left for rejection.
        var currentQualifiers = measure.Qualifiers ?? new MeasureQualifiers();
        var qualifiers = resolution.Kind == MeasureConceptKind.FinancialStatementItem
            && currentQualifiers.ValueGrain == MeasureValueGrain.CompanyTotal
            ? currentQualifiers with { ValueGrain = MeasureValueGrain.Unspecified }
            : currentQualifiers;
        return measure with
        {
            ConceptId = resolution.ConceptId,
            CanonicalizationStatus = resolution.Status,
            CandidateConceptIds = resolution.CandidateConceptIds,
            Kind = resolution.Kind,
            Dimension = resolution.Dimension,
            RequestedUnit = requestedUnit,
            Qualifiers = qualifiers,
        };
    }

    private static readonly HashSet<string> GenericValuePlaceholders = new(StringComparer.Ordinal)
    {
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("value"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("amount"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("مقدار"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("ارزش"),
    };

    // Every statement-item answer states its reporting period, so a requested period field restates the output.
    private static readonly HashSet<string> PeriodPlaceholders = new(StringComparer.Ordinal)
    {
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("period"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("reportingPeriod"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("commonPeriod"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("دوره"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("دوره مشترک"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("دوره گزارش"),
    };

    private static readonly HashSet<string> RankPlaceholders = new(StringComparer.Ordinal)
    {
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("rank"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("ranking"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("رتبه"),
    };

    private static readonly HashSet<string> CompanyIdentityPlaceholders = new(StringComparer.Ordinal)
    {
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("company"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("companies"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("company name"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("companyName"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("companyLabel"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("companySymbol"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("symbol"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("ticker"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("شرکت"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("نام شرکت"),
        CanonicalBusinessMeasureConceptRegistry.NormalizeTerm("نماد"),
    };

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

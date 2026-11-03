namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public sealed record Feature141OperationDefinition(
    string Id,
    int Version,
    bool CompilerSupported,
    IReadOnlyList<string> RequiredArguments,
    string SupportBoundary);

/// <summary>
/// Versioned contract for the operation algebra shared by requirement normalization, plan validation,
/// and SQL compilation. This describes the current compiler surface; it does not authorize query execution.
/// </summary>
public static class Feature141OperationRegistry
{
    public const int Version = 1;

    public static IReadOnlyList<Feature141OperationDefinition> Definitions { get; } =
    [
        new("DirectValueRetrieval", Version, true, ["one catalog-mapped measure/source", "verified entity scope"],
            "Financial statements support direct source-item retrieval; monthly retrieval uses one governed measure."),
        new("TimeSeries", Version, true, ["MonthlyMeasure", "bounded reporting period", "time-series or chart presentation"],
            "Monthly measures only; reporting-period bounds are required."),
        new("AbsoluteChange", Version, true, ["MonthlyMeasure", "MonthOverMonth or YearOverYear", "comparison scope", "bounded baseline"],
            "Monthly reported values only; compiler requires approved comparison semantics and compatible periods."),
        new("GrowthPercentage", Version, true, ["MonthlyMeasure", "MonthOverMonth or YearOverYear", "comparison scope", "bounded baseline"],
            "Monthly reported values only; compiler requires an approved growth operation and compatible periods."),
        new("MonthOverMonth", Version, true, ["MonthlyMeasure", "at least two eligible periods", "period-over-period scope"],
            "Comparison mode for monthly AbsoluteChange or GrowthPercentage; not a standalone metric."),
        new("YearOverYear", Version, true, ["MonthlyMeasure", "at least thirteen eligible periods", "year-over-year scope"],
            "Comparison mode for monthly AbsoluteChange or GrowthPercentage; not a standalone metric."),
        new("Aggregation", Version, true, ["MonthlyMeasure", "catalog-matched Sum"],
            "Current monthly compiler supports governed Sum aggregation; statement aggregation is not supported."),
        new("Ranking", Version, true, ["measure-bound sort", "bounded result",
                "MonthlyMeasure: Sum aggregation; DirectSourceItem: latest common reporting period, no aggregation"],
            "Monthly ranking requires a governed Sum; statement-item ranking orders one approved source item across named companies or the authorized population with deterministic competition ranks for ties."),
        new("MultiCompanyComparison", Version, true, ["at least two authorized companies or an authorized population",
                "common reporting period", "MonthlyMeasure: compatible Sum; DirectSourceItem: same basis and unit"],
            "Monthly comparison requires a shared bounded period; statement-item comparison reads every company at the latest period all of them report, never mixes consolidation bases or units and never treats a missing value as zero."),
        new("PopulationDiscovery", Version, true, ["authorized discovery scope", "approved product concept", "positive monthly production observation", "reporting window"],
            "Current compiler supports product-driven production discovery; incomplete alias/company coverage remains partial."),
        new("ConditionalCompanyFiltering", Version, true, ["catalog-backed dimension", "typed predicate", "authorized company scope"],
            "Typed catalog-backed monthly filters are supported; compiler authorization and coverage checks remain authoritative."),
        new("UnsupportedDerivedFinancialMetric", Version, false, ["catalog-governed formula and approval"],
            "No generic derived primary metric is authorized by the current compiler."),
    ];

    /// <summary>
    /// Corrects a model's generic unsupported label only when its typed shape is a direct retrieval
    /// with exactly one catalog-selected reported reference and no comparison intent.
    /// </summary>
    public static (string Operation, string? Diagnostic) NormalizeRequirementOperation(
        QuestionSchemaRequirements requirements, string question)
    {
        var requested = requirements.RequestedFinancialValueOperation;
        if (!string.Equals(requested, nameof(FinancialValueOperation.UnsupportedDerivedFinancialMetric), StringComparison.Ordinal))
            return (requested, null);

        var shape = requirements.StructuredShape;
        var references = requirements.FinancialValueReferences ?? [];
        if (requirements.HasUnknownOrAmbiguousRequirements || references.Count != 1 || shape is null)
            return (requested, null);
        if (shape.ComparisonMode != PlanComparisonMode.None
            || requirements.ComparisonScopes.Count != 0
            || ContainsExplicitDerivedOrComparisonIntent(question)
            || shape.FinancialOperationKind is not (PlanFinancialOperationKind.DirectValue or PlanFinancialOperationKind.TimeSeries))
            return (requested, null);

        var referenceType = references[0].ReferenceType;
        if (!Enum.TryParse<FinancialValueReferenceType>(referenceType, out var parsed)
            || parsed is not (FinancialValueReferenceType.MonthlyMeasure
                or FinancialValueReferenceType.DirectSourceItem
                or FinancialValueReferenceType.GovernedMetric
                or FinancialValueReferenceType.ProviderFinancialIndicator))
            return (requested, null);

        return (nameof(FinancialValueOperation.DirectValueRetrieval),
            "normalized_unsupported_label_to_cataloged_direct_retrieval_from_typed_shape");
    }

    public static IReadOnlyList<string> ValidatePlan(StructuredQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var issues = new List<string>();
        var discovery = plan.CompanyDiscoveryScope is not null || plan.PopulationCoveragePolicyId is not null
            || plan.ProductConcept is not null;
        var operationIds = new HashSet<string>(StringComparer.Ordinal);

        if (discovery) operationIds.Add("PopulationDiscovery");
        if (plan.Filters.Count > 0) operationIds.Add("ConditionalCompanyFiltering");
        if (plan.Aggregations.Count > 0) operationIds.Add("Aggregation");
        if (plan.Presentation == PlanPresentation.RankedTable) operationIds.Add("Ranking");
        if (plan.FinancialOperation.ComparisonMode == PlanComparisonMode.MultiCompanyComparison)
            operationIds.Add("MultiCompanyComparison");
        switch (plan.FinancialOperation.Kind)
        {
            case PlanFinancialOperationKind.TimeSeries:
                operationIds.Add("TimeSeries");
                break;
            case PlanFinancialOperationKind.AbsoluteChange:
                operationIds.Add("AbsoluteChange");
                break;
            case PlanFinancialOperationKind.GrowthPercentage:
                operationIds.Add("GrowthPercentage");
                break;
            case PlanFinancialOperationKind.UnsupportedDerived:
                operationIds.Add("UnsupportedDerivedFinancialMetric");
                break;
        }
        if (plan.FinancialOperation.ComparisonMode == PlanComparisonMode.MonthOverMonth)
            operationIds.Add("MonthOverMonth");
        if (plan.FinancialOperation.ComparisonMode == PlanComparisonMode.YearOverYear)
            operationIds.Add("YearOverYear");
        if (plan.FinancialOperation.Operation == FinancialValueOperation.UnsupportedDerivedFinancialMetric)
            operationIds.Add("UnsupportedDerivedFinancialMetric");
        else if (!discovery && plan.FinancialOperation.Kind is PlanFinancialOperationKind.DirectValue)
            operationIds.Add("DirectValueRetrieval");

        var definitions = Definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        foreach (var id in operationIds.Where(id => id.Length > 0))
            if (!definitions.TryGetValue(id, out var definition) || !definition.CompilerSupported)
                issues.Add($"query_operation_not_supported:{id}");

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool ContainsExplicitDerivedOrComparisonIntent(string question) =>
        question.Contains("رشد", StringComparison.Ordinal)
        || question.Contains("افزایش", StringComparison.Ordinal)
        || question.Contains("تغییر", StringComparison.Ordinal)
        || question.Contains("مقایسه", StringComparison.Ordinal)
        || question.Contains("نسبت به", StringComparison.Ordinal)
        || question.Contains("حاشیه", StringComparison.Ordinal)
        || question.Contains("growth", StringComparison.OrdinalIgnoreCase)
        || question.Contains("change", StringComparison.OrdinalIgnoreCase)
        || question.Contains("compare", StringComparison.OrdinalIgnoreCase)
        || question.Contains("versus", StringComparison.OrdinalIgnoreCase);
}

using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public enum FinancialSourcePlannerHandoffStatus
{
    ReadyForOfflineValidation,
    Ambiguous,
    Incompatible,
    Incomplete
}

public sealed record VerifiedFinancialBusinessConcept(string ConceptId, string Dimension, string ValueForm,
    string? Provenance);

public sealed record FinancialSourceUnitEvidence(
    BusinessMeasureDimension RequestedDimension,
    string SourceDimensionEvidence,
    string? SourceUnit,
    string? SourceUnitStatus,
    string? EffectiveSourceUnit,
    string? EffectiveUnitStatus,
    string? EffectiveUnitProvenance,
    SemanticUnit RequestedResultUnit,
    EffectiveUnitEvidence IntentEffectiveUnitEvidence);

public sealed record FinancialSourceAuthorizationEvidence(
    string Status,
    bool Queryable,
    string Provenance);

/// <summary>
/// Lossless wrapper around an existing planner selection. The nested intent and source reference
/// preserve details not represented by the narrower planner algebra; this object is not an
/// authorization grant.
/// </summary>
public sealed record FinancialSourcePlannerBinding(
    string ProviderName,
    string StatementType,
    int SourceItemId,
    Guid SourceItemUuid,
    string OfficialTitleFa,
    VerifiedFinancialBusinessConcept? VerifiedBusinessConcept,
    string OriginalUserMeasureExpression,
    MeasureQualifiers StatementAndConsolidationQualifiers,
    ReportingPeriodIntent PeriodIntent,
    EntityScope EntityScope,
    AnalyticalOperation Operation,
    ComparisonIntent Comparison,
    FinancialSourceUnitEvidence UnitEvidence,
    FinancialSourceAuthorizationEvidence Authorization,
    CanonicalSemanticIntent OriginalIntent,
    FinancialValueCatalogReference SourceReference,
    QuestionSchemaRequirements PlannerRequirements);

public sealed record FinancialSourcePlannerHandoffResult(
    FinancialSourcePlannerHandoffStatus Status,
    string DiagnosticCode,
    FinancialSourcePlannerBinding? Binding = null);

/// <summary>Adapts an exact, approved source identity plus explicit intent to the existing
/// StructuredQueryPlanner input contract. Unsupported intent is rejected rather than erased.</summary>
public static class FinancialSourcePlannerHandoff
{
    /// <summary>
    /// The company dimension every direct statement retrieval is scoped by. A direct source item is
    /// read through statements that belong to exactly one company, so the planner must require it.
    /// </summary>
    public const string CompanyDimensionId = "dim_company";

    /// <param name="directSource">
    /// The integrity-checked compiler mapping of the resolved source. When supplied, its verified
    /// join relationships and the company dimension become explicit planner requirements so the
    /// deterministic completeness, validation and compilation gates can check them against the
    /// catalog context instead of silently receiving an empty relationship set.
    /// </param>
    public static FinancialSourcePlannerHandoffResult Create(CanonicalSemanticIntent intent,
        FinancialSourceTitleResolution resolution, string catalogVersion, string catalogHash,
        StructuredSqlDirectSource? directSource = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.Status == FinancialSourceTitleResolutionStatus.Ambiguous)
            return Fail(FinancialSourcePlannerHandoffStatus.Ambiguous, "source_identity_ambiguous");
        if (resolution.Status != FinancialSourceTitleResolutionStatus.Resolved || resolution.Reference is null)
            return Fail(resolution.Status == FinancialSourceTitleResolutionStatus.Incompatible
                    ? FinancialSourcePlannerHandoffStatus.Incompatible : FinancialSourcePlannerHandoffStatus.Incomplete,
                $"source_identity_{resolution.Status.ToString().ToLowerInvariant()}:{resolution.DiagnosticCode}");
        if (intent.ContractVersion != 2 || intent.Ambiguity.RequiresClarification || intent.Measures.Count != 1)
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "intent_contract_ambiguous_or_multiple_measures");

        var reference = resolution.Reference;
        var measure = intent.Measures[0];
        if (reference.SourceItemCatalogId is null || reference.SourceItemCatalogId == Guid.Empty
            || string.IsNullOrWhiteSpace(reference.ProviderName) || string.IsNullOrWhiteSpace(reference.StatementType)
            || reference.SourceItemId <= 0 || string.IsNullOrWhiteSpace(reference.TitleFa))
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "source_identity_fields_incomplete");

        var verifiedConcept = ResolveApprovedConcept(measure, reference);
        if (!string.Equals(FinancialSourceItemTitleResolver.NormalizeTitle(measure.UserPhrase),
                FinancialSourceItemTitleResolver.NormalizeTitle(reference.TitleFa!), StringComparison.Ordinal)
            && verifiedConcept is null)
            return Fail(FinancialSourcePlannerHandoffStatus.Incompatible, "measure_expression_not_exact_title_or_approved_concept");

        var exactRegisteredTitle = string.Equals(FinancialSourceItemTitleResolver.NormalizeTitle(measure.UserPhrase),
            FinancialSourceItemTitleResolver.NormalizeTitle(reference.TitleFa!), StringComparison.Ordinal);
        var sourceDimension = ResolveSourceDimension(reference, verifiedConcept);
        if (measure.Dimension == BusinessMeasureDimension.Unknown
            || sourceDimension is null && !exactRegisteredTitle)
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "source_dimension_evidence_missing");
        if (sourceDimension.HasValue && sourceDimension != measure.Dimension)
            return Fail(FinancialSourcePlannerHandoffStatus.Incompatible, "requested_and_source_dimensions_mismatch");

        var effectiveUnit = reference.EffectiveUnit ?? reference.NormalizedUnit;
        var unitUnresolved = string.IsNullOrWhiteSpace(effectiveUnit)
            || string.Equals(reference.EffectiveUnitStatus, "requires-semantic-review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reference.EffectiveUnitStatus, "unresolved", StringComparison.OrdinalIgnoreCase);
        if (unitUnresolved && measure.RequestedUnit != SemanticUnit.Unspecified)
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "effective_source_unit_unresolved");
        if (measure.RequestedUnit != SemanticUnit.Unspecified
            && !string.Equals(UnitCode(measure.RequestedUnit), effectiveUnit, StringComparison.Ordinal))
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "requested_result_unit_conversion_not_supported");

        if (measure.Qualifiers.ValueForm != MeasureValueForm.ReportedValue
            || measure.Qualifiers.ReportingBasis != MeasureReportingBasis.Unspecified
            || measure.Qualifiers.ValueGrain != MeasureValueGrain.Unspecified)
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "measure_qualifier_not_supported_by_planner_slice");
        if (intent.Operation.Kind != AnalyticalOperationKind.Retrieve || intent.Comparison.Mode != ComparisonMode.None)
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "operation_or_comparison_not_supported_by_planner_slice");
        if (intent.Period.Kind is not (ReportingPeriodKind.LatestAvailable or ReportingPeriodKind.Unspecified))
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "period_intent_not_supported_by_planner_slice");
        if (intent.EntityScope.Kind != EntityScopeKind.NamedCompanies
            || intent.Products.Count != 0 || intent.Categories.Count != 0 || intent.Filters is not null
            || intent.Aggregation is not null || intent.Ranking is not null || intent.Constraints.Count != 0)
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "intent_shape_not_supported_by_planner_slice");
        if (intent.Output.RequestedFields.Count != 0)
            return Fail(FinancialSourcePlannerHandoffStatus.Incomplete, "requested_output_fields_not_representable_in_planner_slice");

        if (directSource is not null && !string.Equals(directSource.CatalogId, reference.CatalogId, StringComparison.Ordinal))
            return Fail(FinancialSourcePlannerHandoffStatus.Incompatible, "direct_source_mapping_identity_mismatch");

        var authorizationStatus = reference.QueryAuthorizationStatus
            ?? (reference.Queryable ? "authorized" : "not-authorized");
        var auth = new FinancialSourceAuthorizationEvidence(authorizationStatus, reference.Queryable,
            $"navigation-hierarchy.yaml#/nodes/*/sourceItemReferences[?(@.catalogId=='{reference.CatalogId}')]/queryAuthorization");
        var hasCompanyScope = intent.EntityScope.CompanyLabels.Count != 0;
        // The current planner slice represents exactly one latest direct statement value (see
        // CanonicalIntentCatalogBinding). A question with no stated period therefore resolves to the
        // latest reporting period, and the rendered answer always states that period explicitly.
        IReadOnlyList<string> periodPolicies = intent.Period.Kind is ReportingPeriodKind.LatestAvailable
            or ReportingPeriodKind.Unspecified ? ["latest"] : [];
        var requirements = new QuestionSchemaRequirements(1, catalogVersion, catalogHash,
            [], directSource is not null && hasCompanyScope ? [CompanyDimensionId] : [],
            hasCompanyScope ? ["company"] : [],
            directSource?.RelationshipIds.Order(StringComparer.Ordinal).ToArray() ?? [], [],
            periodPolicies, [],
            [Presentation(intent.Output.Kind)],
            FinancialValueReferences: [new(nameof(FinancialValueReferenceType.DirectSourceItem), reference.CatalogId)],
            RequestedFinancialValueOperation: nameof(FinancialValueOperation.DirectValueRetrieval))
        {
            EntityReferences = intent.EntityScope.CompanyLabels,
            StatementScopes = measure.Qualifiers.StatementScope switch
            {
                FinancialStatementScope.Standalone => ["standalone"],
                FinancialStatementScope.Consolidated => ["consolidated"],
                _ => []
            }
        };

        return new(FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation,
            reference.Queryable ? "source_identity_ready_for_planner_validation" : "source_identity_ready_but_query_unauthorized",
            new(reference.ProviderName, reference.StatementType, reference.SourceItemId, reference.SourceItemCatalogId.Value,
                reference.TitleFa, verifiedConcept, measure.UserPhrase, measure.Qualifiers, intent.Period, intent.EntityScope,
                intent.Operation, intent.Comparison,
                new(measure.Dimension, verifiedConcept?.Dimension ?? reference.UnitClassification
                    ?? "unreviewed-direct-source-dimension", reference.SourceUnit,
                    reference.SourceUnitStatus, effectiveUnit, reference.EffectiveUnitStatus, reference.EffectiveUnitProvenance,
                    measure.RequestedUnit, measure.EffectiveSourceUnitEvidence),
                auth, intent, reference, requirements));
    }

    private static VerifiedFinancialBusinessConcept? ResolveApprovedConcept(RequestedMeasure measure,
        FinancialValueCatalogReference reference)
    {
        if (string.IsNullOrWhiteSpace(measure.ConceptId)) return null;
        var binding = (reference.CanonicalConceptBindings ?? []).SingleOrDefault(item =>
            string.Equals(item.CanonicalConceptId, measure.ConceptId, StringComparison.Ordinal)
            && string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase));
        if (binding is null
            || !string.Equals(binding.Dimension, measure.Dimension.ToString(), StringComparison.Ordinal)
            || !string.Equals(binding.ValueForm, measure.Qualifiers.ValueForm.ToString(), StringComparison.Ordinal)) return null;
        return new(binding.CanonicalConceptId, binding.Dimension, binding.ValueForm, binding.Provenance);
    }

    private static BusinessMeasureDimension? ResolveSourceDimension(FinancialValueCatalogReference reference,
        VerifiedFinancialBusinessConcept? verifiedConcept)
    {
        if (verifiedConcept is not null && Enum.TryParse<BusinessMeasureDimension>(verifiedConcept.Dimension, out var dimension))
            return dimension;
        return reference.UnitClassification switch
        {
            "MonetaryStatementItem" => BusinessMeasureDimension.CurrencyAmount,
            "MonetaryPerShareItem" => BusinessMeasureDimension.CurrencyPerShare,
            "PhysicalQuantityStatementItem" => BusinessMeasureDimension.PhysicalQuantity,
            _ => null
        };
    }

    internal static string UnitCode(SemanticUnit unit) => unit switch
    {
        SemanticUnit.Count => "Count",
        SemanticUnit.Ton => "Ton",
        SemanticUnit.IRR => "IRR",
        SemanticUnit.MillionIRR => "MillionIRR",
        SemanticUnit.BillionIRR => "BillionIRR",
        SemanticUnit.IRRPerShare => "IRRPerShare",
        SemanticUnit.Percent => "Percent",
        SemanticUnit.Ratio => "Ratio",
        SemanticUnit.CurrencyPerUnit => "CurrencyPerUnit",
        _ => "Unspecified"
    };

    private static string Presentation(RequestedOutputKind kind) => kind switch
    {
        RequestedOutputKind.Scalar => "scalar",
        RequestedOutputKind.TimeSeries => "time-series",
        RequestedOutputKind.RankedList => "ranked-table",
        RequestedOutputKind.Explanation => "summary",
        _ => "table"
    };

    private static FinancialSourcePlannerHandoffResult Fail(FinancialSourcePlannerHandoffStatus status, string code) =>
        new(status, code);
}

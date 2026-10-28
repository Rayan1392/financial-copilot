using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public enum CanonicalCatalogBindingStatus { Bound, Incomplete, Ambiguous, Unsupported, Unauthorized }

/// <summary>Retains the entire canonical request while binding only explicitly reviewed catalog references.</summary>
public sealed record CanonicalCatalogBindingResult(
    CanonicalCatalogBindingStatus Status,
    CanonicalSemanticIntent Intent,
    IReadOnlyList<FinancialValueCatalogReference> References,
    IReadOnlyList<string> Diagnostics);

public static class CanonicalIntentCatalogBinding
{
    public static CanonicalCatalogBindingResult Bind(CanonicalSemanticIntent intent,
        IReadOnlyList<FinancialValueCatalogReference> catalogReferences)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(catalogReferences);
        CanonicalCatalogBindingResult Fail(CanonicalCatalogBindingStatus status, string code) =>
            new(status, intent, [], [code]);

        if (intent.ContractVersion != 2 || intent.Measures is null || intent.Measures.Count == 0)
            return Fail(CanonicalCatalogBindingStatus.Incomplete, "canonical_intent_missing_or_wrong_version");
        if (intent.Ambiguity.RequiresClarification
            || intent.Measures.Any(m => m.CanonicalizationStatus == MeasureCanonicalizationStatus.Ambiguous))
            return Fail(CanonicalCatalogBindingStatus.Ambiguous, "canonical_measure_ambiguous");
        if (intent.Ambiguity.UnresolvedPoints.Count != 0)
            return Fail(CanonicalCatalogBindingStatus.Incomplete, "canonical_intent_unresolved_points");
        if (intent.Operation.Kind == AnalyticalOperationKind.Unsupported)
            return Fail(CanonicalCatalogBindingStatus.Unsupported, "canonical_operation_unsupported");

        var selected = new List<FinancialValueCatalogReference>();
        foreach (var measure in intent.Measures)
        {
            if (measure.CanonicalizationStatus != MeasureCanonicalizationStatus.Canonicalized
                || string.IsNullOrWhiteSpace(measure.ConceptId))
                return Fail(CanonicalCatalogBindingStatus.Incomplete, "canonical_measure_unresolved");
            var matches = catalogReferences.Where(reference => (reference.CanonicalConceptBindings ?? []).Any(binding =>
                string.Equals(binding.Status, "approved", StringComparison.OrdinalIgnoreCase)
                && string.Equals(binding.CanonicalConceptId, measure.ConceptId, StringComparison.Ordinal)
                && string.Equals(binding.Dimension, measure.Dimension.ToString(), StringComparison.Ordinal)
                && string.Equals(binding.ValueForm, measure.Qualifiers.ValueForm.ToString(), StringComparison.Ordinal))).ToArray();
            if (matches.Length == 0)
                return Fail(CanonicalCatalogBindingStatus.Incomplete, $"approved_catalog_binding_missing:{measure.ConceptId}");
            if (matches.Length > 1)
                return Fail(CanonicalCatalogBindingStatus.Ambiguous, $"multiple_approved_catalog_bindings:{measure.ConceptId}");
            selected.Add(matches[0]);
        }

        // Binding conveys meaning only. The separate queryability flag remains an independent gate.
        if (selected.Any(reference => !reference.Queryable))
            return new(CanonicalCatalogBindingStatus.Unauthorized, intent, selected,
                ["catalog_query_authorization_missing"]);
        // The currently proved Planner handoff represents one direct, latest statement value.
        // Preserve the full intent and reject every other requested operator or scope before planning.
        if (selected.Count != 1 || intent.Operation.Kind != AnalyticalOperationKind.Retrieve
            || intent.Comparison.Mode != ComparisonMode.None
            || intent.Period.Kind is not (ReportingPeriodKind.LatestAvailable or ReportingPeriodKind.Unspecified)
            || intent.EntityScope.Kind != EntityScopeKind.NamedCompanies
            || intent.Output.Kind is not (RequestedOutputKind.Scalar or RequestedOutputKind.Table)
            || intent.Products.Count != 0 || intent.Categories.Count != 0 || intent.Filters is not null
            || intent.Aggregation is not null || intent.Ranking is not null || intent.Constraints.Count != 0
            || intent.Measures.Any(m => m.Qualifiers.ReportingBasis != MeasureReportingBasis.Unspecified
                || m.Qualifiers.StatementScope != FinancialStatementScope.Unspecified
                || m.Qualifiers.ValueGrain != MeasureValueGrain.Unspecified)
            || intent.Output.RequestedFields.Count != 0)
            return new(CanonicalCatalogBindingStatus.Unsupported, intent, selected,
                ["canonical_semantics_not_representable_in_current_planner_handoff"]);
        // A requested unit is only representable when it is exactly the approved effective unit of
        // the bound source; any other unit would need a conversion the planner slice does not own.
        if (intent.Measures.Where((measure, index) => measure.RequestedUnit != SemanticUnit.Unspecified
                && !RequestedUnitMatchesEffectiveUnit(measure.RequestedUnit, selected[index])).Any())
            return new(CanonicalCatalogBindingStatus.Unsupported, intent, selected,
                ["requested_result_unit_conversion_not_supported"]);
        return new(CanonicalCatalogBindingStatus.Bound, intent, selected, []);
    }

    private static bool RequestedUnitMatchesEffectiveUnit(SemanticUnit requested, FinancialValueCatalogReference reference) =>
        !string.Equals(reference.EffectiveUnitStatus, "requires-semantic-review", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(reference.EffectiveUnitStatus, "unresolved", StringComparison.OrdinalIgnoreCase)
        && string.Equals(FinancialSourcePlannerHandoff.UnitCode(requested),
            reference.EffectiveUnit ?? reference.NormalizedUnit, StringComparison.Ordinal);
}

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
            var matches = ApprovedMatches(measure, catalogReferences);
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
        // A requested unit is only representable when it is exactly the approved effective unit of
        // the bound source; any other unit would need a conversion the planner slice does not own.
        // It is checked before the shape so a unit restated elsewhere in the intent reports the conversion.
        if (intent.Measures.Where((measure, index) => measure.RequestedUnit != SemanticUnit.Unspecified
                && !UnitMatchesEffectiveUnit(measure.RequestedUnit, selected[index])).Any())
            return new(CanonicalCatalogBindingStatus.Unsupported, intent, selected,
                ["requested_result_unit_conversion_not_supported"]);
        // The proved planner handoffs represent one latest statement value of one company, a comparison
        // of explicitly named companies, a ranking of named companies or the authorized population, or a
        // threshold filter over them (StatementQueryShapeClassifier owns that rule). Every other operator
        // or scope is rejected before planning while the full intent is preserved.
        var shape = StatementQueryShapeClassifier.Classify(intent);
        if (!shape.IsSupported || shape.Shape != StatementQueryShape.Filter && selected.Count != 1)
            return new(CanonicalCatalogBindingStatus.Unsupported, intent, selected,
                ["canonical_semantics_not_representable_in_current_planner_handoff",
                    .. shape.Diagnostic is null ? Array.Empty<string>() : [shape.Diagnostic]]);
        if (shape.Shape == StatementQueryShape.Filter)
        {
            // Every predicate is evaluated on the same statement as the listed value, in the bound item's own unit.
            if (selected.Any(reference => reference.ProviderName != selected[0].ProviderName || reference.StatementType != selected[0].StatementType))
                return new(CanonicalCatalogBindingStatus.Unsupported, intent, selected, ["statement_filter_items_on_different_statements"]);
            foreach (var threshold in shape.Thresholds!)
            {
                var reference = selected[intent.Measures.ToList().FindIndex(measure => measure.ConceptId == threshold.MeasureConcept)];
                if (threshold.Threshold.Unit == SemanticUnit.Unspecified)
                    return new(CanonicalCatalogBindingStatus.Incomplete, intent, selected, ["statement_filter_threshold_unit_unspecified"]);
                if (!UnitMatchesEffectiveUnit(threshold.Threshold.Unit, reference))
                    return new(CanonicalCatalogBindingStatus.Unsupported, intent, selected, ["statement_filter_threshold_unit_conversion_not_supported"]);
            }
        }
        return new(CanonicalCatalogBindingStatus.Bound, intent, selected, []);
    }

    /// <summary>Catalog references with an approved binding of the measure's concept, dimension and value form.</summary>
    internal static FinancialValueCatalogReference[] ApprovedMatches(RequestedMeasure measure,
        IReadOnlyList<FinancialValueCatalogReference> catalogReferences) =>
        catalogReferences.Where(reference => (reference.CanonicalConceptBindings ?? []).Any(binding =>
            string.Equals(binding.Status, "approved", StringComparison.OrdinalIgnoreCase)
            && string.Equals(binding.CanonicalConceptId, measure.ConceptId, StringComparison.Ordinal)
            && string.Equals(binding.Dimension, measure.Dimension.ToString(), StringComparison.Ordinal)
            && string.Equals(binding.ValueForm, measure.Qualifiers.ValueForm.ToString(), StringComparison.Ordinal))).ToArray();

    internal static bool UnitMatchesEffectiveUnit(SemanticUnit requested, FinancialValueCatalogReference reference) =>
        !string.Equals(reference.EffectiveUnitStatus, "requires-semantic-review", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(reference.EffectiveUnitStatus, "unresolved", StringComparison.OrdinalIgnoreCase)
        && string.Equals(FinancialSourcePlannerHandoff.UnitCode(requested),
            reference.EffectiveUnit ?? reference.NormalizedUnit, StringComparison.Ordinal);
}

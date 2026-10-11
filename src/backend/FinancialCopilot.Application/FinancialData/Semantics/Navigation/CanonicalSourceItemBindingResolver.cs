using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public enum CanonicalSourceItemBindingStatus
{
    Bound,
    Incomplete,
    Ambiguous,
    Incompatible
}

public sealed record CanonicalSourceItemBindingResult(
    CanonicalSourceItemBindingStatus Status,
    string DiagnosticCode,
    FinancialValueSelection? Selection = null,
    FinancialValueCatalogReference? Reference = null,
    bool QueryAuthorized = false);

/// <summary>
/// Resolves only explicit approved catalog bindings. It neither infers equivalence from titles nor
/// grants query authorization; a successful source binding remains a catalog identity proposal.
/// </summary>
public static class CanonicalSourceItemBindingResolver
{
    public static CanonicalSourceItemBindingResult Resolve(RequestedMeasure measure,
        IEnumerable<FinancialValueCatalogReference> references, string? providerName = null,
        string? statementType = null)
    {
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(references);
        if (measure.CanonicalizationStatus != MeasureCanonicalizationStatus.Canonicalized
            || string.IsNullOrWhiteSpace(measure.ConceptId))
            return Incomplete("canonical_measure_not_resolved");

        var candidates = references.Where(reference =>
                (providerName is null || string.Equals(reference.ProviderName, providerName, StringComparison.Ordinal))
                && (statementType is null || string.Equals(reference.StatementType, statementType, StringComparison.Ordinal))
                && string.Equals(reference.IdentityApprovalStatus, "OwnerApproved", StringComparison.Ordinal)
                && string.Equals(reference.TitleApprovalStatus, "OwnerApproved", StringComparison.Ordinal)
                && (string.Equals(reference.IdentityStatus, "verified", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(reference.IdentityStatus, "verified-persisted-source-key", StringComparison.OrdinalIgnoreCase))
                && string.Equals(reference.SemanticReviewStatus, "approved", StringComparison.OrdinalIgnoreCase)
                && (reference.CanonicalConceptBindings ?? []).Any(binding =>
                    string.Equals(binding.CanonicalConceptId, measure.ConceptId, StringComparison.Ordinal)
                    && string.Equals(binding.Status, "approved", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (candidates.Length == 0) return Incomplete("no_reviewed_canonical_source_item_binding");
        if (candidates.Length > 1)
            return new(CanonicalSourceItemBindingStatus.Ambiguous, "multiple_reviewed_canonical_source_item_bindings");

        var candidate = candidates[0];
        var binding = candidate.CanonicalConceptBindings!.First(item =>
            string.Equals(item.CanonicalConceptId, measure.ConceptId, StringComparison.Ordinal)
            && string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(binding.Dimension, measure.Dimension.ToString(), StringComparison.Ordinal)
            || !string.Equals(binding.ValueForm, measure.Qualifiers.ValueForm.ToString(), StringComparison.Ordinal))
            return new(CanonicalSourceItemBindingStatus.Incompatible, "canonical_source_item_dimension_or_value_form_mismatch",
                Reference: candidate);

        if (string.IsNullOrWhiteSpace(candidate.NormalizedUnit)
            || string.Equals(candidate.EffectiveUnitStatus, "requires-semantic-review", StringComparison.OrdinalIgnoreCase)
            || !UnitMatchesDimension(candidate.NormalizedUnit, measure.Dimension))
            return new(CanonicalSourceItemBindingStatus.Incomplete, "canonical_source_item_effective_unit_unresolved",
                Reference: candidate);

        return new(CanonicalSourceItemBindingStatus.Bound, "canonical_source_item_bound",
            new FinancialValueSelection(nameof(FinancialValueReferenceType.DirectSourceItem), candidate.CatalogId),
            candidate, candidate.Queryable);
    }

    private static CanonicalSourceItemBindingResult Incomplete(string diagnosticCode) =>
        new(CanonicalSourceItemBindingStatus.Incomplete, diagnosticCode);

    private static bool UnitMatchesDimension(string unit, BusinessMeasureDimension dimension) => dimension switch
    {
        BusinessMeasureDimension.CurrencyAmount => unit is "IRR" or "MillionIRR" or "BillionIRR",
        BusinessMeasureDimension.CurrencyPerShare => unit == "IRRPerShare",
        BusinessMeasureDimension.PhysicalQuantity => unit is "Count" or "Ton",
        BusinessMeasureDimension.Percentage => unit == "Percent",
        BusinessMeasureDimension.Ratio => unit == "Ratio",
        BusinessMeasureDimension.CurrencyPerUnit => unit == "CurrencyPerUnit",
        _ => false
    };
}

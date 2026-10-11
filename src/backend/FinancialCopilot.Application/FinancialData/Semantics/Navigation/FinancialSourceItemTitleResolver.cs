using System.Text;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public enum FinancialSourceTitleResolutionStatus
{
    Resolved,
    Ambiguous,
    NotFound,
    Incompatible,
    Incomplete
}

public sealed record FinancialSourceTitleResolutionRequest(
    string TitleFa,
    string? ProviderName = null,
    string? StatementType = null,
    IReadOnlyDictionary<string, string?>? StatementQualifiers = null);

/// <summary>
/// A source-identity match only. It carries registry evidence and never creates a canonical
/// concept equivalence or grants query authorization.
/// </summary>
public sealed record FinancialSourceTitleResolution(
    FinancialSourceTitleResolutionStatus Status,
    string DiagnosticCode,
    FinancialValueCatalogReference? Reference = null,
    IReadOnlyList<FinancialValueCatalogReference>? Candidates = null,
    IReadOnlyDictionary<string, string?>? StatementQualifiers = null,
    string? DimensionEvidence = null,
    string? AuthorizationStatus = null)
{
    public bool QueryAuthorized => Reference?.Queryable == true;
}

public static class FinancialSourceItemTitleResolver
{
    public static FinancialSourceTitleResolution Resolve(FinancialSourceTitleResolutionRequest request,
        IEnumerable<FinancialValueCatalogReference> references)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(references);
        var qualifiers = request.StatementQualifiers is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(request.StatementQualifiers, StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.TitleFa))
            return Result(FinancialSourceTitleResolutionStatus.Incomplete, "source_title_missing", qualifiers);

        var title = NormalizeTitle(request.TitleFa);
        var approved = references.Where(IsApprovedIdentity).Where(reference =>
                !string.IsNullOrWhiteSpace(reference.TitleFa)
                && string.Equals(NormalizeTitle(reference.TitleFa!), title, StringComparison.Ordinal))
            .ToArray();
        if (approved.Length == 0)
            return Result(FinancialSourceTitleResolutionStatus.NotFound, "approved_exact_source_title_not_found", qualifiers);

        var compatible = approved.Where(reference =>
                (request.ProviderName is null || string.Equals(reference.ProviderName, request.ProviderName, StringComparison.Ordinal))
                && (request.StatementType is null || string.Equals(reference.StatementType, request.StatementType, StringComparison.Ordinal)))
            .ToArray();
        if (compatible.Length == 0)
            return new(FinancialSourceTitleResolutionStatus.Incompatible, "exact_source_title_scope_mismatch",
                Candidates: approved, StatementQualifiers: qualifiers);
        if (compatible.Length > 1)
            return new(FinancialSourceTitleResolutionStatus.Ambiguous, "multiple_approved_exact_source_title_matches",
                Candidates: compatible, StatementQualifiers: qualifiers);

        var reference = compatible[0];
        // This registry has no standalone/consolidated or other statement-scope qualifier
        // evidence. Preserve requested qualifiers, but do not claim they were satisfied.
        if (qualifiers.Count > 0)
            return new(FinancialSourceTitleResolutionStatus.Incomplete, "statement_qualifier_evidence_unavailable",
                Reference: reference, StatementQualifiers: qualifiers,
                DimensionEvidence: DimensionEvidenceFor(reference),
                AuthorizationStatus: reference.QueryAuthorizationStatus ?? (reference.Queryable ? "authorized" : "not-authorized"));

        return new(FinancialSourceTitleResolutionStatus.Resolved, "approved_exact_source_identity_resolved",
            Reference: reference, Candidates: [reference], StatementQualifiers: qualifiers,
            DimensionEvidence: DimensionEvidenceFor(reference),
            AuthorizationStatus: reference.QueryAuthorizationStatus ?? (reference.Queryable ? "authorized" : "not-authorized"));

        static FinancialSourceTitleResolution Result(FinancialSourceTitleResolutionStatus status, string code,
            IReadOnlyDictionary<string, string?> requestedQualifiers) =>
            new(status, code, StatementQualifiers: requestedQualifiers);
    }

    /// <summary>Only Unicode compatibility normalization, Arabic/Persian letter variants,
    /// joiner handling and whitespace folding are applied. No fuzzy or synonym matching.</summary>
    public static string NormalizeTitle(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Normalize(NormalizationForm.FormKC)
            .Replace('\u064A', '\u06CC') // Arabic Yeh -> Persian Yeh
            .Replace('\u0649', '\u06CC') // Alef Maksura -> Persian Yeh
            .Replace('\u0643', '\u06A9') // Arabic Kaf -> Persian Keheh
            .Replace('\u200C', ' '); // ZWNJ is an orthographic word separator here
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace) builder.Append(' ');
            builder.Append(rune.ToString());
            pendingSpace = false;
        }
        return builder.ToString().Trim();
    }

    private static bool IsApprovedIdentity(FinancialValueCatalogReference reference) =>
        !string.IsNullOrWhiteSpace(reference.ProviderName)
        && !string.IsNullOrWhiteSpace(reference.StatementType)
        && string.Equals(reference.IdentityApprovalStatus, "OwnerApproved", StringComparison.Ordinal)
        && string.Equals(reference.TitleApprovalStatus, "OwnerApproved", StringComparison.Ordinal)
        && (string.Equals(reference.IdentityStatus, "verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reference.IdentityStatus, "verified-persisted-source-key", StringComparison.OrdinalIgnoreCase))
        && reference.SourceItemCatalogId is not null;

    private static string? DimensionEvidenceFor(FinancialValueCatalogReference reference) =>
        reference.CanonicalConceptBindings?.FirstOrDefault(binding =>
            string.Equals(binding.Status, "approved", StringComparison.OrdinalIgnoreCase))?.Dimension
        ?? reference.UnitClassification;
}

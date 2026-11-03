using System.Text;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>Owner-reviewed canonical product concept and its approved report-label aliases.</summary>
public sealed record ReviewedProductConcept(string CatalogId, string CatalogVersion,
    string CanonicalLabel, IReadOnlyList<string> Aliases, string ReviewStatus,
    string ApprovalReference, string MappingCoverageStatus = "approved-aliases-only");

public enum ProductConceptResolutionStatus { Resolved, NotFound, Ambiguous, PendingReview, InvalidCatalog }

public sealed record ProductConceptResolution(ProductConceptResolutionStatus Status,
    string? CatalogId, string? CatalogVersion, string? CanonicalLabel,
    IReadOnlyList<string> ApprovedAliases, string? ApprovalReference,
    string? MappingCoverageStatus, string? DiagnosticCode);

public interface IReviewedProductConceptResolver
{
    ProductConceptResolution Resolve(string userReference);
}

public sealed class ReviewedProductConceptResolver : IReviewedProductConceptResolver
{
    private readonly IReadOnlyList<ReviewedProductConcept> concepts;

    public ReviewedProductConceptResolver(IReadOnlyList<ReviewedProductConcept> concepts) =>
        this.concepts = concepts?.ToArray() ?? throw new ArgumentNullException(nameof(concepts));

    public ProductConceptResolution Resolve(string userReference)
    {
        var key = Normalize(userReference);
        if (key.Length == 0) return Failure(ProductConceptResolutionStatus.NotFound, "product_concept_reference_missing");
        var matches = concepts.Where(concept => concept.Aliases.Append(concept.CanonicalLabel)
            .Any(alias => Normalize(alias) == key)).ToArray();
        if (matches.Length == 0) return Failure(ProductConceptResolutionStatus.NotFound, "product_concept_not_approved");
        if (matches.Length > 1) return Failure(ProductConceptResolutionStatus.Ambiguous, "product_concept_alias_ambiguous");
        var selected = matches[0];
        if (!selected.ReviewStatus.Equals("approved", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(selected.ApprovalReference))
            return new(ProductConceptResolutionStatus.PendingReview, selected.CatalogId, selected.CatalogVersion,
                selected.CanonicalLabel, [], selected.ApprovalReference, selected.MappingCoverageStatus,
                "product_concept_owner_review_required");
        if (string.IsNullOrWhiteSpace(selected.CatalogId) || string.IsNullOrWhiteSpace(selected.CatalogVersion)
            || selected.Aliases.Count == 0 || selected.Aliases.Any(alias => string.IsNullOrWhiteSpace(alias))
            || selected.Aliases.Select(Normalize).Distinct(StringComparer.Ordinal).Count() != selected.Aliases.Count)
            return new(ProductConceptResolutionStatus.InvalidCatalog, selected.CatalogId, selected.CatalogVersion,
                selected.CanonicalLabel, [], selected.ApprovalReference, selected.MappingCoverageStatus,
                "product_concept_catalog_entry_invalid");
        return new(ProductConceptResolutionStatus.Resolved, selected.CatalogId, selected.CatalogVersion,
            selected.CanonicalLabel, selected.Aliases, selected.ApprovalReference, selected.MappingCoverageStatus, null);
    }

    internal static string Normalize(string? value)
    {
        var builder = new StringBuilder((value ?? string.Empty).Normalize(NormalizationForm.FormKC).Length);
        foreach (var character in (value ?? string.Empty).Normalize(NormalizationForm.FormKC))
            builder.Append(character switch
            {
                '\u064A' or '\u0649' => '\u06CC',
                '\u0643' => '\u06A9',
                '\u200c' or '\u200f' or '\ufeff' => ' ',
                _ => character
            });
        return string.Join(' ', builder.ToString().Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static ProductConceptResolution Failure(ProductConceptResolutionStatus status, string diagnostic) =>
        new(status, null, null, null, [], null, null, diagnostic);
}

/// <summary>
/// A tenant's separately approved discovery scope. Company IDs are intentionally absent: identities
/// are selected from persisted observations and constrained by the execution grant.
/// </summary>
public sealed record AuthorizedCompanyDiscoveryScope(Guid TenantId, string ScopeId,
    string ApprovalReference, bool CompanyCoverageComplete);

public interface IAuthorizedCompanyDiscoveryScopeResolver
{
    AuthorizedCompanyDiscoveryScope? Resolve(Guid tenantId);
}

public sealed class AuthorizedCompanyDiscoveryScopeResolver(
    IReadOnlyList<AuthorizedCompanyDiscoveryScope> scopes) : IAuthorizedCompanyDiscoveryScopeResolver
{
    private readonly IReadOnlyList<AuthorizedCompanyDiscoveryScope> scopes = scopes?.ToArray()
        ?? throw new ArgumentNullException(nameof(scopes));

    public AuthorizedCompanyDiscoveryScope? Resolve(Guid tenantId)
    {
        var matches = scopes.Where(scope => scope.TenantId == tenantId
            && !string.IsNullOrWhiteSpace(scope.ScopeId)
            && !string.IsNullOrWhiteSpace(scope.ApprovalReference)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

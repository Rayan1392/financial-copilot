namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public sealed record ReviewedCompanyPopulationMember(Guid CompanyId, string CompanyIdentity,
    string IdentityReference, bool IdentityReviewed);

public sealed record ReviewedCompanyPopulation(string CatalogId, string DisplayName,
    IReadOnlyList<string> Aliases, string ReviewStatus, string ApprovalReference,
    IReadOnlyList<ReviewedCompanyPopulationMember> Members);

public enum CompanyPopulationResolutionStatus { Resolved, NotFound, Ambiguous, PendingReview, InvalidMembership }

public sealed record CompanyPopulationResolution(CompanyPopulationResolutionStatus Status,
    string? PopulationId, string? DisplayName, IReadOnlyList<ReviewedCompanyPopulationMember> Members,
    string? ApprovalReference, string? DiagnosticCode);

public interface IReviewedCompanyPopulationResolver
{
    CompanyPopulationResolution Resolve(string userReference);
}

/// <summary>Exact-match, owner-reviewed population lookup. Industry/company rows alone never authorize a population.</summary>
public sealed class ReviewedCompanyPopulationResolver(IReadOnlyList<ReviewedCompanyPopulation> populations)
    : IReviewedCompanyPopulationResolver
{
    private readonly IReadOnlyList<ReviewedCompanyPopulation> populations = populations?.ToArray()
        ?? throw new ArgumentNullException(nameof(populations));

    public CompanyPopulationResolution Resolve(string userReference)
    {
        var key = Normalize(userReference);
        if (key.Length == 0) return Failure(CompanyPopulationResolutionStatus.NotFound, "company_population_reference_missing");
        var matches = populations.Where(population => population.Aliases.Append(population.DisplayName)
            .Any(alias => Normalize(alias) == key)).ToArray();
        if (matches.Length == 0) return Failure(CompanyPopulationResolutionStatus.NotFound, "company_population_not_approved");
        if (matches.Length > 1) return Failure(CompanyPopulationResolutionStatus.Ambiguous, "company_population_alias_ambiguous");
        var selected = matches[0];
        if (!selected.ReviewStatus.Equals("approved", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(selected.ApprovalReference))
            return new(CompanyPopulationResolutionStatus.PendingReview, selected.CatalogId, selected.DisplayName, [],
                selected.ApprovalReference, "company_population_owner_review_required");
        if (string.IsNullOrWhiteSpace(selected.CatalogId) || selected.Members.Count < 2
            || selected.Members.Any(member => member.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(member.CompanyIdentity)
                || string.IsNullOrWhiteSpace(member.IdentityReference) || !member.IdentityReviewed)
            || selected.Members.Select(member => member.CompanyId).Distinct().Count() != selected.Members.Count)
            return new(CompanyPopulationResolutionStatus.InvalidMembership, selected.CatalogId, selected.DisplayName, [],
                selected.ApprovalReference, "company_population_membership_incomplete_or_unverified");
        return new(CompanyPopulationResolutionStatus.Resolved, selected.CatalogId, selected.DisplayName,
            selected.Members, selected.ApprovalReference, null);
    }

    private static CompanyPopulationResolution Failure(CompanyPopulationResolutionStatus status, string diagnostic) =>
        new(status, null, null, [], null, diagnostic);

    internal static string Normalize(string? value) => string.Join(' ', (value ?? string.Empty)
        .Normalize(System.Text.NormalizationForm.FormC).Trim().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// A company's reviewed member of a product equivalence group. Its rows are matched by
/// <see cref="NormalizedLabel"/> (the reviewed source title); <see cref="CompanyProductKey"/> is retained
/// for configuration compatibility only and never matched against the persisted ProductCode.
/// </summary>
public sealed record ReviewedProductEquivalenceMember(Guid CompanyId, string ProviderName,
    string ProviderProductIdentity, string CompanyProductKey, string NormalizedLabel,
    string UnitCode, string IdentityReference, bool IdentityReviewed);

public sealed record ReviewedProductEquivalenceGroup(string CatalogId, string DisplayName,
    IReadOnlyList<string> Aliases, string ReviewStatus, string ApprovalReference,
    IReadOnlyList<ReviewedProductEquivalenceMember> Members);

public enum ProductEquivalenceResolutionStatus { Resolved, NotFound, Ambiguous, PendingReview, MissingCompanyMapping, IncompatibleUnits }

public sealed record ProductEquivalenceResolution(ProductEquivalenceResolutionStatus Status,
    string? EquivalenceGroupId, string? DisplayName,
    IReadOnlyList<ReviewedProductEquivalenceMember> Members, string? UnitCode,
    string? ApprovalReference, string? DiagnosticCode);

public interface IReviewedProductEquivalenceResolver
{
    ProductEquivalenceResolution Resolve(string userReference, IReadOnlyCollection<Guid> companyIds);
}

/// <summary>Only explicitly reviewed equivalence groups can bridge company-specific provider identities.</summary>
public sealed class ReviewedProductEquivalenceResolver(IReadOnlyList<ReviewedProductEquivalenceGroup> groups)
    : IReviewedProductEquivalenceResolver
{
    private readonly IReadOnlyList<ReviewedProductEquivalenceGroup> groups = groups?.ToArray()
        ?? throw new ArgumentNullException(nameof(groups));

    public ProductEquivalenceResolution Resolve(string userReference, IReadOnlyCollection<Guid> companyIds)
    {
        var key = ReviewedCompanyPopulationResolver.Normalize(userReference);
        var requestedCompanies = companyIds?.Where(id => id != Guid.Empty).Distinct().ToHashSet()
            ?? throw new ArgumentNullException(nameof(companyIds));
        if (key.Length == 0 || requestedCompanies.Count < 2)
            return Failure(ProductEquivalenceResolutionStatus.NotFound, "product_equivalence_scope_invalid");
        var matches = groups.Where(group => group.Aliases.Append(group.DisplayName)
            .Any(alias => ReviewedCompanyPopulationResolver.Normalize(alias) == key)).ToArray();
        if (matches.Length == 0) return Failure(ProductEquivalenceResolutionStatus.NotFound, "product_equivalence_not_approved");
        if (matches.Length > 1) return Failure(ProductEquivalenceResolutionStatus.Ambiguous, "product_equivalence_alias_ambiguous");
        var selected = matches[0];
        if (!selected.ReviewStatus.Equals("approved", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(selected.ApprovalReference))
            return new(ProductEquivalenceResolutionStatus.PendingReview, selected.CatalogId, selected.DisplayName,
                [], null, selected.ApprovalReference, "product_equivalence_owner_review_required");
        var scoped = selected.Members.Where(member => requestedCompanies.Contains(member.CompanyId)).ToArray();
        if (scoped.Length != requestedCompanies.Count || scoped.Select(member => member.CompanyId).Distinct().Count() != requestedCompanies.Count
            || scoped.Any(member => member.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(member.ProviderName)
                || string.IsNullOrWhiteSpace(member.ProviderProductIdentity)
                || string.IsNullOrWhiteSpace(member.NormalizedLabel) || string.IsNullOrWhiteSpace(member.IdentityReference)
                || !member.IdentityReviewed))
            return new(ProductEquivalenceResolutionStatus.MissingCompanyMapping, selected.CatalogId, selected.DisplayName,
                [], null, selected.ApprovalReference, "product_equivalence_mapping_missing_or_unreviewed_for_company_scope");
        var units = scoped.Select(member => member.UnitCode).Distinct(StringComparer.Ordinal).ToArray();
        if (units.Length != 1 || string.IsNullOrWhiteSpace(units[0]))
            return new(ProductEquivalenceResolutionStatus.IncompatibleUnits, selected.CatalogId, selected.DisplayName,
                scoped, null, selected.ApprovalReference, "product_equivalence_units_incompatible");
        return new(ProductEquivalenceResolutionStatus.Resolved, selected.CatalogId, selected.DisplayName,
            scoped, units[0], selected.ApprovalReference, null);
    }

    private static ProductEquivalenceResolution Failure(ProductEquivalenceResolutionStatus status, string diagnostic) =>
        new(status, null, null, [], null, null, diagnostic);
}

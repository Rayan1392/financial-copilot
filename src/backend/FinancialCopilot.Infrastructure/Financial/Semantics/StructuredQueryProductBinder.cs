using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.Infrastructure.Financial.Semantics;

/// <summary>
/// Adapts the existing provider/company-scoped product resolver to Feature 141 plans. The resolver
/// (Feature 129) identifies a product by title; Feature 141 additionally requires a single measurement
/// unit for a direct value, so a title the company reports in several units is returned as Ambiguous
/// instead of letting one unit be chosen silently. Feature 129 behavior is unchanged.
/// A Feature 141 product is its normalized title and unit (plus approved concept aliases); the binding
/// carries the persisted source labels with that identity. The persisted ProductCode is not a product
/// identity and is never read (owner decision 2026-10-29).
/// </summary>
public sealed class StructuredQueryProductBinder(
    ICanonicalQueryProductResolver resolver,
    IMonthlyProductCatalogReadRepository? productCatalog = null,
    IReviewedProductConceptResolver? conceptResolver = null) : IStructuredQueryProductBinder
{
    public const string IncompatibleUnitsDiagnostic = "monthly_product_units_incompatible";

    public async Task<StructuredProductBinding> ResolveAsync(string? companyMention, string? productMention,
        CancellationToken cancellationToken)
    {
        var result = await resolver.ResolveAsync(companyMention, productMention, cancellationToken);
        switch (result)
        {
            case ProductResolutionResult.Resolved resolved:
                var product = resolved.Product;
                var observations = productCatalog is null || string.IsNullOrWhiteSpace(product.ExternalCompanyId)
                    ? [] : await productCatalog.GetProductCatalogAsync(product.ExternalCompanyId, cancellationToken);
                var unitConflict = FindUnitConflict(product, observations);
                if (unitConflict.Count > 1)
                    return new(StructuredProductBindingStatus.Ambiguous, product.CompanyId, null,
                        product.DisplayTitle, null, "company-scoped-product-resolver",
                        unitConflict, IncompatibleUnitsDiagnostic);
                return new(StructuredProductBindingStatus.Found,
                    product.CompanyId, IdentityKey(product.ExternalCompanyId, product.DisplayTitle, product.Unit),
                    product.DisplayTitle, product.Unit, product.IdentityProvenance, [], null,
                    SourceLabels(product, productMention, observations));
            case ProductResolutionResult.Ambiguous ambiguous:
                return new(StructuredProductBindingStatus.Ambiguous, null,
                    null, null, null, "company-scoped-product-resolver", ambiguous.Candidates
                        .Select(candidate => IdentityKey(candidate.Product.ExternalCompanyId, candidate.Product.DisplayTitle,
                            candidate.Product.Unit)).Distinct(StringComparer.Ordinal).ToArray(),
                    "monthly_product_identity_ambiguous");
            case ProductResolutionResult.NotFound:
                return new(StructuredProductBindingStatus.NotFound, null,
                    null, null, null, "company-scoped-product-resolver", [], "monthly_product_identity_not_found");
            case ProductResolutionResult.Missing missing:
                return new(StructuredProductBindingStatus.Missing, null,
                    null, null, null, "company-scoped-product-resolver", [], $"monthly_product_reference_missing:{missing.EntityType}");
            default:
                return new(StructuredProductBindingStatus.Missing, null, null, null, null, null, [],
                    "monthly_product_resolution_unavailable");
        }
    }

    /// <summary>The company-scoped semantic product identity: normalized title and unit, nothing else.</summary>
    public static string IdentityKey(string? externalCompanyId, string? title, string? unit) =>
        $"{externalCompanyId}:TITLE:{TitleKey(title)}|UNIT:{UnitKey(unit)}";

    // The existing Feature 129 normalization, compared case-insensitively as the resolver compares titles.
    private static string TitleKey(string? title) => MonthlyProductComparisonNormalizer.Normalize(title).ToLowerInvariant();
    private static string UnitKey(string? unit) => MonthlyProductComparisonNormalizer.Unit(unit).ToLowerInvariant();

    /// <summary>
    /// Identity keys of the resolved product's title in each unit the company reports it in. Semantic
    /// product identity is unit independent (owner rule 1), but a single value must come from one unit
    /// (rules 4–6); the caller decides, the binder never picks.
    /// </summary>
    private static IReadOnlyList<string> FindUnitConflict(CanonicalQueryProduct product,
        IReadOnlyList<ProductSalesObservation> observations)
    {
        var title = TitleKey(product.DisplayTitle);
        if (string.IsNullOrWhiteSpace(title)) return [];
        var variants = observations
            .Where(row => TitleKey(row.Title) == title)
            .Select(row => UnitKey(row.Unit))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(unit => IdentityKey(product.ExternalCompanyId, product.DisplayTitle, unit))
            .ToArray();
        return variants.Length > 1 ? variants : [];
    }

    /// <summary>
    /// Every persisted Title and Unit spelling of the resolved product in the company's accepted single-month
    /// reports: rows whose normalized unit equals the product's and whose normalized title is the product's
    /// title or, when the product title is an alias of an approved product concept, any of that concept's aliases.
    /// </summary>
    private MonthlyProductSelection SourceLabels(CanonicalQueryProduct product, string? productMention,
        IReadOnlyList<ProductSalesObservation> observations)
    {
        var title = TitleKey(product.DisplayTitle);
        var unit = UnitKey(product.Unit);
        var titles = new HashSet<string>(StringComparer.Ordinal) { title };
        if (conceptResolver is not null && !string.IsNullOrWhiteSpace(productMention)
            && conceptResolver.Resolve(productMention) is { Status: ProductConceptResolutionStatus.Resolved } concept)
        {
            var aliases = concept.ApprovedAliases.Select(TitleKey).ToHashSet(StringComparer.Ordinal);
            if (aliases.Contains(title)) titles.UnionWith(aliases);
        }
        var rows = observations.Where(row => titles.Contains(TitleKey(row.Title)) && UnitKey(row.Unit) == unit).ToArray();
        return new(
            rows.Select(row => row.Title).Append(product.DisplayTitle).OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            rows.Select(row => row.Unit ?? string.Empty).Append(product.Unit ?? string.Empty)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}

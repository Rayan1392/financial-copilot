using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.Infrastructure.Financial.Semantics;

/// <summary>
/// Adapts the existing provider/company-scoped product resolver to Feature 141 plans. The resolver
/// (Feature 129) identifies a product by title; Feature 141 additionally requires a single measurement
/// unit for a direct value, so a title the company reports in several units is returned as Ambiguous
/// instead of letting one unit be chosen silently. Feature 129 behavior is unchanged.
/// </summary>
public sealed class StructuredQueryProductBinder(
    ICanonicalQueryProductResolver resolver,
    IMonthlyProductCatalogReadRepository? productCatalog = null) : IStructuredQueryProductBinder
{
    public const string IncompatibleUnitsDiagnostic = "monthly_product_units_incompatible";

    public async Task<StructuredProductBinding> ResolveAsync(string? companyMention, string? productMention,
        CancellationToken cancellationToken)
    {
        var result = await resolver.ResolveAsync(companyMention, productMention, cancellationToken);
        switch (result)
        {
            case ProductResolutionResult.Resolved resolved:
                var unitConflict = await FindUnitConflictAsync(resolved.Product, cancellationToken);
                if (unitConflict.Count > 1)
                    return new(StructuredProductBindingStatus.Ambiguous, resolved.Product.CompanyId, null,
                        resolved.Product.DisplayTitle, null, "company-scoped-product-resolver",
                        unitConflict, IncompatibleUnitsDiagnostic);
                return new(StructuredProductBindingStatus.Found,
                    resolved.Product.CompanyId, resolved.Product.ProductKey, resolved.Product.DisplayTitle,
                    resolved.Product.Unit, resolved.Product.IdentityProvenance, [], null);
            case ProductResolutionResult.Ambiguous ambiguous:
                return new(StructuredProductBindingStatus.Ambiguous, null,
                    null, null, null, "company-scoped-product-resolver", ambiguous.Candidates
                        .Select(candidate => candidate.Product.ProductKey).Distinct(StringComparer.Ordinal).ToArray(),
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

    /// <summary>
    /// Product keys the company reports under the resolved product's title with a different unit.
    /// Semantic product identity is unit independent (owner rule 1), but a single value must come from
    /// one unit (rules 4–6); the caller decides, the binder never picks.
    /// </summary>
    private async Task<IReadOnlyList<string>> FindUnitConflictAsync(CanonicalQueryProduct product, CancellationToken cancellationToken)
    {
        if (productCatalog is null || string.IsNullOrWhiteSpace(product.ExternalCompanyId)) return [];
        var observations = await productCatalog.GetProductCatalogAsync(product.ExternalCompanyId, cancellationToken);
        var title = MonthlyProductTrendCalculator.NormalizeProductText(product.DisplayTitle);
        if (string.IsNullOrWhiteSpace(title)) return [];
        var variants = observations
            .Where(row => string.Equals(MonthlyProductTrendCalculator.NormalizeProductText(row.Title), title, StringComparison.OrdinalIgnoreCase))
            .Select(row => (Unit: NormalizeUnit(row.Unit), Key: row.ProductKey ?? MonthlyProductTrendCalculator.ProductKey(product.ExternalCompanyId, row)))
            .GroupBy(row => row.Unit, StringComparer.Ordinal)
            .Select(group => group.Select(row => row.Key).OrderBy(key => key, StringComparer.Ordinal).First())
            .ToArray();
        return variants.Length > 1 ? variants : [];
    }

    private static string NormalizeUnit(string? unit) =>
        (unit ?? string.Empty).Trim().Normalize().Replace('ي', 'ی').Replace('ك', 'ک').ToLowerInvariant();
}

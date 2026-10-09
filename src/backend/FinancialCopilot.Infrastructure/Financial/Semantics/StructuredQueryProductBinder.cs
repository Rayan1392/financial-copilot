using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.Infrastructure.Financial.Semantics;

/// <summary>Adapts the existing provider/company-scoped product resolver to Feature 141 plans.</summary>
public sealed class StructuredQueryProductBinder(ICanonicalQueryProductResolver resolver) : IStructuredQueryProductBinder
{
    public async Task<StructuredProductBinding> ResolveAsync(string? companyMention, string? productMention,
        CancellationToken cancellationToken)
    {
        var result = await resolver.ResolveAsync(companyMention, productMention, cancellationToken);
        return result switch
        {
            ProductResolutionResult.Resolved resolved => new(StructuredProductBindingStatus.Found,
                resolved.Product.CompanyId, resolved.Product.ProductKey, resolved.Product.DisplayTitle,
                resolved.Product.Unit, resolved.Product.IdentityProvenance, [], null),
            ProductResolutionResult.Ambiguous ambiguous => new(StructuredProductBindingStatus.Ambiguous, null,
                null, null, null, "company-scoped-product-resolver", ambiguous.Candidates
                    .Select(candidate => candidate.Product.ProductKey).Distinct(StringComparer.Ordinal).ToArray(),
                "monthly_product_identity_ambiguous"),
            ProductResolutionResult.NotFound notFound => new(StructuredProductBindingStatus.NotFound, null,
                null, null, null, "company-scoped-product-resolver", [], "monthly_product_identity_not_found"),
            ProductResolutionResult.Missing missing => new(StructuredProductBindingStatus.Missing, null,
                null, null, null, "company-scoped-product-resolver", [], $"monthly_product_reference_missing:{missing.EntityType}"),
            _ => new(StructuredProductBindingStatus.Missing, null, null, null, null, null, [],
                "monthly_product_resolution_unavailable")
        };
    }
}

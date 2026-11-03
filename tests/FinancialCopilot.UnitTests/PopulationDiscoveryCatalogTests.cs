using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class PopulationDiscoveryCatalogTests
{
    private const string Approval = "owner-confirmed:feature141-product-equivalence:2026-10-09";

    [Fact]
    public void OwnerApprovedPelletAliasesResolveToTheSameCanonicalConcept()
    {
        var resolver = Resolver();

        var shortLabel = resolver.Resolve("گندله");
        var qualifiedLabel = resolver.Resolve("گندله سنگ‌آهن");

        Assert.Equal(ProductConceptResolutionStatus.Resolved, shortLabel.Status);
        Assert.Equal(ProductConceptResolutionStatus.Resolved, qualifiedLabel.Status);
        Assert.Equal("product_iron_ore_pellets", shortLabel.CatalogId);
        Assert.Equal(shortLabel.CatalogId, qualifiedLabel.CatalogId);
        Assert.Equal("product-catalog-v1", qualifiedLabel.CatalogVersion);
        Assert.Equal(Approval, qualifiedLabel.ApprovalReference);
    }

    [Fact]
    public void UnknownOrUnreviewedProductLabelCannotBecomeAReviewedMapping()
    {
        Assert.Equal(ProductConceptResolutionStatus.NotFound, Resolver().Resolve("کنسانتره").Status);
        Assert.Equal(ProductConceptResolutionStatus.PendingReview,
            new ReviewedProductConceptResolver([
                new ReviewedProductConcept("candidate", "v1", "گندله", ["گندله"], "pending", "")
            ]).Resolve("گندله").Status);
    }

    [Fact]
    public void AliasCollisionAcrossCanonicalProductsIsAmbiguous()
    {
        var resolver = new ReviewedProductConceptResolver([
            Concept(),
            new ReviewedProductConcept("other", "v1", "آهن", ["گندله"], "approved", "owner-review-2")
        ]);

        Assert.Equal(ProductConceptResolutionStatus.Ambiguous, resolver.Resolve("گندله").Status);
    }

    [Fact]
    public void DiscoveryScopeIsBoundToExactlyOneTenantAndNeverCarriesCompanyIds()
    {
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var resolver = new AuthorizedCompanyDiscoveryScopeResolver([
            new AuthorizedCompanyDiscoveryScope(tenant, "tenant-observed-company-scope", "approval-ref", false)
        ]);

        Assert.NotNull(resolver.Resolve(tenant));
        Assert.Null(resolver.Resolve(otherTenant));
        Assert.DoesNotContain(typeof(AuthorizedCompanyDiscoveryScope).GetProperties(),
            property => property.Name.Contains("CompanyId", StringComparison.Ordinal));
    }

    private static ReviewedProductConceptResolver Resolver() => new([Concept()]);

    private static ReviewedProductConcept Concept() => new("product_iron_ore_pellets", "product-catalog-v1",
        "گندله سنگ‌آهن", ["گندله", "گندله سنگ‌آهن"], "approved", Approval);
}

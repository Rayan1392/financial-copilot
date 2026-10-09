using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class ReviewedSemanticScopeCatalogTests
{
    private static readonly Guid Company1 = Guid.Parse("80848ab5-96d2-4f28-9c65-ae28cb854304");
    private static readonly Guid Company2 = Guid.Parse("88e4e28b-3088-48dd-9ed0-02633c9342a7");

    [Fact]
    public void CompanyPopulation_RequiresExplicitApprovalAndVerifiedMemberships()
    {
        var pending = Population("pending", "pending-review");
        var resolver = new ReviewedCompanyPopulationResolver([pending]);
        var result = resolver.Resolve("فولادی‌ها");
        Assert.Equal(CompanyPopulationResolutionStatus.PendingReview, result.Status);
        Assert.Equal("company_population_owner_review_required", result.DiagnosticCode);

        var approved = Population("approved", "owner-review:industry-steel:v1");
        result = new ReviewedCompanyPopulationResolver([approved]).Resolve(" صنعت  فولاد ");
        Assert.Equal(CompanyPopulationResolutionStatus.Resolved, result.Status);
        Assert.Equal(new[] { Company1, Company2 }, result.Members.Select(member => member.CompanyId));
        Assert.All(result.Members, member => Assert.True(member.IdentityReviewed));
    }

    [Fact]
    public void ProductEquivalence_RequiresReviewedPerCompanyMappingsAndCompatibleUnits()
    {
        var group = new ReviewedProductEquivalenceGroup("eq-pellet-v1", "گندله", ["pellet"], "approved",
            "owner-review:pellet-equivalence:v1",
            [Member(Company1, "company1:pellet", "ton"), Member(Company2, "company2:pellet", "ton")]);
        var resolver = new ReviewedProductEquivalenceResolver([group]);
        var result = resolver.Resolve("گندله", [Company1, Company2]);
        Assert.Equal(ProductEquivalenceResolutionStatus.Resolved, result.Status);
        Assert.Equal(new[] { "company1:pellet", "company2:pellet" }, result.Members.Select(member => member.CompanyProductKey));

        var missingCompany = resolver.Resolve("گندله", [Company1, Guid.NewGuid()]);
        Assert.Equal(ProductEquivalenceResolutionStatus.MissingCompanyMapping, missingCompany.Status);

        var incompatible = group with { Members = [Member(Company1, "one", "ton"), Member(Company2, "two", "pieces")] };
        Assert.Equal(ProductEquivalenceResolutionStatus.IncompatibleUnits,
            new ReviewedProductEquivalenceResolver([incompatible]).Resolve("گندله", [Company1, Company2]).Status);
    }

    [Fact]
    public void SamePersianLabelDoesNotCreateEquivalenceWithoutApproval()
    {
        var unreviewed = new ReviewedProductEquivalenceGroup("eq-pending", "گندله", ["pellet"], "pending",
            "", [Member(Company1, "same-title-company1", "ton"), Member(Company2, "same-title-company2", "ton")]);
        var result = new ReviewedProductEquivalenceResolver([unreviewed]).Resolve("گندله", [Company1, Company2]);
        Assert.Equal(ProductEquivalenceResolutionStatus.PendingReview, result.Status);
        Assert.Equal("product_equivalence_owner_review_required", result.DiagnosticCode);
    }

    private static ReviewedCompanyPopulation Population(string status, string approval) => new("population-steel-v1",
        "صنعت فولاد", ["فولادی‌ها"], status, approval,
        [new(Company1, "MTH", "verified-company:1", true), new(Company2, "MTH2", "verified-company:2", true)]);

    private static ReviewedProductEquivalenceMember Member(Guid company, string key, string unit) => new(company,
        "NoavaranCurrentApi", "provider-product:" + key, key, "گندله", unit, "verified-product:" + key, true);
}

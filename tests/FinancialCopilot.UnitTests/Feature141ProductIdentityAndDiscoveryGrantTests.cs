using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure.Financial.Semantics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Owner rules for product identity: semantic identity is unit independent, observations keep their
/// unit, and a value is never taken from one of several units silently. Discovery grants are bounded
/// by the tenant's reviewed company universe, never by a table scan.
/// </summary>
public sealed class Feature141ProductIdentityAndDiscoveryGrantTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string External = "ext-kgol";

    private static CanonicalQueryProduct Product(string unit, string title = "گندله") =>
        new(CompanyId, External, $"NoavaranCurrentApi:{External}:TITLE:{title}|UNIT:{unit}", title, unit, null, null, "feature-129-company-scoped-product-row");

    // Production-like rows: ProductCode is a per-row hash that changes every month and is not a product identity.
    private static ProductSalesObservation Observation(string title, string unit, string? productCode = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), External, new JalaliPeriod(1405, 6), "NoavaranCurrentApi", "r1",
            new(2026, 8, 23), new(2026, 9, 22), productCode ?? $"PRODUCT:NATURAL:{Guid.NewGuid():N}", title, unit,
            10m, 8m, 1m, 80m);

    private static readonly ReviewedProductConceptResolver PelletConcept = new([new ReviewedProductConcept(
        "product_iron_ore_pellets", "product-catalog-v1", "گندله سنگ‌آهن", ["گندله", "گندله سنگ‌آهن"], "approved",
        "owner-confirmed:feature141-product-equivalence:2026-10-09")]);

    [Fact]
    public async Task SameTitleReportedInTwoUnitsIsAmbiguousNotSilentlyChosen()
    {
        var binder = new StructuredQueryProductBinder(new FixedResolver(new ProductResolutionResult.Resolved(Product("kg"),
                new ProductResolutionEvidence("exact_title_canonicalized", 1m))),
            new FixedCatalog([Observation("گندله", "ton"), Observation("گندله", "kg"), Observation("کنسانتره", "ton")]));

        var binding = await binder.ResolveAsync("کگل", "گندله", CancellationToken.None);

        Assert.Equal(StructuredProductBindingStatus.Ambiguous, binding.Status);
        Assert.Equal(StructuredQueryProductBinder.IncompatibleUnitsDiagnostic, binding.DiagnosticCode);
        Assert.Equal(2, binding.CandidateProductKeys.Count);
        Assert.Null(binding.ProductKey);
        Assert.Null(binding.Selection);
    }

    [Fact]
    public async Task SingleUnitTitleResolvesWithItsSourceUnit()
    {
        var binder = new StructuredQueryProductBinder(new FixedResolver(new ProductResolutionResult.Resolved(Product("ton"),
                new ProductResolutionEvidence("exact_title_canonicalized", 1m))),
            new FixedCatalog([Observation("گندله", "ton"), Observation("گندله", "TON"), Observation("کنسانتره", "kg")]));

        var binding = await binder.ResolveAsync("کگل", "گندله", CancellationToken.None);

        Assert.Equal(StructuredProductBindingStatus.Found, binding.Status);
        Assert.Equal($"{External}:TITLE:گندله|UNIT:ton", binding.ProductKey);
        Assert.Equal("ton", binding.Unit);
        Assert.Equal(["گندله"], binding.Selection!.SourceTitles);
        Assert.Equal(["TON", "ton"], binding.Selection.SourceUnits);
    }

    [Fact]
    public async Task WithoutCatalogAccessFeature129ResolutionIsPassedThroughUnchanged()
    {
        var binder = new StructuredQueryProductBinder(new FixedResolver(new ProductResolutionResult.Resolved(Product("kg"),
            new ProductResolutionEvidence("exact_title_canonicalized", 1m))));
        var binding = await binder.ResolveAsync("کگل", "گندله", CancellationToken.None);
        Assert.Equal(StructuredProductBindingStatus.Found, binding.Status);
        Assert.Equal("kg", binding.Unit);
        Assert.Equal(["گندله"], binding.Selection!.SourceTitles);
        Assert.Equal(["kg"], binding.Selection.SourceUnits);
    }

    [Fact]
    public async Task SourceLabelsCarryEveryPersistedSpellingOfTitleAndUnit()
    {
        // Real Noavaran units carry trailing spaces and punctuation ("تن ", "تن."); they are one unit.
        var binder = new StructuredQueryProductBinder(new FixedResolver(new ProductResolutionResult.Resolved(Product("تن"),
                new ProductResolutionEvidence("exact_title", 1m))),
            new FixedCatalog([Observation("گندله", "تن"), Observation("گندله", "تن "), Observation("گندله ", "تن."),
                Observation("گندله فروش صادراتی", "تن"), Observation("گندله ریزدانه", "تن")]));

        var binding = await binder.ResolveAsync("کگل", "گندله", CancellationToken.None);

        Assert.Equal(StructuredProductBindingStatus.Found, binding.Status);
        Assert.Equal(["گندله", "گندله "], binding.Selection!.SourceTitles);
        Assert.Equal(["تن", "تن ", "تن."], binding.Selection.SourceUnits);
    }

    [Fact]
    public async Task ApprovedConceptAliasesJoinTheSourceLabelsButOtherProductsDoNot()
    {
        var binder = new StructuredQueryProductBinder(new FixedResolver(new ProductResolutionResult.Resolved(Product("تن"),
                new ProductResolutionEvidence("exact_title", 1m))),
            new FixedCatalog([Observation("گندله", "تن"), Observation("گندله سنگ آهن", "تن"), Observation("گندله سنگ آهن", "هزار تن"),
                Observation("گندله فروش صادراتی", "تن")]), PelletConcept);

        var binding = await binder.ResolveAsync("کگل", "گندله", CancellationToken.None);

        Assert.Equal(StructuredProductBindingStatus.Found, binding.Status);
        Assert.Equal(["گندله", "گندله سنگ آهن"], binding.Selection!.SourceTitles);
        Assert.Equal(["تن"], binding.Selection.SourceUnits);
    }

    [Fact]
    public async Task ProductCodeValuesNeverChangeTheBinding()
    {
        CanonicalQueryProduct product = Product("تن");
        async Task<StructuredProductBinding> Bind(params string?[] codes) =>
            await new StructuredQueryProductBinder(new FixedResolver(new ProductResolutionResult.Resolved(product,
                    new ProductResolutionEvidence("exact_title", 1m))),
                new FixedCatalog([Observation("گندله", "تن", codes[0]), Observation("گندله", "تن ", codes[1]),
                    Observation("کنسانتره", "تن", codes[2])]))
                .ResolveAsync("کگل", "گندله", CancellationToken.None);

        var hashed = await Bind("PRODUCT:NATURAL:22d58f29e3fedf1e", "PRODUCT:NATURAL:9a1f", "PRODUCT:NATURAL:22d58f29e3fedf1e");
        var zero = await Bind("0", "0", "0");
        var empty = await Bind(null, "", "x");

        foreach (var other in new[] { zero, empty })
        {
            Assert.Equal(hashed.Status, other.Status);
            Assert.Equal(hashed.ProductKey, other.ProductKey);
            Assert.Equal(hashed.Selection!.SourceTitles, other.Selection!.SourceTitles);
            Assert.Equal(hashed.Selection.SourceUnits, other.Selection.SourceUnits);
        }
    }

    [Fact]
    public void DiscoveryGrantCarriesOnlyTheTenantsReviewedCompanyUniverse()
    {
        var tenant = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
        var otherTenant = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var foreignCompany = Guid.NewGuid();
        var options = Options.Create(new StructuredQueryExecutionOptions
        {
            Enabled = true, ConnectionString = "Host=x;Database=db;Username=reader", DesignatedDatabase = "db", ReadOnlyRole = "reader",
            ReviewedTenantCompanyDiscoveryScopes = [$"{tenant:D}|scope-1|approval-1"],
            ReviewedTenantCompanyBindings = [$"{tenant:D}:{companyA:D}", $"{tenant:D}:{companyB:D}", $"{otherTenant:D}:{foreignCompany:D}"]
        });
        var query = new CompiledReadOnlyQuery("SELECT 1", [], tenant, "1", "hash", "fp", 100, PlanPresentation.Table,
            ["metric_monthly_production_quantity"], ["rel_31e410fb3ae11956"], ["tbl_8ecd0432a4292998"], Guid.Empty,
            FinancialValueOperation.DirectValueRetrieval, true, true, true, isMonthlyValueQuery: true, companyIds: [],
            companyDiscoveryScopeId: "scope-1", companyDiscoveryApprovalReference: "approval-1", companyDiscoveryCoverageComplete: false,
            productMappingCoverageStatus: "approved-aliases-only");

        var grant = new CatalogStructuredQueryAuthorizer(options, new TestEnvironment()).Authorize(query, tenant);

        Assert.NotNull(grant);
        Assert.Equal(new HashSet<Guid> { companyA, companyB }, grant.AllowedCompanyIds);
        Assert.Equal("scope-1", grant.CompanyDiscoveryScopeId);
        Assert.Null(new CatalogStructuredQueryAuthorizer(Options.Create(new StructuredQueryExecutionOptions
        {
            Enabled = true, ConnectionString = "Host=x;Database=db;Username=reader", DesignatedDatabase = "db", ReadOnlyRole = "reader",
            ReviewedTenantCompanyDiscoveryScopes = [$"{tenant:D}|scope-1|approval-1"]
        }), new TestEnvironment()).Authorize(query, tenant));
    }

    [Fact]
    public void DiscoveryConfigurationRequiresTheReviewedCompanyUniverse()
    {
        var validator = new StructuredQueryExecutionOptionsValidator(new TestEnvironment());
        var tenant = Guid.NewGuid();
        var result = validator.Validate(null, new StructuredQueryExecutionOptions
        {
            Enabled = true, ConnectionString = "Host=x;Database=db;Username=reader", DesignatedDatabase = "db", ReadOnlyRole = "reader",
            ReviewedTenantCompanyDiscoveryScopes = [$"{tenant:D}|scope-1|approval-1"]
        });
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("ReviewedTenantCompanyBindings", StringComparison.Ordinal));
    }

    private sealed class FixedResolver(ProductResolutionResult result) : ICanonicalQueryProductResolver
    {
        public Task<ProductResolutionResult> ResolveAsync(string? companyMention, string? productMention, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FixedCatalog(IReadOnlyList<ProductSalesObservation> rows) : IMonthlyProductCatalogReadRepository
    {
        public Task<IReadOnlyList<ProductSalesObservation>> GetProductCatalogAsync(string externalCompanyId, CancellationToken ct = default) =>
            Task.FromResult(rows);

        public Task<IReadOnlyList<ProductSalesObservation>> GetAllProductSalesAsync(string externalCompanyId, CancellationToken ct = default) =>
            Task.FromResult(rows);

        public Task<MonthlyProductFollowUpReadResult> GetProductFollowUpReadAsync(string externalCompanyId, JalaliPeriod notAfter, CancellationToken ct = default) =>
            throw new NotSupportedException("not used by the binder");
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "unit";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

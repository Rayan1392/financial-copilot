using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class CanonicalIntentCatalogBindingTests
{
    [Fact]
    public async Task ApprovedMeaningUsesExplicitCatalogReference()
    {
        var references = await CatalogReferences();
        var intent = Intent("operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare);
        var result = CanonicalIntentCatalogBinding.Bind(intent, references);
        Assert.Equal(CanonicalCatalogBindingStatus.Bound, result.Status);
        Assert.Same(intent, result.Intent);
        Assert.Single(result.References);
        Assert.NotEqual(intent.Measures[0].ConceptId, result.References[0].CatalogId);
    }

    [Fact]
    public async Task ApprovedMeaningWithoutQueryGrantIsUnauthorized()
    {
        var reference = (await CatalogReferences()).Single(item =>
            (item.CanonicalConceptBindings ?? []).Any(binding => binding.CanonicalConceptId == "operatingProfitPerShare"));
        var result = CanonicalIntentCatalogBinding.Bind(
            Intent("operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare),
            [reference with { Queryable = false }]);
        Assert.Equal(CanonicalCatalogBindingStatus.Unauthorized, result.Status);
        Assert.Single(result.References);
    }

    [Fact]
    public async Task MissingApprovedMeaningNeverUsesSimilarTitleOrUnit()
    {
        var result = CanonicalIntentCatalogBinding.Bind(
            Intent("netProfit", BusinessMeasureDimension.CurrencyAmount), await CatalogReferences());
        Assert.Equal(CanonicalCatalogBindingStatus.Incomplete, result.Status);
        Assert.Equal("approved_catalog_binding_missing:netProfit", Assert.Single(result.Diagnostics));
        Assert.Empty(result.References);
    }

    [Fact]
    public async Task UnrepresentedQualifierCannotSelectDifferentValueForm()
    {
        var intent = Intent("operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare) with
        {
            Measures = [Intent("operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare).Measures[0] with
                { Qualifiers = new(ValueForm: MeasureValueForm.PercentageChange) }]
        };
        var result = CanonicalIntentCatalogBinding.Bind(intent, await CatalogReferences());
        Assert.Equal(CanonicalCatalogBindingStatus.Incomplete, result.Status);
        Assert.Same(intent, result.Intent);
    }

    [Fact]
    public async Task AmbiguityAndUnsupportedOperationAreTyped()
    {
        var references = await CatalogReferences();
        var intent = Intent("operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare);
        Assert.Equal(CanonicalCatalogBindingStatus.Ambiguous,
            CanonicalIntentCatalogBinding.Bind(intent with { Ambiguity = new(true, [], ["ambiguous"]) }, references).Status);
        Assert.Equal(CanonicalCatalogBindingStatus.Unsupported,
            CanonicalIntentCatalogBinding.Bind(intent with { Operation = new(AnalyticalOperationKind.Unsupported) }, references).Status);
    }

    [Fact]
    public async Task RequestedUnitEqualToApprovedEffectiveUnitIsRepresentable()
    {
        var references = await CatalogReferences();
        var intent = Intent("operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare);
        var sameUnit = intent with
        {
            Measures = [intent.Measures[0] with { RequestedUnit = SemanticUnit.IRRPerShare }]
        };
        var result = CanonicalIntentCatalogBinding.Bind(sameUnit, references);
        Assert.Equal(CanonicalCatalogBindingStatus.Bound, result.Status);
        Assert.Equal("IRRPerShare", Assert.Single(result.References).EffectiveUnit);
    }

    [Fact]
    public async Task RequestedUnitNeedingConversionIsUnsupportedNotConverted()
    {
        var references = await CatalogReferences();
        var intent = Intent("operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare);
        var otherUnit = intent with
        {
            Measures = [intent.Measures[0] with { RequestedUnit = SemanticUnit.MillionIRR }]
        };
        var result = CanonicalIntentCatalogBinding.Bind(otherUnit, references);
        Assert.Equal(CanonicalCatalogBindingStatus.Unsupported, result.Status);
        Assert.Equal("requested_result_unit_conversion_not_supported", Assert.Single(result.Diagnostics));
        Assert.Same(otherUnit, result.Intent);
    }

    [Fact]
    public async Task ProductScopeIsRetainedAndRejectedBeforeNarrowPlanner()
    {
        var intent = Intent("operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare) with
        {
            Products = [new("گندله")]
        };
        var result = CanonicalIntentCatalogBinding.Bind(intent, await CatalogReferences());
        Assert.Equal(CanonicalCatalogBindingStatus.Unsupported, result.Status);
        Assert.Same(intent, result.Intent);
        Assert.Equal("گندله", Assert.Single(result.Intent.Products).UserLabel);
    }

    private static CanonicalSemanticIntent Intent(string concept, BusinessMeasureDimension dimension) => new(2,
        [new RequestedMeasure(concept, concept, MeasureCanonicalizationStatus.Canonicalized, [concept],
            MeasureConceptKind.FinancialStatementItem, dimension, new(), SemanticUnit.Unspecified,
            null, EffectiveUnitEvidence.Unresolved)],
        new(AnalyticalOperationKind.Retrieve), new(ComparisonMode.None),
        new(ReportingPeriodKind.LatestAvailable), new(EntityScopeKind.NamedCompanies, ["کگل"]),
        [], [], null, null, null, new(RequestedOutputKind.Scalar, []), [], new(false, [], []));

    private static async Task<IReadOnlyList<FinancialValueCatalogReference>> CatalogReferences()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var root = Path.Combine(directory!.FullName, "specs", "141-schema-aware-semantic-query-engine");
        var hierarchyPath = Path.Combine(root, "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var version = hierarchy.RootElement.GetProperty("catalogVersion").GetString()!;
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        return await new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath)
            .GetFinancialSourceReferencesAsync(version, hash, CancellationToken.None);
    }
}

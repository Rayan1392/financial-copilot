using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// The compiled direct-source query must carry the unit the catalog actually reviewed for the value,
/// and nothing else: a provider-reported source unit, or an owner-approved effective unit.
/// </summary>
public sealed class StructuredQueryDirectSourceEffectiveUnitTests
{
    private static StructuredSqlDirectSource Source(string? sourceUnit, string? sourceUnitStatus,
        string? effectiveUnit, string? effectiveUnitStatus, string? provenance) => new(
        "source_item_test", Guid.NewGuid(), "NoavaranCurrentApi", "IncomeStatement", 161,
        "tbl_src", "tbl_line", "tbl_stmt", "tbl_company", ["rel_a", "rel_b", "rel_c"], true,
        sourceUnit, sourceUnitStatus, "IRRPerShare", null, true, true, effectiveUnit, effectiveUnitStatus, provenance);

    [Fact]
    public void ProviderReportedSourceUnitIsCarriedAsRegisteredSourceUnit()
    {
        var unit = StructuredQueryCompiler.ResolveDirectSourceEffectiveUnit(
            Source("MillionIRR", "provider-reported", null, null, null));
        Assert.Equal(("MillionIRR", "provider-reported", "registered-source-unit"), unit);
    }

    [Fact]
    public void OwnerApprovedEffectiveUnitIsCarriedWithItsProvenance()
    {
        var unit = StructuredQueryCompiler.ResolveDirectSourceEffectiveUnit(
            Source("N/A", "unknown", "IRRPerShare", "owner-approved", "business-owner-confirmed:Feature141-2026-10-08"));
        Assert.Equal(("IRRPerShare", "owner-approved", "business-owner-confirmed:Feature141-2026-10-08"), unit);
    }

    [Theory]
    [InlineData("N/A", "unknown", "IRRPerShare", "requires-semantic-review")]
    [InlineData("N/A", "unknown", null, "owner-approved")]
    [InlineData(null, null, null, null)]
    public void UnreviewedUnitsStayNull(string? sourceUnit, string? sourceUnitStatus, string? effectiveUnit, string? status)
    {
        var unit = StructuredQueryCompiler.ResolveDirectSourceEffectiveUnit(
            Source(sourceUnit, sourceUnitStatus, effectiveUnit, status, null));
        Assert.Null(unit.Unit);
        Assert.Null(unit.Provenance);
    }

    [Fact]
    public void RealCatalogOperatingProfitPerShareCompilesWithOwnerApprovedUnit()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var root = Path.Combine(directory!.FullName, "specs", "141-schema-aware-semantic-query-engine");
        var snapshot = new StructuredQueryCatalogSnapshotBuilder().Build(
            Path.Combine(root, "SemanticCatalog"), Path.Combine(root, "artifacts", "navigation-hierarchy.yaml"));
        var source = snapshot.DirectSources.Single(item => item.StatementType == "IncomeStatement" && item.SourceItemId == 161
            && item.ProviderName == "NoavaranCurrentApi");
        Assert.Equal(("IRRPerShare", "owner-approved", "business-owner-confirmed:Feature141-2026-10-08"),
            StructuredQueryCompiler.ResolveDirectSourceEffectiveUnit(source));
    }
}

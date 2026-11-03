using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// A request for exactly reviewed catalog measures is governed by the measures' own catalog review
/// state, mirroring exact direct-source identity. Without measure governance in the context the
/// node-based rule still applies, so existing fixtures are unchanged.
/// </summary>
public sealed class MeasureGovernanceCompletenessTests
{
    private const string Measure = "metric_monthly_production_quantity";

    private static QuestionSchemaRequirements Requirements() => new(1, "1", "hash", [Measure],
        ["dim_company", "dim_monthly_period", "dim_product_identity"], ["company", "product"], ["rel_31e410fb3ae11956"],
        [], ["explicit-period"], [], ["scalar"], EntityReferences: ["کگل"],
        FinancialValueReferences: [new(nameof(FinancialValueReferenceType.MonthlyMeasure), Measure)])
    { ProductReferences = ["گندله"] };

    private static SchemaContext Context(IReadOnlyDictionary<string, MeasureGovernance>? governance)
    {
        var nodes = new[] { "financial-semantic-catalog", "domain-monthly-activity", "table-tbl_8ecd0432a4292998" };
        return new SchemaContext(1, "1", "hash", nodes,
            nodes.Select(id => new SchemaNodeGovernance(id, true, false, false, false)).ToArray(),
            [Measure], ["dim_company", "dim_monthly_period", "dim_product_identity"], ["company", "product"],
            ["rel_31e410fb3ae11956"], [], ["explicit-period"], [], ["scalar"], [],
            EntityReferences: ["کگل", "گندله"], MeasureGovernance: governance);
    }

    [Fact]
    public void ApprovedMeasureIsCompleteAuthorizedAndSemanticallyApprovedDespitePendingNavigationNodes()
    {
        var result = new SchemaContextCompletenessValidator().Validate(Requirements(), Context(
            new Dictionary<string, MeasureGovernance> { [Measure] = new(true, true, "owner-approved", "p") }));
        Assert.Equal(SchemaContextCompletenessStatus.Complete, result.Status);
        Assert.True(result.RetrievalComplete);
        Assert.True(result.SemanticReviewApproved);
        Assert.True(result.QueryAuthorized);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void UnapprovedMeasureIsNeverAuthorized(bool semantic, bool queryable)
    {
        var result = new SchemaContextCompletenessValidator().Validate(Requirements(), Context(
            new Dictionary<string, MeasureGovernance> { [Measure] = new(semantic, queryable, "pending-owner-review", null) }));
        Assert.True(result.RetrievalComplete);
        Assert.False(result.QueryAuthorized);
        Assert.False(result.SemanticReviewApproved);
    }

    [Fact]
    public void MissingMeasureGovernanceKeepsTheNodeBasedRule()
    {
        var result = new SchemaContextCompletenessValidator().Validate(Requirements(), Context(null));
        Assert.Equal(SchemaContextCompletenessStatus.Unsupported, result.Status);
        Assert.False(result.QueryAuthorized);
        Assert.Contains(result.Diagnostics, d => d.StartsWith("Navigation metadata is not approved", StringComparison.Ordinal));
    }

    [Fact]
    public void RealCatalogExposesOnlyTheApprovedMonthlyMeasures()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var root = Path.Combine(directory!.FullName, "specs", "141-schema-aware-semantic-query-engine");
        var snapshot = new StructuredQueryCatalogSnapshotBuilder().Build(
            Path.Combine(root, "SemanticCatalog"), Path.Combine(root, "artifacts", "navigation-hierarchy.yaml"));
        // Owner decision 2026-10-29 (production quantity) plus the core-capabilities directive of
        // 2026-10-29 (sales quantity, sales amount). The sales rate stays unapproved: unit unverified.
        var approved = snapshot.MonthlyMeasures.Where(m => m.SemanticApproved && m.Queryable).Select(m => m.CatalogId).Order().ToArray();
        Assert.Equal([Measure, "metric_monthly_sales", "metric_monthly_sales_quantity"], approved);
        var production = snapshot.MonthlyMeasures.Single(m => m.CatalogId == Measure);
        var lineTable = snapshot.Tables.Single(t => t.CatalogId == production.LineItemsTableCatalogId);
        var reportTable = snapshot.Tables.Single(t => t.CatalogId == production.ReportsTableCatalogId);
        Assert.True(lineTable.Queryable && lineTable.SemanticApproved && reportTable.Queryable && reportTable.SemanticApproved);
        // Least privilege: only the columns behind approved measures are query-authorized.
        Assert.False(snapshot.Columns.Single(c => c.TableCatalogId == lineTable.CatalogId && c.Name == "SalesRate").Queryable);
        Assert.All(new[] { "ProductionQuantity", "SalesQuantity", "SalesAmount" },
            name => Assert.True(snapshot.Columns.Single(c => c.TableCatalogId == lineTable.CatalogId && c.Name == name).Queryable));
    }
}

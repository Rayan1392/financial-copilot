using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class SchemaContextCompletenessValidatorTests
{
    private readonly SchemaContextCompletenessValidator _validator = new();

    [Fact]
    public void MissingUserMeasureIsIncompleteEvenWhenNavigatorDidNotDeclareIt()
    {
        var requirements = new QuestionSchemaRequirements(1, "1", "hash", ["metric-net-profit"], [], ["company"], [], [], [], [], []);
        var context = Context(measures: [], entities: ["company"]);

        var result = _validator.Validate(requirements, context);

        Assert.Equal(SchemaContextCompletenessStatus.Incomplete, result.Status);
        Assert.Contains("measure:metric-net-profit", result.MissingRequirements);
    }

    [Fact]
    public void CompleteMultiDomainContextPassesForIndependentRequirements()
    {
        var requirements = new QuestionSchemaRequirements(1, "1", "hash", ["metric-sales"], ["dim-monthly-period"], ["company"],
            ["rel-company-report"], ["provider-noavaran"], ["reported-month"], ["year-over-year"], ["line-chart"]);
        var context = Context(measures: ["metric-sales"], dimensions: ["dim-monthly-period"], entities: ["company"],
            relationships: ["rel-company-report"], sources: ["provider-noavaran"], periods: ["reported-month"],
            comparisons: ["year-over-year"], presentation: ["line-chart"]);

        Assert.True(_validator.Validate(requirements, context).IsComplete);
    }

    [Fact]
    public void AmbiguousRequirementNeedsClarificationAndCatalogMismatchFailsClosed()
    {
        var ambiguous = new QuestionSchemaRequirements(1, "1", "hash", [], [], [], [], [], [], [], [], true, "Ambiguous company.");
        Assert.Equal(SchemaContextCompletenessStatus.ClarificationRequired, _validator.Validate(ambiguous, Context()).Status);

        var expected = new QuestionSchemaRequirements(1, "2", "different", [], [], [], [], [], [], [], []);
        Assert.Equal(SchemaContextCompletenessStatus.CatalogMismatch, _validator.Validate(expected, Context()).Status);
    }

    [Fact]
    public void UnknownAndUnapprovedNavigationNodesAreRejectedWithoutInferringQueryAuthorization()
    {
        var requirements = new QuestionSchemaRequirements(1, "1", "hash", [], [], [], [], [], [], [], []);
        var unknown = Context(nodeIds: ["invented"], governance: []);
        Assert.Equal(SchemaContextCompletenessStatus.Unsupported, _validator.Validate(requirements, unknown).Status);

        var unapproved = Context(nodeIds: ["candidate"], governance: [new("candidate", true, false, false, false)]);
        Assert.Equal(SchemaContextCompletenessStatus.Unsupported, _validator.Validate(requirements, unapproved).Status);

        var approvedNavigationOnly = Context(nodeIds: ["navigation-only"], governance: [new("navigation-only", true, true, false, false)]);
        Assert.True(_validator.Validate(requirements, approvedNavigationOnly).IsComplete);
        Assert.False(approvedNavigationOnly.NodeGovernance[0].QueryAuthorized);
    }

    [Fact]
    public void UnapprovedContextStillReportsIndependentRetrievalGapsAndGovernanceSeparately()
    {
        var requirements = new QuestionSchemaRequirements(1, "1", "hash", ["metric-needed"], [], [], [], [], [], [], []);
        var context = Context(nodeIds: ["candidate"], governance: [new("candidate", true, false, false, false)]);

        var result = _validator.Validate(requirements, context);

        Assert.Equal(SchemaContextCompletenessStatus.Unsupported, result.Status);
        Assert.False(result.RetrievalComplete);
        Assert.Contains("measure:metric-needed", result.MissingRequirements);
        Assert.False(result.SemanticReviewApproved);
        Assert.False(result.QueryAuthorized);
    }

    private static SchemaContext Context(
        IReadOnlyList<string>? measures = null,
        IReadOnlyList<string>? dimensions = null,
        IReadOnlyList<string>? entities = null,
        IReadOnlyList<string>? relationships = null,
        IReadOnlyList<string>? sources = null,
        IReadOnlyList<string>? periods = null,
        IReadOnlyList<string>? comparisons = null,
        IReadOnlyList<string>? presentation = null,
        IReadOnlyList<string>? nodeIds = null,
        IReadOnlyList<SchemaNodeGovernance>? governance = null) =>
        new(1, "1", "hash", nodeIds ?? [], governance ?? [], measures ?? [], dimensions ?? [], entities ?? [], relationships ?? [], sources ?? [], periods ?? [],
            comparisons ?? [], presentation ?? [], []);
}

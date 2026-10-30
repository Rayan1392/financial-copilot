using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// The Feature 141 default statement basis: a statement-item question that names no basis is standalone (غیرتلفیقی);
/// a basis the model stated, or consolidated wording in the question, is never replaced by the default.
/// </summary>
public sealed class Feature141StatementBasisDefaultTests
{
    private const string NoBasisQuestion = "سود خالص وبملت در آخرین دوره چقدر بوده؟";

    private static CanonicalSemanticIntent Statement(FinancialStatementScope scope, MeasureConceptKind kind = MeasureConceptKind.FinancialStatementItem) => new(2,
        [new RequestedMeasure("سود خالص", "netProfit", MeasureCanonicalizationStatus.Canonicalized, ["netProfit"],
            kind, BusinessMeasureDimension.CurrencyAmount, new(StatementScope: scope), SemanticUnit.Unspecified, null,
            EffectiveUnitEvidence.Unresolved)],
        new(AnalyticalOperationKind.Retrieve), new(ComparisonMode.None), new(ReportingPeriodKind.LatestAvailable),
        new(EntityScopeKind.NamedCompanies, ["وبملت"]), [], [], null, null, null,
        new(RequestedOutputKind.Scalar, []), [], new(false, [], []));

    private static FinancialStatementScope ScopeAfterDefault(CanonicalSemanticIntent intent, string question) =>
        Assert.Single(Feature141ChatQueryService.WithDefaultStatementBasis(intent, question).Measures).Qualifiers.StatementScope;

    [Fact]
    public void AStatementQuestionNamingNoBasisDefaultsToStandalone() =>
        Assert.Equal(FinancialStatementScope.Standalone,
            ScopeAfterDefault(Statement(FinancialStatementScope.Unspecified), NoBasisQuestion));

    [Fact]
    public void AStatedBasisIsNeverReplacedByTheDefault() =>
        Assert.Equal(FinancialStatementScope.Consolidated,
            ScopeAfterDefault(Statement(FinancialStatementScope.Consolidated), NoBasisQuestion));

    [Fact]
    public void ConsolidatedWordingIsNeverReplacedByTheDefault()
    {
        // The model did not capture the consolidated basis as a qualifier; the default must not turn it into standalone.
        Assert.Equal(FinancialStatementScope.Unspecified,
            ScopeAfterDefault(Statement(FinancialStatementScope.Unspecified), "سود خالص وبملت بر اساس صورت مالی تلفیقی چقدر بوده؟"));
    }

    [Fact]
    public void ANonStatementMeasureIsNotDefaulted() =>
        Assert.Equal(FinancialStatementScope.Unspecified,
            ScopeAfterDefault(Statement(FinancialStatementScope.Unspecified, MeasureConceptKind.ProductionQuantity), NoBasisQuestion));
}

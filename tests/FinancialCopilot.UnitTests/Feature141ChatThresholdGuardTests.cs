using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// The chat guard for the general planner path: a numeric threshold it cannot honor (monthly production or sales filters)
/// must never be executed as an unfiltered retrieval; only the threshold that restates discovery itself is allowed.
/// </summary>
public sealed class Feature141ChatThresholdGuardTests
{
    private static CanonicalSemanticIntent Intent(AnalyticalOperationKind operation, FilterExpression? filters) => new(2,
        [new RequestedMeasure("تولید", CanonicalBusinessMeasureConceptRegistry.ProductionQuantity, MeasureCanonicalizationStatus.Canonicalized,
            [CanonicalBusinessMeasureConceptRegistry.ProductionQuantity], MeasureConceptKind.ProductionQuantity,
            BusinessMeasureDimension.PhysicalQuantity, new(), SemanticUnit.Unspecified, null, EffectiveUnitEvidence.Unresolved)],
        new(operation), new(ComparisonMode.None), new(ReportingPeriodKind.ExplicitMonth, ReportingPeriodGranularity.Month, "شهریور ۱۴۰۵"),
        new(EntityScopeKind.UnboundCompanyPopulation, []), [new("گندله")], [], filters, null, null,
        new(RequestedOutputKind.Table, []), [], new(false, [], []));

    private static MeasureThresholdFilter Production(decimal value, NumericComparisonOperator op = NumericComparisonOperator.GreaterThan) =>
        new(CanonicalBusinessMeasureConceptRegistry.ProductionQuantity, new NumericThreshold(value, SemanticUnit.Ton, op));

    [Fact]
    public void MonthlyThresholdsAreRefusedButPositiveProductionDiscoveryIsNot()
    {
        // "کدام شرکت‌ها تولید گندله بالای ۵۰۰ تن داشته‌اند؟"
        Assert.True(Feature141ChatQueryService.DropsMaterialThreshold(Intent(AnalyticalOperationKind.DiscoverPopulation, Production(500m))));
        Assert.True(Feature141ChatQueryService.DropsMaterialThreshold(Intent(AnalyticalOperationKind.Retrieve, Production(0m))));
        Assert.True(Feature141ChatQueryService.DropsMaterialThreshold(Intent(AnalyticalOperationKind.DiscoverPopulation,
            Production(0m, NumericComparisonOperator.GreaterThanOrEqual))));
        Assert.True(Feature141ChatQueryService.DropsMaterialThreshold(Intent(AnalyticalOperationKind.DiscoverPopulation,
            new FilterGroup(FilterLogicalOperator.And, [Production(0m), Production(500m)]))));
        // "کدام شرکت‌ها در شهریور ۱۴۰۵ گندله تولید کرده‌اند؟" with or without the restating "> 0" threshold.
        Assert.False(Feature141ChatQueryService.DropsMaterialThreshold(Intent(AnalyticalOperationKind.DiscoverPopulation, Production(0m))));
        Assert.False(Feature141ChatQueryService.DropsMaterialThreshold(Intent(AnalyticalOperationKind.DiscoverPopulation, null)));
    }
}

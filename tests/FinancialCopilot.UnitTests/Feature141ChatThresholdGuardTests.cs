using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
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

    [Theory]
    [InlineData(MeasureReportingBasis.MonthlyCumulative, true)] // «تولید تجمعی … از ابتدای سال مالی»: OutputType 1, not supported
    [InlineData(MeasureReportingBasis.MonthlyDiscrete, false)]  // «… در مرداد ۱۴۰۵»: single-month OutputType 0
    [InlineData(MeasureReportingBasis.Unspecified, false)]
    public void CumulativeMonthlyScopeIsRefusedInsteadOfAnsweredWithOneMonth(MeasureReportingBasis basis, bool refused)
    {
        var intent = Intent(AnalyticalOperationKind.Retrieve, null);
        intent = intent with { Measures = [intent.Measures[0] with { Qualifiers = new(ReportingBasis: basis) }] };
        Assert.Equal(refused, Feature141ChatQueryService.RequestsCumulativeMonthlyScope(intent));
    }

    [Theory]
    [InlineData(MeasureConceptKind.ProductionQuantity, "metric_monthly_production_quantity")]
    [InlineData(MeasureConceptKind.SalesQuantity, "metric_monthly_sales_quantity")]
    [InlineData(MeasureConceptKind.SalesAmount, "metric_monthly_sales")]
    [InlineData(MeasureConceptKind.FinancialStatementItem, null)]
    public void TypedMonthlyMeasureIsRequiredInTheRequirementsOutline(MeasureConceptKind kind, string? catalogId)
    {
        // «تولید گندله کگل در شهریور ۱۴۰۵»: no multi-word alias of the production measure occurs in the question.
        var intent = Intent(AnalyticalOperationKind.Retrieve, null);
        intent = intent with
        {
            Measures = [intent.Measures[0] with { Kind = kind }],
            EntityScope = new(EntityScopeKind.NamedCompanies, ["کگل"])
        };
        Assert.Equal(catalogId is null ? [] : [catalogId], Feature141ChatQueryService.RequiredMonthlyMeasureIds(intent));
        Assert.Empty(Feature141ChatQueryService.RequiredMonthlyMeasureIds(intent with { Measures = [intent.Measures[0], intent.Measures[0]] }));
        // Unbound producer discovery («کدام شرکت‌ها … گندله تولید کرده‌اند») keeps the planner's deterministic rewrite.
        Assert.Empty(Feature141ChatQueryService.RequiredMonthlyMeasureIds(
            intent with { EntityScope = new(EntityScopeKind.UnboundCompanyPopulation, []) }));
    }

    [Fact]
    public void NamedStatementPeriodIsAnsweredOnlyWhenItIsTheLatestReportedPeriod()
    {
        // As extracted live for «سود خالص وبملت در دوره شش‌ماهه منتهی به شهریور ۱۴۰۵ بر اساس صورت مالی جداگانه چقدر بوده؟».
        const string question = "سود خالص وبملت در دوره شش‌ماهه منتهی به شهریور ۱۴۰۵ بر اساس صورت مالی جداگانه چقدر بوده؟";
        var named = StatementPeriodRequirement.TryCreate(Statement(
            new(ReportingPeriodKind.ExplicitMonth, ReportingPeriodGranularity.Month, "شهریور ۱۴۰۵", 6),
            [new UserConstraint("periodDuration", "شش‌ماهه", ConstraintProvenance.Explicit)]), question);

        Assert.NotNull(named);
        var (intent, requirement) = named.Value;
        Assert.Equal(ReportingPeriodKind.LatestAvailable, intent.Period.Kind);
        Assert.Empty(intent.Constraints);
        Assert.Equal(6, requirement.Months);
        Assert.True(requirement.IsSatisfiedBy([PeriodEvidence("2026-03-21", "2026-09-22", "SixMonths")]));
        // A different length ending in the same month, or another month, is never shown as the requested period.
        Assert.False(requirement.IsSatisfiedBy([PeriodEvidence("2026-06-22", "2026-09-22", "ThreeMonths")]));
        Assert.False(requirement.IsSatisfiedBy([PeriodEvidence("2026-03-21", "2026-06-21", "ThreeMonths")]));
        Assert.Contains("آخرین دورهٔ گزارش‌شده", StatementPeriodRequirement.MismatchText([PeriodEvidence("2026-03-21", "2026-06-21", "ThreeMonths")]));
        // Without a named month, or for several companies, the intent is left unchanged.
        Assert.Null(StatementPeriodRequirement.TryCreate(Statement(new(ReportingPeriodKind.LatestAvailable), []), "سود خالص وبملت"));
        Assert.Null(StatementPeriodRequirement.TryCreate(Statement(
            new(ReportingPeriodKind.ExplicitMonth, ReportingPeriodGranularity.Month, "شهریور ۱۴۰۵"), [], ["کگل", "کگهر"]), question));
    }

    private static CanonicalSemanticIntent Statement(ReportingPeriodIntent period, IReadOnlyList<UserConstraint> constraints,
        IReadOnlyList<string>? companies = null) => new(2,
        [new RequestedMeasure("سود خالص", "netProfit", MeasureCanonicalizationStatus.Canonicalized, ["netProfit"],
            MeasureConceptKind.FinancialStatementItem, BusinessMeasureDimension.CurrencyAmount, new(), SemanticUnit.Unspecified, null,
            EffectiveUnitEvidence.Unresolved)],
        new(AnalyticalOperationKind.Retrieve), new(ComparisonMode.None), period,
        new(EntityScopeKind.NamedCompanies, companies ?? ["وبملت"]), [], [], null, null, null,
        new(RequestedOutputKind.Scalar, []), constraints, new(false, [], []));

    private static StructuredAnswerEvidence PeriodEvidence(string start, string end, string periodType) =>
        new("بانک ملت (وبملت)", null, start, end, "NoavaranCurrentApi", [], [],
            new Dictionary<string, string> { ["PeriodType"] = periodType });
}

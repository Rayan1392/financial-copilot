using System.Text.Json.Serialization;

namespace FinancialCopilot.Application.FinancialData.Semantics.Intent;

/// <summary>
/// Schema-independent interpretation of a financial question. This contract deliberately contains
/// no catalog, database, provider, or physical-entity identifiers.
/// </summary>
public sealed record CanonicalSemanticIntent(
    int ContractVersion,
    IReadOnlyList<RequestedMeasure> Measures,
    AnalyticalOperation Operation,
    ComparisonIntent Comparison,
    ReportingPeriodIntent Period,
    EntityScope EntityScope,
    IReadOnlyList<ProductConstraint> Products,
    IReadOnlyList<CategoryConstraint> Categories,
    FilterExpression? Filters,
    AggregationIntent? Aggregation,
    RankingIntent? Ranking,
    RequestedOutputShape Output,
    IReadOnlyList<UserConstraint> Constraints,
    AmbiguityAssessment Ambiguity);

public enum MeasureConceptKind
{
    FinancialStatementItem,
    ProviderReportedIndicator,
    ProductionQuantity,
    SalesQuantity,
    SalesAmount,
    SalesRate,
    OtherBusinessMeasure
}

public enum SemanticUnit
{
    Unspecified,
    Count,
    Ton,
    IRR,
    MillionIRR,
    IRRPerShare,
    Percent,
    Ratio,
    CurrencyPerUnit,
    BillionIRR
}

public enum BusinessMeasureDimension
{
    Unknown,
    PhysicalQuantity,
    CurrencyAmount,
    CurrencyPerShare,
    Percentage,
    Ratio,
    CurrencyPerUnit
}

public enum MeasureCanonicalizationStatus
{
    Canonicalized,
    Ambiguous,
    Unrecognized
}

public enum MeasureValueForm
{
    ReportedValue,
    AbsoluteChange,
    PercentageChange
}

public enum MeasureReportingBasis
{
    Unspecified,
    MonthlyDiscrete,
    MonthlyCumulative,
    QuarterlyDiscrete,
    QuarterlyCumulative,
    Annual,
    TrailingTwelveMonths
}

public enum FinancialStatementScope
{
    Unspecified,
    Standalone,
    Consolidated
}

public enum MeasureValueGrain
{
    Unspecified,
    CompanyTotal,
    ProductLevel
}

public enum EffectiveUnitEvidence
{
    Unresolved,
    CatalogApproved,
    ProviderReported,
    OwnerApproved
}

public sealed record MeasureQualifiers(
    MeasureValueForm ValueForm = MeasureValueForm.ReportedValue,
    MeasureReportingBasis ReportingBasis = MeasureReportingBasis.Unspecified,
    FinancialStatementScope StatementScope = FinancialStatementScope.Unspecified,
    MeasureValueGrain ValueGrain = MeasureValueGrain.Unspecified);

public sealed record RequestedMeasure(
    string UserPhrase,
    string? ConceptId,
    MeasureCanonicalizationStatus CanonicalizationStatus,
    IReadOnlyList<string> CandidateConceptIds,
    MeasureConceptKind Kind,
    BusinessMeasureDimension Dimension,
    MeasureQualifiers Qualifiers,
    SemanticUnit RequestedUnit,
    SemanticUnit? EffectiveSourceUnit,
    EffectiveUnitEvidence EffectiveSourceUnitEvidence);

public enum AnalyticalOperationKind
{
    Retrieve,
    AbsoluteChange,
    GrowthRate,
    Compare,
    TimeSeries,
    Aggregate,
    Rank,
    DiscoverPopulation,
    Unsupported
}

public sealed record AnalyticalOperation(
    AnalyticalOperationKind Kind,
    string? Direction = null,
    string? UnsupportedReason = null);

public enum ComparisonMode
{
    None,
    MonthOverMonth,
    YearOverYear,
    PeriodOverPeriod,
    MultiCompany,
    CrossMeasure,
    StatementScope
}

public sealed record ComparisonIntent(
    ComparisonMode Mode,
    string? BaselineDescription = null,
    string? TargetDescription = null);

public enum ReportingPeriodKind
{
    Unspecified,
    LatestAvailable,
    ExplicitMonth,
    ExplicitQuarter,
    ExplicitFiscalYear,
    RelativePeriods,
    DateRange
}

public enum ReportingPeriodGranularity
{
    Unspecified,
    Month,
    Quarter,
    FiscalYear,
    Day
}

public sealed record ReportingPeriodIntent(
    ReportingPeriodKind Kind,
    ReportingPeriodGranularity Granularity = ReportingPeriodGranularity.Unspecified,
    string? UserPeriodLabel = null,
    int? NumberOfPeriods = null,
    string? StartLabel = null,
    string? EndLabel = null);

public enum EntityScopeKind
{
    NamedCompanies,
    UnboundCompanyPopulation
}

public sealed record EntityScope(EntityScopeKind Kind, IReadOnlyList<string> CompanyLabels);

public sealed record ProductConstraint(string UserLabel, string? ConceptLabel = null, ConstraintProvenance Provenance = ConstraintProvenance.Explicit);

public sealed record CategoryConstraint(string UserLabel, string CategoryKind, ConstraintProvenance Provenance = ConstraintProvenance.Explicit);

public enum NumericComparisonOperator
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed record NumericThreshold(decimal Value, SemanticUnit Unit, NumericComparisonOperator Operator);

public enum FilterLogicalOperator
{
    And,
    Or
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FilterGroup), "group")]
[JsonDerivedType(typeof(MeasureThresholdFilter), "measureThreshold")]
[JsonDerivedType(typeof(ProductFilter), "product")]
[JsonDerivedType(typeof(CategoryFilter), "category")]
public abstract record FilterExpression;

public sealed record FilterGroup(FilterLogicalOperator Operator, IReadOnlyList<FilterExpression> Children) : FilterExpression;

public sealed record MeasureThresholdFilter(string MeasureConcept, NumericThreshold Threshold) : FilterExpression;

public sealed record ProductFilter(string UserLabel) : FilterExpression;

public sealed record CategoryFilter(string UserLabel, string CategoryKind) : FilterExpression;

public sealed record AggregationIntent(string Function, string Scope);

public sealed record RankingIntent(string Direction, string MeasureConcept, int? Limit = null);

public enum RequestedOutputKind
{
    Scalar,
    Table,
    TimeSeries,
    RankedList,
    Comparison,
    Explanation
}

public sealed record RequestedOutputShape(RequestedOutputKind Kind, IReadOnlyList<string> RequestedFields);

public enum ConstraintProvenance
{
    Explicit,
    Inferred
}

public sealed record UserConstraint(string Name, string Value, ConstraintProvenance Provenance);

public sealed record AmbiguityAssessment(bool RequiresClarification, IReadOnlyList<string> Questions, IReadOnlyList<string> UnresolvedPoints);

public sealed record CanonicalIntentValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}


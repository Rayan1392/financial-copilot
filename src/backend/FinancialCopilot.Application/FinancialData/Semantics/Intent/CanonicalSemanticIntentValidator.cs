namespace FinancialCopilot.Application.FinancialData.Semantics.Intent;

/// <summary>Validates the internal consistency of a semantic intent without consulting catalog availability.</summary>
public sealed class CanonicalSemanticIntentValidator
{
    public const int SupportedContractVersion = 2;
    private readonly CanonicalBusinessMeasureConceptRegistry _conceptRegistry;

    public CanonicalSemanticIntentValidator(CanonicalBusinessMeasureConceptRegistry? conceptRegistry = null) =>
        _conceptRegistry = conceptRegistry ?? CanonicalBusinessMeasureConceptRegistry.Default;

    public CanonicalIntentValidationResult Validate(CanonicalSemanticIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var errors = new List<string>();
        var measures = intent.Measures ?? [];

        if (intent.ContractVersion != SupportedContractVersion)
            errors.Add("unsupported_contract_version");
        foreach (var measure in measures)
        {
            if (measure is null || string.IsNullOrWhiteSpace(measure.UserPhrase))
            {
                errors.Add("invalid_measure_concept");
                continue;
            }
            if (!Enum.IsDefined(measure.CanonicalizationStatus) || !Enum.IsDefined(measure.Kind)
                || !Enum.IsDefined(measure.Dimension) || !Enum.IsDefined(measure.RequestedUnit)
                || !Enum.IsDefined(measure.EffectiveSourceUnitEvidence) || measure.Qualifiers is null
                || !Enum.IsDefined(measure.Qualifiers.ValueForm) || !Enum.IsDefined(measure.Qualifiers.ReportingBasis)
                || !Enum.IsDefined(measure.Qualifiers.StatementScope) || !Enum.IsDefined(measure.Qualifiers.ValueGrain))
                errors.Add("invalid_measure_semantics");
            if (measure.EffectiveSourceUnit is null && measure.EffectiveSourceUnitEvidence != EffectiveUnitEvidence.Unresolved)
                errors.Add("source_unit_evidence_without_source_unit");
            else if (measure.EffectiveSourceUnit is not null && measure.EffectiveSourceUnitEvidence == EffectiveUnitEvidence.Unresolved)
                errors.Add("source_unit_cannot_be_inferred_by_intent_extractor");

            if (measure.CandidateConceptIds is null)
            {
                errors.Add("invalid_measure_candidates");
                continue;
            }

            if (measure.CanonicalizationStatus == MeasureCanonicalizationStatus.Canonicalized)
            {
                if (!_conceptRegistry.TryGet(measure.ConceptId, out var definition))
                    errors.Add("canonical_measure_id_not_registered");
                else if (measure.Dimension != definition.Dimension || measure.Kind != definition.Kind)
                    errors.Add("canonical_measure_definition_mismatch");
                else if (!RequestedUnitMatchesMeasure(measure))
                    errors.Add("requested_unit_dimension_mismatch");
                if (measure.EffectiveSourceUnit is { } sourceUnit &&
                    (sourceUnit == SemanticUnit.Unspecified || !UnitMatchesDimension(sourceUnit, measure.Dimension)))
                    errors.Add("effective_source_unit_dimension_mismatch");
                if (!QualifiersAreCompatible(measure))
                    errors.Add("incompatible_measure_qualifiers");
            }
            else if (measure.ConceptId is not null || measure.Dimension != BusinessMeasureDimension.Unknown)
                errors.Add("unresolved_measure_must_not_claim_concept_or_dimension");
            if (measure.CandidateConceptIds.Any(candidate => !_conceptRegistry.TryGet(candidate, out _)))
                errors.Add("unknown_measure_candidate_id");
            if (measure.CanonicalizationStatus == MeasureCanonicalizationStatus.Ambiguous && measure.CandidateConceptIds.Count < 2)
                errors.Add("ambiguous_measure_candidates_required");
        }
        if (intent.Operation is null)
            errors.Add("missing_operation");
        else if (intent.Operation.Kind == AnalyticalOperationKind.Unsupported && string.IsNullOrWhiteSpace(intent.Operation.UnsupportedReason))
            errors.Add("unsupported_operation_reason_required");
        else if (intent.Operation.Kind != AnalyticalOperationKind.Unsupported && intent.Operation.UnsupportedReason is not null)
            errors.Add("unsupported_reason_on_supported_operation");
        if (intent.Comparison is null || intent.Period is null || intent.EntityScope is null || intent.Output is null || intent.Ambiguity is null)
            errors.Add("missing_required_section");

        if (intent.Operation is not null && intent.Comparison is not null)
        {
            var hasPercentageChangeMeasure = measures.Any(measure =>
                measure?.Qualifiers?.ValueForm == MeasureValueForm.PercentageChange);
            var populationComparisonPredicate = intent.Operation.Kind == AnalyticalOperationKind.DiscoverPopulation
                && intent.EntityScope?.Kind == EntityScopeKind.UnboundCompanyPopulation
                && intent.Output?.Kind == RequestedOutputKind.Table
                && intent.Comparison.Mode is (ComparisonMode.MonthOverMonth or ComparisonMode.YearOverYear or ComparisonMode.PeriodOverPeriod)
                && (!string.IsNullOrWhiteSpace(intent.Operation.Direction)
                    || (intent.Constraints ?? []).Any(constraint =>
                        string.Equals(constraint.Name, "comparisonCondition", StringComparison.OrdinalIgnoreCase)));
            var comparative = intent.Operation.Kind is AnalyticalOperationKind.Compare
                or AnalyticalOperationKind.AbsoluteChange or AnalyticalOperationKind.GrowthRate
                || (hasPercentageChangeMeasure && intent.Operation.Kind is (AnalyticalOperationKind.Rank or AnalyticalOperationKind.DiscoverPopulation))
                || populationComparisonPredicate;
            var clarificationPending = intent.Ambiguity?.RequiresClarification == true;
            if (comparative && intent.Comparison.Mode == ComparisonMode.None && !clarificationPending)
                errors.Add("comparison_mode_required");
            if (!comparative && intent.Operation.Kind != AnalyticalOperationKind.TimeSeries
                && intent.Comparison.Mode != ComparisonMode.None)
                errors.Add("comparison_mode_conflicts_with_operation");
            if (intent.Comparison.Mode == ComparisonMode.PeriodOverPeriod
                && (string.IsNullOrWhiteSpace(intent.Comparison.BaselineDescription)
                    || string.IsNullOrWhiteSpace(intent.Comparison.TargetDescription)) && !clarificationPending)
                errors.Add("comparison_periods_incomplete");
        }

        if (intent.Period is not null)
        {
            if (intent.Period.Kind == ReportingPeriodKind.RelativePeriods
                && (intent.Period.NumberOfPeriods is null or <= 0 || intent.Period.Granularity == ReportingPeriodGranularity.Unspecified))
                errors.Add("relative_period_incomplete");
            if (intent.Period.Kind == ReportingPeriodKind.DateRange
                && (string.IsNullOrWhiteSpace(intent.Period.StartLabel) || string.IsNullOrWhiteSpace(intent.Period.EndLabel)))
                errors.Add("date_range_incomplete");
            if (intent.Period.NumberOfPeriods is <= 0)
                errors.Add("invalid_period_count");
        }

        if (intent.EntityScope is not null)
        {
            var hasNames = intent.EntityScope.CompanyLabels is { Count: > 0 };
            if (intent.EntityScope.Kind == EntityScopeKind.NamedCompanies && !hasNames)
                errors.Add("named_company_scope_without_names");
            if (intent.EntityScope.Kind == EntityScopeKind.UnboundCompanyPopulation && hasNames)
                errors.Add("population_scope_contains_named_companies");
            if (intent.Operation?.Kind == AnalyticalOperationKind.DiscoverPopulation
                && intent.EntityScope.Kind != EntityScopeKind.UnboundCompanyPopulation)
                errors.Add("population_discovery_requires_unbound_scope");
        }

        if (intent.Ranking is not null)
        {
            if (string.IsNullOrWhiteSpace(intent.Ranking.MeasureConcept))
                errors.Add("ranking_measure_required");
            else if (!ReferencesRequestedMeasure(intent.Ranking.MeasureConcept, measures))
                errors.Add("ranking_measure_not_requested");
            if (intent.Ranking.Limit is <= 0)
                errors.Add("invalid_ranking_limit");
        }

        if (intent.Operation?.Kind == AnalyticalOperationKind.Rank && intent.Ranking is null)
            errors.Add("ranking_arguments_required");
        if (intent.Operation?.Kind == AnalyticalOperationKind.Aggregate && intent.Aggregation is null)
            errors.Add("aggregation_arguments_required");
        if (intent.Aggregation is not null
            && (string.IsNullOrWhiteSpace(intent.Aggregation.Function) || string.IsNullOrWhiteSpace(intent.Aggregation.Scope)))
            errors.Add("aggregation_scope_incomplete");

        foreach (var filter in Flatten(intent.Filters))
        {
            if (filter is MeasureThresholdFilter threshold)
            {
                if (string.IsNullOrWhiteSpace(threshold.MeasureConcept) || !ReferencesRequestedMeasure(threshold.MeasureConcept, measures))
                    errors.Add("filter_measure_not_requested");
                if (threshold.Threshold is null)
                    errors.Add("filter_threshold_missing");
                else if (!IsUnitCompatible(threshold.MeasureConcept, threshold.Threshold.Unit, measures))
                    errors.Add("measure_unit_dimension_mismatch");
            }
        }
        if (HasContradictoryThresholds(intent.Filters))
            errors.Add("contradictory_numeric_constraints");

        if ((intent.Products ?? []).Any(x => x is null || string.IsNullOrWhiteSpace(x.UserLabel))
            || (intent.Categories ?? []).Any(x => x is null || string.IsNullOrWhiteSpace(x.UserLabel) || string.IsNullOrWhiteSpace(x.CategoryKind)))
            errors.Add("invalid_product_or_category_constraint");
        if ((intent.Constraints ?? []).Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Value)
            || !Enum.IsDefined(x.Provenance)))
            errors.Add("invalid_constraint_provenance");
        if (intent.Ambiguity is { RequiresClarification: true, Questions.Count: 0 })
            errors.Add("clarification_question_required");

        return new CanonicalIntentValidationResult(errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<FilterExpression> Flatten(FilterExpression? expression)
    {
        if (expression is null) yield break;
        yield return expression;
        if (expression is FilterGroup group)
            foreach (var child in group.Children ?? [])
                foreach (var nested in Flatten(child)) yield return nested;
    }

    private static bool HasContradictoryThresholds(FilterExpression? expression)
    {
        foreach (var branch in GetConjunctionBranches(expression))
        {
            var groups = branch.Where(x => x.Threshold is not null)
                .GroupBy(x => (x.MeasureConcept.ToUpperInvariant(), x.Threshold.Unit));
            foreach (var group in groups)
            {
                decimal? lower = null, upper = null;
                var lowerInclusive = false;
                var upperInclusive = false;
                foreach (var filter in group)
                {
                    var threshold = filter.Threshold;
                    switch (threshold.Operator)
                    {
                        case NumericComparisonOperator.GreaterThan:
                        case NumericComparisonOperator.GreaterThanOrEqual:
                            if (lower is null || threshold.Value > lower || (threshold.Value == lower && threshold.Operator == NumericComparisonOperator.GreaterThan))
                            {
                                lower = threshold.Value;
                                lowerInclusive = threshold.Operator == NumericComparisonOperator.GreaterThanOrEqual;
                            }
                            break;
                        case NumericComparisonOperator.LessThan:
                        case NumericComparisonOperator.LessThanOrEqual:
                            if (upper is null || threshold.Value < upper || (threshold.Value == upper && threshold.Operator == NumericComparisonOperator.LessThan))
                            {
                                upper = threshold.Value;
                                upperInclusive = threshold.Operator == NumericComparisonOperator.LessThanOrEqual;
                            }
                            break;
                        case NumericComparisonOperator.Equal:
                            if ((lower is not null && threshold.Value < lower) || (upper is not null && threshold.Value > upper)) return true;
                            lower = upper = threshold.Value;
                            lowerInclusive = upperInclusive = true;
                            break;
                    }
                }
                if (lower is not null && upper is not null
                    && (lower > upper || (lower == upper && !(lowerInclusive && upperInclusive)))) return true;
            }
        }
        return false;
    }

    private static IEnumerable<IReadOnlyList<MeasureThresholdFilter>> GetConjunctionBranches(FilterExpression? expression)
    {
        if (expression is MeasureThresholdFilter threshold)
            return [[threshold]];
        if (expression is not FilterGroup group || group.Children is null || group.Children.Count == 0)
            return [[]];
        if (group.Operator == FilterLogicalOperator.Or)
            return group.Children.SelectMany(GetConjunctionBranches).ToArray();

        var branches = new List<IReadOnlyList<MeasureThresholdFilter>> { Array.Empty<MeasureThresholdFilter>() };
        foreach (var child in group.Children)
        {
            var childBranches = GetConjunctionBranches(child);
            branches = branches.SelectMany(left => childBranches.Select(right => (IReadOnlyList<MeasureThresholdFilter>)left.Concat(right).ToArray())).ToList();
        }
        return branches;
    }

    private bool IsUnitCompatible(string concept, SemanticUnit unit, IReadOnlyList<RequestedMeasure> measures)
    {
        if (unit == SemanticUnit.Unspecified) return true;
        var measure = measures.FirstOrDefault(x => string.Equals(x.ConceptId, concept, StringComparison.OrdinalIgnoreCase)
            || string.Equals(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(x.UserPhrase),
                CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(concept), StringComparison.Ordinal));
        if (measure is null) return false;
        // Threshold units describe the operation result: percentage change is percent even when
        // the underlying measure remains a physical quantity or monetary amount.
        if (measure.Qualifiers.ValueForm == MeasureValueForm.PercentageChange)
            return unit == SemanticUnit.Percent;
        if (measure.CanonicalizationStatus == MeasureCanonicalizationStatus.Canonicalized)
            return UnitMatchesDimension(unit, measure.Dimension);
        return unit switch
        {
            SemanticUnit.Ton => measure.Kind is MeasureConceptKind.ProductionQuantity or MeasureConceptKind.SalesQuantity,
            SemanticUnit.IRR or SemanticUnit.MillionIRR or SemanticUnit.BillionIRR => measure.Kind is MeasureConceptKind.FinancialStatementItem or MeasureConceptKind.SalesAmount,
            SemanticUnit.IRRPerShare => measure.Kind == MeasureConceptKind.FinancialStatementItem,
            SemanticUnit.Percent or SemanticUnit.Ratio or SemanticUnit.CurrencyPerUnit => measure.Kind is MeasureConceptKind.ProviderReportedIndicator or MeasureConceptKind.OtherBusinessMeasure or MeasureConceptKind.SalesRate,
            SemanticUnit.Count => true,
            _ => true
        };
    }

    private bool ReferencesRequestedMeasure(string reference, IReadOnlyList<RequestedMeasure> measures) => measures.Any(measure =>
        string.Equals(measure.ConceptId, reference, StringComparison.OrdinalIgnoreCase)
        || string.Equals(CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(measure.UserPhrase),
            CanonicalBusinessMeasureConceptRegistry.NormalizeTerm(reference), StringComparison.Ordinal));

    private static bool UnitMatchesDimension(SemanticUnit unit, BusinessMeasureDimension dimension) => unit switch
    {
        SemanticUnit.Unspecified => true,
        SemanticUnit.Count or SemanticUnit.Ton => dimension == BusinessMeasureDimension.PhysicalQuantity,
        SemanticUnit.IRR or SemanticUnit.MillionIRR => dimension == BusinessMeasureDimension.CurrencyAmount,
        SemanticUnit.BillionIRR => dimension == BusinessMeasureDimension.CurrencyAmount,
        SemanticUnit.IRRPerShare => dimension == BusinessMeasureDimension.CurrencyPerShare,
        SemanticUnit.Percent => dimension == BusinessMeasureDimension.Percentage,
        SemanticUnit.Ratio => dimension == BusinessMeasureDimension.Ratio,
        SemanticUnit.CurrencyPerUnit => dimension == BusinessMeasureDimension.CurrencyPerUnit,
        _ => false
    };

    private static bool RequestedUnitMatchesMeasure(RequestedMeasure measure)
    {
        if (measure.RequestedUnit == SemanticUnit.Unspecified) return true;
        if (measure.Qualifiers?.ValueForm == MeasureValueForm.PercentageChange)
            return measure.RequestedUnit == SemanticUnit.Percent;
        return UnitMatchesDimension(measure.RequestedUnit, measure.Dimension);
    }

    private static bool QualifiersAreCompatible(RequestedMeasure measure)
    {
        var id = measure.ConceptId;
        var businessMonthlyMeasure = id is CanonicalBusinessMeasureConceptRegistry.ProductionQuantity
            or CanonicalBusinessMeasureConceptRegistry.SalesQuantity
            or CanonicalBusinessMeasureConceptRegistry.SalesAmount
            or CanonicalBusinessMeasureConceptRegistry.SalesRate;
        var financialStatementItem = measure.Kind == MeasureConceptKind.FinancialStatementItem;

        if (measure.Qualifiers.StatementScope != FinancialStatementScope.Unspecified && !financialStatementItem)
            return false;
        if (measure.Qualifiers.ValueGrain == MeasureValueGrain.ProductLevel && !businessMonthlyMeasure)
            return false;
        if (measure.Qualifiers.ReportingBasis is MeasureReportingBasis.MonthlyDiscrete or MeasureReportingBasis.MonthlyCumulative
            && !businessMonthlyMeasure)
            return false;
        if (measure.Qualifiers.ReportingBasis is MeasureReportingBasis.QuarterlyDiscrete or MeasureReportingBasis.QuarterlyCumulative
            && !financialStatementItem)
            return false;
        if (measure.Qualifiers.ReportingBasis is MeasureReportingBasis.Annual or MeasureReportingBasis.TrailingTwelveMonths
            && !financialStatementItem && measure.Kind != MeasureConceptKind.ProviderReportedIndicator)
            return false;
        return true;
    }
}

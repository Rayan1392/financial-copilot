using System.Globalization;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>Stable policy identifiers recorded in plans, compiled queries, traces and answers.</summary>
public static class StatementComparisonPolicyIds
{
    /// <summary>
    /// Every compared company is read at the most recent reporting period (start, end and period type)
    /// that all of them report; a company is never compared at a different period than the others.
    /// </summary>
    public const string LatestCommonReportingPeriod = "latest-common-reporting-period-v1";

    /// <summary>An unbound population is the tenant's reviewed company universe (no table scan).</summary>
    public const string AuthorizedCompanyUniverse = "statement-comparison-authorized-company-universe-v1";
}

/// <summary>Single owner of the rule identifying a cross-company statement-item comparison plan.</summary>
public static class StatementComparisonPlans
{
    public static bool IsStatementComparison(StructuredQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.FinancialOperation.ComparisonMode == PlanComparisonMode.MultiCompanyComparison
            && plan.Sources.Count == 1 && plan.Sources[0].ReferenceType == FinancialValueReferenceType.DirectSourceItem;
    }
}

/// <summary>The cross-company question shapes the statement-item engine can represent.</summary>
public enum StatementQueryShape { SingleCompanyValue, MultiCompanyComparison, Ranking, Filter }

public sealed record StatementQueryShapeResult(
    StatementQueryShape? Shape,
    string? Diagnostic,
    PlanSortDirection Direction = PlanSortDirection.Descending,
    int? TopN = null,
    bool PopulationScope = false,
    IReadOnlyList<string>? CompanyLabels = null,
    IReadOnlyList<MeasureThresholdFilter>? Thresholds = null)
{
    public bool IsSupported => Shape is not null;
}

/// <summary>
/// Single owner of the rule deciding which canonical statement-item intents are representable: one
/// latest value of one company, a comparison of explicitly named companies, a ranking of named
/// companies or the authorized population, or a threshold filter over them. Typed binding and the
/// cross-company handoff both use it.
/// </summary>
public static class StatementQueryShapeClassifier
{
    public const int MaximumNamedCompanies = 20;
    public const int MaximumTopN = 100;
    public const int MaximumThresholds = 2;

    public static StatementQueryShapeResult Classify(CanonicalSemanticIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Filters is not null) return ClassifyFilter(intent);
        if (intent.Measures is null || intent.Measures.Count != 1) return Unsupported("statement_query_requires_exactly_one_measure");
        var measure = intent.Measures[0];
        if (intent.Period.Kind is not (ReportingPeriodKind.LatestAvailable or ReportingPeriodKind.Unspecified))
            return Unsupported("statement_query_period_not_supported");
        if (intent.Products.Count != 0 || intent.Categories.Count != 0 || intent.Filters is not null
            || intent.Aggregation is not null || intent.Constraints.Count != 0 || intent.Output.RequestedFields.Count != 0)
            return Unsupported("statement_query_shape_not_supported");
        if (measure.Qualifiers.ReportingBasis != MeasureReportingBasis.Unspecified
            || measure.Qualifiers.ValueGrain != MeasureValueGrain.Unspecified)
            return Unsupported("statement_measure_qualifier_not_supported");

        var labels = intent.EntityScope.CompanyLabels.Select(label => label.Trim()).Where(label => label.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        var named = intent.EntityScope.Kind == EntityScopeKind.NamedCompanies;
        if (named ? labels.Length == 0 : labels.Length != 0) return Unsupported("statement_entity_scope_inconsistent");
        if (labels.Length > MaximumNamedCompanies) return Unsupported("statement_comparison_too_many_companies");

        if (intent.Ranking is not null || intent.Operation.Kind == AnalyticalOperationKind.Rank)
        {
            if (intent.Ranking is not { } ranking) return Unsupported("ranking_specification_missing");
            if (intent.Operation.Kind is not (AnalyticalOperationKind.Rank or AnalyticalOperationKind.Compare))
                return Unsupported("ranking_operation_not_supported");
            if (!string.Equals(ranking.MeasureConcept, measure.ConceptId, StringComparison.Ordinal))
                return Unsupported("ranking_measure_is_not_the_requested_measure");
            if (RankingDirection(ranking.Direction) is not { } direction) return Unsupported("ranking_direction_unrecognized");
            if (ranking.Limit is < 1 or > MaximumTopN) return Unsupported("ranking_limit_out_of_bounds");
            if (intent.Comparison.Mode is not (ComparisonMode.None or ComparisonMode.MultiCompany))
                return Unsupported("ranking_comparison_mode_not_supported");
            if (named && labels.Length < 2) return Unsupported("ranking_requires_two_companies_or_population");
            if (intent.Output.Kind is not (RequestedOutputKind.RankedList or RequestedOutputKind.Table or RequestedOutputKind.Comparison))
                return Unsupported("ranking_output_not_supported");
            return new(StatementQueryShape.Ranking, null, direction, ranking.Limit, !named, labels);
        }

        switch (intent.Operation.Kind)
        {
            case AnalyticalOperationKind.Compare:
                if (intent.Comparison.Mode is not (ComparisonMode.MultiCompany or ComparisonMode.None))
                    return Unsupported("comparison_mode_not_supported_for_statement_items");
                if (!named || labels.Length < 2) return Unsupported("comparison_requires_two_named_companies");
                if (intent.Output.Kind is not (RequestedOutputKind.Comparison or RequestedOutputKind.Table
                    or RequestedOutputKind.RankedList or RequestedOutputKind.Scalar))
                    return Unsupported("comparison_output_not_supported");
                return new(StatementQueryShape.MultiCompanyComparison, null, CompanyLabels: labels);
            case AnalyticalOperationKind.Retrieve:
                if (intent.Comparison.Mode != ComparisonMode.None || !named)
                    return Unsupported("retrieval_scope_not_supported");
                if (labels.Length == 1)
                    return intent.Output.Kind is RequestedOutputKind.Scalar or RequestedOutputKind.Table
                        ? new(StatementQueryShape.SingleCompanyValue, null, CompanyLabels: labels)
                        : Unsupported("retrieval_output_not_supported");
                // Retrieving one statement item for several named companies is a multi-company retrieval.
                return intent.Output.Kind is RequestedOutputKind.Scalar or RequestedOutputKind.Table or RequestedOutputKind.Comparison
                    ? new(StatementQueryShape.MultiCompanyComparison, null, CompanyLabels: labels)
                    : Unsupported("retrieval_output_not_supported");
            default:
                return Unsupported("statement_operation_not_supported");
        }
    }

    /// <summary>
    /// A threshold filter keeps the companies whose latest common-period values satisfy every predicate:
    /// one threshold, or one flat AND of two thresholds, each on a requested statement measure. The first
    /// measure is the listed (and sorted) value; any other measure must be a threshold measure. OR, NOT
    /// and nested groups are not represented.
    /// </summary>
    private static StatementQueryShapeResult ClassifyFilter(CanonicalSemanticIntent intent)
    {
        MeasureThresholdFilter[]? thresholds = intent.Filters switch
        {
            MeasureThresholdFilter single => [single],
            FilterGroup { Operator: FilterLogicalOperator.And } group when (group.Children ?? []).All(child => child is MeasureThresholdFilter)
                => (group.Children ?? []).Cast<MeasureThresholdFilter>().ToArray(),
            _ => null
        };
        if (thresholds is null || thresholds.Length is < 1 or > MaximumThresholds || thresholds.Any(item => item.Threshold is null))
            return Unsupported("statement_filter_expression_not_supported");
        if (intent.Measures is null || intent.Measures.Count is < 1 or > MaximumThresholds)
            return Unsupported("statement_filter_requires_one_or_two_measures");
        var concepts = intent.Measures.Select(measure => measure.ConceptId ?? string.Empty).ToArray();
        if (concepts.Any(string.IsNullOrWhiteSpace) || concepts.Distinct(StringComparer.Ordinal).Count() != concepts.Length)
            return Unsupported("statement_filter_measures_invalid");
        if (thresholds.Any(item => !concepts.Contains(item.MeasureConcept, StringComparer.Ordinal))
            || concepts.Skip(1).Any(concept => thresholds.All(item => item.MeasureConcept != concept)))
            return Unsupported("statement_filter_measure_not_requested");
        if (intent.Measures.Any(measure => measure.Kind != MeasureConceptKind.FinancialStatementItem))
            return Unsupported("statement_filter_requires_statement_items");
        if (intent.Period.Kind is not (ReportingPeriodKind.LatestAvailable or ReportingPeriodKind.Unspecified))
            return Unsupported("statement_query_period_not_supported");
        if (intent.Products.Count != 0 || intent.Categories.Count != 0 || intent.Aggregation is not null
            || intent.Constraints.Count != 0 || intent.Output.RequestedFields.Count != 0)
            return Unsupported("statement_query_shape_not_supported");
        if (intent.Measures.Any(measure => measure.Qualifiers.ValueForm != MeasureValueForm.ReportedValue
                || measure.Qualifiers.ReportingBasis != MeasureReportingBasis.Unspecified
                || measure.Qualifiers.ValueGrain != MeasureValueGrain.Unspecified))
            return Unsupported("statement_measure_qualifier_not_supported");
        // Both predicates are read from the same statement, so they must share one consolidation basis.
        if (intent.Measures.Select(measure => measure.Qualifiers.StatementScope).Distinct().Count() != 1)
            return Unsupported("statement_filter_scope_mismatch");
        if (intent.Operation.Kind is not (AnalyticalOperationKind.Retrieve or AnalyticalOperationKind.DiscoverPopulation
                or AnalyticalOperationKind.Rank or AnalyticalOperationKind.Compare)
            || intent.Comparison.Mode is not (ComparisonMode.None or ComparisonMode.MultiCompany))
            return Unsupported("statement_filter_operation_not_supported");
        if (intent.Output.Kind is RequestedOutputKind.TimeSeries or RequestedOutputKind.Explanation)
            return Unsupported("statement_filter_output_not_supported");

        var labels = intent.EntityScope.CompanyLabels.Select(label => label.Trim()).Where(label => label.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        var named = intent.EntityScope.Kind == EntityScopeKind.NamedCompanies;
        if (named ? labels.Length < 2 : labels.Length != 0) return Unsupported("statement_filter_requires_two_companies_or_population");
        if (labels.Length > MaximumNamedCompanies) return Unsupported("statement_comparison_too_many_companies");
        var direction = PlanSortDirection.Descending;
        int? topN = null;
        if (intent.Ranking is { } ranking)
        {
            if (!string.Equals(ranking.MeasureConcept, concepts[0], StringComparison.Ordinal))
                return Unsupported("ranking_measure_is_not_the_requested_measure");
            if (RankingDirection(ranking.Direction) is not { } parsed) return Unsupported("ranking_direction_unrecognized");
            if (ranking.Limit is < 1 or > MaximumTopN) return Unsupported("ranking_limit_out_of_bounds");
            (direction, topN) = (parsed, ranking.Limit);
        }
        return new(StatementQueryShape.Filter, null, direction, topN, !named, labels, thresholds);
    }

    /// <summary>An unstated ranking direction is the conventional highest-first order; an unrecognized one is refused.</summary>
    private static PlanSortDirection? RankingDirection(string? direction) =>
        string.IsNullOrWhiteSpace(direction) || direction.Trim().ToLowerInvariant() is "unspecified" or "none" or "default"
            ? PlanSortDirection.Descending
            : ParseDirection(direction);

    internal static PlanSortDirection? ParseDirection(string? direction) =>
        (direction ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "descending" or "desc" or "highest" or "largest" or "top" => PlanSortDirection.Descending,
            "ascending" or "asc" or "lowest" or "smallest" or "bottom" => PlanSortDirection.Ascending,
            _ => null
        };

    private static StatementQueryShapeResult Unsupported(string code) => new(null, code);
}

/// <summary>Carried by a compiler-sealed statement comparison query; it instructs the result policy only.</summary>
public sealed record StatementComparisonSpecification(
    PlanSortDirection Direction,
    int? TopN,
    bool PopulationScope,
    bool? RequestedConsolidatedScope,
    IReadOnlyDictionary<Guid, string> CompanyLabels,
    bool Ranked,
    string PeriodPolicy = StatementComparisonPolicyIds.LatestCommonReportingPeriod,
    IReadOnlyList<StatementComparisonFilter>? Filters = null);

/// <summary>
/// One compiled numeric predicate. The SQL compiler projects the filter item's value of the compared
/// statement as <see cref="ValueColumn"/> and the database's evaluation of the bound threshold as
/// <see cref="MatchColumn"/> (NULL when the value is missing); the threshold is in <see cref="Unit"/>,
/// which is the item's own effective unit (no conversion is ever applied).
/// </summary>
public sealed record StatementComparisonFilter(int Index, string SourceCatalogId, string? TitleFa,
    PlanFilterOperator Operator, decimal Threshold, string Unit)
{
    public const string ValueColumnPrefix = "FilterValue";
    public string ValueColumn => ValueColumnPrefix + Index.ToString(CultureInfo.InvariantCulture);
    public string MatchColumn => "FilterMatched" + Index.ToString(CultureInfo.InvariantCulture);
}

public sealed record StatementComparisonOutcome(
    StructuredQueryExecutionStatus Status,
    string? DiagnosticCode,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Per-company statement observation policy: the latest eligible statement per company, never a
/// substitute from an older period, never an arbitrary choice between competing statements.
/// </summary>
public static class LatestStatementObservationPolicy
{
    public static (StructuredQueryExecutionStatus Status, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        string? DiagnosticCode) Resolve(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return (StructuredQueryExecutionStatus.NoData, [], null);
        if (rows.Count == 1) return rows[0].GetValueOrDefault("Value") is null
            ? (StructuredQueryExecutionStatus.NoData, rows, "eligible_statement_observation_missing")
            : (StructuredQueryExecutionStatus.Succeeded, rows, null);
        var periods = rows.Select(row => ReportingPeriod(row.GetValueOrDefault("PeriodEnd"))).ToArray();
        if (periods.Any(period => !period.HasValue))
            return (StructuredQueryExecutionStatus.Ambiguous, rows, "reporting_period_evidence_unavailable");
        var latestEnd = periods.Max(period => period!.Value);
        var latest = rows.Where((_, index) => periods[index] == latestEnd).ToArray();
        if (latest.Length == 0) return (StructuredQueryExecutionStatus.NoData, [], null);
        // When no consolidation basis was requested, the compiled query returns the latest statement
        // of each basis. Choosing the newer basis would silently change the reporting basis of the
        // answer, so the choice is left to the user instead of being made here.
        var bases = rows.Select(row => row.GetValueOrDefault("IsComposing")).Distinct().ToArray();
        var latestBases = latest.Select(row => row.GetValueOrDefault("IsComposing")).Distinct().ToArray();
        if (bases.Length > 1 && latestBases.Length == 1)
            return (StructuredQueryExecutionStatus.Ambiguous, rows, "statement_scope_unspecified_across_consolidation_bases");
        // Period type and start are selection dimensions; audit, representation and revision are
        // competing statement identities within one otherwise comparable period.
        var periodScopes = latest.Select(row => (Start: row.GetValueOrDefault("PeriodStart"),
                Type: row.GetValueOrDefault("PeriodType"), Scope: row.GetValueOrDefault("IsComposing")))
            .Distinct().ToArray();
        if (periodScopes.Length != 1)
            return (StructuredQueryExecutionStatus.Ambiguous, latest, "different_reporting_period_scopes");
        if (latest.Any(row => row.GetValueOrDefault("Value") is null))
            return (StructuredQueryExecutionStatus.Incomplete, latest, "eligible_statement_observation_missing");
        var values = latest.Select(row => row.GetValueOrDefault("Value")).ToArray();
        if (values.Skip(1).Any(value => !Equals(value, values[0])))
            return (StructuredQueryExecutionStatus.Ambiguous, latest, "requested_item_values_conflict");
        // Threshold-filter items read from the same competing statements must agree as well.
        var filterKeys = latest[0].Keys.Where(key => key.StartsWith(StatementComparisonFilter.ValueColumnPrefix, StringComparison.Ordinal)).ToArray();
        if (latest.Skip(1).Any(row => filterKeys.Any(key => !Equals(row.GetValueOrDefault(key), latest[0].GetValueOrDefault(key)))))
            return (StructuredQueryExecutionStatus.Ambiguous, latest, "requested_item_values_conflict");
        var resolved = new Dictionary<string, object?>(latest[0], StringComparer.Ordinal);
        // A representative row must not imply that its statement was preferred. The complete
        // competing identities are emitted together and the singular identity is removed.
        foreach (var key in new[] { "StatementId", "IsAudited", "IsRepresented", "ExternalStatementId", "SourcePayloadChecksum",
                     "PublishedAt", "LastSynchronizedAt", "WarningsJson", "LogicalVendor", "SourceMode" })
            resolved.Remove(key);
        resolved["StatementIdentities"] = latest.Select(row => row.GetValueOrDefault("StatementId")).ToArray();
        resolved["SameValueTieResolved"] = true;
        resolved["MatchingStatementCount"] = latest.Length;
        return (StructuredQueryExecutionStatus.Succeeded, [resolved], null);
    }

    internal static DateTime? ReportingPeriod(object? value) => value switch
    {
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        DateTime dateTime => dateTime,
        DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
        string text when DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) => parsed,
        _ => null
    };
}

/// <summary>
/// Deterministic cross-company comparison and ranking over compiler-sealed statement rows.
/// Rules: every company is resolved by <see cref="LatestStatementObservationPolicy"/>; all compared
/// values must share reporting period, consolidation basis and unit; a missing value is never zero;
/// equal values share a competition rank and are ordered by company label, then company id.
/// </summary>
public static class CrossCompanyStatementComparisonPolicy
{
    public const string WarningCompanyWithoutStatements = "company_without_statements";
    public const string WarningCompanyValueMissing = "company_value_missing";
    public const string WarningPopulationMembersWithoutStatements = "population_members_without_statements";
    public const string WarningNewerPeriodAvailable = "newer_period_available";
    public const string WarningTopNExtendedByTies = "top_n_extended_by_ties";
    public const string WarningAmbiguousCompany = "comparison_member_ambiguous";
    public const string WarningCompanyLatestPeriod = "company_latest_period";
    public const string WarningFilterApplied = "statement_filter_applied";
    public const string WarningFilterExcluded = "statement_filter_excluded_companies";
    public const string WarningFilterValueMissing = "statement_filter_value_missing";

    public static StatementComparisonOutcome Resolve(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyCollection<Guid> comparedCompanyIds,
        StatementComparisonSpecification specification,
        bool populationCoverageIncomplete = false)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(comparedCompanyIds);
        ArgumentNullException.ThrowIfNull(specification);
        var warnings = new List<string>();
        if (rows.Any(row => row.GetValueOrDefault("CompanyId") is not Guid))
            return new(StructuredQueryExecutionStatus.Ambiguous, "comparison_row_company_identity_missing", rows, []);
        var compared = comparedCompanyIds.ToHashSet();
        var byCompany = rows.GroupBy(row => (Guid)row["CompanyId"]!)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)group.ToArray());
        if (byCompany.Keys.Any(id => !compared.Contains(id)))
            return new(StructuredQueryExecutionStatus.Ambiguous, "comparison_row_outside_compared_companies", [], []);

        string Label(Guid id)
        {
            if (specification.CompanyLabels.TryGetValue(id, out var label) && !string.IsNullOrWhiteSpace(label)) return label;
            string? Persisted(string key) => byCompany.GetValueOrDefault(id)?.Select(row => row.GetValueOrDefault(key) as string)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return Persisted("CompanySymbol") ?? Persisted("CompanyName") ?? id.ToString("D");
        }

        var companies = byCompany.Keys.OrderBy(Label, StringComparer.Ordinal).ThenBy(id => id).ToArray();
        var withoutStatements = compared.Where(id => !byCompany.ContainsKey(id)).OrderBy(Label, StringComparer.Ordinal)
            .ThenBy(id => id).ToArray();
        if (companies.Length == 0)
            return specification.PopulationScope
                ? new(StructuredQueryExecutionStatus.NoData, "comparison_population_without_statements", [], [])
                : new(StructuredQueryExecutionStatus.Incomplete, "comparison_companies_without_statements", [],
                    withoutStatements.Select(id => $"{WarningCompanyWithoutStatements}:{Label(id)}").ToArray());
        if (!specification.PopulationScope && withoutStatements.Length > 0)
            return new(StructuredQueryExecutionStatus.Incomplete, "comparison_company_statement_missing", [],
                withoutStatements.Select(id => $"{WarningCompanyWithoutStatements}:{Label(id)}").ToArray());

        // The compiled query returns one coverage row without a statement for each company when the
        // companies share no reporting period; the latest period of each is disclosed, nothing is compared.
        var targetless = companies.Where(id => byCompany[id].All(row => row.GetValueOrDefault("StatementId") is null)).ToArray();
        if (targetless.Length == companies.Length)
            return new(StructuredQueryExecutionStatus.Incomplete, "no_common_reporting_period", [],
                companies.Select(id => $"{WarningCompanyLatestPeriod}:{Label(id)}:{DateText(byCompany[id][0].GetValueOrDefault("CompanyLatestPeriodEnd"))}")
                    .ToArray());
        if (targetless.Length > 0)
            return new(StructuredQueryExecutionStatus.Ambiguous, "comparison_target_period_coverage_inconsistent", [], []);

        var resolved = new List<(Guid CompanyId, string Label, decimal Value, IReadOnlyDictionary<string, object?> Row)>();
        var missingValue = new List<string>();
        foreach (var companyId in companies)
        {
            var companyRows = byCompany[companyId];
            var label = Label(companyId);
            // Without a requested basis, a company that reports on more than one consolidation basis has
            // no single comparable value; the comparison never mixes or silently picks a basis.
            if (specification.RequestedConsolidatedScope is null
                && companyRows.Any(row => Count(row.GetValueOrDefault("CompanyBasisCount")) > 1))
                return new(StructuredQueryExecutionStatus.Ambiguous, "statement_scope_unspecified_across_consolidation_bases", [],
                    [$"{WarningAmbiguousCompany}:{label}"]);
            var resolution = LatestStatementObservationPolicy.Resolve(companyRows);
            switch (resolution.Status)
            {
                case StructuredQueryExecutionStatus.Succeeded:
                    resolved.Add((companyId, label, Convert.ToDecimal(resolution.Rows[0]["Value"], CultureInfo.InvariantCulture),
                        resolution.Rows[0]));
                    break;
                case StructuredQueryExecutionStatus.NoData or StructuredQueryExecutionStatus.Incomplete:
                    missingValue.Add(label);
                    break;
                default:
                    return new(StructuredQueryExecutionStatus.Ambiguous, resolution.DiagnosticCode ?? "comparison_member_ambiguous", [],
                        [$"{WarningAmbiguousCompany}:{label}"]);
            }
        }

        warnings.AddRange(missingValue.Select(label => $"{WarningCompanyValueMissing}:{label}"));
        if (!specification.PopulationScope && missingValue.Count > 0)
            return new(StructuredQueryExecutionStatus.Incomplete, "comparison_company_value_missing",
                Annotate(resolved, specification, ranked: false), warnings);
        if (specification.PopulationScope && withoutStatements.Length > 0)
            warnings.Add($"{WarningPopulationMembersWithoutStatements}:{withoutStatements.Length.ToString(CultureInfo.InvariantCulture)}");
        if (resolved.Count == 0)
            return new(StructuredQueryExecutionStatus.NoData, "comparison_values_missing_for_all_companies", [], warnings);

        string? Text(IReadOnlyDictionary<string, object?> row, string key) => row.GetValueOrDefault(key) switch
        {
            null => null,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            var value => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
        if (resolved.Select(item => (Text(item.Row, "PeriodStart"), Text(item.Row, "PeriodEnd"), Text(item.Row, "PeriodType")))
                .Distinct().Count() != 1)
            return new(StructuredQueryExecutionStatus.Ambiguous, "cross_company_reporting_periods_differ", [], warnings);
        if (resolved.Select(item => Text(item.Row, "IsComposing")).Distinct(StringComparer.Ordinal).Count() != 1)
            return new(StructuredQueryExecutionStatus.Ambiguous, "cross_company_consolidation_basis_mismatch", [], warnings);
        if (resolved.Select(item => Text(item.Row, "SourceUnit")).Distinct(StringComparer.Ordinal).Count() != 1)
            return new(StructuredQueryExecutionStatus.Ambiguous, "cross_company_units_incompatible", [], warnings);

        foreach (var item in resolved)
        {
            var latest = LatestStatementObservationPolicy.ReportingPeriod(item.Row.GetValueOrDefault("CompanyLatestPeriodEnd"));
            var target = LatestStatementObservationPolicy.ReportingPeriod(item.Row.GetValueOrDefault("PeriodEnd"));
            if (latest.HasValue && target.HasValue && latest.Value > target.Value)
                warnings.Add($"{WarningNewerPeriodAvailable}:{item.Label}:{latest.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        // Threshold filters run after resolution and comparability, on the database's evaluation of each
        // bound predicate. A missing filter value is never treated as zero or as a failed predicate.
        if (specification.Filters is { Count: > 0 } filters)
        {
            warnings.AddRange(filters.Select(filter => $"{WarningFilterApplied}:{filter.Index.ToString(CultureInfo.InvariantCulture)}:" +
                $"{filter.Operator}:{filter.Threshold.ToString(CultureInfo.InvariantCulture)}:{filter.Unit}:{filter.TitleFa}"));
            var kept = new List<(Guid CompanyId, string Label, decimal Value, IReadOnlyDictionary<string, object?> Row)>();
            var filterValueMissing = new List<string>();
            var excluded = 0;
            foreach (var item in resolved)
            {
                var matches = filters.Select(filter => item.Row.GetValueOrDefault(filter.MatchColumn)).ToArray();
                if (matches.Any(match => match is not bool)) filterValueMissing.Add(item.Label);
                else if (matches.All(match => match is true)) kept.Add(item);
                else excluded++;
            }
            warnings.AddRange(filterValueMissing.Select(label => $"{WarningFilterValueMissing}:{label}"));
            if (!specification.PopulationScope && filterValueMissing.Count > 0)
                return new(StructuredQueryExecutionStatus.Incomplete, "statement_filter_value_missing", [], warnings);
            if (excluded > 0) warnings.Add($"{WarningFilterExcluded}:{excluded.ToString(CultureInfo.InvariantCulture)}");
            if (kept.Count == 0)
                return new(StructuredQueryExecutionStatus.NoData, "statement_filter_no_matching_company", [], warnings);
            resolved = kept;
        }

        var annotated = Annotate(resolved, specification, specification.Ranked);
        if (specification.TopN is { } topN)
        {
            var kept = annotated.Where(row => (int)row["Rank"]! <= topN).ToArray();
            if (kept.Length > topN) warnings.Add(WarningTopNExtendedByTies);
            annotated = kept;
        }
        return populationCoverageIncomplete
            ? new(StructuredQueryExecutionStatus.Incomplete, "population_ranking_authorized_company_coverage_incomplete", annotated, warnings)
            : new(StructuredQueryExecutionStatus.Succeeded, null, annotated, warnings);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Annotate(
        IReadOnlyList<(Guid CompanyId, string Label, decimal Value, IReadOnlyDictionary<string, object?> Row)> resolved,
        StatementComparisonSpecification specification, bool ranked)
    {
        var descending = specification.Direction == PlanSortDirection.Descending;
        var ordered = (descending
                ? resolved.OrderByDescending(item => item.Value)
                : resolved.OrderBy(item => item.Value))
            .ThenBy(item => item.Label, StringComparer.Ordinal).ThenBy(item => item.CompanyId).ToArray();
        return ordered.Select(item =>
        {
            var better = resolved.Count(other => descending ? other.Value > item.Value : other.Value < item.Value);
            var row = new Dictionary<string, object?>(item.Row, StringComparer.Ordinal)
            {
                ["CompanyLabel"] = item.Label,
                ["Rank"] = better + 1,
                ["TiedWithOthers"] = resolved.Count(other => other.Value == item.Value) > 1,
                ["ComparedCompanyCount"] = resolved.Count,
                ["ComparisonPeriodPolicy"] = specification.PeriodPolicy,
                ["RankingApplied"] = ranked
            };
            return (IReadOnlyDictionary<string, object?>)row;
        }).ToArray();
    }

    private static long Count(object? value) => value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);

    private static string DateText(object? value) =>
        LatestStatementObservationPolicy.ReportingPeriod(value)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";
}

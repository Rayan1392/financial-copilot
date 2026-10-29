namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>
/// Offline semantic plan algebra. It contains catalog identities and typed operations only;
/// it intentionally has no SQL, expression, table-name, or executable-instruction field.
/// </summary>
public sealed record StructuredQueryPlan(
    int ContractVersion,
    string CatalogVersion,
    string CatalogHash,
    Guid TenantId,
    IReadOnlyList<PlanEntityReference> Entities,
    IReadOnlyList<PlanSourceReference> Sources,
    IReadOnlyList<string> RelationshipIds,
    IReadOnlyList<PlanProjection> Projection,
    IReadOnlyList<PlanFilter> Filters,
    PlanPeriodSelection Period,
    PlanStatementSelection Statement,
    IReadOnlyList<PlanAggregation> Aggregations,
    IReadOnlyList<PlanSort> Sort,
    PlanPagination Pagination,
    PlanPresentation Presentation,
    PlanFinancialOperation FinancialOperation,
    bool RetrievalComplete,
    bool SemanticReviewApproved,
    bool QueryAuthorized,
    bool ExecutionEligible,
    IReadOnlyList<string> Diagnostics,
    ProductEquivalenceResolution? ReviewedProductEquivalence = null,
    string? CompanyPopulationId = null,
    string? CompanyPopulationApprovalReference = null,
    ProductConceptResolution? ProductConcept = null,
    AuthorizedCompanyDiscoveryScope? CompanyDiscoveryScope = null,
    string? PopulationCoveragePolicyId = null,
    PopulationSalesScope? PopulationSalesScope = null);

public enum PopulationSalesScope { TotalCompanySales, RequestedProductSales }

public sealed record PlanEntityReference(string EntityKind, string UserReference, Guid? ResolvedEntityId = null,
    string? ResolvedEntityKey = null, string? IdentityProvenance = null, bool IdentityVerified = false);

public enum StructuredCompanyBindingStatus { Found, Ambiguous, NotFound, Missing, TenantScopeRequired }

public sealed record StructuredCompanyBinding(StructuredCompanyBindingStatus Status, Guid? CompanyId,
    IReadOnlyList<Guid> CandidateCompanyIds, string? IdentityProvenance, string? DiagnosticCode);

/// <summary>Bridges the existing canonical company resolver to a stable Companies.Id plan binding.</summary>
public interface IStructuredQueryCompanyBinder
{
    Task<StructuredCompanyBinding> ResolveAsync(string? mention, Guid tenantId, CancellationToken cancellationToken);
}

public sealed record PlanSourceReference(
    FinancialValueReferenceType ReferenceType,
    string CatalogId,
    Guid? SourceItemCatalogId,
    string? ProviderName,
    string? StatementType,
    int? SourceItemId,
    string? MetricCode,
    string? Unit,
    string? UnitStatus,
    string IdentityStatus,
    string SemanticReviewStatus,
    bool Queryable,
    string? TitleFa = null);

public sealed record PlanProjection(string CatalogId, PlanProjectionRole Role);
public enum PlanProjectionRole { Value, Grouping, Comparison }

public sealed record PlanFilter(string CatalogId, PlanFilterOperator Operator, PlanLiteral? Value);
public enum PlanFilterOperator { Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, IsNull, IsNotNull }
public sealed record PlanLiteral(PlanLiteralType Type, string Value);
public enum PlanLiteralType { Text, Decimal, Integer, Boolean, Date, CatalogIdentifier }

public sealed record PlanPeriodSelection(
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> ComparisonScopes,
    bool SelectLatestByReportingPeriod,
    bool UseSynchronizationTimestampForRecency,
    DateOnly? ReportingPeriodStart = null,
    DateOnly? ReportingPeriodEnd = null,
    int? MaximumPeriods = null);

public sealed record PlanStatementSelection(
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> StatementTypes,
    IReadOnlyList<PlanStatementSelectionKey> SelectionKeys,
    bool PreferComposing,
    bool PreservePeriodType,
    bool PreserveAuditState,
    bool PreserveRestatementState,
    bool PreserveRevisionIdentity,
    bool RequireUnambiguousSelection);

public enum PlanStatementSelectionKey
{
    ReportingPeriodDescending,
    PeriodType,
    ProviderIdentity,
    StatementType,
    StatementScope,
    AuditState,
    RestatementState,
    RevisionIdentity,
    Availability
}

public sealed record PlanAggregation(string CatalogId, PlanAggregationOperation Operation);
public enum PlanAggregationOperation { Count, Sum, Average, Minimum, Maximum }

public sealed record PlanSort(string CatalogId, PlanSortDirection Direction);
public enum PlanSortDirection { Ascending, Descending }

public sealed record PlanPagination(int Offset, int Limit);
public enum PlanPresentation { Scalar, Table, Chart, TimeSeries, RankedTable, Summary }

/// <summary>Typed extraction of composable operators; all field references must resolve in the catalog context.</summary>
public sealed record StructuredPlanShape(
    IReadOnlyList<string> ProjectionCatalogIds,
    IReadOnlyList<string> GroupByCatalogIds,
    IReadOnlyList<PlanFilter> Filters,
    IReadOnlyList<PlanAggregation> Aggregations,
    IReadOnlyList<PlanSort> Sort,
    int Offset = 0,
    int Limit = 100,
    PlanFinancialOperationKind FinancialOperationKind = PlanFinancialOperationKind.DirectValue,
    DateOnly? ReportingPeriodStart = null,
    DateOnly? ReportingPeriodEnd = null,
    int? MaximumPeriods = null,
    PlanComparisonMode ComparisonMode = PlanComparisonMode.None);

public sealed record PlanFinancialOperation(
    FinancialValueOperation Operation,
    PlanFinancialOperationKind Kind,
    PlanComparisonMode ComparisonMode,
    IReadOnlyList<string> ValueDefinitionCatalogIds,
    bool FormulaAuthorized);

public enum PlanComparisonMode
{
    None,
    MonthOverMonth,
    YearOverYear,
    MultiCompanyComparison
}

public enum PlanFinancialOperationKind
{
    DirectValue,
    AbsoluteChange,
    GrowthPercentage,
    TimeSeries,
    UnsupportedDerived
}

public sealed record StructuredQueryPlanValidation(
    bool IsStructurallyValid,
    bool RetrievalComplete,
    bool SemanticReviewApproved,
    bool QueryAuthorized,
    bool ExecutionEligible,
    IReadOnlyList<string> Diagnostics);

public interface IStructuredQueryPlanBuilder
{
    StructuredQueryPlan Build(
        SchemaNavigationRequest request,
        QuestionSchemaRequirements requirements,
        SchemaNavigationResult navigation,
        SchemaContext context,
        SchemaContextCompletenessResult completeness);
}

public interface IStructuredQueryPlanValidator
{
    StructuredQueryPlanValidation Validate(
        SchemaNavigationRequest request,
        QuestionSchemaRequirements requirements,
        StructuredQueryPlan plan,
        SchemaContext context,
        SchemaContextCompletenessResult completeness);
}

public interface IOfflineStructuredQueryPlanningService
{
    Task<OfflineStructuredQueryPlanningResult> PlanAsync(SchemaNavigationRequest request, CancellationToken cancellationToken);
    Task<OfflineStructuredQueryPlanningResult> PlanAsync(SchemaNavigationRequest request,
        QuestionSchemaRequirements independentlyExtractedRequirements, CancellationToken cancellationToken);
}

public sealed record OfflineStructuredQueryPlanningResult(
    QuestionSchemaRequirements Requirements,
    SchemaNavigationResult Navigation,
    StructuredQueryPlan Plan,
    StructuredQueryPlanValidation Validation,
    StructuredCompanyBinding? CompanyBinding = null,
    StructuredProductBinding? ProductBinding = null,
    IReadOnlyList<StructuredCompanyBinding>? CompanyBindings = null);

public sealed class StructuredQueryPlanBuilder : IStructuredQueryPlanBuilder
{
    public StructuredQueryPlan Build(SchemaNavigationRequest request, QuestionSchemaRequirements requirements,
        SchemaNavigationResult navigation, SchemaContext context, SchemaContextCompletenessResult completeness)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completeness);

        var selectedRefs = requirements.FinancialValueReferences ?? [];
        var refs = new List<PlanSourceReference>();
        foreach (var selection in selectedRefs)
        {
            if (!Enum.TryParse<FinancialValueReferenceType>(selection.ReferenceType, out var type)) continue;
            if (type == FinancialValueReferenceType.DirectSourceItem)
            {
                var identity = (context.FinancialValueReferences ?? []).FirstOrDefault(x => x.CatalogId == selection.CatalogId);
                if (identity is null) continue;
                refs.Add(new(type, identity.CatalogId, identity.SourceItemCatalogId, identity.ProviderName,
                    identity.StatementType, identity.SourceItemId, identity.MetricCode, identity.NormalizedUnit ?? identity.SourceUnit,
                    identity.SourceUnitStatus, identity.IdentityStatus, identity.SemanticReviewStatus, identity.Queryable,
                    identity.TitleFa));
            }
            else
            {
                refs.Add(new(type, selection.CatalogId, null, null, null, null,
                    type == FinancialValueReferenceType.MonthlyMeasure ? selection.CatalogId : null, null,
                    null, type == FinancialValueReferenceType.MonthlyMeasure ? "catalog-mapped" : "catalog-reference",
                    "pending-or-unresolved", false));
            }
        }

        var shape = requirements.StructuredShape;
        var operation = Enum.TryParse<FinancialValueOperation>(requirements.RequestedFinancialValueOperation, out var parsed)
            ? parsed : FinancialValueOperation.UnsupportedDerivedFinancialMetric;
        var comparisonMode = shape?.ComparisonMode ?? PlanComparisonMode.None;
        if (comparisonMode == PlanComparisonMode.MultiCompanyComparison
            && operation == FinancialValueOperation.ApprovedPeriodComparison)
            operation = FinancialValueOperation.DirectValueRetrieval;
        var directStatements = refs.Where(x => x.ReferenceType == FinancialValueReferenceType.DirectSourceItem).ToArray();
        var explicitlyConsolidated = (requirements.StatementScopes ?? []).Contains("consolidated", StringComparer.Ordinal);
        var monthlyMeasureReferences = selectedRefs
            .Where(x => string.Equals(x.ReferenceType, nameof(FinancialValueReferenceType.MonthlyMeasure),
                StringComparison.Ordinal))
            .Select(x => x.CatalogId).ToArray();
        var projectionIds = monthlyMeasureReferences.Length > 0
            ? monthlyMeasureReferences.Distinct(StringComparer.Ordinal).ToArray()
            : selectedRefs.Select(x => x.CatalogId)
                .Concat(shape?.ProjectionCatalogIds ?? []).Distinct(StringComparer.Ordinal).ToArray();
        IEnumerable<PlanProjection> groupBy = monthlyMeasureReferences.Length > 0
            ? Array.Empty<PlanProjection>()
            : (shape?.GroupByCatalogIds ?? []).Distinct(StringComparer.Ordinal)
                .Select(x => new PlanProjection(x, PlanProjectionRole.Grouping)).ToArray();
        var pagination = shape is null ? new PlanPagination(0, 100) : new PlanPagination(shape.Offset, shape.Limit);
        var planSort = (requirements.PresentationRequirements ?? []).Contains("time-series", StringComparer.OrdinalIgnoreCase)
            ? Array.Empty<PlanSort>()
            : shape?.Sort ?? [];
        var plan = new StructuredQueryPlan(
            1, request.CatalogVersion, request.CatalogHash, request.TenantId,
            (requirements.EntityReferences ?? []).Where(value => !(requirements.ProductReferences ?? []).Contains(value, StringComparer.OrdinalIgnoreCase))
                .Select(value => new PlanEntityReference("Company", value))
                .Concat((requirements.ProductReferences ?? []).Select(value => new PlanEntityReference("Product", value))).ToArray(),
            refs,
            RequiredRelationshipIds(requirements, context),
            projectionIds.Select(x => new PlanProjection(x, PlanProjectionRole.Value)).Concat(groupBy).ToArray(),
            shape?.Filters ?? [],
            new PlanPeriodSelection(requirements.PeriodPolicies ?? [], requirements.ComparisonScopes ?? [],
                (requirements.PeriodPolicies ?? []).Contains("latest", StringComparer.Ordinal), false,
                shape?.ReportingPeriodStart, shape?.ReportingPeriodEnd, shape?.MaximumPeriods),
            new PlanStatementSelection(requirements.StatementScopes ?? [],
                directStatements.Where(x => !string.IsNullOrWhiteSpace(x.ProviderName)).Select(x => x.ProviderName!).Distinct(StringComparer.Ordinal).ToArray(),
                directStatements.Where(x => !string.IsNullOrWhiteSpace(x.StatementType)).Select(x => x.StatementType!).Distinct(StringComparer.Ordinal).ToArray(),
                [PlanStatementSelectionKey.ReportingPeriodDescending, PlanStatementSelectionKey.PeriodType,
                    PlanStatementSelectionKey.ProviderIdentity, PlanStatementSelectionKey.StatementType,
                    PlanStatementSelectionKey.StatementScope, PlanStatementSelectionKey.AuditState,
                    PlanStatementSelectionKey.RestatementState, PlanStatementSelectionKey.RevisionIdentity,
                    PlanStatementSelectionKey.Availability],
                explicitlyConsolidated, true, true, true, true, true),
            shape?.Aggregations ?? [], planSort, pagination, ResolvePresentation(requirements.PresentationRequirements),
            new PlanFinancialOperation(operation, shape?.FinancialOperationKind ?? (operation == FinancialValueOperation.UnsupportedDerivedFinancialMetric
                    ? PlanFinancialOperationKind.UnsupportedDerived : PlanFinancialOperationKind.DirectValue), comparisonMode,
                (requirements.MeasureIds ?? []).Except(selectedRefs.Select(x => x.CatalogId), StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal).ToArray(),
                false), completeness.RetrievalComplete, completeness.SemanticReviewApproved, completeness.QueryAuthorized,
            false, navigation.Diagnostics.Concat(completeness.Diagnostics).Concat(requirements.NormalizationDiagnostics)
                .Distinct(StringComparer.Ordinal).ToArray(), requirements.ResolvedProductEquivalence,
            requirements.ResolvedCompanyPopulation?.PopulationId, requirements.ResolvedCompanyPopulation?.ApprovalReference,
            requirements.ResolvedProductConcept, requirements.AuthorizedCompanyDiscoveryScope,
            requirements.PopulationCoveragePolicyId, requirements.PopulationSalesScope);
        return plan;
    }

    private static PlanPresentation ResolvePresentation(IReadOnlyList<string>? values)
    {
        var value = (values ?? []).FirstOrDefault() ?? "table";
        return value.ToLowerInvariant() switch
        {
            "scalar" => PlanPresentation.Scalar,
            "time-series" or "timeseries" => PlanPresentation.TimeSeries,
            "ranked-table" or "ranked" => PlanPresentation.RankedTable,
            "summary" => PlanPresentation.Summary,
            "chart" => PlanPresentation.Chart,
            _ => PlanPresentation.Table
        };
    }

    private static IReadOnlyList<string> RequiredRelationshipIds(QuestionSchemaRequirements requirements, SchemaContext context)
    {
        var mapped = (requirements.MeasureIds ?? []).Where(id => context.MeasureRelationshipIds?.ContainsKey(id) == true)
            .SelectMany(id => context.MeasureRelationshipIds![id]).ToArray();
        return (mapped.Length > 0 ? mapped : (requirements.RelationshipIds ?? []).ToArray())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Deterministic validator independent from LLM output; catalog membership comes from expanded context.</summary>
public sealed class StructuredQueryPlanValidator : IStructuredQueryPlanValidator
{
    private readonly bool _executionEnabled;

    public StructuredQueryPlanValidator(bool executionEnabled = false) => _executionEnabled = executionEnabled;

    public StructuredQueryPlanValidation Validate(SchemaNavigationRequest request, QuestionSchemaRequirements requirements,
        StructuredQueryPlan plan, SchemaContext context, SchemaContextCompletenessResult completeness)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completeness);
        var issues = new List<string>();
        issues.AddRange(Feature141OperationRegistry.ValidatePlan(plan));
        if (plan.ContractVersion != 1 || plan.ContractVersion != request.ContractVersion) issues.Add("plan_contract_version_mismatch");
        if (!string.Equals(plan.CatalogVersion, request.CatalogVersion, StringComparison.Ordinal)
            || !string.Equals(plan.CatalogHash, request.CatalogHash, StringComparison.Ordinal)
            || !string.Equals(context.CatalogHash, request.CatalogHash, StringComparison.Ordinal)) issues.Add("plan_catalog_identity_mismatch");
        if (request.TenantId == Guid.Empty || plan.TenantId == Guid.Empty) issues.Add("plan_tenant_scope_missing");
        if (plan.TenantId != request.TenantId) issues.Add("plan_tenant_scope_mismatch");
        if (plan.Pagination.Offset < 0 || plan.Pagination.Limit is < 1 or > 100) issues.Add("plan_pagination_unbounded_or_invalid");
        if (plan.Period.UseSynchronizationTimestampForRecency) issues.Add("synchronization_timestamp_cannot_select_latest_reporting_period");
        var consolidatedRequested = (requirements.StatementScopes ?? []).Contains("consolidated", StringComparer.Ordinal);
        if (plan.Statement.PreferComposing != consolidatedRequested) issues.Add("composing_statement_selection_scope_mismatch");
        if (!plan.Statement.RequireUnambiguousSelection) issues.Add("statement_selection_must_fail_when_ambiguous");
        if (plan.RelationshipIds.Except(context.RelationshipIds, StringComparer.Ordinal).Any()) issues.Add("plan_contains_unresolved_relationship");
        if (!SetEquals(plan.RelationshipIds, RequiredRelationshipIds(requirements, context)))
            issues.Add("plan_relationship_requirements_mismatch");
        if (plan.Projection.Any(x => !IsCatalogReference(x.CatalogId, context))) issues.Add("plan_contains_unresolved_projection");
        if (plan.Filters.Any(x => !IsCatalogReference(x.CatalogId, context))) issues.Add("plan_contains_unresolved_filter_field");
        if (plan.Aggregations.Any(x => !IsCatalogReference(x.CatalogId, context))) issues.Add("plan_contains_unresolved_aggregation_field");
        if (plan.Sort.Any(x => !IsCatalogReference(x.CatalogId, context))) issues.Add("plan_contains_unresolved_sort_field");
        var shape = requirements.StructuredShape;
        if (!plan.Filters.SequenceEqual(shape?.Filters ?? [])) issues.Add("plan_filter_shape_mismatch");
        if (!plan.Aggregations.SequenceEqual(shape?.Aggregations ?? [])) issues.Add("plan_aggregation_shape_mismatch");
        var expectedSort = (requirements.PresentationRequirements ?? []).Contains("time-series", StringComparer.OrdinalIgnoreCase)
            ? Array.Empty<PlanSort>()
            : shape?.Sort ?? [];
        if (!plan.Sort.SequenceEqual(expectedSort)) issues.Add("plan_sort_shape_mismatch");
        var monthlyMeasureIds = (requirements.FinancialValueReferences ?? [])
            .Where(x => string.Equals(x.ReferenceType, nameof(FinancialValueReferenceType.MonthlyMeasure),
                StringComparison.Ordinal))
            .Select(x => x.CatalogId).ToArray();
        var expectedProjectionIds = (monthlyMeasureIds.Length > 0
                ? monthlyMeasureIds
                : (requirements.FinancialValueReferences ?? []).Select(x => x.CatalogId)
                    .Concat(shape?.ProjectionCatalogIds ?? []).Concat(shape?.GroupByCatalogIds ?? []))
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedProjectionIds.SetEquals(plan.Projection.Select(x => x.CatalogId))) issues.Add("plan_projection_requirements_mismatch");
        var expectedSources = (requirements.FinancialValueReferences ?? []).Select(x => (x.ReferenceType, x.CatalogId)).ToHashSet();
        var actualSources = plan.Sources.Select(x => (x.ReferenceType.ToString(), x.CatalogId)).ToHashSet();
        if (!expectedSources.SetEquals(actualSources)) issues.Add("plan_financial_source_requirements_mismatch");
        if (!SetEquals(plan.Period.Policies, requirements.PeriodPolicies)
            || !SetEquals(plan.Period.ComparisonScopes, requirements.ComparisonScopes)) issues.Add("plan_period_requirements_mismatch");
        if (plan.Period.ReportingPeriodStart != shape?.ReportingPeriodStart
            || plan.Period.ReportingPeriodEnd != shape?.ReportingPeriodEnd
            || plan.Period.MaximumPeriods != shape?.MaximumPeriods)
            issues.Add("plan_reporting_period_bounds_mismatch");
        if (!SetEquals(plan.Statement.Scopes, requirements.StatementScopes)) issues.Add("plan_statement_scope_requirements_mismatch");
        var expectedEntities = (requirements.EntityReferences ?? []).Where(value =>
                !(requirements.ProductReferences ?? []).Contains(value, StringComparer.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(value)).Select(value => ("Company", value))
            .Concat((requirements.ProductReferences ?? []).Select(value => ("Product", value))).ToHashSet();
        if (!expectedEntities.SetEquals(plan.Entities.Select(x => (x.EntityKind, x.UserReference))))
            issues.Add("plan_entity_requirements_mismatch");
        if (plan.Entities.Any(x => x.EntityKind.Equals("Product", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(x.ResolvedEntityKey) || !x.IdentityVerified
                || string.IsNullOrWhiteSpace(x.IdentityProvenance))))
            issues.Add("monthly_product_binding_required");
        if (plan.CompanyPopulationId is not null
            && (string.IsNullOrWhiteSpace(plan.CompanyPopulationApprovalReference)
                || plan.Entities.Count(entity => entity.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase)) < 2
                || plan.Entities.Where(entity => entity.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase))
                    .Any(entity => !entity.ResolvedEntityId.HasValue || !entity.IdentityVerified
                        || string.IsNullOrWhiteSpace(entity.IdentityProvenance))))
            issues.Add("company_population_approval_or_membership_incomplete");
        // A statement-item population (ranking over the authorized universe) has no product; a monthly
        // discovery population is always driven by an approved product concept.
        var statementPopulation = string.Equals(plan.PopulationCoveragePolicyId,
            StatementComparisonPolicyIds.AuthorizedCompanyUniverse, StringComparison.Ordinal);
        if (plan.PopulationCoveragePolicyId is not null
            && (plan.CompanyDiscoveryScope is null
                || plan.CompanyDiscoveryScope.TenantId != request.TenantId
                || string.IsNullOrWhiteSpace(plan.CompanyDiscoveryScope.ScopeId)
                || string.IsNullOrWhiteSpace(plan.CompanyDiscoveryScope.ApprovalReference)
                || statementPopulation == plan.Entities.Any(entity => entity.EntityKind.Equals("Product", StringComparison.OrdinalIgnoreCase))
                || statementPopulation && !(plan.Sources.Count == 1
                    && plan.Sources[0].ReferenceType == FinancialValueReferenceType.DirectSourceItem)
                || plan.Entities.Any(entity => entity.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase))))
            issues.Add("company_population_discovery_scope_or_unbound_entity_invalid");
        if (plan.ProductConcept is { } concept
            && (concept.Status != ProductConceptResolutionStatus.Resolved
                || string.IsNullOrWhiteSpace(concept.CatalogId)
                || string.IsNullOrWhiteSpace(concept.CatalogVersion)
                || string.IsNullOrWhiteSpace(concept.ApprovalReference)
                || concept.ApprovedAliases.Count == 0))
            issues.Add("product_concept_approval_or_alias_mapping_incomplete");
        if (plan.ReviewedProductEquivalence is { } equivalence
            && (equivalence.Status != ProductEquivalenceResolutionStatus.Resolved
                || string.IsNullOrWhiteSpace(equivalence.EquivalenceGroupId)
                || string.IsNullOrWhiteSpace(equivalence.ApprovalReference)
                || equivalence.Members.Select(member => member.CompanyId).Distinct().Count() != equivalence.Members.Count
                || equivalence.Members.Any(member => !member.IdentityReviewed
                    || string.IsNullOrWhiteSpace(member.CompanyProductKey)
                    || string.IsNullOrWhiteSpace(member.ProviderProductIdentity)
                    || string.IsNullOrWhiteSpace(member.IdentityReference)
                    || string.IsNullOrWhiteSpace(member.UnitCode))))
            issues.Add("product_equivalence_approval_or_membership_incomplete");
        if (plan.Pagination != (shape is null ? new PlanPagination(0, 100) : new PlanPagination(shape.Offset, shape.Limit)))
            issues.Add("plan_pagination_shape_mismatch");
        foreach (var filter in plan.Filters)
        {
            var needsValue = filter.Operator is not (PlanFilterOperator.IsNull or PlanFilterOperator.IsNotNull);
            if (needsValue != (filter.Value is not null)) issues.Add("plan_filter_value_operator_mismatch");
            if (filter.Value is { } literal && !IsValidLiteral(literal)) issues.Add("plan_filter_literal_invalid");
        }
        if (plan.Sources.Select(x => x.CatalogId).Distinct(StringComparer.Ordinal).Count() != plan.Sources.Count) issues.Add("plan_contains_duplicate_source");
        foreach (var source in plan.Sources)
        {
            if (!IsCatalogReference(source.CatalogId, context)) issues.Add($"plan_source_not_in_context:{source.CatalogId}");
            if (source.ReferenceType == FinancialValueReferenceType.MonthlyMeasure
                && !(context.MeasureIds ?? []).Contains(source.CatalogId, StringComparer.Ordinal))
                issues.Add($"monthly_measure_not_in_context:{source.CatalogId}");
            if (source.ReferenceType == FinancialValueReferenceType.DirectSourceItem
                && (!source.SourceItemCatalogId.HasValue || source.SourceItemCatalogId == Guid.Empty
                    || string.IsNullOrWhiteSpace(source.ProviderName) || string.IsNullOrWhiteSpace(source.StatementType)
                    || source.SourceItemId is null or <= 0
                    || !(source.IdentityStatus.Equals("verified", StringComparison.OrdinalIgnoreCase)
                         || source.IdentityStatus.Equals("verified-persisted-source-key", StringComparison.OrdinalIgnoreCase))))
                issues.Add($"direct_source_identity_not_verified:{source.CatalogId}");
            if (source.ReferenceType == FinancialValueReferenceType.DirectSourceItem
                && (context.FinancialValueReferences ?? []).All(x => x.CatalogId != source.CatalogId || x.SourceItemCatalogId != source.SourceItemCatalogId))
                issues.Add($"direct_source_identity_catalog_mismatch:{source.CatalogId}");
        }
        if (!Enum.TryParse<FinancialValueOperation>(requirements.RequestedFinancialValueOperation, out var requiredOperation)
            || plan.FinancialOperation.Operation != requiredOperation) issues.Add("plan_financial_operation_mismatch");
        var expectedKind = shape?.FinancialOperationKind ?? (requiredOperation == FinancialValueOperation.UnsupportedDerivedFinancialMetric
            ? PlanFinancialOperationKind.UnsupportedDerived : PlanFinancialOperationKind.DirectValue);
        if (plan.FinancialOperation.Kind != expectedKind) issues.Add("plan_financial_operation_kind_mismatch");
        if (plan.FinancialOperation.Operation == FinancialValueOperation.UnsupportedDerivedFinancialMetric)
            issues.Add("unsupported_derived_financial_metric");
        if (plan.FinancialOperation.Operation == FinancialValueOperation.ApprovedPeriodComparison
            && plan.FinancialOperation.Kind is not (PlanFinancialOperationKind.AbsoluteChange or PlanFinancialOperationKind.GrowthPercentage or PlanFinancialOperationKind.TimeSeries))
            issues.Add("comparison_operation_kind_missing_or_invalid");
        if (plan.FinancialOperation.Kind == PlanFinancialOperationKind.GrowthPercentage
            && !plan.Sources.Any(x => x.ReferenceType is FinancialValueReferenceType.DirectSourceItem
                or FinancialValueReferenceType.MonthlyMeasure))
            issues.Add("approved_growth_requires_cataloged_base_observation_source");
        if (plan.FinancialOperation.Operation == FinancialValueOperation.DirectValueRetrieval
            && plan.FinancialOperation.Kind != PlanFinancialOperationKind.DirectValue
            && !(plan.FinancialOperation.Kind == PlanFinancialOperationKind.TimeSeries
                && (plan.Presentation is PlanPresentation.TimeSeries or PlanPresentation.Chart)
                && ((plan.Period.ReportingPeriodStart.HasValue && plan.Period.ReportingPeriodEnd.HasValue)
                    || plan.Period.MaximumPeriods is > 0)))
            issues.Add("direct_value_operation_kind_mismatch");
        if (plan.FinancialOperation.Operation == FinancialValueOperation.UnsupportedDerivedFinancialMetric
            && plan.FinancialOperation.Kind != PlanFinancialOperationKind.UnsupportedDerived)
            issues.Add("unsupported_derived_operation_kind_mismatch");
        if (plan.FinancialOperation.Operation == FinancialValueOperation.ApprovedPeriodComparison)
        {
            var requiredScope = plan.FinancialOperation.ComparisonMode switch
            {
                PlanComparisonMode.MonthOverMonth => "period-over-period",
                PlanComparisonMode.YearOverYear => "year-over-year",
                _ => null
            };
            if (requiredScope is null || !plan.Period.ComparisonScopes.Contains(requiredScope, StringComparer.Ordinal))
                issues.Add("period_comparison_scope_missing_or_unapproved");
        }
        // A statement-item comparison reads one latest-common-period value per company (no aggregation);
        // a monthly comparison sums a governed measure over a common bounded period.
        var statementComparison = plan.Sources.Count == 1
            && plan.Sources[0].ReferenceType == FinancialValueReferenceType.DirectSourceItem;
        var comparedCompanyCount = plan.Entities.Count(x => x.EntityKind == "Company");
        if (plan.FinancialOperation.ComparisonMode == PlanComparisonMode.MultiCompanyComparison
            && (plan.FinancialOperation.Operation != FinancialValueOperation.DirectValueRetrieval
                || plan.FinancialOperation.Kind != PlanFinancialOperationKind.DirectValue
                || (comparedCompanyCount < 2 && !(statementComparison && statementPopulation && comparedCompanyCount == 0))
                || !plan.Period.ComparisonScopes.Contains("compare", StringComparer.Ordinal)
                || (!plan.Period.SelectLatestByReportingPeriod
                    && (!plan.Period.ReportingPeriodStart.HasValue || !plan.Period.ReportingPeriodEnd.HasValue))
                || (statementComparison
                    ? plan.Aggregations.Count != 0 || !plan.Period.SelectLatestByReportingPeriod
                        || plan.Sort.Any(sort => sort.CatalogId != plan.Sources[0].CatalogId) || plan.Sort.Count > 1
                    : plan.Aggregations.Count(x => x.Operation == PlanAggregationOperation.Sum) != 1)))
            issues.Add("multi_company_comparison_scope_missing_or_unapproved");
        // Every named company of a statement comparison must be bound to one verified company identity;
        // an unknown or ambiguous company stops the comparison before SQL instead of shrinking it.
        if (plan.FinancialOperation.ComparisonMode == PlanComparisonMode.MultiCompanyComparison && statementComparison
            && plan.Entities.Where(x => x.EntityKind == "Company").Any(x => !x.ResolvedEntityId.HasValue
                || x.ResolvedEntityId.Value == Guid.Empty || !x.IdentityVerified))
            issues.Add("statement_comparison_company_binding_unresolved");
        var monthlyMultiCompanyCompare = (requirements.FinancialValueReferences ?? []).Any(x =>
                string.Equals(x.ReferenceType, nameof(FinancialValueReferenceType.MonthlyMeasure), StringComparison.Ordinal))
            && plan.Entities.Count(x => x.EntityKind == "Company") > 1
            && (requirements.ComparisonScopes ?? []).Contains("compare", StringComparer.Ordinal);
        if (monthlyMultiCompanyCompare && ((!plan.Period.SelectLatestByReportingPeriod
            && (!plan.Period.ReportingPeriodStart.HasValue || !plan.Period.ReportingPeriodEnd.HasValue))
            || plan.Aggregations.Count(x => x.Operation == PlanAggregationOperation.Sum) != 1))
            issues.Add("multi_company_comparison_requires_common_bounded_period_and_sum");
        if (plan.FinancialOperation.FormulaAuthorized) issues.Add("offline_plan_cannot_authorize_formula_execution");
        if (!completeness.RetrievalComplete) issues.Add("retrieval_requirements_incomplete");

        var structural = issues.Count == 0;
        var directSourceIdentityAuthorized = plan.Sources.Count == 1
            && plan.Sources[0].ReferenceType == FinancialValueReferenceType.DirectSourceItem
            && plan.Sources[0].SourceItemCatalogId is { } sourceItemId && sourceItemId != Guid.Empty
            && plan.Sources[0].SourceItemId is > 0
            && !string.IsNullOrWhiteSpace(plan.Sources[0].ProviderName)
            && !string.IsNullOrWhiteSpace(plan.Sources[0].StatementType)
            && (plan.Sources[0].IdentityStatus.Equals("verified", StringComparison.OrdinalIgnoreCase)
                || plan.Sources[0].IdentityStatus.Equals("verified-persisted-source-key", StringComparison.OrdinalIgnoreCase));
        var executionEligible = _executionEnabled && structural && completeness.RetrievalComplete
            && completeness.QueryAuthorized && (completeness.SemanticReviewApproved || directSourceIdentityAuthorized);
        if (structural && !executionEligible)
            issues.Add(_executionEnabled ? "plan_execution_not_authorized" : "offline_plan_execution_disabled");
        return new(structural, completeness.RetrievalComplete, completeness.SemanticReviewApproved,
            completeness.QueryAuthorized, executionEligible, issues.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool IsCatalogReference(string id, SchemaContext context) =>
        context.MeasureIds.Contains(id, StringComparer.Ordinal)
        || context.DimensionIds.Contains(id, StringComparer.Ordinal)
        || context.EntityKinds.Contains(id, StringComparer.Ordinal)
        || context.RelationshipIds.Contains(id, StringComparer.Ordinal)
        || context.ProviderFinancialIndicatorIds?.Contains(id, StringComparer.Ordinal) == true
        || context.FinancialValueReferences?.Any(x => x.CatalogId == id) == true;

    private static IReadOnlyList<string> RequiredRelationshipIds(QuestionSchemaRequirements requirements, SchemaContext context)
    {
        var mappedMeasureIds = (requirements.MeasureIds ?? [])
            .Where(id => context.MeasureRelationshipIds?.ContainsKey(id) == true).ToHashSet(StringComparer.Ordinal);
        var mappedRelationships = mappedMeasureIds.SelectMany(id => context.MeasureRelationshipIds![id]).ToArray();
        return (mappedMeasureIds.Count > 0 ? mappedRelationships : (requirements.RelationshipIds ?? []).ToArray())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool SetEquals(IReadOnlyList<string> left, IReadOnlyList<string>? right) =>
        (left ?? []).ToHashSet(StringComparer.Ordinal).SetEquals(right ?? []);

    private static bool IsValidLiteral(PlanLiteral literal) => literal.Type switch
    {
        PlanLiteralType.Text or PlanLiteralType.CatalogIdentifier => !string.IsNullOrWhiteSpace(literal.Value) && literal.Value.Length <= 256,
        PlanLiteralType.Decimal => decimal.TryParse(literal.Value, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out _),
        PlanLiteralType.Integer => long.TryParse(literal.Value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out _),
        PlanLiteralType.Boolean => bool.TryParse(literal.Value, out _),
        PlanLiteralType.Date => DateOnly.TryParseExact(literal.Value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _),
        _ => false
    };
}

/// <summary>Composes the existing extractor, navigator, and deterministic planner for an offline evaluation path only.</summary>
public sealed class OfflineStructuredQueryPlanningService(
    IQuestionSchemaRequirementsExtractor requirementsExtractor,
    IVectorlessSchemaNavigator navigator,
    ICatalogNavigationHierarchyProvider hierarchyProvider,
    ISchemaContextCompletenessValidator completenessValidator,
    IStructuredQueryPlanBuilder planBuilder,
    IStructuredQueryPlanValidator planValidator,
    IStructuredQueryCompanyBinder companyBinder,
    IStructuredQueryProductBinder? productBinder = null,
    IReviewedCompanyPopulationResolver? populationResolver = null,
    IReviewedProductEquivalenceResolver? productEquivalenceResolver = null,
    IReviewedProductConceptResolver? productConceptResolver = null,
    IAuthorizedCompanyDiscoveryScopeResolver? companyDiscoveryScopeResolver = null) : IOfflineStructuredQueryPlanningService
{
    public async Task<OfflineStructuredQueryPlanningResult> PlanAsync(SchemaNavigationRequest request, CancellationToken cancellationToken)
    {
        var requirements = await requirementsExtractor.ExtractAsync(request, cancellationToken);
        return await PlanAsync(request, requirements, cancellationToken);
    }

    public async Task<OfflineStructuredQueryPlanningResult> PlanAsync(SchemaNavigationRequest request,
        QuestionSchemaRequirements independentlyExtractedRequirements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(independentlyExtractedRequirements);
        var requirements = independentlyExtractedRequirements;
        var navigation = await navigator.NavigateAsync(request, requirements, cancellationToken);
        var required = (requirements.MeasureIds ?? []).Concat(requirements.DimensionIds ?? [])
            .Concat(requirements.RelationshipIds ?? []).Concat((requirements.FinancialValueReferences ?? []).Select(x => x.CatalogId))
            .Concat((requirements.StructuredShape?.ProjectionCatalogIds ?? []).Concat(requirements.StructuredShape?.GroupByCatalogIds ?? [])
                .Concat((requirements.StructuredShape?.Filters ?? []).Select(x => x.CatalogId))
                .Concat((requirements.StructuredShape?.Aggregations ?? []).Select(x => x.CatalogId))
                .Concat((requirements.StructuredShape?.Sort ?? []).Select(x => x.CatalogId)))
            .Distinct(StringComparer.Ordinal).ToArray();
        var context = await hierarchyProvider.GetContextAsync(new SchemaContextExpansionRequest(request.ContractVersion,
            request.CatalogVersion, request.CatalogHash, navigation.SelectedNodeIds, 0, RequiredCatalogIds: required), cancellationToken);
        if (context is null) throw new InvalidOperationException("Catalog context is unavailable for offline plan validation.");
        context = context with
        {
            ComparisonScopes = requirements.ComparisonScopes,
            PresentationRequirements = requirements.PresentationRequirements,
            StatementScopes = requirements.StatementScopes,
            AggregationRequirements = requirements.AggregationRequirements,
            OrderingRequirements = requirements.OrderingRequirements,
            EntityReferences = (requirements.EntityReferences ?? []).Concat(requirements.ProductReferences ?? [])
                .Distinct(StringComparer.Ordinal).ToArray(),
            FinancialValueReferences = context.FinancialValueReferences,
            SourcePolicies = context.SourcePolicies.Concat(requirements.SourcePolicies).Distinct(StringComparer.Ordinal).ToArray(),
            PeriodPolicies = context.PeriodPolicies.Concat(requirements.PeriodPolicies).Distinct(StringComparer.Ordinal).ToArray()
        };
        var completeness = completenessValidator.Validate(requirements, context);
        var productMentions = requirements.ProductReferences ?? [];
        IReadOnlyList<string> companyMentions = (requirements.EntityReferences ?? [])
            .Where(mention => !productMentions.Contains(mention, StringComparer.OrdinalIgnoreCase)).ToArray();
        var companyBindings = new List<StructuredCompanyBinding>();
        foreach (var mention in companyMentions)
            companyBindings.Add(await companyBinder.ResolveAsync(mention, request.TenantId, cancellationToken));
        if (companyMentions.Count == 1 && companyBindings[0].Status is StructuredCompanyBindingStatus.NotFound or StructuredCompanyBindingStatus.Ambiguous
            && populationResolver is not null)
        {
            var population = populationResolver.Resolve(companyMentions[0]);
            if (population.Status == CompanyPopulationResolutionStatus.Resolved)
            {
                requirements = requirements with
                {
                    EntityReferences = population.Members.Select(member => member.CompanyIdentity).ToArray(),
                    ResolvedCompanyPopulation = population,
                    NormalizationDiagnostics = requirements.NormalizationDiagnostics
                        .Append($"company_population_resolved:{population.PopulationId}:{population.ApprovalReference}").ToArray()
                };
                companyMentions = population.Members.Select(member => member.CompanyIdentity).ToArray();
                companyBindings = population.Members.Select(member => new StructuredCompanyBinding(
                    StructuredCompanyBindingStatus.Found, member.CompanyId, [member.CompanyId],
                    member.IdentityReference, null)).ToList();
                context = context with { EntityReferences = companyMentions.Concat(productMentions).Distinct(StringComparer.Ordinal).ToArray() };
                completeness = completenessValidator.Validate(requirements, context);
            }
            else
            {
                requirements = requirements with
                {
                    NormalizationDiagnostics = requirements.NormalizationDiagnostics
                        .Append(population.DiagnosticCode ?? "company_population_not_approved").Distinct(StringComparer.Ordinal).ToArray()
                };
            }
        }
        var companyBinding = companyMentions.Count == 1 ? companyBindings[0] : null;

        // An unbound company population for a statement-item comparison or ranking is the tenant's
        // reviewed company universe; without an authorized scope the plan is refused before SQL.
        if (requirements.PopulationDiscoveryRequested && companyMentions.Count == 0 && productMentions.Count == 0
            && requirements.StructuredShape?.ComparisonMode == PlanComparisonMode.MultiCompanyComparison
            && (requirements.FinancialValueReferences ?? []) is [{ ReferenceType: nameof(FinancialValueReferenceType.DirectSourceItem) }])
        {
            var scope = companyDiscoveryScopeResolver?.Resolve(request.TenantId);
            requirements = requirements with
            {
                AuthorizedCompanyDiscoveryScope = scope,
                PopulationCoveragePolicyId = StatementComparisonPolicyIds.AuthorizedCompanyUniverse,
                NormalizationDiagnostics = requirements.NormalizationDiagnostics
                    .Append(scope is null ? "company_discovery_scope_not_authorized"
                        : $"statement_population_scope_resolved:{scope.ScopeId}:{scope.ApprovalReference}")
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
        }

        var shape = requirements.StructuredShape;
        var hasMonthlyMeasure = (requirements.FinancialValueReferences ?? []).Any(reference =>
            reference.ReferenceType == nameof(FinancialValueReferenceType.MonthlyMeasure));
        var hasApprovedSumShape = shape?.Aggregations?.Any(aggregation =>
            aggregation.Operation == PlanAggregationOperation.Sum
            && (requirements.FinancialValueReferences ?? []).Any(reference =>
                reference.ReferenceType == nameof(FinancialValueReferenceType.MonthlyMeasure)
                && reference.CatalogId == aggregation.CatalogId)) == true;
        var periodOnlyAmbiguity = requirements.HasUnknownOrAmbiguousRequirements
            && (requirements.UncertaintyReason?.Contains("period", StringComparison.OrdinalIgnoreCase) == true
                || requirements.UncertaintyReason?.Contains("reporting month", StringComparison.OrdinalIgnoreCase) == true);
        if (shape?.ComparisonMode == PlanComparisonMode.MultiCompanyComparison
            && companyMentions.Count >= 2 && companyBindings.Count == companyMentions.Count
            && companyBindings.All(binding => binding.Status == StructuredCompanyBindingStatus.Found)
            && productMentions.Count == 0 && hasMonthlyMeasure && hasApprovedSumShape
            && (!requirements.HasUnknownOrAmbiguousRequirements || periodOnlyAmbiguity))
        {
            var effectivePeriodPolicies = (requirements.PeriodPolicies ?? []).Append("latest")
                .Distinct(StringComparer.Ordinal).ToArray();
            requirements = requirements with
            {
                PeriodPolicies = effectivePeriodPolicies,
                HasUnknownOrAmbiguousRequirements = false,
                UncertaintyReason = null,
                NormalizationDiagnostics = requirements.NormalizationDiagnostics
                    .Append("multi_company_monthly_comparison_uses_latest_common_eligible_period")
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
            context = context with
            {
                PeriodPolicies = (context.PeriodPolicies ?? []).Concat(effectivePeriodPolicies)
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
            completeness = completenessValidator.Validate(requirements, context);
        }

        StructuredProductBinding? productBinding = null;
        ProductEquivalenceResolution? productEquivalence = null;
        ProductConceptResolution? productConcept = null;
        if (productMentions.Count == 1)
        {
            var companyMention = companyMentions.Count == 1 ? companyMentions[0] : null;
            if (companyMentions.Count == 0 && productConceptResolver is not null)
            {
                productConcept = productConceptResolver.Resolve(productMentions[0]);
                if (productConcept.Status == ProductConceptResolutionStatus.Resolved)
                {
                    var producerSalesIntent = new ProducerSalesQueryIntentClassifier()
                        .Classify(request.Question, productMentions[0]);
                    var containsProductionActivity = request.Question.Contains("تولید", StringComparison.Ordinal)
                        || request.Question.Contains("توليد", StringComparison.Ordinal);
                    const string monthlyProductionMeasureId = "metric_monthly_production_quantity";
                    const string monthlyActivityRelationshipId = "rel_31e410fb3ae11956";
                    if (producerSalesIntent.IsProducerSalesRanking)
                    {
                        if (producerSalesIntent.RequiresClarification || producerSalesIntent.Scope is null
                            || producerSalesIntent.MeasureCatalogId is null)
                        {
                            requirements = requirements with
                            {
                                PopulationDiscoveryRequested = true,
                                PopulationSalesScopeAmbiguous = true,
                                NormalizationDiagnostics = requirements.NormalizationDiagnostics
                                    .Append("producer_sales_scope_ambiguous")
                                    .Distinct(StringComparer.Ordinal).ToArray()
                            };
                        }
                        else
                        {
                            var salesMeasureId = producerSalesIntent.MeasureCatalogId;
                            var dimensions = new[] { "dim_company", "dim_monthly_period", "dim_product_identity" }
                                .Where(dimension => context.DimensionIds.Contains(dimension, StringComparer.Ordinal)).ToArray();
                            var selectedScope = producerSalesIntent.Scope.Value;
                            var productSalesFilter = selectedScope == PopulationSalesScope.RequestedProductSales
                                ? new[] { new PlanFilter("dim_product_identity", PlanFilterOperator.Equal,
                                    new PlanLiteral(PlanLiteralType.CatalogIdentifier, productConcept.CatalogId!)) }
                                : Array.Empty<PlanFilter>();
                            requirements = requirements with
                            {
                                MeasureIds = [salesMeasureId],
                                DimensionIds = dimensions,
                                EntityKinds = ["company", "product"],
                                RelationshipIds = [monthlyActivityRelationshipId],
                                PeriodPolicies = [],
                                ComparisonScopes = [],
                                PresentationRequirements = ["ranked-table"],
                                AggregationRequirements = ["sum"],
                                OrderingRequirements = ["descending"],
                                FinancialValueReferences = [new(nameof(FinancialValueReferenceType.MonthlyMeasure), salesMeasureId)],
                                RequestedFinancialValueOperation = nameof(FinancialValueOperation.DirectValueRetrieval),
                                StructuredShape = new StructuredPlanShape([salesMeasureId], ["dim_company", "dim_monthly_period"],
                                    productSalesFilter, [new(salesMeasureId, PlanAggregationOperation.Sum)],
                                    [new(salesMeasureId, PlanSortDirection.Descending)], 0, 10,
                                    PlanFinancialOperationKind.DirectValue),
                                PopulationSalesScope = selectedScope,
                                HasUnknownOrAmbiguousRequirements = false,
                                UncertaintyReason = null,
                                NormalizationDiagnostics = requirements.NormalizationDiagnostics
                                    .Append($"producer_sales_ranking_scope:{selectedScope}")
                                    .Distinct(StringComparer.Ordinal).ToArray()
                            };
                            navigation = navigation with
                            {
                                SelectedMeasureIds = [salesMeasureId],
                                SelectedDimensionIds = dimensions,
                                SelectedRelationshipIds = [monthlyActivityRelationshipId],
                                SelectedPeriodPolicies = [],
                                SelectedPresentationRequirements = ["ranked-table"],
                                SelectedEntityKinds = ["company", "product"],
                                SelectedEntityReferences = productMentions.ToArray(),
                                RequiredMeasureIds = [salesMeasureId]
                            };
                            context = context with
                            {
                                MeasureIds = context.MeasureIds.Append(salesMeasureId).Distinct(StringComparer.Ordinal).ToArray(),
                                DimensionIds = dimensions,
                                RelationshipIds = context.RelationshipIds.Append(monthlyActivityRelationshipId)
                                    .Distinct(StringComparer.Ordinal).ToArray(),
                                PeriodPolicies = [],
                                PresentationRequirements = ["ranked-table"],
                                AggregationRequirements = ["sum"],
                                OrderingRequirements = ["descending"],
                                EntityReferences = productMentions,
                                EntityKinds = ["company", "product"]
                            };
                            completeness = completenessValidator.Validate(requirements, context);
                        }
                    }
                    // Production discovery is governed deterministically by the approved product concept, the
                    // explicit production verb and the catalog measure. When the navigator could not map the
                    // question onto the monthly domain, the catalog context is expanded to that measure here so
                    // the deterministic rule below decides, not the model's uncertainty about the catalog.
                    if (companyMentions.Count == 0 && containsProductionActivity && !producerSalesIntent.IsProducerSalesRanking
                        && !context.MeasureIds.Contains(monthlyProductionMeasureId, StringComparer.Ordinal))
                    {
                        var expanded = await hierarchyProvider.GetContextAsync(new SchemaContextExpansionRequest(
                            request.ContractVersion, request.CatalogVersion, request.CatalogHash, navigation.SelectedNodeIds, 0,
                            RequiredCatalogIds: [monthlyProductionMeasureId, monthlyActivityRelationshipId,
                                "dim_company", "dim_product_identity", "dim_monthly_period"]), cancellationToken);
                        if (expanded is not null && expanded.MeasureIds.Contains(monthlyProductionMeasureId, StringComparer.Ordinal))
                        {
                            context = expanded with
                            {
                                ComparisonScopes = context.ComparisonScopes,
                                PresentationRequirements = context.PresentationRequirements,
                                StatementScopes = context.StatementScopes,
                                AggregationRequirements = context.AggregationRequirements,
                                OrderingRequirements = context.OrderingRequirements,
                                EntityReferences = context.EntityReferences,
                                SourcePolicies = context.SourcePolicies.Concat(expanded.SourcePolicies).Distinct(StringComparer.Ordinal).ToArray(),
                                PeriodPolicies = context.PeriodPolicies.Concat(expanded.PeriodPolicies).Distinct(StringComparer.Ordinal).ToArray()
                            };
                            requirements = requirements with
                            {
                                NormalizationDiagnostics = requirements.NormalizationDiagnostics
                                    .Append("population_discovery_context_expanded_to_monthly_production_measure")
                                    .Distinct(StringComparer.Ordinal).ToArray()
                            };
                        }
                    }
                    var hasMonthlyProductionReference = (requirements.FinancialValueReferences ?? [])
                        .Any(reference => reference.ReferenceType == nameof(FinancialValueReferenceType.MonthlyMeasure)
                            && reference.CatalogId == monthlyProductionMeasureId);
                    if (companyMentions.Count == 0 && containsProductionActivity && !producerSalesIntent.IsProducerSalesRanking
                        && !hasMonthlyProductionReference
                        && context.MeasureIds.Contains(monthlyProductionMeasureId, StringComparer.Ordinal)
                        && context.RelationshipIds.Contains(monthlyActivityRelationshipId, StringComparer.Ordinal))
                    {
                        var dimensions = new[] { "dim_company", "dim_product_identity", "dim_monthly_period" }
                            .Where(dimension => context.DimensionIds.Contains(dimension, StringComparer.Ordinal)).ToArray();
                        requirements = requirements with
                        {
                            MeasureIds = [monthlyProductionMeasureId],
                            DimensionIds = dimensions,
                            EntityKinds = ["company", "product"],
                            RelationshipIds = [monthlyActivityRelationshipId],
                            // An explicit, deterministically normalized month survives the rewrite; without one the
                            // trailing-12-month observation window policy applies below.
                            PeriodPolicies = requirements.StructuredShape?.ReportingPeriodStart is not null
                                && requirements.StructuredShape?.ReportingPeriodEnd is not null
                                ? ["explicit-period"] : [],
                            PresentationRequirements = ["table"],
                            FinancialValueReferences = [new(nameof(FinancialValueReferenceType.MonthlyMeasure),
                                monthlyProductionMeasureId)],
                            RequestedFinancialValueOperation = nameof(FinancialValueOperation.DirectValueRetrieval),
                            StructuredShape = new StructuredPlanShape([], [], [], [], [], 0, 20,
                                PlanFinancialOperationKind.DirectValue,
                                requirements.StructuredShape?.ReportingPeriodStart,
                                requirements.StructuredShape?.ReportingPeriodEnd),
                            HasUnknownOrAmbiguousRequirements = false,
                            UncertaintyReason = null,
                            NormalizationDiagnostics = requirements.NormalizationDiagnostics
                                .Append("population_product_production_activity_resolved_to_monthly_production_quantity")
                                .Distinct(StringComparer.Ordinal).ToArray()
                        };
                        navigation = navigation with
                        {
                            SelectedMeasureIds = [monthlyProductionMeasureId],
                            SelectedDimensionIds = dimensions,
                            SelectedRelationshipIds = [monthlyActivityRelationshipId],
                            SelectedPeriodPolicies = [],
                            SelectedPresentationRequirements = ["table"],
                            SelectedEntityKinds = ["company", "product"],
                            SelectedEntityReferences = (requirements.ProductReferences ?? []).ToArray(),
                            RequiredMeasureIds = [monthlyProductionMeasureId]
                        };
                        context = context with
                        {
                            DimensionIds = dimensions,
                            RelationshipIds = [monthlyActivityRelationshipId],
                            PeriodPolicies = [],
                            PresentationRequirements = ["table"],
                            EntityReferences = productMentions,
                            EntityKinds = ["company", "product"]
                        };
                        completeness = completenessValidator.Validate(requirements, context);
                    }
                    requirements = requirements with
                    {
                        ResolvedProductConcept = productConcept,
                        PopulationDiscoveryRequested = true,
                        AuthorizedCompanyDiscoveryScope = companyDiscoveryScopeResolver?.Resolve(request.TenantId),
                        PopulationCoveragePolicyId = "explicit-reporting-period-v1",
                        PopulationSalesScope = producerSalesIntent.Scope,
                        PopulationSalesScopeAmbiguous = producerSalesIntent.RequiresClarification,
                        NormalizationDiagnostics = requirements.NormalizationDiagnostics
                            .Append($"reviewed_product_concept_resolved:{productConcept.CatalogId}:{productConcept.CatalogVersion}")
                            .Concat(companyDiscoveryScopeResolver?.Resolve(request.TenantId) is null
                                ? ["company_discovery_scope_not_authorized"] : [])
                            .Concat(productConcept.MappingCoverageStatus == "approved-aliases-only"
                                ? ["product_population_coverage_limited_to_approved_aliases"] : [])
                            .ToArray()
                    };
                    productBinding = new(StructuredProductBindingStatus.Found, null, productConcept.CatalogId,
                        productConcept.CanonicalLabel, null,
                        $"{productConcept.CatalogVersion}:{productConcept.ApprovalReference}", [], null);

                    // Without an explicit month, discovery is bounded to the prior twelve calendar months.
                    // The response describes observed reports in this window and does not claim ongoing production.
                    if (requirements.StructuredShape?.ReportingPeriodStart is null
                        && requirements.StructuredShape?.ReportingPeriodEnd is null
                        && !(requirements.PeriodPolicies ?? []).Contains("explicit-period", StringComparer.Ordinal))
                    {
                        var end = DateOnly.FromDateTime(DateTime.UtcNow);
                        var start = end.AddMonths(-12);
                        requirements = requirements with
                        {
                            StructuredShape = (requirements.StructuredShape ?? new StructuredPlanShape([], [], [], [], [])) with
                            { ReportingPeriodStart = start, ReportingPeriodEnd = end, MaximumPeriods = 12 },
                            PopulationCoveragePolicyId = "observed-production-trailing-12-months-v1",
                            PeriodPolicies = (requirements.PeriodPolicies ?? []).Append("approved-observation-window")
                                .Distinct(StringComparer.Ordinal).ToArray(),
                            NormalizationDiagnostics = requirements.NormalizationDiagnostics
                                .Append("population_discovery_uses_trailing_12_calendar_month_observation_window")
                                .Distinct(StringComparer.Ordinal).ToArray()
                        };
                    }
                }
                else
                    productBinding = new(StructuredProductBindingStatus.Missing, null, null, null, null,
                        productConcept.ApprovalReference, [], productConcept.DiagnosticCode);
            }
            else if (companyBindings.Count > 1 && productEquivalenceResolver is not null
                && companyBindings.All(binding => binding.Status == StructuredCompanyBindingStatus.Found))
            {
                productEquivalence = productEquivalenceResolver.Resolve(productMentions[0],
                    companyBindings.Where(binding => binding.CompanyId.HasValue).Select(binding => binding.CompanyId!.Value).ToArray());
                if (productEquivalence.Status == ProductEquivalenceResolutionStatus.Resolved)
                {
                    requirements = requirements with
                    {
                        ResolvedProductEquivalence = productEquivalence,
                        NormalizationDiagnostics = requirements.NormalizationDiagnostics
                            .Append($"reviewed_product_equivalence_resolved:{productEquivalence.EquivalenceGroupId}:{productEquivalence.ApprovalReference}").ToArray()
                    };
                    productBinding = new(StructuredProductBindingStatus.Found, null, productEquivalence.EquivalenceGroupId,
                        productEquivalence.DisplayName, productEquivalence.UnitCode, productEquivalence.ApprovalReference, [], null);
                }
                else
                    productBinding = new(StructuredProductBindingStatus.Missing, null, null, null, null,
                        productEquivalence.ApprovalReference, [], productEquivalence.DiagnosticCode);
            }
            else productBinding = productBinder is null
                ? new StructuredProductBinding(StructuredProductBindingStatus.Missing, null, null, null, null, null, [],
                    "monthly_product_resolver_unavailable")
                : await productBinder.ResolveAsync(companyMention, productMentions[0], cancellationToken);
            if (productBinding.Status == StructuredProductBindingStatus.Found && companyBinding?.Status == StructuredCompanyBindingStatus.Found
                && productBinding.CompanyId != companyBinding.CompanyId)
                productBinding = productBinding with { Status = StructuredProductBindingStatus.CompanyMismatch,
                    ProductKey = null, DiagnosticCode = "monthly_product_company_binding_mismatch" };
        }
        else if (productMentions.Count > 1)
            productBinding = new StructuredProductBinding(StructuredProductBindingStatus.Ambiguous, null, null, null, null, null, [],
                "multiple_product_mentions_require_explicit_binding");

        if (requirements.PopulationDiscoveryRequested)
        {
            context = context with
            {
                PeriodPolicies = (context.PeriodPolicies ?? []).Concat(requirements.PeriodPolicies ?? [])
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
            completeness = completenessValidator.Validate(requirements, context);
        }

        var plan = planBuilder.Build(request, requirements, navigation, context, completeness);
        if (companyBindings.Count > 0)
        {
            plan = plan with
            {
                Entities = plan.Entities.Select(entity => entity.EntityKind.Equals("company", StringComparison.OrdinalIgnoreCase)
                    ? BindCompany(entity)
                    : entity).ToArray(),
                Diagnostics = plan.Diagnostics.Concat(companyBindings.Where(binding => binding.DiagnosticCode is not null)
                        .Select(binding => binding.DiagnosticCode!))
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
        }
        if (productBinding is not null)
        {
            plan = plan with
            {
                    Entities = plan.Entities.Select(entity => entity.EntityKind.Equals("product", StringComparison.OrdinalIgnoreCase)
                    ? entity with { ResolvedEntityKey = productBinding.Status == StructuredProductBindingStatus.Found
                        ? productBinding.ProductKey : null,
                        IdentityProvenance = productBinding.IdentityProvenance,
                        IdentityVerified = productBinding.Status == StructuredProductBindingStatus.Found
                            && !string.IsNullOrWhiteSpace(productBinding.IdentityProvenance) }
                    : entity).ToArray(),
                Diagnostics = plan.Diagnostics.Concat(productBinding.DiagnosticCode is null ? [] : [productBinding.DiagnosticCode])
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
        }
        if (productConcept is { Status: ProductConceptResolutionStatus.Resolved })
            plan = plan with { ProductConcept = productConcept };
        var validation = planValidator.Validate(request, requirements, plan, context, completeness);
        plan = plan with
        {
            RetrievalComplete = validation.RetrievalComplete,
            SemanticReviewApproved = validation.SemanticReviewApproved,
            QueryAuthorized = validation.QueryAuthorized,
            ExecutionEligible = validation.ExecutionEligible,
            Diagnostics = plan.Diagnostics.Concat(validation.Diagnostics).Distinct(StringComparer.Ordinal).ToArray()
        };
        return new(requirements, navigation, plan, validation, companyBinding, productBinding, companyBindings);

        PlanEntityReference BindCompany(PlanEntityReference entity)
        {
            var index = companyMentions.ToList().FindIndex(mention =>
                string.Equals(mention, entity.UserReference, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index >= companyBindings.Count) return entity;
            var binding = companyBindings[index];
            return entity with { ResolvedEntityId = binding.Status == StructuredCompanyBindingStatus.Found
                ? binding.CompanyId : null,
                IdentityProvenance = binding.IdentityProvenance,
                IdentityVerified = binding.Status == StructuredCompanyBindingStatus.Found
                    && !string.IsNullOrWhiteSpace(binding.IdentityProvenance) };
        }
    }
}

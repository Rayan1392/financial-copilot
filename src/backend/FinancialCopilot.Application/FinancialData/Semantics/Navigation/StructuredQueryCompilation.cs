using System.Security.Cryptography;
using System.Text;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>Immutable physical mappings produced only after validating a catalog package.</summary>
public sealed class StructuredQueryCatalogSnapshot
{
    internal StructuredQueryCatalogSnapshot(string catalogVersion, string catalogHash, bool integrityValidated,
        IReadOnlyList<StructuredSqlTable> tables, IReadOnlyList<StructuredSqlColumn> columns,
        IReadOnlyList<StructuredSqlRelationship> relationships, IReadOnlyList<StructuredSqlDirectSource> directSources,
        IReadOnlyList<StructuredSqlMonthlyMeasure>? monthlyMeasures = null)
    {
        CatalogVersion = catalogVersion; CatalogHash = catalogHash; IntegrityValidated = integrityValidated;
        Tables = Array.AsReadOnly(tables.ToArray()); Columns = Array.AsReadOnly(columns.ToArray());
        Relationships = Array.AsReadOnly(relationships.ToArray());
        DirectSources = Array.AsReadOnly(directSources.Select(x => x with { RelationshipIds = Array.AsReadOnly(x.RelationshipIds.ToArray()) }).ToArray());
        MonthlyMeasures = Array.AsReadOnly((monthlyMeasures ?? []).Select(x => x with
            { RelationshipIds = Array.AsReadOnly(x.RelationshipIds.ToArray()) }).ToArray());
    }
    public string CatalogVersion { get; }
    public string CatalogHash { get; }
    public bool IntegrityValidated { get; }
    public IReadOnlyList<StructuredSqlTable> Tables { get; }
    public IReadOnlyList<StructuredSqlColumn> Columns { get; }
    public IReadOnlyList<StructuredSqlRelationship> Relationships { get; }
    public IReadOnlyList<StructuredSqlDirectSource> DirectSources { get; }
    public IReadOnlyList<StructuredSqlMonthlyMeasure> MonthlyMeasures { get; }
}

public sealed record StructuredSqlTable(string CatalogId, string Schema, string Name, bool Queryable,
    bool SemanticApproved = false);
public sealed record StructuredSqlColumn(string CatalogId, string TableCatalogId, string Name, bool Queryable);
public sealed record StructuredSqlRelationship(string CatalogId, string FromTableCatalogId, string FromColumnCatalogId,
    string ToTableCatalogId, string ToColumnCatalogId, string Cardinality, bool Queryable,
    bool Verified = false, bool PhysicalConstraintPresent = false, bool SemanticApproved = false,
    bool QueryAuthorized = false, string? JoinGrain = null, bool FromKeyNullable = true,
    bool ToKeyNullable = true, bool ReferentialIntegrityEnforced = false);
public sealed record StructuredSqlDirectSource(string CatalogId, Guid SourceItemCatalogId, string ProviderName,
    string StatementType, int SourceItemId, string SourceItemsTableCatalogId, string LineItemsTableCatalogId, string StatementsTableCatalogId,
    string CompaniesTableCatalogId, IReadOnlyList<string> RelationshipIds, bool Queryable,
    string? SourceUnit = null, string? SourceUnitStatus = null, string? NormalizedUnit = null,
    string? MetricCode = null, bool IdentityVerified = false, bool SemanticApproved = false,
    string? EffectiveUnit = null, string? EffectiveUnitStatus = null, string? EffectiveUnitProvenance = null);
public sealed record StructuredSqlMonthlyMeasure(string CatalogId, string MetricCode, string LineItemsTableCatalogId,
    string ReportsTableCatalogId, string ValueColumnCatalogId, string ProductCodeColumnCatalogId,
    string ProductTitleColumnCatalogId, string QuantityUnitColumnCatalogId, string PeriodStartColumnCatalogId,
    string PeriodEndColumnCatalogId, string ReportProviderColumnCatalogId, string ReportTypeColumnCatalogId,
    string OutputTypeColumnCatalogId, string CompanyIdColumnCatalogId, string AcceptedColumnCatalogId,
    string RevisionStatusColumnCatalogId, string ExternalReportIdColumnCatalogId,
    string SourceRowFingerprintColumnCatalogId, string SourceMultiplicityColumnCatalogId,
    IReadOnlyList<string> RelationshipIds, string ProviderName, string ReportType, int OutputType,
    bool IsAccepted, string RevisionStatus, string UnitKind, string? UnitCode, string? SourceUnitColumn,
    bool UnitVerified, bool RecomputeAllowed, bool Queryable, bool SemanticApproved);

public sealed record StructuredQueryParameter(string Name, object? Value);
public sealed class CompiledReadOnlyQuery
{
    internal CompiledReadOnlyQuery(string sql, IReadOnlyList<StructuredQueryParameter> parameters, Guid tenantId, string catalogVersion,
        string catalogHash, string queryFingerprint, int maximumRows, PlanPresentation resultShape,
        IReadOnlyList<string> sourceCatalogIds, IReadOnlyList<string> relationshipCatalogIds,
        IReadOnlyList<string> physicalObjectCatalogIds, Guid companyId, FinancialValueOperation operation,
        bool semanticReviewApproved, bool queryAuthorized, bool retrievalComplete,
        string? effectiveUnit = null, string? effectiveUnitStatus = null, string? effectiveUnitProvenance = null,
        bool isMonthlyValueQuery = false, IReadOnlyList<Guid>? companyIds = null,
        string? companyDiscoveryScopeId = null, string? companyDiscoveryApprovalReference = null,
        bool companyDiscoveryCoverageComplete = false, string? productMappingCoverageStatus = null,
        bool identityAuthorized = false)
    {
        Sql = sql; Parameters = Array.AsReadOnly(parameters.ToArray()); TenantId = tenantId; CatalogVersion = catalogVersion; CatalogHash = catalogHash;
        QueryFingerprint = queryFingerprint; MaximumRows = maximumRows; ResultShape = resultShape;
        SourceCatalogIds = Array.AsReadOnly(sourceCatalogIds.ToArray()); SemanticReviewApproved = semanticReviewApproved;
        RelationshipCatalogIds = Array.AsReadOnly(relationshipCatalogIds.ToArray());
        PhysicalObjectCatalogIds = Array.AsReadOnly(physicalObjectCatalogIds.ToArray());
        CompanyId = companyId; Operation = operation;
        QueryAuthorized = queryAuthorized; RetrievalComplete = retrievalComplete;
        EffectiveUnit = effectiveUnit; EffectiveUnitStatus = effectiveUnitStatus; EffectiveUnitProvenance = effectiveUnitProvenance;
        IsMonthlyValueQuery = isMonthlyValueQuery;
        CompanyIds = Array.AsReadOnly((companyIds ?? [companyId]).Distinct().ToArray());
        IsPopulationDiscoveryQuery = companyDiscoveryScopeId is not null;
        CompanyDiscoveryScopeId = companyDiscoveryScopeId;
        CompanyDiscoveryApprovalReference = companyDiscoveryApprovalReference;
        CompanyDiscoveryCoverageComplete = companyDiscoveryCoverageComplete;
        ProductMappingCoverageStatus = productMappingCoverageStatus;
        IdentityAuthorized = identityAuthorized;
    }

    public string Sql { get; }
    public IReadOnlyList<StructuredQueryParameter> Parameters { get; }
    public Guid TenantId { get; }
    public string CatalogVersion { get; }
    public string CatalogHash { get; }
    public string QueryFingerprint { get; }
    public int MaximumRows { get; }
    public PlanPresentation ResultShape { get; }
    public IReadOnlyList<string> SourceCatalogIds { get; }
    public IReadOnlyList<string> RelationshipCatalogIds { get; }
    public IReadOnlyList<string> PhysicalObjectCatalogIds { get; }
    public Guid CompanyId { get; }
    public FinancialValueOperation Operation { get; }
    public bool SemanticReviewApproved { get; }
    /// <summary>Authorizes exact registered source retrieval without asserting canonical equivalence.</summary>
    public bool IdentityAuthorized { get; }
    public bool QueryAuthorized { get; }
    public bool RetrievalComplete { get; }
    public string? EffectiveUnit { get; }
    public string? EffectiveUnitStatus { get; }
    public string? EffectiveUnitProvenance { get; }
    public bool IsMonthlyValueQuery { get; }
    public IReadOnlyList<Guid> CompanyIds { get; }
    public bool IsPopulationDiscoveryQuery { get; }
    public string? CompanyDiscoveryScopeId { get; }
    public string? CompanyDiscoveryApprovalReference { get; }
    public bool CompanyDiscoveryCoverageComplete { get; }
    public string? ProductMappingCoverageStatus { get; }
}

public sealed record StructuredQueryCompilationResult(bool Success, CompiledReadOnlyQuery? Query,
    IReadOnlyList<string> Diagnostics);

public enum StructuredQueryExecutionStatus { Succeeded, NoData, Incomplete, Ambiguous, Truncated, Unauthorized, TimedOut, Cancelled, Failed }
public sealed record StructuredQueryExecutionResult(StructuredQueryExecutionStatus Status,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, IReadOnlyList<string> RequestedReferenceIds,
    string? CatalogVersion, string? CatalogHash, string? QueryFingerprint, TimeSpan? Elapsed,
    long? ResultBytes, string? AuthorizationProvenance, IReadOnlyList<string> Warnings,
    string? DiagnosticCode, string? EffectiveUnit = null, string? EffectiveUnitStatus = null,
    string? EffectiveUnitProvenance = null);

/// <summary>A short-lived grant issued from an integrity-checked catalog-backed query.</summary>
public sealed class StructuredQueryExecutionGrant
{
    internal StructuredQueryExecutionGrant(Guid tenantId, string environmentName, string databaseName,
        string databaseRole, string catalogHash, IReadOnlySet<string> allowedSourceCatalogIds,
        IReadOnlySet<string> allowedRelationshipCatalogIds, IReadOnlySet<string> allowedPhysicalObjectCatalogIds,
        IReadOnlySet<Guid> allowedCompanyIds, IReadOnlySet<FinancialValueOperation> allowedOperations,
        string provenance, DateTimeOffset expiresAt, string? companyDiscoveryScopeId = null)
    {
        TenantId = tenantId; EnvironmentName = environmentName; DatabaseName = databaseName;
        DatabaseRole = databaseRole; CatalogHash = catalogHash;
        AllowedSourceCatalogIds = new HashSet<string>(allowedSourceCatalogIds, StringComparer.Ordinal);
        AllowedRelationshipCatalogIds = new HashSet<string>(allowedRelationshipCatalogIds, StringComparer.Ordinal);
        AllowedPhysicalObjectCatalogIds = new HashSet<string>(allowedPhysicalObjectCatalogIds, StringComparer.Ordinal);
        AllowedCompanyIds = new HashSet<Guid>(allowedCompanyIds);
        AllowedOperations = new HashSet<FinancialValueOperation>(allowedOperations);
        CompanyDiscoveryScopeId = companyDiscoveryScopeId;
        Provenance = provenance; ExpiresAt = expiresAt;
    }
    public Guid TenantId { get; }
    public string EnvironmentName { get; }
    public string DatabaseName { get; }
    public string DatabaseRole { get; }
    public string CatalogHash { get; }
    public IReadOnlySet<string> AllowedSourceCatalogIds { get; }
    public IReadOnlySet<string> AllowedRelationshipCatalogIds { get; }
    public IReadOnlySet<string> AllowedPhysicalObjectCatalogIds { get; }
    public IReadOnlySet<Guid> AllowedCompanyIds { get; }
    public IReadOnlySet<FinancialValueOperation> AllowedOperations { get; }
    public string? CompanyDiscoveryScopeId { get; }
    public string Provenance { get; }
    public DateTimeOffset ExpiresAt { get; }
}

public interface IStructuredQueryAuthorizer
{
    StructuredQueryExecutionGrant? Authorize(CompiledReadOnlyQuery query, Guid tenantId);
}

/// <summary>Compatibility alias for older integration tests and tooling.</summary>
public interface IDevelopmentStructuredQueryAuthorizer : IStructuredQueryAuthorizer;

public interface IReadOnlyStructuredQueryExecutor
{
    Task<StructuredQueryExecutionResult> ExecuteAsync(CompiledReadOnlyQuery query,
        StructuredQueryExecutionGrant grant, CancellationToken cancellationToken);
}

public interface IStructuredQueryCompiler
{
    StructuredQueryCompilationResult Compile(StructuredQueryPlan plan, StructuredQueryCatalogSnapshot snapshot);
}

/// <summary>
/// Compiles a small, deterministic SELECT-only subset. Physical identifiers come exclusively from
/// the integrity-checked snapshot; values are always parameters. Unsupported plan shapes fail closed.
/// </summary>
public sealed class StructuredQueryCompiler : IStructuredQueryCompiler
{
    private const int MaximumRows = 100;
    private static readonly System.Text.RegularExpressions.Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    public StructuredQueryCompilationResult Compile(StructuredQueryPlan plan, StructuredQueryCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        var operationDiagnostics = Feature141OperationRegistry.ValidatePlan(plan);
        if (operationDiagnostics.Count > 0) return new(false, null, operationDiagnostics);
        try { return CompileCore(plan, snapshot); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return new(false, null, ["catalog_snapshot_mapping_invalid"]);
        }
    }

    private StructuredQueryCompilationResult CompileCore(StructuredQueryPlan plan, StructuredQueryCatalogSnapshot snapshot)
    {
        if (plan.Sources.Count == 1 && plan.Sources[0].ReferenceType == FinancialValueReferenceType.MonthlyMeasure)
            return CompileMonthly(plan, snapshot, plan.Sources[0]);

        var errors = new List<string>();
        if (!snapshot.IntegrityValidated || string.IsNullOrWhiteSpace(snapshot.CatalogHash)) errors.Add("catalog_snapshot_not_integrity_validated");
        if (!string.Equals(plan.CatalogHash, snapshot.CatalogHash, StringComparison.Ordinal)
            || !string.Equals(plan.CatalogVersion, snapshot.CatalogVersion, StringComparison.Ordinal)) errors.Add("catalog_snapshot_mismatch");
        if (plan.ContractVersion != 1) errors.Add("unsupported_plan_contract_version");
        if (!plan.RetrievalComplete) errors.Add("plan_requirements_incomplete");
        if (plan.TenantId == Guid.Empty) errors.Add("tenant_binding_missing");
        if (plan.Pagination.Offset < 0 || plan.Pagination.Limit is < 1 or > MaximumRows) errors.Add("pagination_out_of_bounds");
        if (plan.Period.UseSynchronizationTimestampForRecency) errors.Add("synchronization_timestamp_is_not_reporting_recency");
        if (!plan.Period.SelectLatestByReportingPeriod || plan.Pagination.Offset != 0)
            errors.Add("current_source_item_subset_requires_unoffset_latest_reporting_period");
        if (plan.FinancialOperation.Operation != FinancialValueOperation.DirectValueRetrieval
            || plan.FinancialOperation.Kind != PlanFinancialOperationKind.DirectValue || plan.FinancialOperation.FormulaAuthorized
            || plan.FinancialOperation.ValueDefinitionCatalogIds.Count != 0)
            errors.Add("financial_formula_not_supported");
        if (plan.FinancialOperation.Operation == FinancialValueOperation.ApprovedPeriodComparison)
            errors.Add("period_comparison_requires_approved_comparison_compiler");
        if (plan.Aggregations.Count > 0 || plan.Projection.Any(x => x.Role == PlanProjectionRole.Grouping))
            errors.Add("grouping_and_aggregation_not_supported_by_current_sql_subset");
        if (plan.Sources.Count != 1) errors.Add("exactly_one_query_source_required");
        if (plan.Sources.Any(x => x.ReferenceType != FinancialValueReferenceType.DirectSourceItem))
            errors.Add("only_direct_source_item_retrieval_is_supported_by_current_sql_subset");
        if (plan.Entities.Count != 1 || plan.Entities.Any(x => x.EntityKind != "Company"
            || !x.ResolvedEntityId.HasValue || x.ResolvedEntityId.Value == Guid.Empty))
            errors.Add("exactly_one_resolved_company_binding_required");
        if (plan.Sort.Count != 0) errors.Add("explicit_sort_is_not_supported_until_catalog_sort_fields_are_bound");
        if (plan.Filters.Count != 0) errors.Add("filters_are_not_supported_until_catalog_filter_fields_are_bound");
        if (plan.Presentation is not (PlanPresentation.Scalar or PlanPresentation.Table))
            errors.Add("result_shape_not_supported");

        if (errors.Count == 0)
        {
            var source = plan.Sources[0];
            var mapping = snapshot.DirectSources.SingleOrDefault(x => x.CatalogId == source.CatalogId);
            if (mapping is null) errors.Add("direct_source_not_mapped_in_snapshot");
            else
            {
                if (!source.SourceItemCatalogId.HasValue || source.SourceItemCatalogId != mapping.SourceItemCatalogId
                    || source.ProviderName != mapping.ProviderName || source.StatementType != mapping.StatementType
                    || source.SourceItemId != mapping.SourceItemId) errors.Add("direct_source_identity_mismatch");
                if (plan.Projection.Count != 1 || plan.Projection[0].CatalogId != source.CatalogId
                    || plan.Projection[0].Role != PlanProjectionRole.Value)
                    errors.Add("projection_does_not_match_direct_value_source");
                if (!plan.Statement.Providers.SequenceEqual([source.ProviderName], StringComparer.Ordinal)
                    || !plan.Statement.StatementTypes.SequenceEqual([source.StatementType], StringComparer.Ordinal))
                    errors.Add("statement_provider_or_type_binding_mismatch");
                if (!plan.Statement.PreservePeriodType || !plan.Statement.PreserveAuditState
                    || !plan.Statement.PreserveRestatementState || !plan.Statement.PreserveRevisionIdentity
                    || !plan.Statement.SelectionKeys.Contains(PlanStatementSelectionKey.ReportingPeriodDescending)
                    || !plan.Statement.SelectionKeys.Contains(PlanStatementSelectionKey.Availability))
                    errors.Add("statement_evidence_selection_incomplete");
                if (mapping.SourceItemCatalogId == Guid.Empty || mapping.SourceItemId <= 0) errors.Add("direct_source_identity_invalid");
                if (!mapping.IdentityVerified) errors.Add("direct_source_identity_not_verified");
                var requiredRelationships = mapping.RelationshipIds.ToHashSet(StringComparer.Ordinal);
                if (mapping.RelationshipIds.Count != 3 || !requiredRelationships.SetEquals(plan.RelationshipIds)
                    || mapping.RelationshipIds.Any(id => snapshot.Relationships.SingleOrDefault(x => x.CatalogId == id) is not
                        { Cardinality: "many-to-one" or "one-to-many", Verified: true }))
                    errors.Add("direct_source_relationship_requirements_incomplete");
                var consolidatedRequested = plan.Statement.Scopes.Contains("consolidated", StringComparer.Ordinal);
                if (!plan.Statement.RequireUnambiguousSelection || plan.Statement.PreferComposing != consolidatedRequested
                    || plan.Statement.Scopes.Contains("standalone", StringComparer.Ordinal) && consolidatedRequested)
                    errors.Add("statement_selection_policy_invalid");
                foreach (var tableId in new[] { mapping.SourceItemsTableCatalogId, mapping.LineItemsTableCatalogId, mapping.StatementsTableCatalogId, mapping.CompaniesTableCatalogId })
                    if (snapshot.Tables.All(x => x.CatalogId != tableId)) errors.Add("direct_source_table_missing_from_snapshot");
                foreach (var relationshipId in mapping.RelationshipIds)
                    if (snapshot.Relationships.All(x => x.CatalogId != relationshipId)) errors.Add("direct_source_relationship_missing_from_snapshot");
                if (errors.Count == 0) return CompileDirect(plan, snapshot, mapping);
            }
        }
        return new(false, null, errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static StructuredQueryCompilationResult CompileMonthly(StructuredQueryPlan plan,
        StructuredQueryCatalogSnapshot snapshot, PlanSourceReference source)
    {
        if (plan.CompanyDiscoveryScope is not null || plan.PopulationCoveragePolicyId is not null
            || plan.ProductConcept is not null)
        {
            if (source.CatalogId == "metric_monthly_production_quantity")
                return CompileMonthlyCompanyDiscovery(plan, snapshot, source);
            if (plan.PopulationSalesScope is null)
                return new(false, null, ["producer_sales_scope_ambiguous"]);
            return CompileMonthlyProducerSalesRanking(plan, snapshot, source);
        }

        var errors = new List<string>();
        if (!snapshot.IntegrityValidated || string.IsNullOrWhiteSpace(snapshot.CatalogHash)) errors.Add("catalog_snapshot_not_integrity_validated");
        if (plan.CatalogHash != snapshot.CatalogHash || plan.CatalogVersion != snapshot.CatalogVersion) errors.Add("catalog_snapshot_mismatch");
        if (plan.ContractVersion != 1) errors.Add("unsupported_plan_contract_version");
        if (!plan.RetrievalComplete) errors.Add("plan_requirements_incomplete");
        if (plan.TenantId == Guid.Empty) errors.Add("tenant_binding_missing");
        if (plan.Pagination.Offset != 0 || plan.Pagination.Limit is < 1 or > MaximumRows) errors.Add("pagination_out_of_bounds");
        var isComparison = plan.FinancialOperation.Operation == FinancialValueOperation.ApprovedPeriodComparison;
        var isDirect = plan.FinancialOperation.Operation == FinancialValueOperation.DirectValueRetrieval
            && (plan.FinancialOperation.Kind == PlanFinancialOperationKind.DirectValue
                || plan.FinancialOperation.Kind == PlanFinancialOperationKind.TimeSeries
                    && (plan.Presentation is PlanPresentation.TimeSeries or PlanPresentation.Chart));
        if (!isDirect && !isComparison) errors.Add("monthly_financial_operation_not_supported");
        if (plan.FinancialOperation.ValueDefinitionCatalogIds.Count != 0)
            errors.Add("monthly_formula_definition_not_supported");
        var companyEntities = plan.Entities.Where(x => x.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase)).ToArray();
        var productEntities = plan.Entities.Where(x => x.EntityKind.Equals("Product", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (plan.Sources.Count != 1 || companyEntities.Length is < 1 or > 20
            || companyEntities.Any(x => !x.ResolvedEntityId.HasValue || x.ResolvedEntityId.GetValueOrDefault() == Guid.Empty)
            || companyEntities.Select(x => x.ResolvedEntityId!.Value).Distinct().Count() != companyEntities.Length
            || plan.Entities.Count != companyEntities.Length + productEntities.Length || productEntities.Length > 1)
            errors.Add("monthly_query_requires_resolved_company_bindings");
        var companyIds = companyEntities.Where(x => x.ResolvedEntityId.HasValue).Select(x => x.ResolvedEntityId!.Value).Distinct().ToArray();
        var hasProduct = productEntities.Length == 1;
        var hasAggregation = plan.Aggregations.Count == 1;
        var rankedProducts = hasAggregation && plan.Presentation == PlanPresentation.RankedTable;
        if (hasProduct && (string.IsNullOrWhiteSpace(productEntities[0].ResolvedEntityKey)
            || productEntities[0].ResolvedEntityKey!.Length > 256 || !productEntities[0].IdentityVerified
            || string.IsNullOrWhiteSpace(productEntities[0].IdentityProvenance)))
            errors.Add("monthly_product_binding_not_verified");
        if ((!isComparison && isDirect && !hasProduct && !hasAggregation) || (isComparison && !hasProduct && !rankedProducts))
            errors.Add("monthly_direct_measure_requires_product_binding");
        if (!isDirect && !isComparison) errors.Add("monthly_operation_shape_not_supported");
        if (plan.Aggregations.Count > 1 || plan.Aggregations.Any(x => x.Operation != PlanAggregationOperation.Sum))
            errors.Add("monthly_only_catalog_governed_sum_is_supported");
        if (hasAggregation && hasProduct && companyIds.Length == 1)
            errors.Add("monthly_sum_requires_company_or_product_grouping_without_product_filter");
        if (plan.Projection.Any(x => x.Role != PlanProjectionRole.Value)
            || plan.Projection.Count != 1 || plan.Projection[0].CatalogId != source.CatalogId)
            errors.Add("monthly_projection_does_not_match_measure");
        if (plan.Presentation is not (PlanPresentation.Scalar or PlanPresentation.Table or PlanPresentation.TimeSeries
            or PlanPresentation.Chart or PlanPresentation.RankedTable))
            errors.Add("monthly_result_shape_not_supported");
        if (plan.Presentation == PlanPresentation.RankedTable && !hasAggregation)
            errors.Add("monthly_ranking_requires_approved_sum_aggregation");
        if (plan.Period.UseSynchronizationTimestampForRecency) errors.Add("synchronization_timestamp_is_not_reporting_recency");
        if (plan.Period.SelectLatestByReportingPeriod == (plan.Period.ReportingPeriodStart.HasValue || plan.Period.ReportingPeriodEnd.HasValue)
            || plan.Period.ReportingPeriodStart.HasValue != plan.Period.ReportingPeriodEnd.HasValue)
            errors.Add("monthly_period_selection_invalid");
        if (plan.Period.MaximumPeriods is < 1 or > 36) errors.Add("monthly_period_limit_out_of_bounds");
        var latestBoundedSeries = plan.Period.SelectLatestByReportingPeriod && plan.Period.MaximumPeriods is > 0;
        if ((plan.Presentation == PlanPresentation.TimeSeries
                || plan.Presentation == PlanPresentation.Chart
                    && plan.FinancialOperation.Kind == PlanFinancialOperationKind.TimeSeries)
            && !plan.Period.ReportingPeriodStart.HasValue && !latestBoundedSeries)
            errors.Add("monthly_time_series_requires_explicit_bounded_period");
        if (companyIds.Length > 1 && !plan.Period.ReportingPeriodStart.HasValue
            && !plan.Period.SelectLatestByReportingPeriod)
            errors.Add("monthly_multi_company_comparison_requires_common_bounded_period");
        var reviewedEquivalence = plan.ReviewedProductEquivalence;
        if (companyIds.Length > 1 && hasProduct
            && (reviewedEquivalence is not { Status: ProductEquivalenceResolutionStatus.Resolved }
                || string.IsNullOrWhiteSpace(reviewedEquivalence.EquivalenceGroupId)
                || string.IsNullOrWhiteSpace(reviewedEquivalence.ApprovalReference)
                || reviewedEquivalence.Members.Select(member => member.CompanyId).Distinct().Count() != companyIds.Length
                || !reviewedEquivalence.Members.Select(member => member.CompanyId).ToHashSet().SetEquals(companyIds)
                || reviewedEquivalence.Members.Any(member => !member.IdentityReviewed
                    || string.IsNullOrWhiteSpace(member.ProviderProductIdentity)
                    || string.IsNullOrWhiteSpace(member.CompanyProductKey)
                    || string.IsNullOrWhiteSpace(member.IdentityReference))
                || reviewedEquivalence.Members.Select(member => member.UnitCode).Distinct(StringComparer.Ordinal).Count() != 1
                || string.IsNullOrWhiteSpace(reviewedEquivalence.UnitCode)
                || reviewedEquivalence.Members.Any(member => member.UnitCode != reviewedEquivalence.UnitCode)
                || productEntities[0].ResolvedEntityKey != reviewedEquivalence.EquivalenceGroupId
                || !productEntities[0].IdentityVerified
                || productEntities[0].IdentityProvenance != reviewedEquivalence.ApprovalReference))
            errors.Add("monthly_cross_company_product_equivalence_requires_reviewed_mapping");
        var expectedComparisonMode = plan.FinancialOperation.ComparisonMode switch
        {
            PlanComparisonMode.MonthOverMonth => "period-over-period",
            PlanComparisonMode.YearOverYear => "year-over-year",
            _ => null
        };
        var minimumComparisonPeriods = plan.FinancialOperation.ComparisonMode switch
        {
            PlanComparisonMode.MonthOverMonth => 2,
            PlanComparisonMode.YearOverYear => 13,
            _ => int.MaxValue
        };
        var comparisonIsBounded = plan.Period.ReportingPeriodStart.HasValue
            || plan.Period.MaximumPeriods >= minimumComparisonPeriods;
        var rankedGrowthComparison = isComparison && rankedProducts
            && plan.FinancialOperation.Kind == PlanFinancialOperationKind.GrowthPercentage
            && plan.FinancialOperation.ComparisonMode == PlanComparisonMode.MonthOverMonth
            && plan.Period.SelectLatestByReportingPeriod && plan.Period.MaximumPeriods is >= 2;
        if (isComparison && (plan.FinancialOperation.Kind is not (PlanFinancialOperationKind.AbsoluteChange or PlanFinancialOperationKind.GrowthPercentage)
            || !plan.FinancialOperation.FormulaAuthorized
            || expectedComparisonMode is null
            || plan.Presentation is not (PlanPresentation.TimeSeries or PlanPresentation.Scalar) && !rankedGrowthComparison
            || !comparisonIsBounded))
            errors.Add("monthly_comparison_requires_approved_bounded_monthly_scope");
        if (isComparison && hasAggregation && !rankedGrowthComparison) errors.Add("monthly_comparison_aggregation_not_supported");
        if (isComparison && !plan.Period.ComparisonScopes.Contains(expectedComparisonMode ?? string.Empty, StringComparer.Ordinal))
            errors.Add("monthly_comparison_scope_mismatch");

        var mapping = snapshot.MonthlyMeasures.SingleOrDefault(x => x.CatalogId == source.CatalogId);
        if (mapping is null) errors.Add("monthly_measure_not_mapped_in_snapshot");
        else
        {
            if (hasAggregation && plan.Aggregations[0].CatalogId != mapping.CatalogId)
                errors.Add("monthly_sum_must_match_selected_measure");
            if ((!rankedProducts && plan.Sort.Count > 1)
                || rankedProducts && (plan.Sort.Count != 1 || plan.Sort[0].CatalogId != mapping.CatalogId
                    || plan.Sort[0].Direction != PlanSortDirection.Descending)
                || !rankedProducts && plan.Sort.Any(x => x.CatalogId != mapping.PeriodEndColumnCatalogId))
                errors.Add("monthly_sort_field_not_supported");
            if (plan.Period.MaximumPeriods.HasValue && !plan.Period.ReportingPeriodStart.HasValue && !latestBoundedSeries)
                errors.Add("monthly_period_limit_requires_bounded_range");
            if (source.MetricCode != mapping.CatalogId) errors.Add("monthly_measure_identity_mismatch");
            if (mapping.UnitKind == "provider-reported" && !mapping.UnitVerified)
                errors.Add("monthly_provider_rate_unit_unverified");
            if (mapping.RelationshipIds.Count != 1 || !mapping.RelationshipIds.SequenceEqual(plan.RelationshipIds, StringComparer.Ordinal))
                errors.Add("monthly_relationship_requirements_incomplete");
            var relationship = mapping.RelationshipIds.Count == 1
                ? snapshot.Relationships.SingleOrDefault(x => x.CatalogId == mapping.RelationshipIds[0]) : null;
            if (relationship is not { Verified: true } || !Connects(relationship, mapping.LineItemsTableCatalogId, mapping.ReportsTableCatalogId))
                errors.Add("monthly_relationship_not_verified");
            foreach (var tableId in new[] { mapping.LineItemsTableCatalogId, mapping.ReportsTableCatalogId })
                if (snapshot.Tables.All(x => x.CatalogId != tableId)) errors.Add("monthly_measure_table_missing");
            foreach (var columnId in MonthlyColumnIds(mapping))
                if (snapshot.Columns.All(x => x.CatalogId != columnId)) errors.Add("monthly_measure_column_missing");
        }

        var productCode = productEntities.Length == 1 && companyIds.Length == 1
            ? productEntities[0].ResolvedEntityKey : null;
        if (plan.Filters.Count > 0) errors.Add("monthly_filters_require_typed_resolved_dimensions");

        if (errors.Count > 0) return new(false, null, errors.Distinct(StringComparer.Ordinal).ToArray());
        var tables = snapshot.Tables.ToDictionary(x => x.CatalogId, StringComparer.Ordinal);
        var columns = snapshot.Columns.ToDictionary(x => x.CatalogId, StringComparer.Ordinal);
        var lineItems = tables[mapping!.LineItemsTableCatalogId];
        var reports = tables[mapping.ReportsTableCatalogId];
        var rel = snapshot.Relationships.Single(x => x.CatalogId == mapping.RelationshipIds[0]);
        var (lineReportKey, reportId) = ColumnPair(rel, mapping.LineItemsTableCatalogId, columns);
        if (lineReportKey.TableCatalogId != lineItems.CatalogId || reportId.TableCatalogId != reports.CatalogId)
            return new(false, null, ["monthly_relationship_column_binding_invalid"]);

        var value = columns[mapping.ValueColumnCatalogId];
        var product = columns[mapping.ProductCodeColumnCatalogId];
        var title = columns[mapping.ProductTitleColumnCatalogId];
        var unit = columns[mapping.QuantityUnitColumnCatalogId];
        var periodStart = columns[mapping.PeriodStartColumnCatalogId];
        var periodEnd = columns[mapping.PeriodEndColumnCatalogId];
        var provider = columns[mapping.ReportProviderColumnCatalogId];
        var reportType = columns[mapping.ReportTypeColumnCatalogId];
        var outputType = columns[mapping.OutputTypeColumnCatalogId];
        var companyId = columns[mapping.CompanyIdColumnCatalogId];
        var accepted = columns[mapping.AcceptedColumnCatalogId];
        var revision = columns[mapping.RevisionStatusColumnCatalogId];
        var externalReportId = columns[mapping.ExternalReportIdColumnCatalogId];
        var rowFingerprint = columns[mapping.SourceRowFingerprintColumnCatalogId];
        var multiplicity = columns[mapping.SourceMultiplicityColumnCatalogId];
        var parameters = new List<StructuredQueryParameter>
        {
            new("provider_name", mapping.ProviderName), new("report_type", mapping.ReportType),
            new("output_type", mapping.OutputType), new("is_accepted", mapping.IsAccepted),
            new("revision_status", mapping.RevisionStatus),
            new("row_limit", Math.Min(plan.Pagination.Limit, MaximumRows) + 1), new("row_offset", 0)
        };
        var useProductEquivalence = companyIds.Length > 1 && hasProduct;
        if (useProductEquivalence)
        {
            for (var index = 0; index < reviewedEquivalence!.Members.Count; index++)
            {
                var member = reviewedEquivalence.Members[index];
                parameters.Add(new($"product_company_{index}", member.CompanyId));
                parameters.Add(new($"product_code_{index}", member.CompanyProductKey));
            }
        }
        string ProductEquivalencePredicate(string reportAlias, string lineAlias) => "(" + string.Join(" OR ",
            reviewedEquivalence!.Members.Select((member, index) =>
                $"({reportAlias}.{Q(companyId.Name)} = @product_company_{index} AND EXISTS (SELECT 1 FROM {QT(lineItems)} {lineAlias} " +
                $"WHERE {lineAlias}.{Q(lineReportKey.Name)} = {reportAlias}.{Q(reportId.Name)} " +
                $"AND {lineAlias}.{Q(product.Name)} = @product_code_{index}))")) + ")";
        string ProductExistsPredicate(string reportAlias, string lineAlias) =>
            $"EXISTS (SELECT 1 FROM {QT(lineItems)} {lineAlias} WHERE {lineAlias}.{Q(lineReportKey.Name)} = {reportAlias}.{Q(reportId.Name)} " +
            $"AND {lineAlias}.{Q(product.Name)} = @product_code)";
        var companyPredicate = companyIds.Length == 1
            ? $"r.{Q(companyId.Name)} = @company_id"
            : $"r.{Q(companyId.Name)} = ANY(@company_ids)";
        if (companyIds.Length == 1) parameters.Add(new("company_id", companyIds[0]));
        else parameters.Add(new("company_ids", companyIds));
        var where = new List<string>
        {
            companyPredicate, $"r.{Q(provider.Name)} = @provider_name",
            $"r.{Q(reportType.Name)} = @report_type", $"r.{Q(outputType.Name)} = @output_type",
            $"r.{Q(accepted.Name)} = @is_accepted", $"r.{Q(revision.Name)} = @revision_status"
        };
        if (productCode is not null)
        {
            parameters.Add(new("product_code", productCode));
            where.Add($"li.{Q(product.Name)} = @product_code");
        }
        else if (useProductEquivalence)
        {
            where.Add(ProductEquivalencePredicate("r", "li_scope"));
            where.Add("(" + string.Join(" OR ", reviewedEquivalence!.Members.Select((member, index) =>
                $"(r.{Q(companyId.Name)} = @product_company_{index} AND li.{Q(product.Name)} = @product_code_{index})")) + ")");
        }
        var ctes = new List<string>();
        if (plan.Period.SelectLatestByReportingPeriod)
        {
            var latestPeriodFilter = $"r0.{Q(provider.Name)} = @provider_name AND r0.{Q(reportType.Name)} = @report_type " +
                $"AND r0.{Q(outputType.Name)} = @output_type AND r0.{Q(accepted.Name)} = @is_accepted " +
                $"AND r0.{Q(revision.Name)} = @revision_status";
            if (productCode is not null)
                latestPeriodFilter += $" AND {ProductExistsPredicate("r0", "li0")}";
            else if (useProductEquivalence)
                latestPeriodFilter += $" AND {ProductEquivalencePredicate("r0", "li0_scope")}";
            if (companyIds.Length > 1)
            {
                var eligibleCommonMonths = $"SELECT r0.{Q(periodEnd.Name)} AS {Q("PeriodEnd")} FROM {QT(reports)} r0 " +
                    $"WHERE r0.{Q(companyId.Name)} = ANY(@company_ids) AND {latestPeriodFilter} " +
                    $"GROUP BY r0.{Q(periodEnd.Name)} HAVING COUNT(DISTINCT r0.{Q(companyId.Name)}) = @company_count";
                parameters.Add(new("company_count", companyIds.Length));
                if (plan.Period.MaximumPeriods is { } commonCount)
                {
                    parameters.Add(new("maximum_periods", commonCount));
                    ctes.Add($"latest_bounded_months AS (SELECT {Q("PeriodEnd")} FROM ({eligibleCommonMonths}) common_months " +
                        $"ORDER BY {Q("PeriodEnd")} DESC LIMIT @maximum_periods)");
                    where.Add($"EXISTS (SELECT 1 FROM latest_bounded_months lbm WHERE lbm.{Q("PeriodEnd")} = r.{Q(periodEnd.Name)})");
                }
                else
                {
                    ctes.Add($"latest_month AS (SELECT {Q("PeriodEnd")} FROM ({eligibleCommonMonths}) common_months " +
                        $"ORDER BY {Q("PeriodEnd")} DESC LIMIT 1)");
                    where.Add($"EXISTS (SELECT 1 FROM latest_month lm WHERE lm.{Q("PeriodEnd")} = r.{Q(periodEnd.Name)})");
                }
            }
            else if (plan.Period.MaximumPeriods is { } latestCount)
            {
                parameters.Add(new("maximum_periods", latestCount));
                ctes.Add($"latest_bounded_months AS (SELECT {Q("CompanyId")}, {Q("PeriodEnd")} FROM (" +
                    $"SELECT r0.{Q(companyId.Name)} AS {Q("CompanyId")}, r0.{Q(periodEnd.Name)} AS {Q("PeriodEnd")}, " +
                    $"ROW_NUMBER() OVER (PARTITION BY r0.{Q(companyId.Name)} ORDER BY r0.{Q(periodEnd.Name)} DESC) AS {Q("PeriodRank")} " +
                    $"FROM {QT(reports)} r0 WHERE {(companyIds.Length == 1 ? $"r0.{Q(companyId.Name)} = @company_id" : $"r0.{Q(companyId.Name)} = ANY(@company_ids)")} " +
                    $"AND {latestPeriodFilter} GROUP BY r0.{Q(companyId.Name)}, r0.{Q(periodEnd.Name)}) ranked_latest_months " +
                    $"WHERE {Q("PeriodRank")} <= @maximum_periods)");
                where.Add($"EXISTS (SELECT 1 FROM latest_bounded_months lbm WHERE lbm.{Q("CompanyId")} = r.{Q(companyId.Name)} " +
                    $"AND lbm.{Q("PeriodEnd")} = r.{Q(periodEnd.Name)})");
            }
            else
            {
                ctes.Add($"latest_month AS (SELECT r0.{Q(companyId.Name)} AS {Q("CompanyId")}, MAX(r0.{Q(periodEnd.Name)}) AS {Q("PeriodEnd")} " +
                $"FROM {QT(reports)} r0 WHERE {(companyIds.Length == 1 ? $"r0.{Q(companyId.Name)} = @company_id" : $"r0.{Q(companyId.Name)} = ANY(@company_ids)")} " +
                $"AND {latestPeriodFilter} GROUP BY r0.{Q(companyId.Name)})");
                where.Add($"EXISTS (SELECT 1 FROM latest_month lm WHERE lm.{Q("CompanyId")} = r.{Q(companyId.Name)} " +
                    $"AND lm.{Q("PeriodEnd")} = r.{Q(periodEnd.Name)})");
            }
        }
        else
        {
            parameters.Add(new("period_start", plan.Period.ReportingPeriodStart!.Value));
            parameters.Add(new("period_end", plan.Period.ReportingPeriodEnd!.Value));
            where.Add($"r.{Q(periodStart.Name)} >= @period_start AND r.{Q(periodEnd.Name)} <= @period_end");
            if (companyIds.Length > 1)
            {
                parameters.Add(new("company_count", companyIds.Length));
                var commonPeriods = $"SELECT r0.{Q(periodEnd.Name)} AS {Q("PeriodEnd")} FROM {QT(reports)} r0 " +
                    $"WHERE r0.{Q(companyId.Name)} = ANY(@company_ids) AND r0.{Q(provider.Name)} = @provider_name " +
                    $"AND r0.{Q(reportType.Name)} = @report_type AND r0.{Q(outputType.Name)} = @output_type " +
                    $"AND r0.{Q(accepted.Name)} = @is_accepted AND r0.{Q(revision.Name)} = @revision_status " +
                    $"AND r0.{Q(periodStart.Name)} >= @period_start AND r0.{Q(periodEnd.Name)} <= @period_end " +
                    (productCode is not null ? $"AND {ProductExistsPredicate("r0", "li0")}" : useProductEquivalence
                        ? $"AND {ProductEquivalencePredicate("r0", "li0_scope")}" : string.Empty) + " " +
                    $"GROUP BY r0.{Q(periodEnd.Name)} HAVING COUNT(DISTINCT r0.{Q(companyId.Name)}) = @company_count";
                var limitCommonPeriods = plan.Period.MaximumPeriods.HasValue
                    ? " ORDER BY \"PeriodEnd\" DESC LIMIT @maximum_periods" : string.Empty;
                if (plan.Period.MaximumPeriods.HasValue)
                    parameters.Add(new("maximum_periods", plan.Period.MaximumPeriods.Value));
                ctes.Add($"bounded_months AS (SELECT {Q("PeriodEnd")} FROM ({commonPeriods}) common_periods{limitCommonPeriods})");
                where.Add($"EXISTS (SELECT 1 FROM bounded_months bm WHERE bm.{Q("PeriodEnd")} = r.{Q(periodEnd.Name)})");
            }
            else if (plan.Period.MaximumPeriods.HasValue)
            {
                parameters.Add(new("maximum_periods", plan.Period.MaximumPeriods.Value));
                var eligiblePeriods = $"SELECT r0.{Q(companyId.Name)} AS {Q("CompanyId")}, r0.{Q(periodEnd.Name)} AS {Q("PeriodEnd")} " +
                    $"FROM {QT(reports)} r0 WHERE r0.{Q(companyId.Name)} = @company_id " +
                    $"AND r0.{Q(provider.Name)} = @provider_name AND r0.{Q(reportType.Name)} = @report_type " +
                    $"AND r0.{Q(outputType.Name)} = @output_type AND r0.{Q(accepted.Name)} = @is_accepted " +
                    $"AND r0.{Q(revision.Name)} = @revision_status " +
                    $"AND r0.{Q(periodStart.Name)} >= @period_start AND r0.{Q(periodEnd.Name)} <= @period_end " +
                    (productCode is not null ? $"AND {ProductExistsPredicate("r0", "li0")}" : string.Empty) + " " +
                    $"GROUP BY r0.{Q(companyId.Name)}, r0.{Q(periodEnd.Name)}";
                ctes.Add($"bounded_months AS (SELECT {Q("CompanyId")}, {Q("PeriodEnd")} FROM (SELECT monthly_periods.*, " +
                    $"ROW_NUMBER() OVER (PARTITION BY {Q("CompanyId")} ORDER BY {Q("PeriodEnd")} DESC) AS {Q("PeriodRank")} " +
                    $"FROM ({eligiblePeriods}) monthly_periods) ranked_months WHERE {Q("PeriodRank")} <= @maximum_periods)");
                where.Add($"EXISTS (SELECT 1 FROM bounded_months bm WHERE bm.{Q("CompanyId")} = r.{Q(companyId.Name)} " +
                    $"AND bm.{Q("PeriodEnd")} = r.{Q(periodEnd.Name)})");
            }
        }

        var monthlyRowSql = $"eligible_monthly_rows AS (SELECT r.{Q(companyId.Name)} AS {Q("CompanyId")}, " +
            $"r.{Q(periodStart.Name)} AS {Q("PeriodStart")}, r.{Q(periodEnd.Name)} AS {Q("PeriodEnd")}, " +
            $"li.{Q(product.Name)} AS {Q("ProductCode")}, li.{Q(title.Name)} AS {Q("ProductTitle")}, " +
            $"li.{Q(value.Name)} AS {Q("Value")}, li.{Q(unit.Name)} AS {Q("SourceUnit")}, " +
            $"COUNT(*) OVER (PARTITION BY r.{Q(companyId.Name)}, r.{Q(periodEnd.Name)}, li.{Q(product.Name)}) AS {Q("SamePeriodCandidateCount")}, " +
            $"r.{Q(provider.Name)} AS {Q("ProviderName")}, r.{Q(reportType.Name)} AS {Q("ReportType")}, " +
            $"r.{Q(outputType.Name)} AS {Q("OutputType")}, r.{Q(revision.Name)} AS {Q("RevisionStatus")}, " +
            $"r.{Q(reportId.Name)} AS {Q("ReportIdentity")}, r.{Q(externalReportId.Name)} AS {Q("ExternalReportId")}, " +
            $"li.{Q(rowFingerprint.Name)} AS {Q("SourceRowFingerprint")}, li.{Q(multiplicity.Name)} AS {Q("SourceMultiplicity")} " +
            $"FROM {QT(lineItems)} li JOIN {QT(reports)} r ON li.{Q(lineReportKey.Name)} = r.{Q(reportId.Name)} " +
            $"WHERE {string.Join(" AND ", where)})";
        ctes.Add(monthlyRowSql);

        var valueCteName = "eligible_monthly_rows";
        if (hasAggregation)
        {
            // A multi-company ranked query without a product scope ranks company totals. A
            // single-company ranked query retains the established product-ranking behavior.
            var groupProduct = rankedProducts && !(companyIds.Length > 1 && productCode is null);
            var groupBy = $"{Q("CompanyId")}, {Q("PeriodEnd")}" + (groupProduct ? $", {Q("ProductCode")}" : string.Empty);
            var productColumns = groupProduct
                ? $"{Q("ProductCode")}, MIN({Q("ProductTitle")}) AS {Q("ProductTitle")}"
                : $"NULL::text AS {Q("ProductCode")}, NULL::text AS {Q("ProductTitle")}";
            var unitCount = mapping!.UnitKind is "quantity" or "source-quantity"
                ? $"COUNT(DISTINCT {Q("SourceUnit")})" : "1";
            ctes.Add($"aggregated_monthly_rows AS (SELECT {Q("CompanyId")}, MIN({Q("PeriodStart")}) AS {Q("PeriodStart")}, " +
                $"{Q("PeriodEnd")}, {productColumns}, " +
                $"SUM({Q("Value")}) AS {Q("Value")}, MIN({Q("SourceUnit")}) AS {Q("SourceUnit")}, " +
                $"MAX({Q("SamePeriodCandidateCount")}) AS {Q("SamePeriodCandidateCount")}, " +
                $"COUNT(DISTINCT {Q("ReportIdentity")}) AS {Q("EligibleReportCount")}, " +
                $"GREATEST(COUNT(*) - COUNT(DISTINCT {Q("ProductCode")}), 0) AS {Q("DuplicateProductCount")}, " +
                $"{unitCount} AS {Q("UnitCompatibilityCount")}, " +
                $"COUNT(*) FILTER (WHERE {Q("SourceMultiplicity")} <> 1) AS {Q("InvalidMultiplicityCount")}, " +
                $"MAX({Q("SourceMultiplicity")}) AS {Q("SourceMultiplicity")}, " +
                $"MIN({Q("ProviderName")}) AS {Q("ProviderName")}, " +
                $"ARRAY_AGG(DISTINCT {Q("ReportIdentity")}) AS {Q("ReportIdentity")}, " +
                $"ARRAY_AGG(DISTINCT {Q("ExternalReportId")}) AS {Q("ExternalReportId")}, " +
                $"ARRAY_AGG(DISTINCT {Q("SourceRowFingerprint")}) AS {Q("SourceRowFingerprint")} " +
                $"FROM eligible_monthly_rows GROUP BY {groupBy})");
            valueCteName = "aggregated_monthly_rows";
        }

        if (isComparison)
        {
            var months = plan.FinancialOperation.ComparisonMode == PlanComparisonMode.YearOverYear ? 12 : 1;
            parameters.Add(new("comparison_month_offset", months));
            var periodWindowMismatch = plan.FinancialOperation.ComparisonMode == PlanComparisonMode.YearOverYear
                ? $"WHEN {Q("PreviousPeriodStart")} IS NOT NULL AND (" +
                  $"{Q("PeriodStart")} <> ({Q("PreviousPeriodStart")} + INTERVAL '1 year')::date OR " +
                  $"{Q("PeriodEnd")} <> ({Q("PreviousPeriodEnd")} + INTERVAL '1 year')::date OR " +
                  $"({Q("PeriodEnd")} - {Q("PeriodStart")}) <> ({Q("PreviousPeriodEnd")} - {Q("PreviousPeriodStart")}) OR " +
                  $"EXTRACT(DAY FROM {Q("PeriodStart")}) <> EXTRACT(DAY FROM {Q("PreviousPeriodStart")}) OR " +
                  $"EXTRACT(DAY FROM {Q("PeriodEnd")}) <> EXTRACT(DAY FROM {Q("PreviousPeriodEnd")})" +
                  ") THEN 'period-window-mismatch' "
                : string.Empty;
            var missingFirstComparisonBaseline = plan.Period.ReportingPeriodStart.HasValue
                ? $"CASE WHEN date_trunc('month', {Q("PeriodEnd")}) > date_trunc('month', @period_start) + " +
                  "(@comparison_month_offset * INTERVAL '1 month') THEN 'missing-comparison-period' ELSE 'baseline-row' END"
                : "'baseline-row'";
            var scalarLatestComparison = plan.Presentation == PlanPresentation.Scalar
                && plan.Period.SelectLatestByReportingPeriod && plan.Period.MaximumPeriods >= 2;
            var status = plan.FinancialOperation.Kind == PlanFinancialOperationKind.GrowthPercentage
                ? $"CASE WHEN {Q("PreviousPeriodEnd")} IS NULL THEN {missingFirstComparisonBaseline} " +
                  $"WHEN date_trunc('month', {Q("PreviousPeriodEnd")}) <> date_trunc('month', {Q("PeriodEnd")}) - (@comparison_month_offset * INTERVAL '1 month') THEN 'missing-comparison-period' " +
                  periodWindowMismatch +
                  $"WHEN {Q("PreviousValue")} = 0 THEN 'zero-denominator' WHEN {Q("PreviousValue")} < 0 THEN 'negative-baseline-unsupported' ELSE 'comparable' END"
                : $"CASE WHEN {Q("PreviousPeriodEnd")} IS NULL THEN {missingFirstComparisonBaseline} " +
                  $"WHEN date_trunc('month', {Q("PreviousPeriodEnd")}) <> date_trunc('month', {Q("PeriodEnd")}) - (@comparison_month_offset * INTERVAL '1 month') THEN 'missing-comparison-period' " +
                  periodWindowMismatch + "ELSE 'comparable' END";
            ctes.Add($"compared_monthly_rows AS (SELECT *, " +
                $"LAG({Q("PeriodStart")}, @comparison_month_offset) OVER (PARTITION BY {Q("CompanyId")}, {Q("ProductCode")} ORDER BY {Q("PeriodEnd")}) AS {Q("PreviousPeriodStart")}, " +
                $"LAG({Q("PeriodEnd")}, @comparison_month_offset) OVER (PARTITION BY {Q("CompanyId")}, {Q("ProductCode")} ORDER BY {Q("PeriodEnd")}) AS {Q("PreviousPeriodEnd")}, " +
                $"LAG({Q("Value")}, @comparison_month_offset) OVER (PARTITION BY {Q("CompanyId")}, {Q("ProductCode")} ORDER BY {Q("PeriodEnd")}) AS {Q("PreviousValue")}, " +
                $"LAG({Q("SourceUnit")}, @comparison_month_offset) OVER (PARTITION BY {Q("CompanyId")}, {Q("ProductCode")} ORDER BY {Q("PeriodEnd")}) AS {Q("PreviousSourceUnit")}, " +
                $"ROW_NUMBER() OVER (PARTITION BY {Q("CompanyId")}, {Q("ProductCode")} ORDER BY {Q("PeriodEnd")} DESC) AS {Q("LatestProductPeriodRank")} " +
                $"FROM {valueCteName})");
            var computedValue = plan.FinancialOperation.Kind == PlanFinancialOperationKind.AbsoluteChange
                ? $"({Q("Value")} - {Q("PreviousValue")})"
                : $"CASE WHEN {Q("PreviousValue")} > 0 THEN (({Q("Value")} - {Q("PreviousValue")}) / {Q("PreviousValue")}) * 100 ELSE NULL END";
            var comparisonColumn = plan.FinancialOperation.Kind == PlanFinancialOperationKind.AbsoluteChange ? "ComparisonValue" : "GrowthPercentage";
            var comparisonFilter = rankedGrowthComparison || scalarLatestComparison
                ? $" WHERE {Q("LatestProductPeriodRank")} = 1 AND {Q("PreviousPeriodEnd")} IS NOT NULL " +
                  $"AND {Q("PreviousValue")} > 0 AND date_trunc('month', {Q("PreviousPeriodEnd")}) = date_trunc('month', {Q("PeriodEnd")}) - (@comparison_month_offset * INTERVAL '1 month') "
                : scalarLatestComparison
                ? $" WHERE {Q("PreviousPeriodEnd")} IS NOT NULL AND {Q("PreviousValue")} > 0 " +
                  $"AND date_trunc('month', {Q("PreviousPeriodEnd")}) = date_trunc('month', {Q("PeriodEnd")}) - (@comparison_month_offset * INTERVAL '1 month') "
                : string.Empty;
            var sqlComparisonOrder = rankedGrowthComparison
                ? $"{Q("GrowthPercentage")} DESC, {Q("ProductCode")} ASC"
                : plan.Sort.Count == 1 && plan.Sort[0].Direction == PlanSortDirection.Descending
                ? $"{Q("PeriodEnd")} DESC" : $"{Q("PeriodEnd")} ASC";
            var sql = "WITH " + string.Join(",\n", ctes) +
                $"\nSELECT {Q("Value")}, {Q("PeriodStart")}, {Q("PeriodEnd")}, {Q("ProductCode")}, " +
                $"{Q("SamePeriodCandidateCount")}, {Q("PreviousPeriodStart")}, {Q("PreviousPeriodEnd")}, {Q("PreviousValue")}, {computedValue} AS {Q(comparisonColumn)}, " +
                $"{status} AS {Q("ComparisonStatus")}, {Q("ProductTitle")}, {Q("SourceUnit")}, {Q("PreviousSourceUnit")}, {Q("CompanyId")}, {Q("ProviderName")}, " +
                $"{Q("ReportIdentity")} AS {Q("ReportId")}, {Q("ExternalReportId")}, {Q("SourceRowFingerprint")}, {Q("SourceMultiplicity")} " +
                $"FROM compared_monthly_rows{comparisonFilter} ORDER BY {Q("CompanyId")}, {sqlComparisonOrder} LIMIT @row_limit OFFSET @row_offset";
            return BuildCompiledMonthly(sql, plan, snapshot, mapping, parameters, companyIds);
        }

        string finalSql;
        if (hasAggregation)
        {
            var companyRanking = rankedProducts && companyIds.Length > 1 && productCode is null;
            var order = rankedProducts
                ? companyRanking ? $"{Q("Value")} DESC, {Q("CompanyId")} ASC"
                    : $"{Q("Value")} DESC, {Q("ProductCode")} ASC"
                : $"{Q("CompanyId")} ASC, {Q("PeriodEnd")} ASC";
            finalSql = "WITH " + string.Join(",\n", ctes) +
                $"\nSELECT * FROM aggregated_monthly_rows ORDER BY {order} LIMIT @row_limit OFFSET @row_offset";
        }
        else
        {
            var order = plan.Sort.Count == 1 && plan.Sort[0].Direction == PlanSortDirection.Descending
                ? $"{Q("PeriodEnd")} DESC" : $"{Q("PeriodEnd")} ASC";
            ctes.Add("projected_monthly_rows AS (SELECT * FROM eligible_monthly_rows)");
            finalSql = "WITH " + string.Join(",\n", ctes) +
                $"\nSELECT *, {Q("ReportIdentity")} AS {Q("ReportId")} FROM projected_monthly_rows " +
                $"ORDER BY {Q("CompanyId")}, {order}, {Q("ProductCode")}, {Q("SourceMultiplicity")} LIMIT @row_limit OFFSET @row_offset";
        }
        return BuildCompiledMonthly(finalSql, plan, snapshot, mapping, parameters, companyIds);
    }

    private static StructuredQueryCompilationResult CompileMonthlyCompanyDiscovery(StructuredQueryPlan plan,
        StructuredQueryCatalogSnapshot snapshot, PlanSourceReference source)
    {
        var errors = new List<string>();
        var product = plan.Entities.Where(entity => entity.EntityKind.Equals("Product", StringComparison.OrdinalIgnoreCase)).ToArray();
        var companies = plan.Entities.Where(entity => entity.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase)).ToArray();
        var concept = plan.ProductConcept;
        var scope = plan.CompanyDiscoveryScope;
        if (scope is null) return new(false, null, ["company_discovery_scope_not_authorized"]);
        var mapping = snapshot.MonthlyMeasures.SingleOrDefault(measure => measure.CatalogId == source.CatalogId);
        if (!snapshot.IntegrityValidated || string.IsNullOrWhiteSpace(snapshot.CatalogHash)) errors.Add("catalog_snapshot_not_integrity_validated");
        if (plan.CatalogHash != snapshot.CatalogHash || plan.CatalogVersion != snapshot.CatalogVersion) errors.Add("catalog_snapshot_mismatch");
        if (!plan.RetrievalComplete) errors.Add("plan_requirements_incomplete");
        if (plan.TenantId == Guid.Empty || scope.TenantId != plan.TenantId) errors.Add("tenant_binding_missing_or_mismatched");
        if (string.IsNullOrWhiteSpace(scope.ScopeId) || string.IsNullOrWhiteSpace(scope.ApprovalReference))
            errors.Add("company_discovery_scope_not_authorized");
        if (companies.Length != 0 || product.Length != 1 || plan.Entities.Count != 1)
            errors.Add("population_discovery_requires_unbound_company_and_one_product_concept");
        if (concept is not { Status: ProductConceptResolutionStatus.Resolved }
            || string.IsNullOrWhiteSpace(concept.CatalogId) || string.IsNullOrWhiteSpace(concept.CatalogVersion)
            || string.IsNullOrWhiteSpace(concept.ApprovalReference) || concept.ApprovedAliases.Count == 0
            || product.Length == 1 && (product[0].ResolvedEntityKey != concept.CatalogId || !product[0].IdentityVerified))
            errors.Add("population_discovery_product_mapping_not_approved");
        if (mapping is null) errors.Add("monthly_measure_not_mapped_in_snapshot");
        else if (mapping.CatalogId != "metric_monthly_production_quantity" || mapping.MetricCode != "MONTHLY_PRODUCTION_QUANTITY")
            errors.Add("population_discovery_requires_monthly_production_measure");
        if (plan.FinancialOperation.Operation != FinancialValueOperation.DirectValueRetrieval
            || plan.FinancialOperation.Kind != PlanFinancialOperationKind.DirectValue
            || plan.FinancialOperation.FormulaAuthorized || plan.FinancialOperation.ValueDefinitionCatalogIds.Count != 0)
            errors.Add("population_discovery_financial_operation_unsupported");
        if (plan.Presentation is not (PlanPresentation.Table or PlanPresentation.RankedTable)
            || plan.Pagination.Offset != 0 || plan.Pagination.Limit is < 1 or > MaximumRows)
            errors.Add("population_discovery_result_bound_invalid");
        if (plan.Projection.Count != 1 || plan.Projection[0].CatalogId != source.CatalogId
            || plan.Projection[0].Role != PlanProjectionRole.Value || plan.Sources.Count != 1
            || plan.Sources[0].ReferenceType != FinancialValueReferenceType.MonthlyMeasure)
            errors.Add("population_discovery_measure_projection_invalid");
        if (plan.Period.ReportingPeriodStart is null || plan.Period.ReportingPeriodEnd is null
            || plan.Period.ReportingPeriodStart > plan.Period.ReportingPeriodEnd
            || plan.Period.SelectLatestByReportingPeriod || plan.Period.UseSynchronizationTimestampForRecency)
            errors.Add("population_discovery_requires_explicit_approved_observation_window");
        if (plan.PopulationCoveragePolicyId is not ("explicit-reporting-period-v1" or "observed-production-trailing-12-months-v1"))
            errors.Add("population_discovery_period_policy_unapproved");
        if (plan.Filters.Count > 0 || plan.Aggregations.Count > 0 || plan.Sort.Count > 0)
            errors.Add("population_discovery_unsupported_unbound_operator");
        if (mapping is not null)
        {
            if (!mapping.Queryable || !mapping.SemanticApproved || !mapping.UnitVerified
                || !string.Equals(mapping.UnitKind, "quantity", StringComparison.Ordinal))
                errors.Add("population_discovery_measure_not_approved_for_query");
            if (mapping.RelationshipIds.Count != 1 || !mapping.RelationshipIds.SequenceEqual(plan.RelationshipIds, StringComparer.Ordinal))
                errors.Add("monthly_relationship_requirements_incomplete");
        }
        if (errors.Count > 0) return new(false, null, errors.Distinct(StringComparer.Ordinal).ToArray());

        var tables = snapshot.Tables.ToDictionary(table => table.CatalogId, StringComparer.Ordinal);
        var columns = snapshot.Columns.ToDictionary(column => column.CatalogId, StringComparer.Ordinal);
        var lineItems = tables[mapping!.LineItemsTableCatalogId];
        var reports = tables[mapping.ReportsTableCatalogId];
        var lineReportRelationship = snapshot.Relationships.SingleOrDefault(relationship =>
            relationship.CatalogId == mapping.RelationshipIds[0]
            && relationship.Verified && relationship.Queryable && relationship.SemanticApproved
            && relationship.QueryAuthorized && Connects(relationship, lineItems.CatalogId, reports.CatalogId));
        var companyRelationship = snapshot.Relationships.SingleOrDefault(relationship =>
            relationship.Verified && relationship.Queryable && relationship.SemanticApproved
            && relationship.QueryAuthorized && Connects(relationship, reports.CatalogId,
                tables.Values.SingleOrDefault(table => table.Name == "Companies")?.CatalogId ?? string.Empty));
        var companyTable = tables.Values.SingleOrDefault(table => table.Name == "Companies");
        if (companyTable is null || !companyTable.Queryable || !companyTable.SemanticApproved
            || lineReportRelationship is null || companyRelationship is null)
            return new(false, null, ["population_discovery_company_identity_relationship_unapproved"]);
        var (lineReportKey, reportId) = ColumnPair(lineReportRelationship, lineItems.CatalogId, columns);
        var (reportCompanyFk, companyId) = ColumnPair(companyRelationship, reports.CatalogId, columns);
        var value = columns[mapping.ValueColumnCatalogId];
        var productCode = columns[mapping.ProductCodeColumnCatalogId];
        var productTitle = columns[mapping.ProductTitleColumnCatalogId];
        var unit = columns[mapping.QuantityUnitColumnCatalogId];
        var periodStart = columns[mapping.PeriodStartColumnCatalogId];
        var periodEnd = columns[mapping.PeriodEndColumnCatalogId];
        var reportProvider = columns[mapping.ReportProviderColumnCatalogId];
        var reportType = columns[mapping.ReportTypeColumnCatalogId];
        var outputType = columns[mapping.OutputTypeColumnCatalogId];
        var accepted = columns[mapping.AcceptedColumnCatalogId];
        var revision = columns[mapping.RevisionStatusColumnCatalogId];
        var externalReportId = columns[mapping.ExternalReportIdColumnCatalogId];
        var fingerprintColumn = columns[mapping.SourceRowFingerprintColumnCatalogId];
        var multiplicity = columns[mapping.SourceMultiplicityColumnCatalogId];
        var companyName = columns.Values.SingleOrDefault(column => column.TableCatalogId == companyTable.CatalogId
            && column.Name == "Name" && column.Queryable);
        var companyTicker = columns.Values.SingleOrDefault(column => column.TableCatalogId == companyTable.CatalogId
            && column.Name == "Ticker" && column.Queryable)
            ?? columns.Values.SingleOrDefault(column => column.TableCatalogId == companyTable.CatalogId
                && column.Name == "CompanySymbol" && column.Queryable);
        var companyKey = columns[companyId.CatalogId];
        var requiredColumns = new[] { lineReportKey, reportId, reportCompanyFk, companyId, value, productCode,
            productTitle, unit, periodStart, periodEnd, reportProvider, reportType, outputType, accepted,
            revision, externalReportId, fingerprintColumn, multiplicity, companyName, companyKey };
        if (requiredColumns.Any(column => column is null || !column.Queryable)
            || companyName is null || companyId.TableCatalogId != companyTable.CatalogId
            || reportCompanyFk.TableCatalogId != reports.CatalogId)
            return new(false, null, ["population_discovery_required_catalog_columns_unapproved"]);

        var q = Q;
        var qt = QT;
        var bounds = plan.PopulationCoveragePolicyId == "explicit-reporting-period-v1"
            ? $"r.{q(periodStart.Name)} = @period_start AND r.{q(periodEnd.Name)} = @period_end"
            : $"r.{q(periodEnd.Name)} >= @period_start AND r.{q(periodEnd.Name)} <= @period_end";
        var productCoverage = string.Equals(concept!.MappingCoverageStatus, "complete", StringComparison.OrdinalIgnoreCase)
            ? "complete" : "partial";
        var companySymbolExpression = companyTicker is null ? "NULL::text" : $"c.{q(companyTicker.Name)}";
        var sql = "WITH eligible_observations AS (" +
            $"SELECT c.{q(companyKey.Name)} AS {q("CompanyId")}, c.{q(companyName.Name)} AS {q("CompanyName")}, " +
            $"{companySymbolExpression} AS {q("CompanySymbol")}, li.{q(productTitle.Name)} AS {q("ProductTitle")}, " +
            $"li.{q(productCode.Name)} AS {q("ProductCode")}, li.{q(value.Name)} AS {q("Value")}, " +
            $"li.{q(unit.Name)} AS {q("SourceUnit")}, r.{q(periodStart.Name)} AS {q("PeriodStart")}, " +
            $"r.{q(periodEnd.Name)} AS {q("PeriodEnd")}, r.{q(reportProvider.Name)} AS {q("ProviderName")}, " +
            $"r.{q(reportId.Name)} AS {q("ReportId")}, r.{q(externalReportId.Name)} AS {q("ExternalReportId")}, " +
            $"li.{q(fingerprintColumn.Name)} AS {q("SourceRowFingerprint")}, li.{q(multiplicity.Name)} AS {q("SourceMultiplicity")}, " +
            $"COUNT(*) OVER (PARTITION BY c.{q(companyKey.Name)}, r.{q(periodEnd.Name)}) AS {q("SamePeriodCandidateCount")}, " +
            $"ROW_NUMBER() OVER (PARTITION BY c.{q(companyKey.Name)} ORDER BY r.{q(periodEnd.Name)} DESC, " +
            $"r.{q(periodStart.Name)} DESC, r.{q(reportId.Name)} ASC, li.{q(productCode.Name)} ASC) AS {q("DiscoveryRank")} " +
            $"FROM {qt(lineItems)} li JOIN {qt(reports)} r ON li.{q(lineReportKey.Name)} = r.{q(reportId.Name)} " +
            $"JOIN {qt(companyTable)} c ON r.{q(reportCompanyFk.Name)} = c.{q(companyId.Name)} " +
            $"WHERE c.{q(companyKey.Name)} = ANY(@company_ids) AND r.{q(reportProvider.Name)} = @provider_name " +
            $"AND r.{q(reportType.Name)} = @report_type AND r.{q(outputType.Name)} = @output_type " +
            $"AND r.{q(accepted.Name)} = @is_accepted AND r.{q(revision.Name)} = @revision_status " +
                     $"AND {bounds} AND li.{q(value.Name)} > 0 AND BTRIM(li.{q(productTitle.Name)}) = ANY(@product_aliases)) " +
            $"SELECT *, '{productCoverage}' AS {q("ProductMappingCoverageStatus")}, " +
            $"@company_coverage_complete AS {q("CompanyCoverageComplete")} FROM eligible_observations " +
            $"WHERE {q("DiscoveryRank")} = 1 ORDER BY {q("CompanyName")} ASC, {q("CompanyId")} ASC LIMIT @row_limit";
        var parameters = new List<StructuredQueryParameter>
        {
            new("provider_name", mapping.ProviderName), new("report_type", mapping.ReportType),
            new("output_type", mapping.OutputType), new("is_accepted", mapping.IsAccepted),
            new("revision_status", mapping.RevisionStatus), new("period_start", plan.Period.ReportingPeriodStart!.Value),
            new("period_end", plan.Period.ReportingPeriodEnd!.Value),
            new("product_aliases", concept.ApprovedAliases.ToArray()),
            new("company_coverage_complete", scope.CompanyCoverageComplete),
            new("row_limit", Math.Min(plan.Pagination.Limit, MaximumRows) + 1)
        };
        foreach (var table in new[] { lineItems, reports, companyTable })
            if (!Identifier.IsMatch(table.Name) || !Identifier.IsMatch(table.Schema)) return new(false, null, ["catalog_identifier_invalid"]);
        var relationIds = new[] { lineReportRelationship.CatalogId, companyRelationship.CatalogId };
        var physicalIds = requiredColumns.Where(column => column is not null).Select(column => column!.CatalogId)
            .Concat([lineItems.CatalogId, reports.CatalogId, companyTable.CatalogId])
            .Concat([lineReportRelationship.FromColumnCatalogId, lineReportRelationship.ToColumnCatalogId,
                companyRelationship.FromColumnCatalogId, companyRelationship.ToColumnCatalogId])
            .Distinct(StringComparer.Ordinal).ToArray();
        var reviewApproved = plan.SemanticReviewApproved && mapping.SemanticApproved
            && lineItems.SemanticApproved && reports.SemanticApproved && companyTable.SemanticApproved
            && lineReportRelationship.SemanticApproved && companyRelationship.SemanticApproved;
        var authorized = plan.QueryAuthorized && scope.TenantId == plan.TenantId
            && mapping.Queryable && lineItems.Queryable && reports.Queryable && companyTable.Queryable
            && lineReportRelationship.Queryable && lineReportRelationship.QueryAuthorized
            && companyRelationship.Queryable && companyRelationship.QueryAuthorized
            && physicalIds.Where(id => columns.ContainsKey(id)).All(id => columns[id].Queryable);
        if (!authorized) return new(false, null, ["population_discovery_scope_or_catalog_query_authorization_missing"]);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
        return new(true, new CompiledReadOnlyQuery(sql, parameters, plan.TenantId, snapshot.CatalogVersion,
            snapshot.CatalogHash, fingerprint, Math.Min(plan.Pagination.Limit, MaximumRows), plan.Presentation,
            [mapping.CatalogId], relationIds, physicalIds, Guid.Empty, plan.FinancialOperation.Operation,
            reviewApproved, authorized, plan.RetrievalComplete, "row-source-unit", "row-scoped-source-unit",
            "approved monthly production source unit", isMonthlyValueQuery: true, companyIds: [],
            companyDiscoveryScopeId: scope.ScopeId, companyDiscoveryApprovalReference: scope.ApprovalReference,
            companyDiscoveryCoverageComplete: scope.CompanyCoverageComplete,
            productMappingCoverageStatus: concept.MappingCoverageStatus), []);
    }

    private static StructuredQueryCompilationResult CompileMonthlyProducerSalesRanking(StructuredQueryPlan plan,
        StructuredQueryCatalogSnapshot snapshot, PlanSourceReference source)
    {
        var scope = plan.CompanyDiscoveryScope;
        var concept = plan.ProductConcept;
        var products = plan.Entities.Where(entity => entity.EntityKind.Equals("Product", StringComparison.OrdinalIgnoreCase)).ToArray();
        var companies = plan.Entities.Where(entity => entity.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase)).ToArray();
        var sales = snapshot.MonthlyMeasures.SingleOrDefault(measure => measure.CatalogId == source.CatalogId);
        var production = snapshot.MonthlyMeasures.SingleOrDefault(measure => measure.CatalogId == "metric_monthly_production_quantity");
        var errors = new List<string>();
        if (!snapshot.IntegrityValidated || string.IsNullOrWhiteSpace(snapshot.CatalogHash)) errors.Add("catalog_snapshot_not_integrity_validated");
        if (plan.CatalogHash != snapshot.CatalogHash || plan.CatalogVersion != snapshot.CatalogVersion) errors.Add("catalog_snapshot_mismatch");
        if (!plan.RetrievalComplete) errors.Add("plan_requirements_incomplete");
        if (scope is null || scope.TenantId != plan.TenantId || string.IsNullOrWhiteSpace(scope.ScopeId)
            || string.IsNullOrWhiteSpace(scope.ApprovalReference)) errors.Add("company_discovery_scope_not_authorized");
        if (plan.TenantId == Guid.Empty || companies.Length != 0 || products.Length != 1 || plan.Entities.Count != 1)
            errors.Add("producer_sales_ranking_requires_unbound_product_population");
        if (concept is not { Status: ProductConceptResolutionStatus.Resolved }
            || products.Length != 1 || products[0].ResolvedEntityKey != concept.CatalogId
            || !products[0].IdentityVerified || concept.ApprovedAliases.Count == 0)
            errors.Add("population_discovery_product_mapping_not_approved");
        if (plan.PopulationCoveragePolicyId is not ("explicit-reporting-period-v1" or "observed-production-trailing-12-months-v1")
            || plan.Period.ReportingPeriodStart is null || plan.Period.ReportingPeriodEnd is null
            || plan.Period.ReportingPeriodStart > plan.Period.ReportingPeriodEnd)
            errors.Add("population_discovery_period_policy_unapproved");
        if (plan.PopulationSalesScope is null) errors.Add("producer_sales_scope_ambiguous");
        if (plan.FinancialOperation.Operation != FinancialValueOperation.DirectValueRetrieval
            || plan.FinancialOperation.Kind != PlanFinancialOperationKind.DirectValue
            || plan.Presentation != PlanPresentation.RankedTable || plan.Pagination.Offset != 0
            || plan.Pagination.Limit is < 1 or > MaximumRows || plan.Aggregations.Count != 1
            || plan.Aggregations[0].Operation != PlanAggregationOperation.Sum || plan.Sort.Count != 1
            || plan.Sort[0].Direction != PlanSortDirection.Descending)
            errors.Add("producer_sales_ranking_shape_not_approved");
        if (plan.PopulationSalesScope == PopulationSalesScope.TotalCompanySales && source.CatalogId != "metric_monthly_sales")
            errors.Add("producer_total_sales_requires_monthly_sales_amount");
        if (plan.PopulationSalesScope == PopulationSalesScope.RequestedProductSales
            && source.CatalogId is not ("metric_monthly_sales" or "metric_monthly_sales_quantity"))
            errors.Add("producer_product_sales_measure_not_supported");
        if (sales is null || production is null || !sales.Queryable || !sales.SemanticApproved || !sales.UnitVerified
            || sales.UnitKind is not ("monetary" or "quantity") || !production.Queryable
            || !production.SemanticApproved || !production.UnitVerified || production.UnitKind != "quantity")
            errors.Add("producer_sales_measure_mapping_not_approved");
        if (plan.Projection.Count != 1 || plan.Projection[0].CatalogId != source.CatalogId
            || plan.Projection[0].Role != PlanProjectionRole.Value || plan.Sources.Count != 1
            || plan.Sources[0].ReferenceType != FinancialValueReferenceType.MonthlyMeasure)
            errors.Add("producer_sales_projection_invalid");
        if (errors.Count > 0) return new(false, null, errors.Distinct(StringComparer.Ordinal).ToArray());

        var tables = snapshot.Tables.ToDictionary(table => table.CatalogId, StringComparer.Ordinal);
        var columns = snapshot.Columns.ToDictionary(column => column.CatalogId, StringComparer.Ordinal);
        var prodLines = tables[production!.LineItemsTableCatalogId];
        var prodReports = tables[production.ReportsTableCatalogId];
        var salesLines = tables[sales!.LineItemsTableCatalogId];
        var salesReports = tables[sales.ReportsTableCatalogId];
        var companyTable = tables.Values.SingleOrDefault(table => table.Name == "Companies");
        if (companyTable is null || !companyTable.Queryable || !companyTable.SemanticApproved)
            return new(false, null, ["population_discovery_company_identity_relationship_unapproved"]);
        var prodRelation = snapshot.Relationships.SingleOrDefault(relation => relation.Verified && relation.Queryable
            && relation.SemanticApproved && relation.QueryAuthorized && Connects(relation, prodLines.CatalogId, prodReports.CatalogId));
        var prodCompanyRelation = snapshot.Relationships.SingleOrDefault(relation => relation.Verified && relation.Queryable
            && relation.SemanticApproved && relation.QueryAuthorized && Connects(relation, prodReports.CatalogId, companyTable.CatalogId));
        var salesRelation = snapshot.Relationships.SingleOrDefault(relation => relation.Verified && relation.Queryable
            && relation.SemanticApproved && relation.QueryAuthorized && Connects(relation, salesLines.CatalogId, salesReports.CatalogId));
        var salesCompanyRelation = snapshot.Relationships.SingleOrDefault(relation => relation.Verified && relation.Queryable
            && relation.SemanticApproved && relation.QueryAuthorized && Connects(relation, salesReports.CatalogId, companyTable.CatalogId));
        if (prodRelation is null || prodCompanyRelation is null || salesRelation is null || salesCompanyRelation is null)
            return new(false, null, ["producer_sales_catalog_relationship_unapproved"]);

        var (prodLineReportFk, prodReportId) = ColumnPair(prodRelation, prodLines.CatalogId, columns);
        var (prodReportCompanyFk, prodCompanyId) = ColumnPair(prodCompanyRelation, prodReports.CatalogId, columns);
        var (salesLineReportFk, salesReportId) = ColumnPair(salesRelation, salesLines.CatalogId, columns);
        var (salesReportCompanyFk, salesCompanyId) = ColumnPair(salesCompanyRelation, salesReports.CatalogId, columns);
        var prodValue = columns[production.ValueColumnCatalogId];
        var prodTitle = columns[production.ProductTitleColumnCatalogId];
        var prodPeriodStart = columns[production.PeriodStartColumnCatalogId];
        var prodPeriodEnd = columns[production.PeriodEndColumnCatalogId];
        var prodProvider = columns[production.ReportProviderColumnCatalogId];
        var prodType = columns[production.ReportTypeColumnCatalogId];
        var prodOutputType = columns[production.OutputTypeColumnCatalogId];
        var prodAccepted = columns[production.AcceptedColumnCatalogId];
        var prodRevision = columns[production.RevisionStatusColumnCatalogId];
        var salesValue = columns[sales.ValueColumnCatalogId];
        var salesUnit = columns[sales.QuantityUnitColumnCatalogId];
        var salesTitle = columns[sales.ProductTitleColumnCatalogId];
        var salesPeriodStart = columns[sales.PeriodStartColumnCatalogId];
        var salesPeriodEnd = columns[sales.PeriodEndColumnCatalogId];
        var salesProvider = columns[sales.ReportProviderColumnCatalogId];
        var salesType = columns[sales.ReportTypeColumnCatalogId];
        var salesOutputType = columns[sales.OutputTypeColumnCatalogId];
        var salesAccepted = columns[sales.AcceptedColumnCatalogId];
        var salesRevision = columns[sales.RevisionStatusColumnCatalogId];
        var salesExternalId = columns[sales.ExternalReportIdColumnCatalogId];
        var salesFingerprint = columns[sales.SourceRowFingerprintColumnCatalogId];
        var salesMultiplicity = columns[sales.SourceMultiplicityColumnCatalogId];
        var companyName = columns.Values.SingleOrDefault(column => column.TableCatalogId == companyTable.CatalogId
            && column.Name == "Name" && column.Queryable);
        var companyTicker = columns.Values.Where(column => column.TableCatalogId == companyTable.CatalogId
                && (column.Name == "Ticker" || column.Name == "CompanySymbol") && column.Queryable)
            .OrderByDescending(column => column.Name == "Ticker").FirstOrDefault();
        var requiredColumns = new[] { prodLineReportFk, prodReportId, prodReportCompanyFk, prodCompanyId, prodValue,
            prodTitle, prodPeriodStart, prodPeriodEnd, prodProvider, prodType, prodOutputType, prodAccepted, prodRevision,
            salesLineReportFk, salesReportId, salesReportCompanyFk, salesCompanyId, salesValue, salesUnit, salesTitle,
            salesPeriodStart, salesPeriodEnd, salesProvider, salesType, salesOutputType, salesAccepted, salesRevision,
            salesExternalId, salesFingerprint, salesMultiplicity, companyName };
        if (requiredColumns.Any(column => column is null || !column.Queryable) || companyName is null)
            return new(false, null, ["producer_sales_required_catalog_columns_unapproved"]);

        var q = Q;
        var qt = QT;
        var explicitMonth = plan.PopulationCoveragePolicyId == "explicit-reporting-period-v1";
        var producerPeriod = explicitMonth
            ? $"pr.{q(prodPeriodStart.Name)} = @period_start AND pr.{q(prodPeriodEnd.Name)} = @period_end"
            : $"pr.{q(prodPeriodEnd.Name)} >= @period_start AND pr.{q(prodPeriodEnd.Name)} <= @period_end";
        var salesPeriod = $"sr.{q(salesPeriodEnd.Name)} >= @period_start AND sr.{q(salesPeriodEnd.Name)} <= @period_end";
        var productSalesPredicate = plan.PopulationSalesScope == PopulationSalesScope.RequestedProductSales
            ? $" AND BTRIM(sl.{q(salesTitle.Name)}) = ANY(@product_aliases)" : string.Empty;
        var companyKey = columns[salesCompanyId.CatalogId];
        var ticker = companyTicker is null ? "NULL::text" : $"c.{q(companyTicker.Name)}";
        var unitCount = sales.UnitKind == "quantity" ? $"COUNT(DISTINCT sl.{q(salesUnit.Name)})" : "1";
        var productMappingCoverage = string.Equals(concept!.MappingCoverageStatus, "complete", StringComparison.OrdinalIgnoreCase)
            ? "complete" : "partial";
        var sql = "WITH producers AS (" +
            $"SELECT DISTINCT pr.{q(prodReportCompanyFk.Name)} AS {q("CompanyId")} " +
            $"FROM {qt(prodLines)} pl JOIN {qt(prodReports)} pr ON pl.{q(prodLineReportFk.Name)} = pr.{q(prodReportId.Name)} " +
            $"WHERE pr.{q(prodProvider.Name)} = @production_provider AND pr.{q(prodType.Name)} = @production_report_type " +
            $"AND pr.{q(prodOutputType.Name)} = @production_output_type AND pr.{q(prodAccepted.Name)} = @production_accepted " +
            $"AND pr.{q(prodRevision.Name)} = @production_revision AND {producerPeriod} " +
            $"AND pr.{q(prodReportCompanyFk.Name)} = ANY(@company_ids) AND pl.{q(prodValue.Name)} > 0 " +
            $"AND BTRIM(pl.{q(prodTitle.Name)}) = ANY(@product_aliases)), " +
            $"eligible_sales AS (SELECT sr.{q(salesReportCompanyFk.Name)} AS {q("CompanyId")}, " +
            $"c.{q(companyName.Name)} AS {q("CompanyName")}, {ticker} AS {q("CompanySymbol")}, " +
            $"MIN(sr.{q(salesPeriodStart.Name)}) AS {q("PeriodStart")}, sr.{q(salesPeriodEnd.Name)} AS {q("PeriodEnd")}, " +
            $"SUM(sl.{q(salesValue.Name)}) AS {q("Value")}, MIN(sl.{q(salesUnit.Name)}) AS {q("SourceUnit")}, " +
            $"{unitCount} AS {q("UnitCompatibilityCount")}, COUNT(DISTINCT sr.{q(salesReportId.Name)}) AS {q("EligibleReportCount")}, " +
            $"COUNT(DISTINCT sr.{q(salesReportId.Name)}) AS {q("SamePeriodCandidateCount")}, " +
            $"COUNT(*) FILTER (WHERE sl.{q(salesMultiplicity.Name)} <> 1) AS {q("InvalidMultiplicityCount")}, " +
            $"MAX(sl.{q(salesMultiplicity.Name)}) AS {q("SourceMultiplicity")}, MIN(sr.{q(salesProvider.Name)}) AS {q("ProviderName")}, " +
            $"ARRAY_AGG(DISTINCT sr.{q(salesReportId.Name)}) AS {q("ReportIdentity")}, " +
            $"ARRAY_AGG(DISTINCT sr.{q(salesExternalId.Name)}) AS {q("ExternalReportId")}, " +
            $"ARRAY_AGG(DISTINCT sl.{q(salesFingerprint.Name)}) AS {q("SourceRowFingerprint")} " +
            $"FROM producers p JOIN {qt(salesReports)} sr ON sr.{q(salesReportCompanyFk.Name)} = p.{q("CompanyId")} " +
            $"JOIN {qt(salesLines)} sl ON sl.{q(salesLineReportFk.Name)} = sr.{q(salesReportId.Name)} " +
            $"JOIN {qt(companyTable)} c ON c.{q(companyKey.Name)} = sr.{q(salesReportCompanyFk.Name)} " +
            $"WHERE sr.{q(salesProvider.Name)} = @sales_provider AND sr.{q(salesType.Name)} = @sales_report_type " +
            $"AND sr.{q(salesOutputType.Name)} = @sales_output_type AND sr.{q(salesAccepted.Name)} = @sales_accepted " +
            $"AND sr.{q(salesRevision.Name)} = @sales_revision AND {salesPeriod}{productSalesPredicate} " +
            $"GROUP BY sr.{q(salesReportCompanyFk.Name)}, c.{q(companyName.Name)}, {ticker}, sr.{q(salesPeriodEnd.Name)}), " +
            $"common_periods AS (SELECT {q("PeriodStart")}, {q("PeriodEnd")} FROM eligible_sales " +
            $"GROUP BY {q("PeriodStart")}, {q("PeriodEnd")} HAVING COUNT(DISTINCT {q("CompanyId")}) = (SELECT COUNT(*) FROM producers)), " +
            $"latest_common_period AS (SELECT {q("PeriodStart")}, {q("PeriodEnd")} FROM common_periods " +
            $"ORDER BY {q("PeriodEnd")} DESC, {q("PeriodStart")} DESC LIMIT 1) " +
            $"SELECT es.*, '{productMappingCoverage}' AS {q("ProductMappingCoverageStatus")}, " +
            $"@company_coverage_complete AS {q("CompanyCoverageComplete")} FROM eligible_sales es JOIN latest_common_period lp " +
            $"ON lp.{q("PeriodStart")} = es.{q("PeriodStart")} AND lp.{q("PeriodEnd")} = es.{q("PeriodEnd")} " +
            $"ORDER BY es.{q("Value")} DESC, es.{q("CompanyName")} ASC, es.{q("CompanyId")} ASC LIMIT @row_limit";

        var parameters = new List<StructuredQueryParameter>
        {
            new("production_provider", production.ProviderName), new("production_report_type", production.ReportType),
            new("production_output_type", production.OutputType), new("production_accepted", production.IsAccepted),
            new("production_revision", production.RevisionStatus), new("sales_provider", sales.ProviderName),
            new("sales_report_type", sales.ReportType), new("sales_output_type", sales.OutputType),
            new("sales_accepted", sales.IsAccepted), new("sales_revision", sales.RevisionStatus),
            new("period_start", plan.Period.ReportingPeriodStart!.Value), new("period_end", plan.Period.ReportingPeriodEnd!.Value),
            new("product_aliases", concept.ApprovedAliases.ToArray()),
            new("company_coverage_complete", scope!.CompanyCoverageComplete),
            new("row_limit", Math.Min(plan.Pagination.Limit, MaximumRows) + 1)
        };
        foreach (var table in new[] { prodLines, prodReports, salesLines, salesReports, companyTable })
            if (!Identifier.IsMatch(table.Name) || !Identifier.IsMatch(table.Schema))
                return new(false, null, ["catalog_identifier_invalid"]);
        var relationshipIds = new[] { prodRelation.CatalogId, prodCompanyRelation.CatalogId,
            salesRelation.CatalogId, salesCompanyRelation.CatalogId }.Distinct(StringComparer.Ordinal).ToArray();
        var physicalIds = requiredColumns.Select(column => column!.CatalogId)
            .Concat(new[] { prodLines.CatalogId, prodReports.CatalogId, salesLines.CatalogId, salesReports.CatalogId, companyTable.CatalogId })
            .Concat(relationshipIds)
            .Concat(new[] { prodRelation.FromColumnCatalogId, prodRelation.ToColumnCatalogId,
                prodCompanyRelation.FromColumnCatalogId, prodCompanyRelation.ToColumnCatalogId,
                salesRelation.FromColumnCatalogId, salesRelation.ToColumnCatalogId,
                salesCompanyRelation.FromColumnCatalogId, salesCompanyRelation.ToColumnCatalogId })
            .Distinct(StringComparer.Ordinal).ToArray();
        var reviewApproved = plan.SemanticReviewApproved && production.SemanticApproved && sales.SemanticApproved
            && prodLines.SemanticApproved && prodReports.SemanticApproved && salesLines.SemanticApproved
            && salesReports.SemanticApproved && companyTable.SemanticApproved && prodRelation.SemanticApproved
            && prodCompanyRelation.SemanticApproved && salesRelation.SemanticApproved && salesCompanyRelation.SemanticApproved;
        var authorized = plan.QueryAuthorized && production.Queryable && sales.Queryable && prodLines.Queryable
            && prodReports.Queryable && salesLines.Queryable && salesReports.Queryable && companyTable.Queryable
            && prodRelation.Queryable && prodRelation.QueryAuthorized && prodCompanyRelation.Queryable
            && prodCompanyRelation.QueryAuthorized && salesRelation.Queryable && salesRelation.QueryAuthorized
            && salesCompanyRelation.Queryable && salesCompanyRelation.QueryAuthorized
            && physicalIds.Where(columns.ContainsKey).All(id => columns[id].Queryable);
        if (!authorized) return new(false, null, ["population_discovery_scope_or_catalog_query_authorization_missing"]);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
        var rowScopedUnit = sales.UnitKind == "quantity";
        return new(true, new CompiledReadOnlyQuery(sql, parameters, plan.TenantId, snapshot.CatalogVersion,
            snapshot.CatalogHash, fingerprint, Math.Min(plan.Pagination.Limit, MaximumRows), plan.Presentation,
            [production.CatalogId, sales.CatalogId], relationshipIds, physicalIds, Guid.Empty,
            plan.FinancialOperation.Operation, reviewApproved, authorized, plan.RetrievalComplete,
            rowScopedUnit ? "row-source-unit" : sales.UnitCode,
            rowScopedUnit ? "row-scoped-source-unit" : "catalog-defined",
            rowScopedUnit ? "monthly-line-item:Unit" : "catalog:monthly-measures", isMonthlyValueQuery: true,
            companyIds: [], companyDiscoveryScopeId: scope!.ScopeId,
            companyDiscoveryApprovalReference: scope.ApprovalReference,
            companyDiscoveryCoverageComplete: scope.CompanyCoverageComplete,
            productMappingCoverageStatus: concept.MappingCoverageStatus), []);
    }

    private static StructuredQueryCompilationResult BuildCompiledMonthly(string sql, StructuredQueryPlan plan,
        StructuredQueryCatalogSnapshot snapshot, StructuredSqlMonthlyMeasure mapping,
        IReadOnlyList<StructuredQueryParameter> parameters, IReadOnlyList<Guid> companyIds)
    {
        foreach (var identifier in new[] { mapping.LineItemsTableCatalogId, mapping.ReportsTableCatalogId,
                     mapping.ValueColumnCatalogId, mapping.ProductCodeColumnCatalogId, mapping.ProductTitleColumnCatalogId,
                     mapping.QuantityUnitColumnCatalogId, mapping.PeriodStartColumnCatalogId, mapping.PeriodEndColumnCatalogId,
                     mapping.ReportProviderColumnCatalogId, mapping.ReportTypeColumnCatalogId, mapping.OutputTypeColumnCatalogId,
                     mapping.CompanyIdColumnCatalogId, mapping.AcceptedColumnCatalogId, mapping.RevisionStatusColumnCatalogId,
                     mapping.ExternalReportIdColumnCatalogId, mapping.SourceRowFingerprintColumnCatalogId,
                     mapping.SourceMultiplicityColumnCatalogId })
            if (!Identifier.IsMatch(identifier)) return new(false, null, ["catalog_identifier_invalid"]);
        var lineItems = snapshot.Tables.Single(x => x.CatalogId == mapping.LineItemsTableCatalogId);
        var reports = snapshot.Tables.Single(x => x.CatalogId == mapping.ReportsTableCatalogId);
        var relationship = snapshot.Relationships.Single(x => x.CatalogId == mapping.RelationshipIds[0]);
        var physicalIds = MonthlyColumnIds(mapping).Append(relationship.FromColumnCatalogId).Append(relationship.ToColumnCatalogId)
            .Append(relationship.CatalogId).Append(lineItems.CatalogId).Append(reports.CatalogId).Distinct(StringComparer.Ordinal).ToArray();
        var semanticApproved = plan.SemanticReviewApproved && mapping.SemanticApproved
            && lineItems.SemanticApproved && reports.SemanticApproved && relationship.SemanticApproved;
        var authorized = plan.QueryAuthorized && mapping.Queryable && lineItems.Queryable && reports.Queryable
            && relationship.Queryable && relationship.QueryAuthorized
            && physicalIds.Where(id => snapshot.Columns.Any(x => x.CatalogId == id))
                .All(id => snapshot.Columns.Single(x => x.CatalogId == id).Queryable);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
        var hasRowScopedUnit = mapping.UnitKind is "quantity" or "source-quantity";
        var unitStatus = hasRowScopedUnit ? "row-scoped-source-unit" : "catalog-defined";
        return new(true, new(sql, parameters, plan.TenantId, snapshot.CatalogVersion, snapshot.CatalogHash,
            fingerprint, Math.Min(plan.Pagination.Limit, MaximumRows), plan.Presentation, [mapping.CatalogId],
            mapping.RelationshipIds, physicalIds, companyIds[0], plan.FinancialOperation.Operation,
            semanticApproved, authorized, plan.RetrievalComplete,
            hasRowScopedUnit ? "row-source-unit" : mapping.UnitCode ?? "row-source-unit", unitStatus,
            hasRowScopedUnit ? "monthly-line-item:Unit" : mapping.UnitKind == "provider-reported"
                ? "provider-reported; unit review pending" : "catalog:monthly-measures",
            isMonthlyValueQuery: true, companyIds: companyIds), []);
    }

    private static IEnumerable<string> MonthlyColumnIds(StructuredSqlMonthlyMeasure x) =>
    [x.ValueColumnCatalogId, x.ProductCodeColumnCatalogId, x.ProductTitleColumnCatalogId, x.QuantityUnitColumnCatalogId,
        x.PeriodStartColumnCatalogId, x.PeriodEndColumnCatalogId, x.ReportProviderColumnCatalogId,
        x.ReportTypeColumnCatalogId, x.OutputTypeColumnCatalogId, x.CompanyIdColumnCatalogId,
        x.AcceptedColumnCatalogId, x.RevisionStatusColumnCatalogId, x.ExternalReportIdColumnCatalogId,
        x.SourceRowFingerprintColumnCatalogId, x.SourceMultiplicityColumnCatalogId];

    private static StructuredQueryCompilationResult CompileDirect(StructuredQueryPlan plan,
        StructuredQueryCatalogSnapshot snapshot, StructuredSqlDirectSource source)
    {
        var tableMap = snapshot.Tables.ToDictionary(x => x.CatalogId, StringComparer.Ordinal);
        var relationships = source.RelationshipIds.Select(id => snapshot.Relationships.Single(x => x.CatalogId == id)).ToArray();
        var lineItems = tableMap[source.LineItemsTableCatalogId];
        var statements = tableMap[source.StatementsTableCatalogId];
        var companies = tableMap[source.CompaniesTableCatalogId];
        var sourceItems = tableMap[source.SourceItemsTableCatalogId];
        var columns = snapshot.Columns.ToDictionary(x => x.CatalogId, StringComparer.Ordinal);
        var lineItemColumns = columns.Values.Where(x => x.TableCatalogId == lineItems.CatalogId).ToDictionary(x => x.Name, StringComparer.Ordinal);
        var statementColumns = columns.Values.Where(x => x.TableCatalogId == statements.CatalogId).ToDictionary(x => x.Name, StringComparer.Ordinal);
        var lineToStatement = relationships.Single(x => Connects(x, lineItems.CatalogId, statements.CatalogId));
        var statementToCompany = relationships.Single(x => Connects(x, statements.CatalogId, companies.CatalogId));
        var sourceToLine = relationships.Single(x => Connects(x, sourceItems.CatalogId, lineItems.CatalogId));
        var srcId = columns.Values.Single(x => x.TableCatalogId == sourceItems.CatalogId && x.Name == "SourceItemId");
        var (sourceCatalogId, lineSourceFk) = ColumnPair(sourceToLine, sourceItems.CatalogId, columns);
        var (lineStatementFk, statementId) = ColumnPair(lineToStatement, lineItems.CatalogId, columns);
        var (statementCompanyFk, companyId) = ColumnPair(statementToCompany, statements.CatalogId, columns);
        if (sourceCatalogId.TableCatalogId != sourceItems.CatalogId || sourceCatalogId.Name != "Id"
            || lineSourceFk.TableCatalogId != lineItems.CatalogId || lineSourceFk.Name != "SourceItemCatalogId"
            || lineStatementFk.TableCatalogId != lineItems.CatalogId || lineStatementFk.Name != "FinancialStatementId"
            || statementId.TableCatalogId != statements.CatalogId || statementId.Name != "Id"
            || statementCompanyFk.TableCatalogId != statements.CatalogId || statementCompanyFk.Name != "CompanyId"
            || companyId.TableCatalogId != companies.CatalogId || companyId.Name != "Id")
            return new(false, null, ["direct_source_relationship_column_binding_invalid"]);
        var value = lineItemColumns["Value"];
        var provider = columns.Values.Single(x => x.TableCatalogId == sourceItems.CatalogId && x.Name == "ProviderName");
        var unit = columns.Values.Single(x => x.TableCatalogId == sourceItems.CatalogId && x.Name == "Unit");
        var statementType = statementColumns["StatementType"];
        var statementProvider = statementColumns["ProviderName"];
        var persistedStatementType = statementColumns["StatementType"];
        var isComposing = statementColumns["IsComposing"];
        var periodEnd = statementColumns["PeriodEnd"];
        var requestedConsolidationScope = plan.Statement.Scopes.Contains("consolidated", StringComparer.Ordinal)
            ? true
            : plan.Statement.Scopes.Contains("standalone", StringComparer.Ordinal) ? false : (bool?)null;
        var scopeFilter = requestedConsolidationScope.HasValue
            ? $" AND st_scope.{Q(isComposing.Name)} = @is_composing"
            : string.Empty;
        var parameters = new List<StructuredQueryParameter>
        {
            new("company_id", plan.Entities[0].ResolvedEntityId!.Value),
            new("source_item_catalog_id", source.SourceItemCatalogId), new("provider_name", source.ProviderName),
            new("statement_type", source.StatementType), new("source_item_id", source.SourceItemId)
        };
        if (requestedConsolidationScope.HasValue)
            parameters.Add(new("is_composing", requestedConsolidationScope.Value));
        var sql = $"WITH latest_period AS (\n" +
                  $"  SELECT MAX(st_scope.{Q(periodEnd.Name)}) AS \"PeriodEnd\"\n" +
                  $"  FROM {QT(statements)} st_scope\n" +
                  $"  WHERE st_scope.{Q(statementCompanyFk.Name)} = @company_id\n" +
                  $"    AND st_scope.{Q(statementProvider.Name)} = @provider_name\n" +
                  $"    AND st_scope.{Q(persistedStatementType.Name)} = @statement_type" + scopeFilter + "\n" +
                  $"  GROUP BY st_scope.{Q(isComposing.Name)}\n" +
                  $")\n" +
                  $"SELECT li.{Q(value.Name)} AS \"Value\", st.{Q(periodEnd.Name)} AS \"PeriodEnd\", " +
                  $"st.{Q(statementId.Name)} AS \"StatementId\", st.{Q(statementProvider.Name)} AS \"ProviderName\", " +
                  $"st.{Q(persistedStatementType.Name)} AS \"StatementType\", @source_item_id AS \"SourceItemId\", " +
                  $"(SELECT si.{Q(unit.Name)} FROM {QT(sourceItems)} si WHERE si.{Q(sourceCatalogId.Name)} = @source_item_catalog_id) AS \"SourceUnit\", " +
                  $"@source_item_catalog_id AS \"SourceItemCatalogId\", st.{Q(isComposing.Name)} AS \"IsComposing\", " +
                  $"st.{Q(statementCompanyFk.Name)} AS \"CompanyId\", st.{Q(statementColumns["PeriodType"].Name)} AS \"PeriodType\", " +
                  $"st.{Q(statementColumns["IsAudited"].Name)} AS \"IsAudited\", st.{Q(statementColumns["IsRepresented"].Name)} AS \"IsRepresented\", " +
                  $"st.{Q(statementColumns["ExternalStatementId"].Name)} AS \"ExternalStatementId\", st.{Q(statementColumns["PeriodStart"].Name)} AS \"PeriodStart\", " +
                  $"st.{Q(statementColumns["SourcePayloadChecksum"].Name)} AS \"SourcePayloadChecksum\", st.{Q(statementColumns["PublishedAt"].Name)} AS \"PublishedAt\", " +
                  $"st.{Q(statementColumns["LastSynchronizedAt"].Name)} AS \"LastSynchronizedAt\", st.{Q(statementColumns["WarningsJson"].Name)} AS \"WarningsJson\", " +
                  $"st.{Q(statementColumns["LogicalVendor"].Name)} AS \"LogicalVendor\", st.{Q(statementColumns["SourceMode"].Name)} AS \"SourceMode\"\n" +
                  $"FROM {QT(statements)} st\n" +
                  $"JOIN {QT(companies)} co ON st.{Q(statementCompanyFk.Name)} = co.{Q(companyId.Name)}\n" +
                  $"LEFT JOIN {QT(lineItems)} li ON li.{Q(lineStatementFk.Name)} = st.{Q(statementId.Name)} " +
                  $"AND li.{Q(lineSourceFk.Name)} = @source_item_catalog_id\n" +
                  $"WHERE co.{Q(companyId.Name)} = @company_id\n" +
                  $"  AND st.{Q(statementProvider.Name)} = @provider_name\n  AND st.{Q(persistedStatementType.Name)} = @statement_type\n" +
                  $"  AND EXISTS (SELECT 1 FROM {QT(sourceItems)} si WHERE si.{Q(sourceCatalogId.Name)} = @source_item_catalog_id " +
                  $"AND si.{Q(provider.Name)} = @provider_name AND si.{Q(statementType.Name)} = @statement_type AND si.{Q(srcId.Name)} = @source_item_id)\n" +
                  (requestedConsolidationScope.HasValue ? $"  AND st.{Q(isComposing.Name)} = @is_composing\n" : string.Empty) +
                  $"  AND st.{Q(periodEnd.Name)} IN (SELECT \"PeriodEnd\" FROM latest_period)\n" +
                  $"ORDER BY st.{Q(periodEnd.Name)} DESC, st.{Q(statementColumns["PeriodType"].Name)}, st.{Q(statementId.Name)}\nLIMIT @row_limit OFFSET @row_offset";
        parameters.Add(new("row_limit", Math.Min(plan.Pagination.Limit, MaximumRows) + 1));
        parameters.Add(new("row_offset", plan.Pagination.Offset));
        foreach (var identifier in new[] { lineItems.Schema, lineItems.Name, statements.Schema, statements.Name, companies.Schema, companies.Name,
                     sourceItems.Schema, sourceItems.Name, value.Name, periodEnd.Name, statementId.Name, provider.Name, statementType.Name,
                     statementProvider.Name, persistedStatementType.Name, srcId.Name,
                     lineSourceFk.Name, isComposing.Name, statementColumns["IsAudited"].Name, statementColumns["IsRepresented"].Name,
                     statementColumns["ExternalStatementId"].Name, statementColumns["PeriodStart"].Name, statementColumns["SourcePayloadChecksum"].Name,
                     statementColumns["PublishedAt"].Name, statementColumns["LastSynchronizedAt"].Name, statementColumns["WarningsJson"].Name,
                     statementColumns["LogicalVendor"].Name, statementColumns["SourceMode"].Name, statementColumns["PeriodType"].Name, lineStatementFk.Name,
                     statementCompanyFk.Name, companyId.Name })
            if (!Identifier.IsMatch(identifier)) return new(false, null, ["catalog_identifier_invalid"]);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
        var physicalObjectIds = new[] { sourceItems.CatalogId, lineItems.CatalogId, statements.CatalogId, companies.CatalogId,
            srcId.CatalogId, unit.CatalogId, sourceCatalogId.CatalogId, lineSourceFk.CatalogId, lineStatementFk.CatalogId,
            statementId.CatalogId, statementCompanyFk.CatalogId, companyId.CatalogId, value.CatalogId, provider.CatalogId,
            statementType.CatalogId, statementProvider.CatalogId, persistedStatementType.CatalogId, isComposing.CatalogId,
            periodEnd.CatalogId, statementColumns["IsAudited"].CatalogId, statementColumns["IsRepresented"].CatalogId,
            statementColumns["ExternalStatementId"].CatalogId, statementColumns["PeriodStart"].CatalogId,
            statementColumns["SourcePayloadChecksum"].CatalogId, statementColumns["PublishedAt"].CatalogId,
            statementColumns["LastSynchronizedAt"].CatalogId, statementColumns["WarningsJson"].CatalogId,
            statementColumns["LogicalVendor"].CatalogId, statementColumns["SourceMode"].CatalogId,
            statementColumns["PeriodType"].CatalogId }.Distinct(StringComparer.Ordinal).ToArray();
        var requiredTables = new[] { sourceItems, lineItems, statements, companies };
        var requiredColumnsAuthorized = physicalObjectIds.Where(columns.ContainsKey)
            .All(id => columns[id].Queryable);
        var semanticReviewApproved = plan.SemanticReviewApproved && source.SemanticApproved
            && requiredTables.All(table => table.SemanticApproved)
            && relationships.All(relationship => relationship.SemanticApproved);
        var queryAuthorized = plan.QueryAuthorized && source.Queryable
            && requiredTables.All(table => table.Queryable) && requiredColumnsAuthorized
            && relationships.All(relationship => relationship.Queryable && relationship.QueryAuthorized);
        var effectiveUnit = ResolveDirectSourceEffectiveUnit(source);
        var identityAuthorized = queryAuthorized && source.IdentityVerified
            && plan.Sources.Count == 1
            && plan.Sources[0].ReferenceType == FinancialValueReferenceType.DirectSourceItem
            && plan.Sources[0].SourceItemCatalogId == source.SourceItemCatalogId
            && plan.Sources[0].ProviderName == source.ProviderName
            && plan.Sources[0].StatementType == source.StatementType
            && plan.Sources[0].SourceItemId == source.SourceItemId;
        return new(true, new(sql, parameters, plan.TenantId, snapshot.CatalogVersion, snapshot.CatalogHash, fingerprint,
            Math.Min(plan.Pagination.Limit, MaximumRows), plan.Presentation, plan.Sources.Select(x => x.CatalogId).ToArray(),
            source.RelationshipIds, physicalObjectIds, plan.Entities[0].ResolvedEntityId!.Value,
            plan.FinancialOperation.Operation, semanticReviewApproved, queryAuthorized, plan.RetrievalComplete,
            effectiveUnit.Unit, effectiveUnit.Status, effectiveUnit.Provenance,
            identityAuthorized: identityAuthorized), []);
    }

    /// <summary>
    /// The unit a direct source value is reported in. A provider-reported source unit is used as-is;
    /// otherwise only an owner-approved effective unit recorded in the reviewed catalog is carried.
    /// Anything else stays null so the renderer cannot present an unreviewed unit as a fact.
    /// </summary>
    internal static (string? Unit, string? Status, string? Provenance) ResolveDirectSourceEffectiveUnit(StructuredSqlDirectSource source)
    {
        if (string.Equals(source.SourceUnitStatus, "provider-reported", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(source.SourceUnit))
            return (source.SourceUnit, source.SourceUnitStatus, "registered-source-unit");
        if (string.Equals(source.EffectiveUnitStatus, "owner-approved", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(source.EffectiveUnit))
            return (source.EffectiveUnit, source.EffectiveUnitStatus, source.EffectiveUnitProvenance ?? "catalog-effective-unit");
        return (null, source.EffectiveUnitStatus ?? source.SourceUnitStatus, null);
    }

    private static string Q(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static string QT(StructuredSqlTable table) => $"{Q(table.Schema)}.{Q(table.Name)}";
    private static bool Connects(StructuredSqlRelationship relationship, string first, string second) =>
        relationship.FromTableCatalogId == first && relationship.ToTableCatalogId == second
        || relationship.FromTableCatalogId == second && relationship.ToTableCatalogId == first;
    private static (StructuredSqlColumn From, StructuredSqlColumn To) ColumnPair(StructuredSqlRelationship relationship,
        string fromTableId, IReadOnlyDictionary<string, StructuredSqlColumn> columns)
    {
        var forward = relationship.FromTableCatalogId == fromTableId;
        return (columns[forward ? relationship.FromColumnCatalogId : relationship.ToColumnCatalogId],
            columns[forward ? relationship.ToColumnCatalogId : relationship.FromColumnCatalogId]);
    }
}

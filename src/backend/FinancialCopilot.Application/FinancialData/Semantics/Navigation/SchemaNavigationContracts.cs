namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <param name="RequiredCatalogIds">
/// Catalog IDs already identified deterministically (for example the typed monthly measure of the canonical
/// intent). The requirements outline always contains them; they grant nothing and are validated like any other ID.
/// </param>
public sealed record SchemaNavigationRequest(
    int ContractVersion,
    string Question,
    string CatalogVersion,
    string CatalogHash,
    int MaximumExpansionDepth = 2,
    Guid TenantId = default,
    string? CorrelationId = null,
    IReadOnlyList<string>? RequiredCatalogIds = null);

/// <summary>
/// Independently validated user intent requirements. This is supplied to completeness validation
/// separately from the navigator result so an omission by the navigator cannot make context complete.
/// </summary>
public sealed record QuestionSchemaRequirements(
    int ContractVersion,
    string CatalogVersion,
    string CatalogHash,
    IReadOnlyList<string> MeasureIds,
    IReadOnlyList<string> DimensionIds,
    IReadOnlyList<string> EntityKinds,
    IReadOnlyList<string> RelationshipIds,
    IReadOnlyList<string> SourcePolicies,
    IReadOnlyList<string> PeriodPolicies,
    IReadOnlyList<string> ComparisonScopes,
    IReadOnlyList<string> PresentationRequirements,
    bool HasUnknownOrAmbiguousRequirements = false,
    string? UncertaintyReason = null,
    IReadOnlyList<string>? StatementScopes = null,
    IReadOnlyList<string>? AggregationRequirements = null,
    IReadOnlyList<string>? OrderingRequirements = null,
    IReadOnlyList<string>? EntityReferences = null,
    int ModelCalls = 0,
    int? InputTokens = null,
    int? OutputTokens = null,
    decimal? ProviderReportedCost = null,
    string? ProviderReportedCurrency = null,
    TimeSpan? ModelLatency = null,
    IReadOnlyList<FinancialValueSelection>? FinancialValueReferences = null,
    string RequestedFinancialValueOperation = "DirectValueRetrieval",
    StructuredPlanShape? StructuredShape = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProviderResponseId { get; init; }

    public IReadOnlyList<string>? ProductReferences { get; init; }
    public CompanyPopulationResolution? ResolvedCompanyPopulation { get; init; }
    public ProductEquivalenceResolution? ResolvedProductEquivalence { get; init; }
    public ProductConceptResolution? ResolvedProductConcept { get; init; }
    public AuthorizedCompanyDiscoveryScope? AuthorizedCompanyDiscoveryScope { get; init; }
    public bool PopulationDiscoveryRequested { get; init; }
    public string? PopulationCoveragePolicyId { get; init; }
    public PopulationSalesScope? PopulationSalesScope { get; init; }
    public bool PopulationSalesScopeAmbiguous { get; init; }
    public IReadOnlyList<string> NormalizationDiagnostics { get; init; } = [];
    public int RetryAttempts { get; init; }
    public int? EstimatedInputTokens { get; init; }
    public int? EstimatedOutputTokens { get; init; }
}

public enum FinancialValueReferenceType
{
    GovernedMetric,
    DirectSourceItem,
    ProviderFinancialIndicator,
    MonthlyMeasure
}

public enum FinancialValueOperation
{
    DirectValueRetrieval,
    ApprovedPeriodComparison,
    UnsupportedDerivedFinancialMetric
}

/// <summary>A catalog identity selected for retrieval; it does not itself authorize runtime querying.</summary>
public sealed record FinancialValueSelection(
    [property: System.Text.Json.Serialization.JsonPropertyName("referenceType")] string ReferenceType,
    [property: System.Text.Json.Serialization.JsonPropertyName("catalogId")] string CatalogId);

public enum StructuredProductBindingStatus { Found, Ambiguous, NotFound, Missing, CompanyMismatch }
/// <summary>
/// A company-scoped semantic product. <see cref="ProductKey"/> is the normalized title/unit identity and
/// <see cref="Selection"/> lists the persisted source labels that carry it; the persisted ProductCode is
/// never a product identity (owner decision 2026-10-29).
/// </summary>
public sealed record StructuredProductBinding(StructuredProductBindingStatus Status, Guid? CompanyId,
    string? ProductKey, string? DisplayTitle, string? Unit, string? IdentityProvenance,
    IReadOnlyList<string> CandidateProductKeys, string? DiagnosticCode,
    MonthlyProductSelection? Selection = null);

/// <summary>
/// The persisted source labels of one company's semantic product: every MonthlyReportLineItems Title and
/// Unit spelling that the existing product normalization maps to it, approved concept aliases included.
/// Every title/unit pair denotes the same product, so rows are matched by these labels alone. A missing
/// unit is the empty string.
/// </summary>
public sealed record MonthlyProductSelection(IReadOnlyList<string> SourceTitles, IReadOnlyList<string> SourceUnits);

public interface IStructuredQueryProductBinder
{
    Task<StructuredProductBinding> ResolveAsync(string? companyMention, string? productMention,
        CancellationToken cancellationToken);
}

/// <summary>Verified persisted identity and governance metadata for a direct provider source item.</summary>
public sealed record FinancialValueCatalogReference(
    string CatalogId,
    Guid? SourceItemCatalogId,
    string ProviderName,
    string StatementType,
    int SourceItemId,
    string? TitleFa,
    string? TitleEn,
    string? SourceUnit,
    string? NormalizedUnit,
    string? MetricCode,
    string IdentityStatus,
    string SemanticReviewStatus,
    bool Queryable,
    string? SourceUnitStatus = null,
    string? IdentityApprovalStatus = null,
    string? TitleApprovalStatus = null,
    IReadOnlyList<CanonicalFinancialMeasureBinding>? CanonicalConceptBindings = null,
    string? EffectiveUnitStatus = null,
    string? UnitClassification = null,
    string? EffectiveUnit = null,
    string? EffectiveUnitProvenance = null,
    string? QueryAuthorizationStatus = null);

/// <summary>Reviewed semantic equivalence; separate from provider identity, unit evidence, and runtime authorization.</summary>
public sealed record CanonicalFinancialMeasureBinding(
    string CanonicalConceptId,
    string Dimension,
    string ValueForm,
    string Status,
    string? Evidence,
    string? Provenance);

public sealed record FinancialPeriodComparisonFacts(
    bool SameCompany,
    bool SameStatementScope,
    bool SameValueDefinition,
    bool SameProvider,
    bool SameUnit,
    bool ComparablePeriods,
    bool HasNegativeBaseline);

public sealed record FinancialValuePolicyDecision(bool Allowed, string Code);

/// <summary>Deterministic guardrails for retrieval and explicitly approved period comparisons.</summary>
public static class FinancialValueOperationPolicy
{
    public static FinancialValuePolicyDecision ValidateOperation(string? operation) => operation switch
    {
        nameof(FinancialValueOperation.DirectValueRetrieval) => new(true, "direct_value_retrieval"),
        nameof(FinancialValueOperation.ApprovedPeriodComparison) => new(true, "approved_period_comparison_requires_validated_values"),
        nameof(FinancialValueOperation.UnsupportedDerivedFinancialMetric) => new(false, "derived_financial_metric_not_supported"),
        _ => new(false, "unknown_financial_value_operation")
    };

    public static FinancialValuePolicyDecision ValidatePeriodComparison(FinancialPeriodComparisonFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.HasNegativeBaseline) return new(false, "negative_baseline_growth_convention_unapproved");
        if (!facts.SameCompany || !facts.SameStatementScope || !facts.SameValueDefinition || !facts.SameProvider
            || !facts.SameUnit || !facts.ComparablePeriods)
            return new(false, "period_comparison_values_not_comparable");
        return new(true, "period_comparison_values_comparable");
    }
}

public sealed record NavigationCandidate(string NodeId, double Confidence, string Rationale);

public sealed record SchemaNavigationResult(
    int ContractVersion,
    string CatalogVersion,
    string CatalogHash,
    IReadOnlyList<NavigationCandidate> Candidates,
    IReadOnlyList<string> SelectedNodeIds,
    IReadOnlyList<string> SelectedMeasureIds,
    IReadOnlyList<string> SelectedDimensionIds,
    IReadOnlyList<string> SelectedEntityKinds,
    IReadOnlyList<string> SelectedRelationshipIds,
    IReadOnlyList<string> SelectedSourcePolicies,
    IReadOnlyList<string> SelectedPeriodPolicies,
    IReadOnlyList<string> MissingContext,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string>? SelectedComparisonScopes = null,
    IReadOnlyList<string>? SelectedPresentationRequirements = null,
    IReadOnlyList<string>? SelectedStatementScopes = null,
    IReadOnlyList<string>? SelectedAggregationRequirements = null,
    IReadOnlyList<string>? SelectedOrderingRequirements = null,
    IReadOnlyList<string>? SelectedEntityReferences = null,
    int ModelCalls = 0,
    int? InputTokens = null,
    int? OutputTokens = null,
    decimal? ProviderReportedCost = null,
    string? ProviderReportedCurrency = null,
    TimeSpan? ModelLatency = null,
    SchemaContextCompletenessStatus? CompletenessStatus = null,
    IReadOnlyList<FinancialValueSelection>? SelectedFinancialValueReferences = null,
    string? FinancialValueOperation = null,
    int RetryAttempts = 0,
    int? EstimatedInputTokens = null,
    int? EstimatedOutputTokens = null,
    bool RetrievalComplete = false,
    bool SemanticReviewApproved = false,
    bool QueryAuthorized = false,
    IReadOnlyList<string>? CandidateMeasureIds = null,
    IReadOnlyList<string>? RequiredMeasureIds = null,
    IReadOnlyList<string>? CandidateDimensionIds = null,
    IReadOnlyList<string>? CandidateRelationshipIds = null)
{
    public int RequirementsCalls { get; init; }
    public int NavigationCalls { get; init; }
    public int ExpansionCalls { get; init; }
    public int RequirementsRetryAttempts { get; init; }
    public int NavigationRetryAttempts { get; init; }
}

public sealed record SchemaContextExpansionRequest(
    int ContractVersion,
    string CatalogVersion,
    string CatalogHash,
    IReadOnlyList<string> NodeIds,
    int Depth,
    int MaximumFanOut = 12,
    IReadOnlyList<string>? RequiredCatalogIds = null);

/// <summary>Catalog review state of a governed measure (from dictionaries/monthly-measures.yaml); independent from navigation-node review.</summary>
public sealed record MeasureGovernance(bool SemanticApproved, bool Queryable, string? Status, string? Provenance);

public sealed record SchemaNodeGovernance(
    string NodeId,
    bool ExistsInCatalog,
    bool NavigationMetadataApproved,
    bool SemanticMeaningApproved,
    bool QueryAuthorized);

public sealed record SchemaContext(
    int ContractVersion,
    string CatalogVersion,
    string CatalogHash,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<SchemaNodeGovernance> NodeGovernance,
    IReadOnlyList<string> MeasureIds,
    IReadOnlyList<string> DimensionIds,
    IReadOnlyList<string> EntityKinds,
    IReadOnlyList<string> RelationshipIds,
    IReadOnlyList<string> SourcePolicies,
    IReadOnlyList<string> PeriodPolicies,
    IReadOnlyList<string> ComparisonScopes,
    IReadOnlyList<string> PresentationRequirements,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string>? StatementScopes = null,
    IReadOnlyList<string>? AggregationRequirements = null,
    IReadOnlyList<string>? OrderingRequirements = null,
    IReadOnlyList<string>? EntityReferences = null,
    IReadOnlyList<FinancialValueCatalogReference>? FinancialValueReferences = null,
    IReadOnlyList<string>? ProviderFinancialIndicatorIds = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? MeasureRelationshipIds = null,
    IReadOnlyList<string>? SourceSemantics = null,
    IReadOnlyList<string>? UnitAndPeriodSemantics = null,
    IReadOnlyDictionary<string, MeasureGovernance>? MeasureGovernance = null);

public enum SchemaContextCompletenessStatus
{
    Complete,
    Incomplete,
    ClarificationRequired,
    Unsupported,
    CatalogMismatch
}

public sealed record SchemaContextCompletenessResult(
    SchemaContextCompletenessStatus Status,
    IReadOnlyList<string> MissingRequirements,
    IReadOnlyList<string> Diagnostics,
    bool RetrievalComplete = false,
    bool SemanticReviewApproved = false,
    bool QueryAuthorized = false)
{
    public bool IsComplete => Status == SchemaContextCompletenessStatus.Complete;
}

public interface IVectorlessSchemaNavigator
{
    Task<SchemaNavigationResult> NavigateQuestionAsync(SchemaNavigationRequest request, CancellationToken cancellationToken);

    Task<SchemaNavigationResult> NavigateAsync(
        SchemaNavigationRequest request,
        QuestionSchemaRequirements independentlyExtractedRequirements,
        CancellationToken cancellationToken);
}

public interface ICatalogNavigationHierarchyProvider
{
    Task<SchemaContext?> GetContextAsync(SchemaContextExpansionRequest request, CancellationToken cancellationToken);
}

public interface ISchemaContextExpander
{
    Task<SchemaContext?> ExpandAsync(SchemaContext context, IReadOnlyList<string> knownNodeIds, int depth, CancellationToken cancellationToken);

    Task<SchemaContext?> ExpandAsync(SchemaContext context, IReadOnlyList<string> knownNodeIds, int depth,
        IReadOnlyList<string> requiredCatalogIds, CancellationToken cancellationToken) =>
        ExpandAsync(context, knownNodeIds, depth, cancellationToken);
}

public interface ISchemaContextCompletenessValidator
{
    SchemaContextCompletenessResult Validate(QuestionSchemaRequirements requirements, SchemaContext context);
}

/// <summary>Deterministic completeness gate; it does not trust requirements declared by the navigator.</summary>
public sealed class SchemaContextCompletenessValidator : ISchemaContextCompletenessValidator
{
    public SchemaContextCompletenessResult Validate(QuestionSchemaRequirements requirements, SchemaContext context)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(context);

        if (requirements.ContractVersion != 1 || context.ContractVersion != 1)
            return new(SchemaContextCompletenessStatus.Unsupported, [], ["Unsupported schema-navigation contract version."]);

        if (!string.Equals(requirements.CatalogVersion, context.CatalogVersion, StringComparison.Ordinal)
            || !string.Equals(requirements.CatalogHash, context.CatalogHash, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(context.CatalogHash))
            return new(SchemaContextCompletenessStatus.CatalogMismatch, [], ["Schema context has no compatible catalog identity."]);

        if (requirements.HasUnknownOrAmbiguousRequirements)
            return new(SchemaContextCompletenessStatus.ClarificationRequired, [],
                [requirements.UncertaintyReason ?? "The requested entity, measure, dimension, or scope is ambiguous or unsupported."]);

        var operationDecision = FinancialValueOperationPolicy.ValidateOperation(requirements.RequestedFinancialValueOperation);
        if (!operationDecision.Allowed)
            return new(SchemaContextCompletenessStatus.Unsupported, [], [operationDecision.Code]);
        if (requirements.RequestedFinancialValueOperation == nameof(FinancialValueOperation.ApprovedPeriodComparison)
            && !(requirements.ComparisonScopes ?? []).Any(scope => scope is "period-over-period" or "year-over-year"))
            return new(SchemaContextCompletenessStatus.Unsupported, [], ["period_comparison_scope_missing"]);

        var governanceDiagnostics = new List<string>();
        var selectedFinancialReferences = requirements.FinancialValueReferences ?? [];
        var directSourceIdentityRequest = selectedFinancialReferences.Count > 0
            && selectedFinancialReferences.All(reference => reference.ReferenceType
                == nameof(FinancialValueReferenceType.DirectSourceItem));
        // A request for exactly reviewed catalog measures is governed by the measures' own catalog
        // review state (the same principle as exact direct-source identity); navigation-node review
        // stays a navigation concern. Contexts without measure governance keep the node-based rule.
        var measureIdentityRequest = selectedFinancialReferences.Count > 0 && context.MeasureGovernance is not null
            && selectedFinancialReferences.All(reference => reference.ReferenceType
                == nameof(FinancialValueReferenceType.MonthlyMeasure));
        if (context.NodeGovernance.GroupBy(item => item.NodeId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            governanceDiagnostics.Add("Schema context contains duplicate node-governance records.");
        var governance = context.NodeGovernance.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
        foreach (var nodeId in context.NodeIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!governance.TryGetValue(nodeId, out var node) || !node.ExistsInCatalog)
            {
                governanceDiagnostics.Add($"Unknown navigation node: {nodeId}.");
                continue;
            }
            if (!node.NavigationMetadataApproved && !directSourceIdentityRequest && !measureIdentityRequest)
                governanceDiagnostics.Add($"Navigation metadata is not approved: {nodeId}.");
        }

        var missing = new List<string>();
        AddMissing("measure", requirements.MeasureIds, context.MeasureIds, missing);
        AddMissing("dimension", requirements.DimensionIds, context.DimensionIds, missing);
        AddMissing("entity", requirements.EntityKinds, context.EntityKinds, missing);
        AddMissing("relationship", requirements.RelationshipIds, context.RelationshipIds, missing);
        AddMissing("source policy", requirements.SourcePolicies, context.SourcePolicies, missing);
        AddMissing("period policy", requirements.PeriodPolicies, context.PeriodPolicies, missing);
        AddMissing("comparison scope", requirements.ComparisonScopes ?? [], context.ComparisonScopes ?? [], missing);
        AddMissing("presentation", requirements.PresentationRequirements, context.PresentationRequirements, missing);
        AddMissing("statement scope", requirements.StatementScopes ?? [], context.StatementScopes ?? [], missing);
        AddMissing("aggregation", requirements.AggregationRequirements ?? [], context.AggregationRequirements ?? [], missing);
        AddMissing("ordering", requirements.OrderingRequirements ?? [], context.OrderingRequirements ?? [], missing);
        AddMissing("entity reference", requirements.EntityReferences ?? [], context.EntityReferences ?? [], missing);
        AddMissing("product reference", requirements.ProductReferences ?? [], context.EntityReferences ?? [], missing);
        var shapeReferences = (requirements.StructuredShape?.ProjectionCatalogIds ?? [])
            .Concat(requirements.StructuredShape?.GroupByCatalogIds ?? [])
            .Concat((requirements.StructuredShape?.Filters ?? []).Select(x => x.CatalogId))
            .Concat((requirements.StructuredShape?.Aggregations ?? []).Select(x => x.CatalogId))
            .Concat((requirements.StructuredShape?.Sort ?? []).Select(x => x.CatalogId));
        AddMissing("plan catalog reference", shapeReferences.ToArray(),
            context.MeasureIds.Concat(context.DimensionIds).Concat(context.RelationshipIds)
                .Concat(context.ProviderFinancialIndicatorIds ?? []).Concat((context.FinancialValueReferences ?? []).Select(x => x.CatalogId)).ToArray(), missing);

        var resolvedFinancialReferences = context.FinancialValueReferences ?? [];
        var directSourceIdentityAuthorized = directSourceIdentityRequest;
        foreach (var selectedReference in selectedFinancialReferences)
        {
            if (!Enum.TryParse<FinancialValueReferenceType>(selectedReference.ReferenceType, out var referenceType))
                return new(SchemaContextCompletenessStatus.Unsupported, missing, [$"Unknown financial value reference type: {selectedReference.ReferenceType}."]);
            if (referenceType is FinancialValueReferenceType.GovernedMetric or FinancialValueReferenceType.MonthlyMeasure)
            {
                if (!(context.MeasureIds ?? []).Contains(selectedReference.CatalogId, StringComparer.Ordinal))
                    missing.Add($"financial-value-reference:{selectedReference.CatalogId}");
                continue;
            }
            if (referenceType == FinancialValueReferenceType.ProviderFinancialIndicator)
            {
                if (!(context.ProviderFinancialIndicatorIds ?? []).Contains(selectedReference.CatalogId, StringComparer.Ordinal))
                    missing.Add($"financial-value-reference:{selectedReference.CatalogId}");
                continue;
            }
            var resolved = resolvedFinancialReferences.FirstOrDefault(item =>
                string.Equals(item.CatalogId, selectedReference.CatalogId, StringComparison.Ordinal));
            if (resolved is null)
            {
                missing.Add($"financial-value-reference:{selectedReference.CatalogId}");
                continue;
            }
            if (referenceType == FinancialValueReferenceType.DirectSourceItem
                && (!resolved.SourceItemCatalogId.HasValue || resolved.SourceItemCatalogId == Guid.Empty
                    || string.IsNullOrWhiteSpace(resolved.ProviderName) || string.IsNullOrWhiteSpace(resolved.StatementType)
                    || resolved.SourceItemId <= 0
                    || !(string.Equals(resolved.IdentityStatus, "verified", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(resolved.IdentityStatus, "verified-persisted-source-key", StringComparison.OrdinalIgnoreCase))))
                governanceDiagnostics.Add($"Direct source-item identity is incomplete: {selectedReference.CatalogId}.");
            if (referenceType == FinancialValueReferenceType.DirectSourceItem
                && (!resolved.Queryable || !resolved.SourceItemCatalogId.HasValue
                    || string.IsNullOrWhiteSpace(resolved.ProviderName)
                    || string.IsNullOrWhiteSpace(resolved.StatementType) || resolved.SourceItemId <= 0
                    || !(string.Equals(resolved.IdentityStatus, "verified", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(resolved.IdentityStatus, "verified-persisted-source-key", StringComparison.OrdinalIgnoreCase))))
                directSourceIdentityAuthorized = false;
            if (!resolved.Queryable || referenceType != FinancialValueReferenceType.DirectSourceItem
                && !string.Equals(resolved.SemanticReviewStatus, "approved", StringComparison.OrdinalIgnoreCase))
                governanceDiagnostics.Add($"Financial value reference is not approved for retrieval: {selectedReference.CatalogId}.");
        }

        var measureIdentityApproved = measureIdentityRequest && selectedFinancialReferences.All(reference =>
            context.MeasureGovernance!.TryGetValue(reference.CatalogId, out var measure) && measure.SemanticApproved && measure.Queryable);
        var semanticReviewApproved = measureIdentityRequest
            ? measureIdentityApproved
            : context.NodeIds.All(id => governance.TryGetValue(id, out var item) && item.SemanticMeaningApproved)
            && resolvedFinancialReferences.All(item => string.Equals(item.SemanticReviewStatus, "approved", StringComparison.OrdinalIgnoreCase));
        var queryAuthorized = measureIdentityRequest
            ? measureIdentityApproved
            : directSourceIdentityRequest
            ? directSourceIdentityAuthorized
                && selectedFinancialReferences.All(reference => resolvedFinancialReferences.Any(item =>
                    string.Equals(item.CatalogId, reference.CatalogId, StringComparison.Ordinal) && item.Queryable))
            : context.NodeIds.All(id => governance.TryGetValue(id, out var item) && item.QueryAuthorized)
                && resolvedFinancialReferences.All(item => item.Queryable);
        var diagnostics = context.Diagnostics.Concat(governanceDiagnostics).Distinct(StringComparer.Ordinal).ToArray();
        var retrievalComplete = missing.Count == 0;
        var status = governanceDiagnostics.Count > 0 ? SchemaContextCompletenessStatus.Unsupported
            : retrievalComplete ? SchemaContextCompletenessStatus.Complete : SchemaContextCompletenessStatus.Incomplete;
        return new(status, missing, diagnostics, retrievalComplete, semanticReviewApproved, queryAuthorized);
    }

    private static void AddMissing(string kind, IReadOnlyList<string> required, IReadOnlyList<string> available, ICollection<string> missing)
    {
        var availableSet = new HashSet<string>(available, StringComparer.Ordinal);
        foreach (var id in required.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            if (!availableSet.Contains(id)) missing.Add($"{kind}:{id}");
    }
}

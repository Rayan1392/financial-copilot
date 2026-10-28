using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialSourcePlannerHandoffTests
{
    [Theory]
    [InlineData("IncomeStatement", 143, "سود خالص", "MonetaryStatementItem", "MillionIRR", null)]
    [InlineData("IncomeStatement", 161, "سود عملیاتی هر سهم", "MonetaryPerShareItem", "IRRPerShare", "operatingProfitPerShare")]
    [InlineData("BalanceSheet", 138, "جمع کل دارایی‌ها", "MonetaryStatementItem", "MillionIRR", null)]
    [InlineData("CashFlow", 1, "جریان وجه نقد عملیاتی", "MonetaryStatementItem", "MillionIRR", null)]
    public async Task ExactIdentity_CarriesLosslessTypedIntentThroughPlannerAndValidator(
        string statementType, int sourceItemId, string officialTitle, string sourceDimensionEvidence,
        string unit, string? canonicalConcept)
    {
        var (provider, version, hash) = CreateProvider();
        var references = await provider.GetFinancialSourceReferencesAsync(version, hash, CancellationToken.None);
        var resolved = FinancialSourceItemTitleResolver.Resolve(new(officialTitle, "NoavaranCurrentApi", statementType), references);
        var intent = CreateIntent(officialTitle, canonicalConcept, statementType == "IncomeStatement" && sourceItemId == 161
            ? BusinessMeasureDimension.CurrencyPerShare : BusinessMeasureDimension.CurrencyAmount);

        var handoff = FinancialSourcePlannerHandoff.Create(intent, resolved, version, hash);

        Assert.Equal(FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation, handoff.Status);
        var binding = handoff.Binding!;
        Assert.Equal("NoavaranCurrentApi", binding.ProviderName);
        Assert.Equal(statementType, binding.StatementType);
        Assert.Equal(sourceItemId, binding.SourceItemId);
        Assert.NotEqual(Guid.Empty, binding.SourceItemUuid);
        Assert.Equal(officialTitle, binding.OriginalUserMeasureExpression);
        Assert.Equal(FinancialSourceItemTitleResolver.NormalizeTitle(officialTitle),
            FinancialSourceItemTitleResolver.NormalizeTitle(binding.OfficialTitleFa));
        Assert.Equal(intent, binding.OriginalIntent);
        Assert.Equal(intent.Period, binding.PeriodIntent);
        Assert.Equal(intent.EntityScope, binding.EntityScope);
        Assert.Equal(intent.Operation, binding.Operation);
        Assert.Equal(intent.Comparison, binding.Comparison);
        Assert.Equal(intent.Measures[0].Qualifiers, binding.StatementAndConsolidationQualifiers);
        Assert.Equal(unit, binding.UnitEvidence.EffectiveSourceUnit);
        Assert.Equal(canonicalConcept is null ? sourceDimensionEvidence
            : "CurrencyPerShare", binding.UnitEvidence.SourceDimensionEvidence);
        Assert.Equal("unknown", binding.UnitEvidence.SourceUnitStatus);
        Assert.Equal(EffectiveUnitEvidence.Unresolved, binding.UnitEvidence.IntentEffectiveUnitEvidence);
        Assert.Equal(canonicalConcept, binding.VerifiedBusinessConcept?.ConceptId);
        Assert.False(binding.Authorization.Queryable);
        Assert.Equal("not-authorized", binding.Authorization.Status);

        var context = await provider.GetContextAsync(new SchemaContextExpansionRequest(1, version, hash, [], 0,
            RequiredCatalogIds: [binding.SourceReference.CatalogId]), CancellationToken.None);
        Assert.NotNull(context);
        context = context with
        {
            PeriodPolicies = binding.PlannerRequirements.PeriodPolicies,
            PresentationRequirements = binding.PlannerRequirements.PresentationRequirements,
            StatementScopes = binding.PlannerRequirements.StatementScopes,
            EntityReferences = binding.PlannerRequirements.EntityReferences
        };
        var navigation = new SchemaNavigationResult(1, version, hash, [], context.NodeIds,
            [], [], binding.PlannerRequirements.EntityKinds, [], [], binding.PlannerRequirements.PeriodPolicies,
            [], [], SelectedPresentationRequirements: binding.PlannerRequirements.PresentationRequirements,
            SelectedStatementScopes: binding.PlannerRequirements.StatementScopes,
            SelectedEntityReferences: binding.PlannerRequirements.EntityReferences,
            SelectedFinancialValueReferences: binding.PlannerRequirements.FinancialValueReferences,
            FinancialValueOperation: binding.PlannerRequirements.RequestedFinancialValueOperation);
        var completeness = new SchemaContextCompletenessValidator().Validate(binding.PlannerRequirements, context);
        var plan = new StructuredQueryPlanBuilder().Build(new(1, officialTitle, version, hash,
                TenantId: Guid.Parse("22345678-1234-1234-1234-123456789abc")), binding.PlannerRequirements,
            navigation, context, completeness);
        var validation = new StructuredQueryPlanValidator().Validate(
            new(1, officialTitle, version, hash, TenantId: plan.TenantId), binding.PlannerRequirements,
            plan, context, completeness);

        var plannedSource = Assert.Single(plan.Sources);
        Assert.Equal(FinancialValueReferenceType.DirectSourceItem, plannedSource.ReferenceType);
        Assert.Equal(binding.SourceReference.CatalogId, plannedSource.CatalogId);
        Assert.Equal(binding.SourceItemUuid, plannedSource.SourceItemCatalogId);
        Assert.Equal(statementType, plannedSource.StatementType);
        Assert.Equal(sourceItemId, plannedSource.SourceItemId);
        Assert.Equal(unit, plannedSource.Unit);
        Assert.False(completeness.QueryAuthorized);
        Assert.True(validation.IsStructurallyValid, string.Join(", ", validation.Diagnostics));
        Assert.False(validation.QueryAuthorized);
        Assert.False(validation.ExecutionEligible);
        Assert.False(plan.QueryAuthorized);
        Assert.False(plan.ExecutionEligible);
    }

    [Fact]
    public async Task ConsolidationQualifierWithoutCatalogEvidenceStopsBeforePlanner()
    {
        var (provider, version, hash) = CreateProvider();
        var references = await provider.GetFinancialSourceReferencesAsync(version, hash, CancellationToken.None);
        var resolution = FinancialSourceItemTitleResolver.Resolve(
            new("سود خالص", "NoavaranCurrentApi", "IncomeStatement"), references);
        var intent = CreateIntent("سود خالص", null, BusinessMeasureDimension.CurrencyAmount) with
        {
            Measures = [CreateIntent("سود خالص", null, BusinessMeasureDimension.CurrencyAmount).Measures[0] with
                { Qualifiers = new(StatementScope: FinancialStatementScope.Consolidated) }]
        };

        var result = FinancialSourcePlannerHandoff.Create(intent, resolution, version, hash);

        Assert.Equal(FinancialSourcePlannerHandoffStatus.Incomplete, result.Status);
        Assert.Equal("statement_scope_evidence_unavailable", result.DiagnosticCode);
        Assert.Null(result.Binding);
    }

    [Fact]
    public async Task DirectSourceMappingAddsCompanyDimensionAndVerifiedJoinsToPlannerRequirements()
    {
        var (provider, version, hash) = CreateProvider();
        var references = await provider.GetFinancialSourceReferencesAsync(version, hash, CancellationToken.None);
        var resolved = FinancialSourceItemTitleResolver.Resolve(
            new("سود عملیاتی هر سهم", "NoavaranCurrentApi", "IncomeStatement"), references);
        var snapshot = CreateSnapshot();
        var directSource = snapshot.DirectSources.Single(source => source.CatalogId == resolved.Reference!.CatalogId);
        var intent = CreateIntent("سود عملیاتی هر سهم", "operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare) with
        {
            Period = new(ReportingPeriodKind.Unspecified),
            EntityScope = new(EntityScopeKind.NamedCompanies, ["کگهر"])
        };

        var handoff = FinancialSourcePlannerHandoff.Create(intent, resolved, version, hash, directSource);

        Assert.Equal(FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation, handoff.Status);
        var requirements = handoff.Binding!.PlannerRequirements;
        Assert.Equal([FinancialSourcePlannerHandoff.CompanyDimensionId], requirements.DimensionIds);
        Assert.Equal(["company"], requirements.EntityKinds);
        Assert.Equal(directSource.RelationshipIds.Order(StringComparer.Ordinal), requirements.RelationshipIds);
        Assert.Equal(3, requirements.RelationshipIds.Count);
        Assert.Equal(["latest"], requirements.PeriodPolicies);
        Assert.Equal(["کگهر"], requirements.EntityReferences);
        Assert.Equal(intent.Period, handoff.Binding.PeriodIntent);

        // The real hierarchy context must now satisfy the deterministic completeness gate.
        var required = requirements.DimensionIds.Concat(requirements.RelationshipIds)
            .Concat(requirements.FinancialValueReferences!.Select(reference => reference.CatalogId)).ToArray();
        var context = await provider.GetContextAsync(new SchemaContextExpansionRequest(1, version, hash, [], 0,
            RequiredCatalogIds: required), CancellationToken.None);
        Assert.NotNull(context);
        context = context with
        {
            PeriodPolicies = context.PeriodPolicies.Concat(requirements.PeriodPolicies).Distinct().ToArray(),
            PresentationRequirements = requirements.PresentationRequirements,
            StatementScopes = requirements.StatementScopes,
            EntityReferences = requirements.EntityReferences
        };
        var completeness = new SchemaContextCompletenessValidator().Validate(requirements, context);
        Assert.True(completeness.RetrievalComplete, string.Join(", ", completeness.MissingRequirements));
        Assert.Contains("company", context.EntityKinds);
    }

    [Fact]
    public async Task DirectSourceMappingForAnotherSourceIsRejected()
    {
        var (provider, version, hash) = CreateProvider();
        var references = await provider.GetFinancialSourceReferencesAsync(version, hash, CancellationToken.None);
        var resolved = FinancialSourceItemTitleResolver.Resolve(
            new("سود عملیاتی هر سهم", "NoavaranCurrentApi", "IncomeStatement"), references);
        var other = CreateSnapshot().DirectSources.First(source => source.CatalogId != resolved.Reference!.CatalogId);
        var intent = CreateIntent("سود عملیاتی هر سهم", "operatingProfitPerShare", BusinessMeasureDimension.CurrencyPerShare);

        var handoff = FinancialSourcePlannerHandoff.Create(intent, resolved, version, hash, other);

        Assert.Equal(FinancialSourcePlannerHandoffStatus.Incompatible, handoff.Status);
        Assert.Equal("direct_source_mapping_identity_mismatch", handoff.DiagnosticCode);
    }

    private static StructuredQueryCatalogSnapshot CreateSnapshot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var root = directory!.FullName;
        return new StructuredQueryCatalogSnapshotBuilder().Build(
            Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog"),
            Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml"));
    }

    private static CanonicalSemanticIntent CreateIntent(string phrase, string? concept,
        BusinessMeasureDimension dimension)
    {
        var canonicalized = concept is not null;
        return new CanonicalSemanticIntent(2,
            [new RequestedMeasure(phrase, concept,
                canonicalized ? MeasureCanonicalizationStatus.Canonicalized : MeasureCanonicalizationStatus.Unrecognized,
                canonicalized ? [concept!] : [], MeasureConceptKind.FinancialStatementItem, dimension,
                new(), SemanticUnit.Unspecified, null, EffectiveUnitEvidence.Unresolved)],
            new(AnalyticalOperationKind.Retrieve), new(ComparisonMode.None),
            new(ReportingPeriodKind.LatestAvailable), new(EntityScopeKind.NamedCompanies, []),
            [], [], null, null, null, new(RequestedOutputKind.Scalar, []), [], new(false, [], []));
    }

    private static (JsonCatalogNavigationHierarchyProvider Provider, string Version, string Hash) CreateProvider()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var root = directory!.FullName;
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var version = hierarchy.RootElement.GetProperty("catalogVersion").GetString()!;
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        return (new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath), version, hash);
    }
}

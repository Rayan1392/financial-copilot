using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FinancialCopilot.UnitTests;

public sealed class SchemaNavigatorTests
{
    [Fact]
    public async Task ReviewedCanonicalSourceBinding_IsExactAndDoesNotGrantQueryAuthorization()
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(await File.ReadAllTextAsync(hierarchyPath));
        var catalogHash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        var catalogVersion = hierarchy.RootElement.GetProperty("catalogVersion").GetString()!;
        var directItemNode = hierarchy.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.TryGetProperty("sourceItemReferences", out var refs)
                && refs.EnumerateArray().Any(reference => reference.GetProperty("statementType").GetString() == "IncomeStatement"
                    && reference.GetProperty("sourceItemId").GetInt32() == 161))
            .GetProperty("nodeId").GetString()!;
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
        var context = await provider.GetContextAsync(new SchemaContextExpansionRequest(1, catalogVersion, catalogHash,
            [directItemNode], 0), CancellationToken.None);
        Assert.NotNull(context);

        var measure = new RequestedMeasure("سود عملیاتی هر سهم", "operatingProfitPerShare",
            MeasureCanonicalizationStatus.Canonicalized, ["operatingProfitPerShare"], MeasureConceptKind.FinancialStatementItem,
            BusinessMeasureDimension.CurrencyPerShare, new MeasureQualifiers(), SemanticUnit.Unspecified, null,
            EffectiveUnitEvidence.Unresolved);
        var binding = CanonicalSourceItemBindingResolver.Resolve(measure, context.FinancialValueReferences!,
            "NoavaranCurrentApi", "IncomeStatement");

        Assert.Equal(CanonicalSourceItemBindingStatus.Bound, binding.Status);
        Assert.Equal("canonical_source_item_bound", binding.DiagnosticCode);
        Assert.Equal(nameof(FinancialValueReferenceType.DirectSourceItem), binding.Selection!.ReferenceType);
        Assert.Equal("NoavaranCurrentApi", binding.Reference!.ProviderName);
        Assert.Equal("IncomeStatement", binding.Reference.StatementType);
        Assert.Equal(161, binding.Reference.SourceItemId);
        Assert.Equal("IRRPerShare", binding.Reference.NormalizedUnit);
        Assert.False(binding.QueryAuthorized);

        var dimensionMismatch = CanonicalSourceItemBindingResolver.Resolve(measure with
            { Dimension = BusinessMeasureDimension.CurrencyAmount }, context.FinancialValueReferences!);
        Assert.Equal(CanonicalSourceItemBindingStatus.Incompatible, dimensionMismatch.Status);
        var unreviewedConcept = CanonicalSourceItemBindingResolver.Resolve(measure with { ConceptId = "netProfit" },
            context.FinancialValueReferences!);
        Assert.Equal(CanonicalSourceItemBindingStatus.Incomplete, unreviewedConcept.Status);
        var unresolvedUnitReference = context.FinancialValueReferences!.Single() with
            { EffectiveUnitStatus = "requires-semantic-review" };
        var unresolvedUnit = CanonicalSourceItemBindingResolver.Resolve(measure, [unresolvedUnitReference]);
        Assert.Equal(CanonicalSourceItemBindingStatus.Incomplete, unresolvedUnit.Status);
        Assert.Equal("canonical_source_item_effective_unit_unresolved", unresolvedUnit.DiagnosticCode);
    }

    [Fact]
    public async Task ExactApprovedSourceTitles_ResolveIdentityWithoutCreatingAliasesOrAuthorization()
    {
        var (provider, catalogVersion, catalogHash) = CreateHierarchyProvider();
        var references = await provider.GetFinancialSourceReferencesAsync(catalogVersion, catalogHash, CancellationToken.None);
        Assert.Equal(296, references.Count);

        var cases = new[]
        {
            ("سود خالص", "IncomeStatement", 143, "MillionIRR", "MonetaryStatementItem"),
            ("سود عملیاتی هر سهم", "IncomeStatement", 161, "IRRPerShare", "CurrencyPerShare"),
            ("جمع کل دارایی‌ها", "BalanceSheet", 138, "MillionIRR", "MonetaryStatementItem"),
            ("جریان وجه نقد عملیاتی", "CashFlow", 1, "MillionIRR", "MonetaryStatementItem")
        };

        foreach (var (title, statementType, sourceItemId, unit, dimensionEvidence) in cases)
        {
            var result = FinancialSourceItemTitleResolver.Resolve(new(title, "NoavaranCurrentApi", statementType), references);
            Assert.Equal(FinancialSourceTitleResolutionStatus.Resolved, result.Status);
            Assert.Equal(statementType, result.Reference!.StatementType);
            Assert.Equal(sourceItemId, result.Reference.SourceItemId);
            Assert.NotNull(result.Reference.SourceItemCatalogId);
            Assert.Equal(FinancialSourceItemTitleResolver.NormalizeTitle(title),
                FinancialSourceItemTitleResolver.NormalizeTitle(result.Reference.TitleFa!));
            Assert.Equal(unit, result.Reference.NormalizedUnit);
            Assert.Equal(dimensionEvidence, result.DimensionEvidence);
            Assert.Equal("not-authorized", result.AuthorizationStatus);
            Assert.False(result.QueryAuthorized);
        }

        var operatingPerShare = FinancialSourceItemTitleResolver.Resolve(
            new("سود عملیاتی هر سهم", "NoavaranCurrentApi", "IncomeStatement"), references);
        Assert.Equal("operatingProfitPerShare", Assert.Single(operatingPerShare.Reference!.CanonicalConceptBindings!,
            binding => binding.Status == "approved").CanonicalConceptId);
        var netProfit = FinancialSourceItemTitleResolver.Resolve(new("سود خالص", "NoavaranCurrentApi", "IncomeStatement"), references);
        Assert.Empty(netProfit.Reference!.CanonicalConceptBindings ?? []);
    }

    [Fact]
    public async Task ExactSourceTitleResolution_IsOrthographicOnlyAndReturnsTypedScopeBlockers()
    {
        var (provider, catalogVersion, catalogHash) = CreateHierarchyProvider();
        var references = await provider.GetFinancialSourceReferencesAsync(catalogVersion, catalogHash, CancellationToken.None);
        // Arabic/Persian letter variants, ZWNJ, and repeated whitespace normalize; content does not.
        var arabicYehVariant = "جریان وجه نقد عملیاتی".Replace('\u06CC', '\u064A');
        Assert.Equal(FinancialSourceTitleResolutionStatus.Resolved,
            FinancialSourceItemTitleResolver.Resolve(new(arabicYehVariant, "NoavaranCurrentApi", "CashFlow"), references).Status);
        Assert.Equal(FinancialSourceTitleResolutionStatus.Resolved,
            FinancialSourceItemTitleResolver.Resolve(new("سود  خالص", "NoavaranCurrentApi", "IncomeStatement"), references).Status);
        Assert.Equal(FinancialSourceTitleResolutionStatus.NotFound,
            FinancialSourceItemTitleResolver.Resolve(new("سود خالص اضافی", "NoavaranCurrentApi", "IncomeStatement"), references).Status);
        Assert.Equal(FinancialSourceTitleResolutionStatus.Incompatible,
            FinancialSourceItemTitleResolver.Resolve(new("سود خالص", "NoavaranCurrentApi", "BalanceSheet"), references).Status);

        var qualifiers = new Dictionary<string, string?> { ["consolidation"] = "consolidated" };
        var qualified = FinancialSourceItemTitleResolver.Resolve(
            new("سود خالص", "NoavaranCurrentApi", "IncomeStatement", qualifiers), references);
        Assert.Equal(FinancialSourceTitleResolutionStatus.Incomplete, qualified.Status);
        Assert.Equal("statement_qualifier_evidence_unavailable", qualified.DiagnosticCode);
        Assert.Equal("consolidated", qualified.StatementQualifiers!["consolidation"]);
    }

    [Fact]
    public async Task DuplicateExactSourceTitles_ReturnAmbiguityWithoutArbitrarySelection()
    {
        var (provider, catalogVersion, catalogHash) = CreateHierarchyProvider();
        var references = await provider.GetFinancialSourceReferencesAsync(catalogVersion, catalogHash, CancellationToken.None);
        var original = references.Single(reference => reference.StatementType == "IncomeStatement" && reference.SourceItemId == 143);
        var duplicate = original with
        {
            CatalogId = "source_item_test_duplicate",
            SourceItemCatalogId = Guid.NewGuid(),
            StatementType = "CashFlow",
            SourceItemId = 999
        };

        var ambiguous = FinancialSourceItemTitleResolver.Resolve(new("سود خالص"), [original, duplicate]);
        Assert.Equal(FinancialSourceTitleResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Null(ambiguous.Reference);
        Assert.Equal(2, ambiguous.Candidates!.Count);

        var disambiguated = FinancialSourceItemTitleResolver.Resolve(
            new("سود خالص", StatementType: "IncomeStatement"), [original, duplicate]);
        Assert.Equal(FinancialSourceTitleResolutionStatus.Resolved, disambiguated.Status);
        Assert.Equal(143, disambiguated.Reference!.SourceItemId);
    }

    private static (JsonCatalogNavigationHierarchyProvider Provider, string Version, string Hash) CreateHierarchyProvider()
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var version = hierarchy.RootElement.GetProperty("catalogVersion").GetString()!;
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        return (new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath), version, hash);
    }

    [Fact]
    public async Task RequirementsAndNavigationContracts_EnumerateOnlyCurrentCatalogCandidates()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: true);
        try
        {
            var model = new FakeExecutionService(RequirementsOutput(), NavigationOutput());
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider,
                new SchemaContextCompletenessValidator());
            var request = new SchemaNavigationRequest(1, "revenue for فولاد", "v1", catalogHash,
                TenantId: Guid.NewGuid());

            var requirements = await navigator.ExtractAsync(request, CancellationToken.None);
            await navigator.NavigateAsync(request, requirements, CancellationToken.None);

            using var requirementsSchema = JsonDocument.Parse(model.Contracts[0].JsonSchema!);
            var candidateMetricIds = requirementsSchema.RootElement.GetProperty("properties").GetProperty("requirements")
                .GetProperty("properties").GetProperty("measureIds").GetProperty("items").GetProperty("enum");
            Assert.Equal(["metric_revenue"], candidateMetricIds.EnumerateArray().Select(item => item.GetString()));

            using var navigationSchema = JsonDocument.Parse(model.Contracts[1].JsonSchema!);
            var candidateNodeIds = navigationSchema.RootElement.GetProperty("properties").GetProperty("selectedNodeIds")
                .GetProperty("items").GetProperty("enum");
            Assert.Contains("domain-financial", candidateNodeIds.EnumerateArray().Select(item => item.GetString()));
            Assert.DoesNotContain("metric_revenue", candidateNodeIds.EnumerateArray().Select(item => item.GetString()));
        }
        finally { Directory.Delete(catalogPath, recursive: true); }
    }

    [Fact]
    public async Task RequirementsExtraction_RejectsInventedCatalogIdsWithFieldAndValueDiagnostic()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: true);
        try
        {
            var invalidOutput = RequirementsOutput().Replace(
                "\"measureIds\":[\"metric_revenue\"]", "\"measureIds\":[\"metric_invented\"]", StringComparison.Ordinal);
            var model = new FakeExecutionService(invalidOutput);
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider,
                new SchemaContextCompletenessValidator());
            var request = new SchemaNavigationRequest(1, "revenue", "v1", catalogHash, TenantId: Guid.NewGuid());

            var exception = await Assert.ThrowsAsync<SchemaNavigationOutputException>(() =>
                navigator.ExtractAsync(request, CancellationToken.None));

            Assert.Equal("requirements_contain_unknown_catalog_id", exception.Code);
            Assert.Contains("field=measureIds", exception.DiagnosticDetail, StringComparison.Ordinal);
            Assert.Contains("metric_invented", exception.DiagnosticDetail, StringComparison.Ordinal);
        }
        finally { Directory.Delete(catalogPath, recursive: true); }
    }

    [Fact]
    public async Task RequirementsExtraction_EnforcesTypedPerCallTimeoutWhenProviderIgnoresCancellation()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: true);
        try
        {
            var model = new NonCooperativeExecutionService();
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider,
                new SchemaContextCompletenessValidator(), new SchemaNavigatorBudgets(
                    PerCallTimeout: TimeSpan.FromMilliseconds(30), TotalTimeout: TimeSpan.FromSeconds(2)));
            var request = new SchemaNavigationRequest(1, "revenue", "v1", catalogHash, TenantId: Guid.NewGuid());

            var exception = await Assert.ThrowsAsync<SchemaNavigationTimeoutException>(() =>
                navigator.ExtractAsync(request, CancellationToken.None));

            Assert.Equal("provider_call_timeout", exception.Code);
            Assert.Equal("schema_requirements_v1", exception.Stage);
            Assert.Equal("per_call_deadline", exception.Reason);
        }
        finally { Directory.Delete(catalogPath, recursive: true); }
    }

    [Fact]
    public void OperationRegistry_NormalizesOnlyTypedDirectRetrievalAndDeclaresCompilerBoundaries()
    {
        var direct = new QuestionSchemaRequirements(1, "v1", "hash", [], [], [], [], [], [], [], [])
        {
            FinancialValueReferences = [new(nameof(FinancialValueReferenceType.MonthlyMeasure), "metric_monthly_sales")],
            RequestedFinancialValueOperation = nameof(FinancialValueOperation.UnsupportedDerivedFinancialMetric),
            StructuredShape = new([], [], [], [], [])
        };
        var comparisonIntent = Feature141OperationRegistry.NormalizeRequirementOperation(direct, "مبلغ فروش کگل چقدر است؟");
        var derivedIntent = Feature141OperationRegistry.NormalizeRequirementOperation(direct, "رشد فروش کگل نسبت به ماه قبل چند درصد بود؟");

        Assert.Equal(nameof(FinancialValueOperation.DirectValueRetrieval), comparisonIntent.Operation);
        Assert.Equal("normalized_unsupported_label_to_cataloged_direct_retrieval_from_typed_shape", comparisonIntent.Diagnostic);
        Assert.Equal(nameof(FinancialValueOperation.UnsupportedDerivedFinancialMetric), derivedIntent.Operation);
        Assert.Contains(Feature141OperationRegistry.Definitions, item => item.Id == "YearOverYear" && item.CompilerSupported);
        Assert.Contains(Feature141OperationRegistry.Definitions, item => item.Id == "ConditionalCompanyFiltering" && item.CompilerSupported);
        Assert.Equal(1, Feature141OperationRegistry.Version);
    }

    [Fact]
    public async Task RequirementsExtraction_NormalizesExplicitMonthlyGrowthWithScopedComparison()
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var catalogHash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
        var requirementsJson = JsonNode.Parse(RequirementsOutput())!;
        var requirementsNode = requirementsJson["requirements"]!;
        requirementsNode["measureIds"] = new JsonArray("metric_monthly_production_quantity");
        requirementsNode["dimensionIds"] = new JsonArray("dim_company", "dim_monthly_period");
        requirementsNode["entityKinds"] = new JsonArray("company", "product");
        requirementsNode["periodPolicies"] = new JsonArray("latest");
        requirementsNode["comparisonScopes"] = new JsonArray();
        requirementsNode["presentationRequirements"] = new JsonArray("scalar");
        requirementsNode["entityReferences"] = new JsonArray("کگل");
        requirementsNode["productReferences"] = new JsonArray("کنسانتره");
        requirementsNode["financialValueReferences"] = new JsonArray(
            new JsonObject { ["referenceType"] = "GovernedMetric", ["catalogId"] = "metric_monthly_production_quantity" });
        requirementsNode["financialValueOperation"] = "UnsupportedDerivedFinancialMetric";
        requirementsNode["structuredShape"]!["financialOperationKind"] = "DirectValue";
        requirementsNode["structuredShape"]!["comparisonMode"] = "None";
        requirementsNode["structuredShape"]!["maximumPeriods"] = 2;
        var model = new FakeExecutionService(requirementsJson.ToJsonString(), NavigationOutput());
        var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider,
            new SchemaContextCompletenessValidator());
        var request = new SchemaNavigationRequest(1,
            "تولید کنسانتره کگل نسبت به ماه قبل چند درصد رشد کرده؟", "1", catalogHash,
            TenantId: Guid.NewGuid(), CorrelationId: "monthly-growth-normalization-test");

        var requirements = await navigator.ExtractAsync(request, CancellationToken.None);

        Assert.Equal(nameof(FinancialValueOperation.ApprovedPeriodComparison), requirements.RequestedFinancialValueOperation);
        Assert.Equal(PlanFinancialOperationKind.GrowthPercentage, requirements.StructuredShape!.FinancialOperationKind);
        Assert.Equal(PlanComparisonMode.MonthOverMonth, requirements.StructuredShape.ComparisonMode);
        Assert.Contains("normalized_financial_reference_types_from_exact_catalog_semantics", requirements.NormalizationDiagnostics);
        Assert.Contains("inferred_month_over_month_scope_from_explicit_prior_month_language", requirements.NormalizationDiagnostics);
        Assert.Contains("normalized_explicit_monthly_growth_and_comparison_scope_to_growth_percentage",
            requirements.NormalizationDiagnostics);
    }

    [Fact]
    public async Task RequirementsExtraction_KeepsUncomparedMonthlySeriesDistinctFromMonthOverMonth()
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var catalogHash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
        var requirementsJson = JsonNode.Parse(RequirementsOutput())!;
        var requirementsNode = requirementsJson["requirements"]!;
        requirementsNode["measureIds"] = new JsonArray("metric_monthly_sales");
        requirementsNode["dimensionIds"] = new JsonArray("dim_company", "dim_monthly_period");
        requirementsNode["entityKinds"] = new JsonArray("company");
        requirementsNode["periodPolicies"] = new JsonArray("latest");
        requirementsNode["comparisonScopes"] = new JsonArray("period-over-period");
        requirementsNode["presentationRequirements"] = new JsonArray("time-series");
        requirementsNode["entityReferences"] = new JsonArray("کگل");
        requirementsNode["financialValueReferences"] = new JsonArray(
            new JsonObject { ["referenceType"] = "MonthlyMeasure", ["catalogId"] = "metric_monthly_sales" });
        requirementsNode["financialValueOperation"] = "UnsupportedDerivedFinancialMetric";
        requirementsNode["aggregationRequirements"] = new JsonArray("aggregate");
        requirementsNode["structuredShape"]!["aggregations"] = new JsonArray(
            new JsonObject { ["catalogId"] = "metric_monthly_sales", ["operation"] = "Sum" });
        requirementsNode["structuredShape"]!["financialOperationKind"] = "TimeSeries";
        requirementsNode["structuredShape"]!["comparisonMode"] = "MonthOverMonth";
        requirementsNode["structuredShape"]!["maximumPeriods"] = 6;
        var model = new FakeExecutionService(requirementsJson.ToJsonString(), NavigationOutput());
        var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider,
            new SchemaContextCompletenessValidator());
        var request = new SchemaNavigationRequest(1,
            "فروش ریالی کگل در شش ماه گذشته چه روندی داشته؟", "1", catalogHash,
            TenantId: Guid.NewGuid(), CorrelationId: "monthly-timeseries-normalization-test");

        var requirements = await navigator.ExtractAsync(request, CancellationToken.None);

        Assert.Equal(nameof(FinancialValueOperation.DirectValueRetrieval), requirements.RequestedFinancialValueOperation);
        Assert.Equal(PlanFinancialOperationKind.TimeSeries, requirements.StructuredShape!.FinancialOperationKind);
        Assert.Equal(PlanComparisonMode.None, requirements.StructuredShape.ComparisonMode);
        Assert.Empty(requirements.ComparisonScopes);
        Assert.Contains("normalized_uncompared_monthly_series_to_time_series_without_comparison_scope",
            requirements.NormalizationDiagnostics);
    }

    [Fact]
    public void FinancialValuePolicy_RejectsLocalPrimaryMetricDerivationAndNegativeBaselineGrowth()
    {
        Assert.False(FinancialValueOperationPolicy.ValidateOperation(
            nameof(FinancialValueOperation.UnsupportedDerivedFinancialMetric)).Allowed);
        Assert.Equal("negative_baseline_growth_convention_unapproved",
            FinancialValueOperationPolicy.ValidatePeriodComparison(new(true, true, true, true, true, true, true)).Code);
        Assert.False(FinancialValueOperationPolicy.ValidatePeriodComparison(new(true, true, true, true, false, true, false)).Allowed);
        Assert.True(FinancialValueOperationPolicy.ValidatePeriodComparison(new(true, true, true, true, true, true, false)).Allowed);
    }

    [Fact]
    public void CompletenessValidator_AcceptsExactDirectSourceIdentityWithoutMetricCodeWhenApproved()
    {
        var reference = new FinancialValueCatalogReference("source_item_verified", Guid.NewGuid(), "NoavaranCurrentApi",
            "IncomeStatement", 161, "verified title", null, "N/A", null, null, "verified", "approved", true);
        var requirements = new QuestionSchemaRequirements(1, "v1", "catalog-hash", [], [], [], [], [], [], [], [])
        {
            FinancialValueReferences = [new(nameof(FinancialValueReferenceType.DirectSourceItem), reference.CatalogId)]
        };
        var context = new SchemaContext(1, "v1", "catalog-hash", [], [], [], [], [], [], [], [], [], [], [],
            FinancialValueReferences: [reference]);

        Assert.Equal(SchemaContextCompletenessStatus.Complete,
            new SchemaContextCompletenessValidator().Validate(requirements, context).Status);
    }

    [Fact]
    public void CompletenessValidator_RejectsDirectSourceItemUntilSemanticAndQueryApproval()
    {
        var reference = new FinancialValueCatalogReference("source_item_pending", Guid.NewGuid(), "NoavaranCurrentApi",
            "IncomeStatement", 161, "verified title", null, "N/A", null, null, "verified", "pending-review", false);
        var requirements = new QuestionSchemaRequirements(1, "v1", "catalog-hash", [], [], [], [], [], [], [], [])
        {
            FinancialValueReferences = [new(nameof(FinancialValueReferenceType.DirectSourceItem), reference.CatalogId)]
        };
        var context = new SchemaContext(1, "v1", "catalog-hash", [], [], [], [], [], [], [], [], [], [], [],
            FinancialValueReferences: [reference]);

        var result = new SchemaContextCompletenessValidator().Validate(requirements, context);
        Assert.Equal(SchemaContextCompletenessStatus.Unsupported, result.Status);
        Assert.Contains("not approved", result.Diagnostics[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Navigator_UsesIndependentRequirements_AndPreservesGovernanceAndUsage()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: false);
        try
        {
            var model = new FakeExecutionService(RequirementsOutput(), NavigationOutput());
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var validator = new SchemaContextCompletenessValidator();
            var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider, validator);
            var request = new SchemaNavigationRequest(1, "درآمد فولاد را به صورت جدول مقایسه کن", "v1", catalogHash,
                TenantId: Guid.NewGuid(), CorrelationId: "navigation-test");

            var requirements = await navigator.ExtractAsync(request, CancellationToken.None);
            var result = await navigator.NavigateAsync(request, requirements, CancellationToken.None);

            Assert.Equal(["metric_revenue"], requirements.MeasureIds);
            Assert.Equal(["metric_revenue"], result.SelectedMeasureIds);
            Assert.Equal(["metric_revenue"], result.RequiredMeasureIds);
            Assert.Equal(["metric_revenue"], result.CandidateMeasureIds);
            Assert.Equal(["فولاد"], requirements.EntityReferences);
            Assert.Equal(["standalone"], requirements.StatementScopes);
            Assert.Contains("domain-financial", result.SelectedNodeIds);
            Assert.Equal(2, result.ModelCalls);
            Assert.Equal(12, result.InputTokens);
            Assert.Equal(4, result.OutputTokens);
            Assert.Equal(SchemaContextCompletenessStatus.Unsupported, result.CompletenessStatus);
            Assert.Contains(result.Diagnostics, item => item.StartsWith("navigation_metadata_pending:", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, item => item.StartsWith("query_not_authorized:", StringComparison.Ordinal));
            Assert.Equal(2, model.CallCount); // One independent extraction call plus one navigation call.
        }
        finally
        {
            Directory.Delete(catalogPath, recursive: true);
        }
    }

    [Fact]
    public async Task Navigator_AcceptsTypedPlanShapeProjectionBoundToASelectedMetric()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: false);
        try
        {
            var requirementsOutput = RequirementsOutput().Replace(
                "\"projectionCatalogIds\":[]", "\"projectionCatalogIds\":[\"metric_revenue\"]", StringComparison.Ordinal);
            var model = new FakeExecutionService(requirementsOutput, NavigationOutput());
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider,
                new SchemaContextCompletenessValidator());
            var request = new SchemaNavigationRequest(1, "revenue table", "v1", catalogHash,
                TenantId: Guid.NewGuid(), CorrelationId: "navigation-metric-projection-test");

            var requirements = await navigator.ExtractAsync(request, CancellationToken.None);
            var result = await navigator.NavigateAsync(request, requirements, CancellationToken.None);

            Assert.Equal(["metric_revenue"], requirements.StructuredShape!.ProjectionCatalogIds);
            Assert.Contains("domain-financial", result.SelectedNodeIds);
        }
        finally
        {
            Directory.Delete(catalogPath, recursive: true);
        }
    }

    [Fact]
    public async Task Navigator_RejectsModelOutputThatAddsSql()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: true);
        try
        {
            var model = new FakeExecutionService(RequirementsOutput(), NavigationOutput().Replace(
                "\"outcome\":\"selected\"", "\"outcome\":\"selected\",\"sql\":\"select 1\"", StringComparison.Ordinal));
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider,
                new SchemaContextCompletenessValidator());
            var request = new SchemaNavigationRequest(1, "revenue for فولاد", "v1", catalogHash, TenantId: Guid.NewGuid());
            var requirements = await navigator.ExtractAsync(request, CancellationToken.None);

            var exception = await Assert.ThrowsAsync<SchemaNavigationOutputException>(() =>
                navigator.NavigateAsync(request, requirements, CancellationToken.None));
            Assert.Equal("output_contains_unrecognized_property", exception.Code);
        }
        finally
        {
            Directory.Delete(catalogPath, recursive: true);
        }
    }

    [Fact]
    public async Task Navigator_RetriesTruncatedOutputOnceWithinTheBoundedAdaptiveBudget()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: true);
        try
        {
            var model = new TruncationRetryExecutionService();
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider,
                new SchemaContextCompletenessValidator(), new SchemaNavigatorBudgets(
                    MaximumModelCalls: 4, MaximumNavigationCalls: 3, InitialOutputTokens: 700,
                    MaximumOutputTokens: 1200, MaximumTotalOutputTokens: 3000, MaximumRetryAttempts: 1));
            var request = new SchemaNavigationRequest(1, "revenue for company", "v1", catalogHash,
                TenantId: Guid.NewGuid(), CorrelationId: "navigation-truncation-retry-test");
            var requirements = await navigator.ExtractAsync(request, CancellationToken.None);

            var result = await navigator.NavigateAsync(request, requirements, CancellationToken.None);

            Assert.Equal(3, model.CallCount);
            Assert.Equal(3, result.ModelCalls);
            Assert.Equal(1, result.RetryAttempts);
            Assert.Contains(result.Diagnostics, item => item.Contains("truncated_retrying_once", StringComparison.Ordinal));
            Assert.Contains("domain-financial", result.SelectedNodeIds);
            Assert.Equal(1200, model.OutputCaps[2]);
        }
        finally { Directory.Delete(catalogPath, recursive: true); }
    }

    [Fact]
    public async Task HierarchyProvider_RejectsChangedCanonicalSource()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: false);
        try
        {
            File.WriteAllText(Path.Combine(catalogPath, "catalog-scope.yaml"), "{\"changed\":true}");
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var exception = await Assert.ThrowsAsync<SchemaNavigationOutputException>(() =>
                provider.GetCompactOutlineAsync("v1", catalogHash, "revenue", 10000, CancellationToken.None));
            Assert.Equal("catalog_source_hash_mismatch", exception.Code);
        }
        finally { Directory.Delete(catalogPath, recursive: true); }
    }

    [Fact]
    public async Task HierarchyProvider_RejectsArtifactEditWithoutHashUpdate()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: false);
        try
        {
            var contents = File.ReadAllText(hierarchyPath).Replace("Financial", "Tampered", StringComparison.Ordinal);
            File.WriteAllText(hierarchyPath, contents);
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var exception = await Assert.ThrowsAsync<SchemaNavigationOutputException>(() =>
                provider.GetCompactOutlineAsync("v1", catalogHash, "revenue", 10000, CancellationToken.None));
            Assert.Equal("catalog_hierarchy_hash_mismatch", exception.Code);
        }
        finally { Directory.Delete(catalogPath, recursive: true); }
    }

    [Fact]
    public async Task HierarchyProvider_RejectsRehashedArtifactThatDisagreesWithManifestLock()
    {
        var (hierarchyPath, catalogPath, catalogHash) = CreateCatalogFixture(navigationApproved: false);
        try
        {
            var hierarchy = JsonNode.Parse(File.ReadAllText(hierarchyPath))!.AsObject();
            hierarchy["nodes"]![0]!["title"]!["en"] = "Rehashed modification";
            hierarchy.Remove("hierarchyHash");
            using var edited = JsonDocument.Parse(hierarchy.ToJsonString());
            hierarchy["hierarchyHash"] = CanonicalSemanticCatalogIntegrityValidator.ComputeHierarchyHash(edited.RootElement);
            File.WriteAllText(hierarchyPath, hierarchy.ToJsonString());
            var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
            var exception = await Assert.ThrowsAsync<SchemaNavigationOutputException>(() =>
                provider.GetCompactOutlineAsync("v1", catalogHash, "revenue", 10000, CancellationToken.None));
            Assert.Equal("catalog_hierarchy_lock_mismatch", exception.Code);
        }
        finally { Directory.Delete(catalogPath, recursive: true); }
    }

    [Fact]
    public async Task HierarchyProvider_BoundsOutlineAndRanksTheExactPersianSourceItemAheadOfSimilarTitles()
    {
        var root = FindRepositoryRoot();
        var evaluation = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "evaluation", "navigation-golden-v1.yaml");
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        using var dataset = JsonDocument.Parse(File.ReadAllText(evaluation));
        var question = dataset.RootElement.GetProperty("cases").EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == "fa-value-01")
            .GetProperty("question").GetString()!;
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);

        var outline = await provider.GetCompactOutlineAsync("1", hash, question, 16_000, CancellationToken.None);
        var candidates = new List<(int Id, string? Title, double Score)>();
        foreach (var line in outline.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var node = JsonDocument.Parse(line);
            foreach (var candidate in node.RootElement.GetProperty("sourceItemCandidates").EnumerateArray())
                candidates.Add((candidate.GetProperty("sourceItemId").GetInt32(),
                    candidate.GetProperty("titleFa").GetString(), candidate.GetProperty("matchScore").GetDouble()));
        }
        var target = Assert.Single(candidates, item => item.Id == 161);
        var similarOperatingProfit = Assert.Single(candidates, item => item.Id == 140);

        Assert.True(outline.Length <= 16_000);
        Assert.Contains("سود عملیاتی هر سهم", target.Title, StringComparison.Ordinal);
        Assert.True(target.Score > similarOperatingProfit.Score);
        Assert.InRange(candidates.Count, 1, 12);

        var exception = await Assert.ThrowsAsync<SchemaNavigationBudgetException>(() =>
            provider.GetCompactOutlineAsync("1", hash, question, 1, CancellationToken.None));
        Assert.Equal("catalog_outline_budget_exceeded", exception.Code);
        Assert.True(exception.Observed > exception.Limit);
    }

    [Fact]
    public async Task HierarchyProvider_RequiredCatalogAncestorCannotCauseNonTerminatingBudgetTrim()
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);

        var operation = provider.GetCompactOutlineAsync("1", hash,
            "سود خالص فولاد چقدر است؟",
            ["metric_net_profit", "dim_company"], 1, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SchemaNavigationBudgetException>(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("catalog_outline_budget_exceeded", exception.Code);
    }

    [Fact]
    public async Task HierarchyProvider_BuildsSmallMonthlyOutlineWithCanonicalMeasureUnitsAndNoStatementSourceItems()
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
        var outline = await provider.GetCompactOutlineAsync("1", hash,
            "Ù…Ø¨Ù„Øº ÙØ±ÙˆØ´ Ú©Ú¯Ù„ Ø¯Ø± Ø´Ù‡Ø±ÛŒÙˆØ± Ú†Ù‚Ø¯Ø± Ø¨ÙˆØ¯Ù‡ØŸ",
            ["metric_monthly_sales", "dim_company", "dim_monthly_period", "rel_31e410fb3ae11956"],
            18_000, CancellationToken.None);

        var monthlyMeasures = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var line in outline.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            var node = document.RootElement;
            Assert.Empty(node.GetProperty("sourceItemCandidates").EnumerateArray());
            if (node.TryGetProperty("metricSemantics", out var semantics))
                foreach (var semantic in semantics.EnumerateArray())
                    monthlyMeasures[semantic.GetProperty("catalogId").GetString()!] = semantic.Clone();
        }

        Assert.True(outline.Length < 8_000, $"Monthly outline was {outline.Length} characters.");
        Assert.Contains("metric_monthly_sales", monthlyMeasures.Keys);
        Assert.Contains("metric_monthly_sales_quantity", monthlyMeasures.Keys);
        Assert.Equal("MonthlyMeasure", monthlyMeasures["metric_monthly_sales"].GetProperty("referenceType").GetString());
        Assert.Equal("MonthlyMeasure", monthlyMeasures["metric_monthly_sales_quantity"].GetProperty("referenceType").GetString());
        Assert.Equal("GovernedMetric", monthlyMeasures["metric_monthly_sales_growth_mom"].GetProperty("referenceType").GetString());
        Assert.Equal("amount", monthlyMeasures["metric_monthly_sales"].GetProperty("unitCode").GetString());
        Assert.Equal("quantity", monthlyMeasures["metric_monthly_sales_quantity"].GetProperty("unitCode").GetString());
        Assert.Contains("monetary sales value", monthlyMeasures["metric_monthly_sales"].GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HierarchyProvider_PrioritizesRequiredReferencesAcrossTruncatedBranches()
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        var reference = hierarchy.RootElement.GetProperty("nodes").EnumerateArray()
            .Where(node => node.TryGetProperty("sourceItemReferences", out _))
            .SelectMany(node => node.GetProperty("sourceItemReferences").EnumerateArray())
            .Single(item => item.GetProperty("sourceItemId").GetInt32() == 161
                && item.GetProperty("providerName").GetString() == "NoavaranCurrentApi"
                && item.GetProperty("statementType").GetString() == "IncomeStatement");
        var catalogId = reference.GetProperty("catalogId").GetString()!;
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);

        var context = await provider.GetContextAsync(new SchemaContextExpansionRequest(1,
            hierarchy.RootElement.GetProperty("catalogVersion").GetString()!, hash,
            ["source-items-incomestatement"], Depth: 1, MaximumFanOut: 12, RequiredCatalogIds: [catalogId]),
            CancellationToken.None);

        Assert.NotNull(context);
        Assert.Contains(context.FinancialValueReferences!, item => item.CatalogId == catalogId && item.SourceItemId == 161);
        Assert.Single(context.FinancialValueReferences!);
        Assert.Contains(context.Diagnostics, item => item.StartsWith("context_expansion_fanout_limited:", StringComparison.Ordinal));

        var statementItems = hierarchy.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.GetProperty("nodeId").GetString() == "source-items-incomestatement");
        var allCandidates = statementItems.GetProperty("childNodeIds").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        var bounded = await provider.GetContextAsync(new SchemaContextExpansionRequest(1,
            hierarchy.RootElement.GetProperty("catalogVersion").GetString()!, hash, allCandidates,
            Depth: 0, MaximumFanOut: 12, RequiredCatalogIds: [catalogId]), CancellationToken.None);

        Assert.NotNull(bounded);
        Assert.True(bounded.NodeIds.Count <= 48);
        Assert.Contains(bounded.FinancialValueReferences!, item => item.CatalogId == catalogId && item.SourceItemId == 161);
        Assert.Contains(bounded.Diagnostics, item => item.Contains("node_budget_limited:initial_selection", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(48, 48)]
    [InlineData(49, 48)]
    [InlineData(60, 48)]
    public async Task HierarchyProvider_EnforcesFinalNodeLimitAt48(int requestedCount, int expectedCount)
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        var nodeIds = hierarchy.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(node => node.GetProperty("nodeId").GetString()!).Take(requestedCount).ToArray();
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);

        var context = await provider.GetContextAsync(new SchemaContextExpansionRequest(1,
            hierarchy.RootElement.GetProperty("catalogVersion").GetString()!, hash, nodeIds,
            Depth: 0, RequiredCatalogIds: []), CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal(expectedCount, context.NodeIds.Count);
        Assert.Equal(context.NodeIds.Count, context.NodeIds.Distinct(StringComparer.Ordinal).Count());
        if (requestedCount > 48)
            Assert.Contains(context.Diagnostics, item => item.Contains("node_budget_limited:initial_selection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Navigator_ResolvesAnIndependentlyValidatedDirectSourceReferenceDeterministically()
    {
        var root = FindRepositoryRoot();
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        using var hierarchy = JsonDocument.Parse(File.ReadAllText(hierarchyPath));
        var hash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        using var dataset = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "evaluation", "navigation-golden-v1.yaml")));
        var question = dataset.RootElement.GetProperty("cases").EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == "fa-value-01").GetProperty("question").GetString()!;
        var provider = new JsonCatalogNavigationHierarchyProvider(hierarchyPath, catalogPath);
        var model = new FakeExecutionService(NavigationOutputEmpty());
        var navigator = new LlmVectorlessSchemaNavigator(model, provider, provider, provider, new SchemaContextCompletenessValidator());
        var request = new SchemaNavigationRequest(1, question, "1", hash, TenantId: Guid.NewGuid());
        var requirements = new QuestionSchemaRequirements(1, "1", hash, [], [], [], [], [], ["latest"], [], [])
        {
            FinancialValueReferences = [new(nameof(FinancialValueReferenceType.DirectSourceItem), "source_item_682beceadddc1458")]
        };

        var result = await navigator.NavigateAsync(request, requirements, CancellationToken.None);

        Assert.Contains("direct-source-item-source_item_682beceadddc1458", result.SelectedNodeIds);
        Assert.NotEmpty(result.SelectedFinancialValueReferences!);
    }

    private static string Hierarchy(bool navigationApproved) => $$"""
        {
          "catalogVersion":"v1",
          "catalogHash":"catalog-hash",
          "nodes":[{
            "nodeId":"domain-financial",
            "parentNodeId":"financial-semantic-catalog",
            "title":{"en":"Financial"},
            "description":"Financial metrics",
            "conceptsAndSynonyms":["company financial metrics"],
            "childNodeIds":[],
            "catalogReferences":{"tableIds":[],"metricIds":["metric_revenue"],"dimensionIds":["dim_company"],"relationshipIds":[]},
            "providerRestrictions":[],"unitAndPeriodSemantics":[],
            "navigationMetadataApproval":{"approved":{{navigationApproved.ToString().ToLowerInvariant()}}},
            "semanticApproval":{"approved":true},
            "queryAuthorization":{"queryable":false,"reason":"not authorized"}
          }]
        }
        """;

    private static (string HierarchyPath, string CatalogPath, string CatalogHash) CreateCatalogFixture(bool navigationApproved)
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"schema-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(catalogPath);
        foreach (var name in new[] { "catalog.yaml", "catalog-scope.yaml", "manifest.yaml", "navigation.yaml", "source-item-snapshot.json" })
        {
            var content = name switch
            {
                "manifest.yaml" => "{\"formatVersion\":\"v1\"}",
                "catalog.yaml" => "{\"manifest\":\"manifest.yaml\",\"navigation\":\"navigation.yaml\",\"tables\":[],\"relationships\":[],\"metrics\":[],\"dimensions\":[],\"dictionaries\":[],\"sources\":[],\"policies\":[]}",
                "source-item-snapshot.json" => "{\"formatVersion\":1,\"items\":[]}",
                _ => "{}"
            };
            File.WriteAllText(Path.Combine(catalogPath, name), content);
        }
        var catalogHash = CanonicalSemanticCatalogIntegrityValidator.ComputeSourceHash(catalogPath);
        var hierarchy = JsonNode.Parse(Hierarchy(navigationApproved))!.AsObject();
        hierarchy["catalogHash"] = catalogHash;
        using var beforeHash = JsonDocument.Parse(hierarchy.ToJsonString());
        var hierarchyHash = CanonicalSemanticCatalogIntegrityValidator.ComputeHierarchyHash(beforeHash.RootElement);
        hierarchy["hierarchyHash"] = hierarchyHash;
        File.WriteAllText(Path.Combine(catalogPath, "manifest.yaml"), JsonSerializer.Serialize(new
        {
            formatVersion = "v1",
            navigationHierarchyHash = hierarchyHash
        }));
        var hierarchyPath = Path.Combine(catalogPath, "hierarchy.json");
        File.WriteAllText(hierarchyPath, hierarchy.ToJsonString());
        return (hierarchyPath, catalogPath, catalogHash);
    }

    private static string RequirementsOutput() => """
        {"schemaVersion":1,"requirements":{
          "measureIds":["metric_revenue"],"dimensionIds":["dim_company"],"entityKinds":["company"],"relationshipIds":[],
          "sourcePolicies":[],"periodPolicies":[],"comparisonScopes":[],"presentationRequirements":["table"],
          "statementScopes":["standalone"],"aggregationRequirements":[],"orderingRequirements":[],"entityReferences":["فولاد"],"productReferences":[],
          "financialValueReferences":[],"financialValueOperation":"DirectValueRetrieval",
          "structuredShape":{"projectionCatalogIds":[],"groupByCatalogIds":[],"filters":[],"aggregations":[],"sort":[],"offset":0,"limit":100,"financialOperationKind":"DirectValue","comparisonMode":"None","reportingPeriodStart":null,"reportingPeriodEnd":null,"maximumPeriods":null},
          "unknownOrAmbiguous":false,"uncertaintyReason":null}}
        """;

    private static string NavigationOutput() => """
        {"schemaVersion":1,"outcome":"selected","selectedNodeIds":["domain-financial"],
         "missingRequirements":[],"expansionNodeIds":[]}
        """;

    private static string NavigationOutputEmpty() => """
        {"schemaVersion":1,"outcome":"selected","selectedNodeIds":[],
         "missingRequirements":[],"expansionNodeIds":[]}
        """;

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(Directory.GetCurrentDirectory()); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, "specs", "141-schema-aware-semantic-query-engine")))
                return current.FullName;
        throw new DirectoryNotFoundException("Feature 141 repository root was not found.");
    }

    private sealed class FakeExecutionService(params string[] outputs) : IAiModelExecutionService
    {
        public int CallCount { get; private set; }
        public List<AiStructuredOutputContract> Contracts { get; } = [];

        public Task<AiModelResult> ExecuteAsync(AiModelSelectionRequest selection, AiModelRequest request,
            CancellationToken cancellationToken)
        {
            if (request.StructuredOutput is { } contract) Contracts.Add(contract);
            var output = outputs[CallCount++];
            var usage = new AiExecutionUsageFacts(selection.CorrelationId, "fake", "fake-model", AiExecutionStatus.Completed,
                TimeSpan.FromMilliseconds(10), 1, InputTokens: 6, OutputTokens: 2, ProviderReportedCost: 0.01m,
                ProviderReportedCurrency: "USD");
            return Task.FromResult(new AiModelResult(null, output, [], usage));
        }
    }

    private sealed class NonCooperativeExecutionService : IAiModelExecutionService
    {
        public Task<AiModelResult> ExecuteAsync(AiModelSelectionRequest selection, AiModelRequest request,
            CancellationToken cancellationToken) => new TaskCompletionSource<AiModelResult>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }


    private sealed class TruncationRetryExecutionService : IAiModelExecutionService
    {
        public int CallCount { get; private set; }
        public List<int?> OutputCaps { get; } = [];

        public Task<AiModelResult> ExecuteAsync(AiModelSelectionRequest selection, AiModelRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            OutputCaps.Add(request.MaxOutputTokens);
            if (CallCount == 1)
                return Task.FromResult(new AiModelResult(null, RequirementsOutput(), [], new AiExecutionUsageFacts(
                    selection.CorrelationId, "fake", "fake-model", AiExecutionStatus.Completed, TimeSpan.FromMilliseconds(2), 1,
                    InputTokens: 10, OutputTokens: 5), ResponseId: "requirements-response"));
            if (CallCount == 2)
                throw new AiModelProviderException(AiExecutionStatus.InvalidStructuredOutput, "ModelOutputTruncated",
                    "truncated", providerResponseId: "truncated-response", providerReportedInputTokens: 20,
                    providerReportedOutputTokens: 700, providerCompletionReason: "max_output_tokens");
            return Task.FromResult(new AiModelResult(null, NavigationOutput(), [], new AiExecutionUsageFacts(
                selection.CorrelationId, "fake", "fake-model", AiExecutionStatus.Completed, TimeSpan.FromMilliseconds(2), 1,
                InputTokens: 20, OutputTokens: 20), ResponseId: "navigation-response"));
        }
    }
}


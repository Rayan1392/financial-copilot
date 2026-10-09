using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class StructuredQueryPlanningTests
{
    private static readonly Guid SourceItemCatalogId = Guid.Parse("f2a6e460-176f-4426-a4e7-78d4ffd86ba6");

    [Fact]
    public void Builder_preserves_direct_source_identity_and_separates_comparison_from_metric()
    {
        var (request, requirements, navigation, context, completeness) = Scenario();
        var plan = new StructuredQueryPlanBuilder().Build(request, requirements, navigation, context, completeness);
        var validation = new StructuredQueryPlanValidator().Validate(request, requirements, plan, context, completeness);

        Assert.Equal(FinancialValueOperation.ApprovedPeriodComparison, plan.FinancialOperation.Operation);
        Assert.Equal(PlanComparisonMode.YearOverYear, plan.FinancialOperation.ComparisonMode);
        Assert.Equal(PlanFinancialOperationKind.GrowthPercentage, plan.FinancialOperation.Kind);
        Assert.Equal("metric_operating_profit_growth_yoy", Assert.Single(plan.FinancialOperation.ValueDefinitionCatalogIds));
        var source = Assert.Single(plan.Sources);
        Assert.Equal(FinancialValueReferenceType.DirectSourceItem, source.ReferenceType);
        Assert.Equal("source_item_9c617846fc7c5c13", source.CatalogId);
        Assert.Equal(SourceItemCatalogId, source.SourceItemCatalogId);
        Assert.Equal("NoavaranCurrentApi", source.ProviderName);
        Assert.Equal("IncomeStatement", source.StatementType);
        Assert.Equal(140, source.SourceItemId);
        Assert.Equal("N/A", source.Unit);
        Assert.Equal("unknown", source.UnitStatus);
        Assert.False(plan.Statement.PreferComposing);
        Assert.True(plan.Statement.PreservePeriodType);
        Assert.True(plan.Statement.PreserveAuditState);
        Assert.True(plan.Statement.PreserveRestatementState);
        Assert.True(plan.Statement.PreserveRevisionIdentity);
        Assert.Contains(PlanStatementSelectionKey.ReportingPeriodDescending, plan.Statement.SelectionKeys);
        Assert.Contains(PlanStatementSelectionKey.Availability, plan.Statement.SelectionKeys);
        Assert.True(validation.IsStructurallyValid, string.Join(",", validation.Diagnostics));
        Assert.True(validation.RetrievalComplete);
        Assert.False(validation.SemanticReviewApproved);
        Assert.False(validation.QueryAuthorized);
        Assert.False(validation.ExecutionEligible);
    }

    [Fact]
    public void Validator_rejects_tenant_source_catalog_and_unbounded_scope_tampering()
    {
        var (request, requirements, navigation, context, completeness) = Scenario();
        var builder = new StructuredQueryPlanBuilder();
        var plan = builder.Build(request, requirements, navigation, context, completeness);
        var validator = new StructuredQueryPlanValidator();

        var forged = plan with
        {
            TenantId = Guid.NewGuid(),
            Pagination = new PlanPagination(0, int.MaxValue),
            Projection = [new PlanProjection("arbitrary.database.column", PlanProjectionRole.Value)],
            Sources = [plan.Sources[0] with { SourceItemCatalogId = Guid.NewGuid() }]
        };

        var result = validator.Validate(request, requirements, forged, context, completeness);
        Assert.False(result.IsStructurallyValid);
        Assert.Contains("plan_tenant_scope_mismatch", result.Diagnostics);
        Assert.Contains("plan_pagination_unbounded_or_invalid", result.Diagnostics);
        Assert.Contains("plan_contains_unresolved_projection", result.Diagnostics);
        Assert.Contains("direct_source_identity_catalog_mismatch:source_item_9c617846fc7c5c13", result.Diagnostics);
        Assert.False(result.ExecutionEligible);
    }

    [Fact]
    public void Period_comparison_policy_rejects_negative_baseline_without_inventing_a_convention()
    {
        var result = FinancialValueOperationPolicy.ValidatePeriodComparison(new FinancialPeriodComparisonFacts(
            SameCompany: true, SameStatementScope: true, SameValueDefinition: true, SameProvider: true,
            SameUnit: true, ComparablePeriods: true, HasNegativeBaseline: true));

        Assert.False(result.Allowed);
        Assert.Equal("negative_baseline_growth_convention_unapproved", result.Code);
    }

    [Fact]
    public async Task Offline_service_composes_independent_extraction_navigation_and_plan_validation()
    {
        var (request, requirements, navigation, context, completeness) = Scenario();
        var navigator = new FakeNavigator(navigation);
        var service = new OfflineStructuredQueryPlanningService(
            new FakeRequirementsExtractor(requirements), navigator, new FakeContextProvider(context),
            new FakeCompletenessValidator(completeness), new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(),
            new FakeCompanyBinder());

        var result = await service.PlanAsync(request, CancellationToken.None);

        Assert.Same(requirements, navigator.ReceivedRequirements);
        Assert.True(result.Validation.IsStructurallyValid);
        Assert.Equal(FinancialValueOperation.ApprovedPeriodComparison, result.Plan.FinancialOperation.Operation);
        Assert.False(result.Plan.ExecutionEligible);
        Assert.False(result.Validation.QueryAuthorized);
    }

    [Fact]
    public async Task Offline_service_binds_one_company_to_canonical_internal_id_before_plan_validation()
    {
        var (request, baseRequirements, navigation, context, completeness) = Scenario();
        var requirements = baseRequirements with { EntityReferences = ["کگل"] };
        var companyId = Guid.Parse("d6ea49d8-98e6-4ea4-868b-81c7b8e2c53a");
        var binder = new FakeCompanyBinder(new StructuredCompanyBinding(StructuredCompanyBindingStatus.Found,
            companyId, [companyId], "exact_ticker", null));
        var service = new OfflineStructuredQueryPlanningService(
            new FakeRequirementsExtractor(requirements), new FakeNavigator(navigation), new FakeContextProvider(context),
            new FakeCompletenessValidator(completeness), new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(), binder);

        var result = await service.PlanAsync(request, CancellationToken.None);

        Assert.Equal(companyId, Assert.Single(result.Plan.Entities).ResolvedEntityId);
        Assert.Equal(StructuredCompanyBindingStatus.Found, result.CompanyBinding!.Status);
        Assert.Equal("کگل", binder.LastMention);
        Assert.Equal(request.TenantId, binder.LastTenantId);
    }

    [Fact]
    public async Task Monthly_plan_binds_company_and_product_through_existing_scoped_resolvers()
    {
        var (baseRequest, _, baseNavigation, baseContext, _) = Scenario();
        var companyId = Guid.Parse("d6ea49d8-98e6-4ea4-868b-81c7b8e2c53a");
        var relationId = "rel_31e410fb3ae11956";
        const string measureId = "metric_monthly_production_quantity";
        var request = baseRequest with { Question = "گندله تولید کگل" };
        var shape = new StructuredPlanShape(["dim_company", "dim_product_identity", "dim_monthly_period"], [], [], [], [], 0, 20,
            PlanFinancialOperationKind.DirectValue);
        var requirements = new QuestionSchemaRequirements(1, request.CatalogVersion, request.CatalogHash,
            [measureId], ["dim_company", "dim_product_identity"], ["company"], [relationId], [], ["latest"], [],
            ["scalar"], EntityReferences: ["کگهر"],
            FinancialValueReferences: [new(nameof(FinancialValueReferenceType.MonthlyMeasure), measureId)],
            RequestedFinancialValueOperation: nameof(FinancialValueOperation.DirectValueRetrieval),
            StructuredShape: shape)
        {
            ProductReferences = ["گندله"]
        };
        var context = baseContext with
        {
            MeasureIds = [measureId], DimensionIds = ["dim_company", "dim_product_identity"],
            RelationshipIds = [relationId], PeriodPolicies = ["latest"],
            FinancialValueReferences = []
        };
        var navigation = baseNavigation with
        {
            SelectedMeasureIds = context.MeasureIds, SelectedDimensionIds = context.DimensionIds,
            SelectedRelationshipIds = context.RelationshipIds, SelectedPeriodPolicies = context.PeriodPolicies,
            SelectedEntityKinds = ["company"], SelectedEntityReferences = ["کگهر"]
        };
        var completeness = new SchemaContextCompletenessResult(SchemaContextCompletenessStatus.Complete, [], [],
            RetrievalComplete: true, SemanticReviewApproved: false, QueryAuthorized: false);
        var productKey = "PRODUCT:COMPANY:PELLET";
        var productBinder = new FakeProductBinder(new StructuredProductBinding(StructuredProductBindingStatus.Found,
            companyId, productKey, "گندله", "ton", "feature-129-company-scoped-product-row", [], null));
        var service = new OfflineStructuredQueryPlanningService(
            new FakeRequirementsExtractor(requirements), new FakeNavigator(navigation), new FakeContextProvider(context),
            new FakeCompletenessValidator(completeness), new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(),
            new FakeCompanyBinder(new StructuredCompanyBinding(StructuredCompanyBindingStatus.Found, companyId,
                [companyId], "canonical-company", null)), productBinder);

        var planned = await service.PlanAsync(request, CancellationToken.None);

        Assert.True(planned.Validation.IsStructurallyValid, string.Join(",", planned.Validation.Diagnostics));
        Assert.Equal(companyId, Assert.Single(planned.Plan.Entities, x => x.EntityKind == "Company").ResolvedEntityId);
        Assert.Equal([measureId], planned.Plan.Projection.Select(x => x.CatalogId));
        var product = Assert.Single(planned.Plan.Entities, x => x.EntityKind == "Product");
        Assert.Equal(productKey, product.ResolvedEntityKey);
        Assert.True(product.IdentityVerified);
        Assert.Equal(productKey, planned.ProductBinding!.ProductKey);
        Assert.False(planned.Plan.QueryAuthorized);
        Assert.False(planned.Plan.ExecutionEligible);
    }

    [Fact]
    public async Task Monthly_product_population_plan_has_no_company_binding_and_discloses_observation_window()
    {
        var (baseRequest, _, baseNavigation, baseContext, _) = Scenario();
        const string measureId = "metric_monthly_production_quantity";
        const string relationshipId = "rel_31e410fb3ae11956";
        var request = baseRequest with { Question = "کدام شرکت‌ها گندله تولید می‌کنند؟" };
        var requirements = new QuestionSchemaRequirements(1, request.CatalogVersion, request.CatalogHash,
            [], ["dim_company", "dim_product_identity", "dim_monthly_period"], ["company", "product"],
            [], [], [], [], ["table"],
            FinancialValueReferences: [],
            RequestedFinancialValueOperation: nameof(FinancialValueOperation.DirectValueRetrieval),
            StructuredShape: new StructuredPlanShape([], [], [], [], [], 0, 20, PlanFinancialOperationKind.DirectValue))
        {
            ProductReferences = ["گندله"]
        };
        var context = baseContext with
        {
            MeasureIds = [measureId],
            DimensionIds = ["dim_company", "dim_product_identity", "dim_monthly_period"],
            RelationshipIds = [relationshipId],
            PeriodPolicies = [],
            PresentationRequirements = ["table"],
            FinancialValueReferences = []
        };
        var navigation = baseNavigation with
        {
            SelectedMeasureIds = [],
            SelectedDimensionIds = context.DimensionIds,
            SelectedRelationshipIds = [],
            SelectedPeriodPolicies = context.PeriodPolicies,
            SelectedPresentationRequirements = context.PresentationRequirements,
            SelectedEntityKinds = ["company", "product"],
            SelectedEntityReferences = []
        };
        var completeness = new SchemaContextCompletenessResult(SchemaContextCompletenessStatus.Complete, [], [],
            RetrievalComplete: true, SemanticReviewApproved: false, QueryAuthorized: false);
        var conceptResolver = new ReviewedProductConceptResolver([
            new ReviewedProductConcept("product_iron_ore_pellets", "product-catalog-v1", "گندله سنگ‌آهن",
                ["گندله", "گندله سنگ‌آهن"], "approved", "owner-confirmed:feature141-product-equivalence:2026-10-09")
        ]);
        var scopeResolver = new AuthorizedCompanyDiscoveryScopeResolver([
            new AuthorizedCompanyDiscoveryScope(request.TenantId, "tenant-test-scope", "tenant-approval", false)
        ]);
        var service = new OfflineStructuredQueryPlanningService(new FakeRequirementsExtractor(requirements),
            new FakeNavigator(navigation), new FakeContextProvider(context), new FakeCompletenessValidator(completeness),
            new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(), new FakeCompanyBinder(),
            productConceptResolver: conceptResolver, companyDiscoveryScopeResolver: scopeResolver);

        var result = await service.PlanAsync(request, CancellationToken.None);

        Assert.True(result.Validation.IsStructurallyValid, string.Join("|", result.Validation.Diagnostics));
        Assert.DoesNotContain(result.Plan.Entities, entity => entity.EntityKind == "Company");
        Assert.Equal("product_iron_ore_pellets", result.Plan.ProductConcept!.CatalogId);
        Assert.Equal("observed-production-trailing-12-months-v1", result.Plan.PopulationCoveragePolicyId);
        Assert.False(result.Plan.CompanyDiscoveryScope!.CompanyCoverageComplete);
        Assert.True(result.Plan.Period.ReportingPeriodStart < result.Plan.Period.ReportingPeriodEnd);
        Assert.Contains("population_discovery_uses_trailing_12_calendar_month_observation_window", result.Plan.Diagnostics);
    }

    [Fact]
    public void Planner_composes_catalog_backed_filters_grouping_aggregation_sort_and_bounded_page()
    {
        var (request, baseRequirements, navigation, context, completeness) = Scenario();
        var shape = new StructuredPlanShape(
            ["metric_operating_profit"], ["dim_company"],
            [new PlanFilter("metric_operating_profit", PlanFilterOperator.GreaterThan,
                new PlanLiteral(PlanLiteralType.Decimal, "100.5"))],
            [new PlanAggregation("metric_operating_profit", PlanAggregationOperation.Sum)],
            [new PlanSort("metric_operating_profit", PlanSortDirection.Descending)], 0, 25,
            PlanFinancialOperationKind.DirectValue);
        var requirements = baseRequirements with
        {
            RequestedFinancialValueOperation = nameof(FinancialValueOperation.DirectValueRetrieval),
            FinancialValueReferences = [],
            StructuredShape = shape
        };

        var plan = new StructuredQueryPlanBuilder().Build(request, requirements, navigation, context, completeness);
        var validation = new StructuredQueryPlanValidator().Validate(request, requirements, plan, context, completeness);

        Assert.Equal(25, plan.Pagination.Limit);
        Assert.Equal(PlanProjectionRole.Grouping, plan.Projection.Single(x => x.CatalogId == "dim_company").Role);
        Assert.Equal(PlanFilterOperator.GreaterThan, Assert.Single(plan.Filters).Operator);
        Assert.Equal(PlanAggregationOperation.Sum, Assert.Single(plan.Aggregations).Operation);
        Assert.Equal(PlanSortDirection.Descending, Assert.Single(plan.Sort).Direction);
        Assert.True(validation.IsStructurallyValid, string.Join(",", validation.Diagnostics));
    }

    [Fact]
    public void Consolidated_scope_is_explicit_and_standalone_keeps_composing_disabled_by_default()
    {
        var (request, baseRequirements, navigation, context, completeness) = Scenario();
        Assert.False(new StructuredQueryPlanBuilder().Build(request, baseRequirements, navigation, context, completeness)
            .Statement.PreferComposing);

        var requirements = baseRequirements with { StatementScopes = ["consolidated"] };
        var plan = new StructuredQueryPlanBuilder().Build(request, requirements, navigation, context, completeness);
        var result = new StructuredQueryPlanValidator().Validate(request, requirements, plan, context, completeness);

        Assert.True(plan.Statement.PreferComposing);
        Assert.True(result.IsStructurallyValid);
    }

    [Theory]
    [InlineData(PlanFinancialOperationKind.AbsoluteChange)]
    [InlineData(PlanFinancialOperationKind.TimeSeries)]
    public void Approved_period_comparison_supports_typed_non_formula_operations(PlanFinancialOperationKind kind)
    {
        var (request, baseRequirements, navigation, context, completeness) = Scenario();
        var requirements = baseRequirements with
        {
            StructuredShape = baseRequirements.StructuredShape! with { FinancialOperationKind = kind }
        };
        var plan = new StructuredQueryPlanBuilder().Build(request, requirements, navigation, context, completeness);
        var result = new StructuredQueryPlanValidator().Validate(request, requirements, plan, context, completeness);

        Assert.Equal(kind, plan.FinancialOperation.Kind);
        Assert.False(plan.FinancialOperation.FormulaAuthorized);
        Assert.True(result.IsStructurallyValid, string.Join(",", result.Diagnostics));
        Assert.False(result.ExecutionEligible);
    }

    [Fact]
    public void Time_series_plan_uses_inherent_chronological_order_instead_of_model_sort()
    {
        var (request, baseRequirements, navigation, context, completeness) = Scenario();
        var requirements = baseRequirements with
        {
            PresentationRequirements = ["time-series"],
            StructuredShape = baseRequirements.StructuredShape! with
            {
                Sort = [new PlanSort("metric_operating_profit", PlanSortDirection.Descending)]
            }
        };

        var plan = new StructuredQueryPlanBuilder().Build(request, requirements, navigation, context, completeness);
        var result = new StructuredQueryPlanValidator().Validate(request, requirements, plan, context, completeness);

        Assert.Empty(plan.Sort);
        Assert.True(result.IsStructurallyValid, string.Join(",", result.Diagnostics));
    }

    [Fact]
    public void Unsupported_derived_financial_metric_cannot_become_an_executable_plan()
    {
        var (request, baseRequirements, navigation, context, completeness) = Scenario();
        var requirements = baseRequirements with
        {
            RequestedFinancialValueOperation = nameof(FinancialValueOperation.UnsupportedDerivedFinancialMetric),
            StructuredShape = baseRequirements.StructuredShape! with { FinancialOperationKind = PlanFinancialOperationKind.UnsupportedDerived }
        };
        var plan = new StructuredQueryPlanBuilder().Build(request, requirements, navigation, context, completeness);
        var result = new StructuredQueryPlanValidator().Validate(request, requirements, plan, context, completeness);

        Assert.False(result.IsStructurallyValid);
        Assert.Contains("unsupported_derived_financial_metric", result.Diagnostics);
        Assert.False(result.ExecutionEligible);
        Assert.Null(typeof(StructuredQueryPlan).GetProperty("Sql"));
        Assert.Null(typeof(StructuredQueryPlan).GetProperty("Expression"));
    }

    private static (SchemaNavigationRequest Request, QuestionSchemaRequirements Requirements,
        SchemaNavigationResult Navigation, SchemaContext Context, SchemaContextCompletenessResult Completeness) Scenario()
    {
        var tenantId = Guid.Parse("22345678-1234-1234-1234-123456789abc");
        const string catalogVersion = "test-v1";
        const string catalogHash = "catalog-sha256";
        const string sourceId = "source_item_9c617846fc7c5c13";
        var request = new SchemaNavigationRequest(1, "operating profit year over year", catalogVersion, catalogHash, TenantId: tenantId);
        var requirements = new QuestionSchemaRequirements(1, catalogVersion, catalogHash,
            ["metric_operating_profit_growth_yoy"], ["dim_company"], ["company"], [], [], ["latest"], ["year-over-year"], [],
            FinancialValueReferences: [new(nameof(FinancialValueReferenceType.DirectSourceItem), sourceId)],
            RequestedFinancialValueOperation: nameof(FinancialValueOperation.ApprovedPeriodComparison),
            StructuredShape: new StructuredPlanShape([], [], [], [], [], 0, 100, PlanFinancialOperationKind.GrowthPercentage,
                ComparisonMode: PlanComparisonMode.YearOverYear));
        var identity = new FinancialValueCatalogReference(sourceId, SourceItemCatalogId, "NoavaranCurrentApi", "IncomeStatement", 140,
            "ÙØ³ÙˆØ¯ Ø¹Ù…Ù„ÛŒØ§ØªÛŒ", null, "N/A", null, "OPERATING_PROFIT", "verified-persisted-source-key", "pending-review", false, "unknown");
        var context = new SchemaContext(1, catalogVersion, catalogHash,
            ["metric_operating_profit_growth_yoy", "metric_operating_profit", "dim_company"], [],
            ["metric_operating_profit_growth_yoy", "metric_operating_profit"], ["dim_company"], ["company"], [], [], ["latest"],
            ["year-over-year"], [], [], FinancialValueReferences: [identity]);
        var completeness = new SchemaContextCompletenessResult(SchemaContextCompletenessStatus.Unsupported, [],
            ["financial value is pending review"], RetrievalComplete: true, SemanticReviewApproved: false, QueryAuthorized: false);
        var navigation = new SchemaNavigationResult(1, catalogVersion, catalogHash, [], context.NodeIds,
            context.MeasureIds, context.DimensionIds, context.EntityKinds, context.RelationshipIds, context.SourcePolicies,
            context.PeriodPolicies, [], [], context.ComparisonScopes, context.PresentationRequirements);
        return (request, requirements, navigation, context, completeness);
    }

    private sealed class FakeRequirementsExtractor(QuestionSchemaRequirements requirements) : IQuestionSchemaRequirementsExtractor
    {
        public Task<QuestionSchemaRequirements> ExtractAsync(SchemaNavigationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(requirements);
    }

    private sealed class FakeNavigator(SchemaNavigationResult navigation) : IVectorlessSchemaNavigator
    {
        public QuestionSchemaRequirements? ReceivedRequirements { get; private set; }
        public Task<SchemaNavigationResult> NavigateQuestionAsync(SchemaNavigationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(navigation);
        public Task<SchemaNavigationResult> NavigateAsync(SchemaNavigationRequest request,
            QuestionSchemaRequirements independentlyExtractedRequirements, CancellationToken cancellationToken)
        {
            ReceivedRequirements = independentlyExtractedRequirements;
            return Task.FromResult(navigation);
        }
    }

    private sealed class FakeContextProvider(SchemaContext context) : ICatalogNavigationHierarchyProvider
    {
        public Task<SchemaContext?> GetContextAsync(SchemaContextExpansionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<SchemaContext?>(context);
    }

    private sealed class FakeCompletenessValidator(SchemaContextCompletenessResult result) : ISchemaContextCompletenessValidator
    {
        public SchemaContextCompletenessResult Validate(QuestionSchemaRequirements requirements, SchemaContext context) => result;
    }

    private sealed class FakeCompanyBinder(StructuredCompanyBinding? result = null) : IStructuredQueryCompanyBinder
    {
        public string? LastMention { get; private set; }
        public Guid LastTenantId { get; private set; }
        public Task<StructuredCompanyBinding> ResolveAsync(string? mention, Guid tenantId, CancellationToken cancellationToken)
        {
            LastMention = mention;
            LastTenantId = tenantId;
            return Task.FromResult(result ?? new StructuredCompanyBinding(StructuredCompanyBindingStatus.Missing, null, [], null,
                "company_reference_missing"));
        }
    }

    private sealed class FakeProductBinder(StructuredProductBinding result) : IStructuredQueryProductBinder
    {
        public Task<StructuredProductBinding> ResolveAsync(string? companyMention, string? productMention,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}


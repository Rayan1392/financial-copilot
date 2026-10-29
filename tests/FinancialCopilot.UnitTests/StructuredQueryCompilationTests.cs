using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure.Financial.Semantics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class StructuredQueryCompilationTests
{
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
    private static readonly Guid ItemId = Guid.Parse("3cafc7d3-b0ab-4341-85ef-ebcbc9d60528");

    [Fact]
    public void RuntimeCatalogSnapshotBuilder_UsesCanonicalSourceAndLogicalRelationshipWithoutGrantingAccess()
    {
        var root = FindRepositoryRoot();
        var catalog = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var hierarchy = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");

        var snapshot = new StructuredQueryCatalogSnapshotBuilder().Build(catalog, hierarchy);

        Assert.True(snapshot.IntegrityValidated);
        var source = Assert.Single(snapshot.DirectSources, item => item.SourceItemId == 161
            && item.ProviderName == "NoavaranCurrentApi" && item.StatementType == "IncomeStatement");
        Assert.True(source.IdentityVerified);
        Assert.False(source.Queryable);
        Assert.Null(source.MetricCode);
        Assert.Equal("N/A", source.SourceUnit);
        Assert.Equal("unknown", source.SourceUnitStatus);
        Assert.Equal("IRRPerShare", source.EffectiveUnit);
        Assert.Equal("owner-approved", source.EffectiveUnitStatus);
        Assert.StartsWith("business-owner-confirmed:", source.EffectiveUnitProvenance);
        Assert.True(source.SemanticApproved);
        Assert.Contains(source.RelationshipIds, id => id == "rel_2588cf95d01a651a");
        var statementLink = Assert.Single(snapshot.Relationships, item => item.CatalogId == "rel_2588cf95d01a651a");
        Assert.True(statementLink.Verified);
        Assert.False(statementLink.PhysicalConstraintPresent);
        Assert.False(statementLink.SemanticApproved);
        Assert.False(statementLink.QueryAuthorized);
        Assert.False(statementLink.Queryable);
    }

    [Fact]
    public void RuntimeCatalogSnapshotBuilder_LoadsMonthlyMeasureMappingsWithoutAuthorizingThem()
    {
        var root = FindRepositoryRoot();
        var catalog = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var hierarchy = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");

        var snapshot = new StructuredQueryCatalogSnapshotBuilder().Build(catalog, hierarchy);

        Assert.Equal(4, snapshot.MonthlyMeasures.Count);
        // Owner decision 2026-10-29 (evaluation/governance/metric_monthly_production_quantity-decision-request-2026-10-28.json):
        // only the monthly production quantity is approved and queryable; the sales measures stay pending.
        Assert.Equal(["metric_monthly_production_quantity"],
            snapshot.MonthlyMeasures.Where(measure => measure.Queryable || measure.SemanticApproved).Select(measure => measure.CatalogId));
        Assert.All(snapshot.MonthlyMeasures, measure =>
        {
            Assert.Equal("NoavaranCurrentApi", measure.ProviderName);
            Assert.Equal("ProductSales", measure.ReportType);
            Assert.Equal(0, measure.OutputType);
            Assert.Equal("Accepted", measure.RevisionStatus);
        });
        Assert.Equal("MillionIRR", Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales").UnitCode);
        var rate = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales_rate");
        Assert.False(rate.UnitVerified);
        Assert.False(rate.RecomputeAllowed);
    }

    [Fact]
    public void MonthlyCompiler_CompilesProductScopedLatestSingleMonthWithCatalogAndRevisionParameters()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_production_quantity");
        var plan = MonthlyPlan(snapshot, mapping, latest: true, productCode: "PRODUCT:COMPANY:PELLET");

        var result = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(result.Success, string.Join(",", result.Diagnostics));
        var query = Assert.IsType<CompiledReadOnlyQuery>(result.Query);
        Assert.Contains("JOIN \"public\".\"MonthlyReports\"", query.Sql);
        Assert.Contains("WITH latest_month", query.Sql);
        Assert.Contains("\"IsAccepted\" = @is_accepted", query.Sql);
        Assert.Contains("\"RevisionStatus\" = @revision_status", query.Sql);
        Assert.Contains("\"ReportType\" = @report_type", query.Sql);
        Assert.Contains("\"OutputType\" = @output_type", query.Sql);
        Assert.DoesNotContain("PRODUCT:COMPANY:PELLET", query.Sql);
        Assert.Contains(query.Parameters, x => x.Name == "product_code" && Equals(x.Value, "PRODUCT:COMPANY:PELLET"));
        Assert.Contains(query.Parameters, x => x.Name == "output_type" && Equals(x.Value, 0));
        Assert.False(query.QueryAuthorized);
        Assert.False(query.SemanticReviewApproved);
    }

    [Fact]
    public void MonthlyCompiler_CompilesBoundedChronologicalTimeSeriesWithoutFormula()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales");
        var plan = MonthlyPlan(snapshot, mapping, latest: false, productCode: "PRODUCT:COMPANY:PELLET") with
        {
            Period = new PlanPeriodSelection([], [], false, false, new DateOnly(2026, 3, 21), new DateOnly(2026, 9, 21), 6),
            Presentation = PlanPresentation.TimeSeries,
            Sort = [new PlanSort(mapping.PeriodEndColumnCatalogId, PlanSortDirection.Ascending)]
        };

        var result = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(result.Success, string.Join(",", result.Diagnostics));
        Assert.Contains("WITH bounded_months", result.Query!.Sql);
        Assert.Contains("ORDER BY \"CompanyId\", \"PeriodEnd\" ASC", result.Query.Sql);
        Assert.Contains(result.Query.Parameters, x => x.Name == "maximum_periods" && Equals(x.Value, 6));
        Assert.Contains(result.Query.Parameters, x => x.Name == "period_start" && Equals(x.Value, new DateOnly(2026, 3, 21)));
        Assert.Contains("\"SalesAmount\" AS \"Value\"", result.Query.Sql);
        Assert.Equal("MillionIRR", result.Query.EffectiveUnit);
    }

    [Fact]
    public void MonthlyCompiler_CompilesLatestBoundedChronologicalTimeSeries()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales");
        var plan = MonthlyPlan(snapshot, mapping, latest: true, productCode: "PRODUCT:COMPANY:PELLET") with
        {
            Period = new PlanPeriodSelection(["latest"], [], true, false, MaximumPeriods: 6),
            Presentation = PlanPresentation.TimeSeries
        };

        var result = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(result.Success, string.Join(",", result.Diagnostics));
        Assert.Contains("WITH latest_bounded_months", result.Query!.Sql);
        Assert.Contains("ORDER BY \"CompanyId\", \"PeriodEnd\" ASC", result.Query.Sql);
        Assert.Contains(result.Query.Parameters, x => x.Name == "maximum_periods" && Equals(x.Value, 6));
        Assert.DoesNotContain(result.Query.Parameters, x => x.Name == "period_start");
    }

    [Fact]
    public void MonthlyCompiler_CompilesApprovedMonthOverMonthGrowthWithMissingAndDenominatorGuards()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales");
        var plan = MonthlyPlan(snapshot, mapping, latest: false, productCode: "PRODUCT:COMPANY:PELLET") with
        {
            Period = new PlanPeriodSelection(["explicit-period"], ["period-over-period"], false, false,
                new DateOnly(2026, 3, 21), new DateOnly(2026, 6, 21), 4),
            Sort = [new PlanSort(mapping.PeriodEndColumnCatalogId, PlanSortDirection.Ascending)],
            Presentation = PlanPresentation.TimeSeries,
            FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.MonthOverMonth, [], FormulaAuthorized: true)
        };

        var result = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(result.Success, string.Join(",", result.Diagnostics));
        Assert.Contains("LAG(\"Value\", @comparison_month_offset) OVER", result.Query!.Sql);
        Assert.Contains("'missing-comparison-period'", result.Query.Sql);
        Assert.Contains("'zero-denominator'", result.Query.Sql);
        Assert.Contains("'negative-baseline-unsupported'", result.Query.Sql);
        Assert.Contains(result.Query.Parameters, x => x.Name == "comparison_month_offset" && Equals(x.Value, 1));
    }

    [Fact]
    public void MonthlyCompiler_RequiresExactYoYReportingWindowAlignment()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales");
        var plan = MonthlyPlan(snapshot, mapping, latest: true, productCode: "PRODUCT:COMPANY:PELLET") with
        {
            Period = new PlanPeriodSelection(["latest"], ["year-over-year"], true, false, MaximumPeriods: 13),
            FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.YearOverYear, [], FormulaAuthorized: true)
        };

        var result = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(result.Success, string.Join("|", result.Diagnostics));
        Assert.Contains("LAG(\"PeriodStart\", @comparison_month_offset)", result.Query!.Sql);
        Assert.Contains("PreviousPeriodStart\" + INTERVAL '1 year'", result.Query.Sql);
        Assert.Contains("PreviousPeriodEnd\" + INTERVAL '1 year'", result.Query.Sql);
        Assert.Contains("period-window-mismatch", result.Query.Sql);
        Assert.Contains("PeriodEnd\" - \"PeriodStart\") <> (\"PreviousPeriodEnd\" - \"PreviousPeriodStart\")", result.Query.Sql);
    }

    [Fact]
    public void MonthlyCompiler_CompilesBoundedSalesAggregationProductRankingAndMultiCompanyAmounts()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales");
        var single = MonthlyPlan(snapshot, mapping, latest: true, productCode: null) with
        {
            Entities = [new PlanEntityReference("Company", "company", Guid.Parse("4843ec8e-d35a-4f87-98bb-152702dd6f4f"))],
            Aggregations = [new PlanAggregation(mapping.CatalogId, PlanAggregationOperation.Sum)],
            Presentation = PlanPresentation.RankedTable,
            Sort = [new PlanSort(mapping.CatalogId, PlanSortDirection.Descending)],
            Pagination = new PlanPagination(0, 5)
        };
        var ranked = new StructuredQueryCompiler().Compile(single, snapshot);
        Assert.True(ranked.Success, string.Join(",", ranked.Diagnostics));
        Assert.Contains("SUM(\"Value\")", ranked.Query!.Sql);
        Assert.Contains("ORDER BY \"Value\" DESC", ranked.Query.Sql);
        Assert.Contains("LIMIT @row_limit", ranked.Query.Sql);

        var rankedGrowth = single with
        {
            Period = new PlanPeriodSelection(["latest"], ["period-over-period"], true, false, MaximumPeriods: 3),
            FinancialOperation = new PlanFinancialOperation(FinancialValueOperation.ApprovedPeriodComparison,
                PlanFinancialOperationKind.GrowthPercentage, PlanComparisonMode.MonthOverMonth, [], FormulaAuthorized: true)
        };
        var rankedGrowthResult = new StructuredQueryCompiler().Compile(rankedGrowth, snapshot);
        Assert.True(rankedGrowthResult.Success, string.Join(",", rankedGrowthResult.Diagnostics));
        Assert.Contains("LatestProductPeriodRank", rankedGrowthResult.Query!.Sql);
        Assert.Contains("GrowthPercentage\" DESC, \"ProductCode\" ASC", rankedGrowthResult.Query.Sql);

        var company2 = Guid.Parse("a91aa2bd-1d52-4ef9-9cb1-a59ab31f70e2");
        var multiCompany = single with
        {
            Entities = [single.Entities[0], new PlanEntityReference("Company", "second company", company2)],
            Aggregations = [new PlanAggregation(mapping.CatalogId, PlanAggregationOperation.Sum)],
            Presentation = PlanPresentation.Table,
            Sort = [],
            Period = new PlanPeriodSelection(["explicit-period"], [], false, false,
                new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 20))
        };
        var compared = new StructuredQueryCompiler().Compile(multiCompany, snapshot);
        Assert.True(compared.Success, string.Join(",", compared.Diagnostics));
        Assert.Contains("= ANY(@company_ids)", compared.Query!.Sql);
        Assert.Equal(new[] { single.Entities[0].ResolvedEntityId!.Value, company2 }, compared.Query.CompanyIds);
        Assert.Contains(compared.Query.Parameters, x => x.Name == "company_ids" && x.Value is Guid[] ids && ids.Length == 2);
    }

    [Fact]
    public void MonthlyCompiler_CompilesUnboundPopulationDiscoveryAndFailsClosedWithoutTenantScope()
    {
        var original = RuntimeSnapshot();
        const string lineItemsTableId = "tbl_8ecd0432a4292998";
        const string reportsTableId = "tbl_8e759d70dada6bd8";
        const string companyTableId = "tbl_8646d83cdb350f92";
        var snapshot = new StructuredQueryCatalogSnapshot(original.CatalogVersion, original.CatalogHash, true,
            original.Tables.Select(table => table.CatalogId is lineItemsTableId or reportsTableId or companyTableId
                ? table with { Queryable = true, SemanticApproved = true } : table).ToArray(),
            original.Columns.Select(column => column.TableCatalogId is lineItemsTableId or reportsTableId or companyTableId
                ? column with { Queryable = true } : column).ToArray(),
            original.Relationships.Select(relationship => relationship.CatalogId is "rel_31e410fb3ae11956" or "rel_4e54d40aee8b45e4"
                ? relationship with { Queryable = true, SemanticApproved = true, QueryAuthorized = true } : relationship).ToArray(),
            original.DirectSources,
            original.MonthlyMeasures.Select(measure => measure with
                { Queryable = true, SemanticApproved = true, UnitVerified = true }).ToArray());
        var measure = Assert.Single(snapshot.MonthlyMeasures, item => item.CatalogId == "metric_monthly_production_quantity");
        var product = new ProductConceptResolution(ProductConceptResolutionStatus.Resolved,
            "product_iron_ore_pellets", "product-catalog-v1", "گندله سنگ‌آهن",
            ["گندله", "گندله سنگ‌آهن"], "owner-confirmed:feature141-product-equivalence:2026-10-09",
            "approved-aliases-only", null);
        var plan = new StructuredQueryPlan(1, snapshot.CatalogVersion, snapshot.CatalogHash, TenantId,
            [new PlanEntityReference("Product", "گندله", ResolvedEntityKey: product.CatalogId,
                IdentityProvenance: product.ApprovalReference, IdentityVerified: true)],
            [new PlanSourceReference(FinancialValueReferenceType.MonthlyMeasure, measure.CatalogId, null, null, null, null,
                measure.CatalogId, measure.UnitCode, measure.UnitKind, "catalog-mapped", "approved", true)],
            measure.RelationshipIds, [new PlanProjection(measure.CatalogId, PlanProjectionRole.Value)], [],
            new PlanPeriodSelection(["explicit-period"], [], false, false,
                new DateOnly(2026, 8, 23), new DateOnly(2026, 9, 22), 1),
            new PlanStatementSelection([], [], [], [], false, true, true, true, true, true), [], [],
            new PlanPagination(0, 20), PlanPresentation.Table,
            new PlanFinancialOperation(FinancialValueOperation.DirectValueRetrieval, PlanFinancialOperationKind.DirectValue,
                PlanComparisonMode.None, [], false), true, true, true, true, [], ProductConcept: product,
            CompanyDiscoveryScope: new AuthorizedCompanyDiscoveryScope(TenantId, "tenant-test-scope", "tenant-approval", true),
            PopulationCoveragePolicyId: "explicit-reporting-period-v1");

        var compiled = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(compiled.Success, string.Join("|", compiled.Diagnostics));
        Assert.Empty(compiled.Query!.CompanyIds);
        Assert.Contains("JOIN \"public\".\"Companies\"", compiled.Query.Sql, StringComparison.Ordinal);
        Assert.Contains("c.\"Id\" = ANY(@company_ids)", compiled.Query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("r.\"Id\" = ANY(@company_ids)", compiled.Query.Sql, StringComparison.Ordinal);
        Assert.Contains(compiled.Query.Parameters, parameter => parameter.Name == "product_aliases"
            && parameter.Value is string[] aliases && aliases.SequenceEqual(product.ApprovedAliases));

        var withoutScope = new StructuredQueryCompiler().Compile(plan with { CompanyDiscoveryScope = null }, snapshot);
        Assert.Contains("company_discovery_scope_not_authorized", withoutScope.Diagnostics);
    }

    [Fact]
    public void MonthlyCompiler_ComposesProducerPopulationWithTotalCompanySalesAndLatestCommonMonth()
    {
        var original = RuntimeSnapshot();
        const string lineItemsTableId = "tbl_8ecd0432a4292998";
        const string reportsTableId = "tbl_8e759d70dada6bd8";
        const string companyTableId = "tbl_8646d83cdb350f92";
        var snapshot = new StructuredQueryCatalogSnapshot(original.CatalogVersion, original.CatalogHash, true,
            original.Tables.Select(table => table.CatalogId is lineItemsTableId or reportsTableId or companyTableId
                ? table with { Queryable = true, SemanticApproved = true } : table).ToArray(),
            original.Columns.Select(column => column.TableCatalogId is lineItemsTableId or reportsTableId or companyTableId
                ? column with { Queryable = true } : column).ToArray(),
            original.Relationships.Select(relationship => relationship.CatalogId is "rel_31e410fb3ae11956" or "rel_4e54d40aee8b45e4"
                ? relationship with { Queryable = true, SemanticApproved = true, QueryAuthorized = true } : relationship).ToArray(),
            original.DirectSources,
            original.MonthlyMeasures.Select(measure => measure with
                { Queryable = true, SemanticApproved = true, UnitVerified = true }).ToArray());
        var sales = Assert.Single(snapshot.MonthlyMeasures, item => item.CatalogId == "metric_monthly_sales");
        var concept = new ProductConceptResolution(ProductConceptResolutionStatus.Resolved,
            "product_iron_ore_pellets", "product-catalog-v1", "گندله سنگ‌آهن", ["گندله", "گندله سنگ‌آهن"],
            "owner-approved-product-equivalence-v1", "complete", null);
        var plan = new StructuredQueryPlan(1, snapshot.CatalogVersion, snapshot.CatalogHash, TenantId,
            [new PlanEntityReference("Product", "گندله", ResolvedEntityKey: concept.CatalogId,
                IdentityProvenance: concept.ApprovalReference, IdentityVerified: true)],
            [new PlanSourceReference(FinancialValueReferenceType.MonthlyMeasure, sales.CatalogId, null, null, null, null,
                sales.CatalogId, sales.UnitCode, sales.UnitKind, "catalog-mapped", "approved", true)],
            sales.RelationshipIds, [new PlanProjection(sales.CatalogId, PlanProjectionRole.Value)], [],
            new PlanPeriodSelection(["approved-observation-window"], [], false, false,
                new DateOnly(2025, 10, 1), new DateOnly(2026, 9, 30), 12),
            new PlanStatementSelection([], [], [], [], false, true, true, true, true, true),
            [new PlanAggregation(sales.CatalogId, PlanAggregationOperation.Sum)],
            [new PlanSort(sales.CatalogId, PlanSortDirection.Descending)], new PlanPagination(0, 10),
            PlanPresentation.RankedTable,
            new PlanFinancialOperation(FinancialValueOperation.DirectValueRetrieval, PlanFinancialOperationKind.DirectValue,
                PlanComparisonMode.None, [], false), true, true, true, true, [], ProductConcept: concept,
            CompanyDiscoveryScope: new AuthorizedCompanyDiscoveryScope(TenantId, "tenant-test-scope", "tenant-approval", true),
            PopulationCoveragePolicyId: "observed-production-trailing-12-months-v1",
            PopulationSalesScope: PopulationSalesScope.TotalCompanySales);

        var compiled = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(compiled.Success, string.Join("|", compiled.Diagnostics));
        var query = Assert.IsType<CompiledReadOnlyQuery>(compiled.Query);
        Assert.Empty(query.CompanyIds);
        Assert.Contains("WITH producers AS", query.Sql, StringComparison.Ordinal);
        Assert.Contains("ProductionQuantity\" > 0", query.Sql, StringComparison.Ordinal);
        Assert.Contains("common_periods AS", query.Sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(DISTINCT \"CompanyId\") = (SELECT COUNT(*) FROM producers)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY es.\"Value\" DESC", query.Sql, StringComparison.Ordinal);
        Assert.Contains("metric_monthly_production_quantity", query.SourceCatalogIds);
        Assert.Contains("metric_monthly_sales", query.SourceCatalogIds);
        Assert.Contains(query.Parameters, parameter => parameter.Name == "product_aliases"
            && parameter.Value is string[] aliases && aliases.SequenceEqual(concept.ApprovedAliases));

        var ambiguous = new StructuredQueryCompiler().Compile(plan with { PopulationSalesScope = null }, snapshot);
        Assert.Contains("producer_sales_scope_ambiguous", ambiguous.Diagnostics);
    }

    [Fact]
    public void MonthlyQuantityCompilerMarksUnitAsRowScopedNotAsCatalogPlaceholder()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, item => item.CatalogId == "metric_monthly_production_quantity");
        var plan = MonthlyPlan(snapshot, mapping, latest: true, productCode: "PRODUCT:COMPANY:PELLET");

        var compiled = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(compiled.Success, string.Join("|", compiled.Diagnostics));
        Assert.Equal("row-source-unit", compiled.Query!.EffectiveUnit);
        Assert.Equal("row-scoped-source-unit", compiled.Query.EffectiveUnitStatus);
        Assert.DoesNotContain("source-quantity-unit", compiled.Query.EffectiveUnit, StringComparison.Ordinal);
    }

    [Fact]
    public void MonthlyCompiler_RejectsUnresolvedProductInjectionAggregationAndUnboundedSeries()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales_quantity");
        var basePlan = MonthlyPlan(snapshot, mapping, latest: true, productCode: "PRODUCT:COMPANY:PELLET");

        var injectionPlan = basePlan with { Entities = [basePlan.Entities[0], basePlan.Entities[1] with
            { ResolvedEntityKey = "x' OR TRUE --" }] };
        var unresolvedProduct = basePlan with { Entities = [basePlan.Entities[0], basePlan.Entities[1] with
            { ResolvedEntityKey = null, IdentityVerified = false }] };
        var aggregation = basePlan with { Aggregations = [new PlanAggregation(mapping.CatalogId, PlanAggregationOperation.Sum)] };
        var unboundedSeries = basePlan with { Presentation = PlanPresentation.TimeSeries };
        var rateMapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_sales_rate");
        var unverifiedRate = MonthlyPlan(snapshot, rateMapping, latest: true, productCode: "PRODUCT:COMPANY:PELLET");

        var injected = new StructuredQueryCompiler().Compile(injectionPlan, snapshot);
        Assert.True(injected.Success, string.Join(",", injected.Diagnostics));
        Assert.DoesNotContain("x' OR TRUE --", injected.Query!.Sql);
        Assert.Contains(injected.Query.Parameters, x => x.Name == "product_code" && Equals(x.Value, "x' OR TRUE --"));
        Assert.Contains("monthly_product_binding_not_verified",
            new StructuredQueryCompiler().Compile(unresolvedProduct, snapshot).Diagnostics);
        Assert.Contains("monthly_sum_requires_company_or_product_grouping_without_product_filter",
            new StructuredQueryCompiler().Compile(aggregation, snapshot).Diagnostics);
        Assert.Contains("monthly_time_series_requires_explicit_bounded_period",
            new StructuredQueryCompiler().Compile(unboundedSeries, snapshot).Diagnostics);
        Assert.Contains("monthly_provider_rate_unit_unverified",
            new StructuredQueryCompiler().Compile(unverifiedRate, snapshot).Diagnostics);
    }

    [Fact]
    public void MonthlyCompiler_UsesReviewedCompanySpecificProductKeysAcrossCompanies()
    {
        var snapshot = RuntimeSnapshot();
        var mapping = Assert.Single(snapshot.MonthlyMeasures, x => x.CatalogId == "metric_monthly_production_quantity");
        var company1 = Guid.Parse("4843ec8e-d35a-4f87-98bb-152702dd6f4f");
        var company2 = Guid.Parse("a91aa2bd-1d52-4ef9-9cb1-a59ab31f70e2");
        const string groupId = "equivalence-pellet-v1";
        const string approval = "owner-review:pellet-equivalence:v1";
        var equivalence = new ProductEquivalenceResolution(ProductEquivalenceResolutionStatus.Resolved, groupId, "گندله",
            [new(company1, "NoavaranCurrentApi", "provider-product-1", "company-one-pellet", "گندله", "ton", "source:product-1", true),
             new(company2, "NoavaranCurrentApi", "provider-product-2", "company-two-pellet", "گندله", "ton", "source:product-2", true)],
            "ton", approval, null);
        var plan = MonthlyPlan(snapshot, mapping, latest: true, productCode: groupId) with
        {
            Entities = [new("Company", "MTH", company1, IdentityProvenance: "company:1", IdentityVerified: true),
                new("Company", "MTH2", company2, IdentityProvenance: "company:2", IdentityVerified: true),
                new("Product", "گندله", ResolvedEntityKey: groupId, IdentityProvenance: approval, IdentityVerified: true)],
            ReviewedProductEquivalence = equivalence,
            Presentation = PlanPresentation.RankedTable,
            Aggregations = [new PlanAggregation(mapping.CatalogId, PlanAggregationOperation.Sum)],
            Sort = [new PlanSort(mapping.CatalogId, PlanSortDirection.Descending)],
            Pagination = new PlanPagination(0, 5)
        };

        var result = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(result.Success, string.Join("|", result.Diagnostics));
        Assert.Contains("product_company_0", result.Query!.Sql);
        Assert.Contains("product_company_1", result.Query.Sql);
        Assert.Contains(result.Query.Parameters, parameter => parameter.Name == "product_code_0"
            && Equals(parameter.Value, "company-one-pellet"));
        Assert.Contains(result.Query.Parameters, parameter => parameter.Name == "product_code_1"
            && Equals(parameter.Value, "company-two-pellet"));
        Assert.DoesNotContain("company-one-pellet", result.Query.Sql, StringComparison.Ordinal);
    }

    private static StructuredQueryCatalogSnapshot RuntimeSnapshot()
    {
        var root = FindRepositoryRoot();
        var catalog = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var hierarchy = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        return new StructuredQueryCatalogSnapshotBuilder().Build(catalog, hierarchy);
    }

    private static StructuredQueryPlan MonthlyPlan(StructuredQueryCatalogSnapshot snapshot,
        StructuredSqlMonthlyMeasure mapping, bool latest, string? productCode)
    {
        return new StructuredQueryPlan(1, snapshot.CatalogVersion, snapshot.CatalogHash, TenantId,
            [new PlanEntityReference("Company", "کگهر", Guid.Parse("4843ec8e-d35a-4f87-98bb-152702dd6f4f")),
                new PlanEntityReference("Product", "گندله", ResolvedEntityKey: productCode,
                    IdentityProvenance: productCode is null ? null : "feature-129-company-scoped-product-row",
                    IdentityVerified: productCode is not null)],
            [new PlanSourceReference(FinancialValueReferenceType.MonthlyMeasure, mapping.CatalogId, null, null, null, null,
                mapping.CatalogId, mapping.UnitCode, mapping.UnitKind, "catalog-mapped", "pending-review", false)],
            mapping.RelationshipIds,
            [new PlanProjection(mapping.CatalogId, PlanProjectionRole.Value)], [],
            new PlanPeriodSelection(latest ? ["latest"] : [], [], latest, false),
            new PlanStatementSelection([], [], [], [], false, true, true, true, true, true),
            [], [], new PlanPagination(0, 20), latest ? PlanPresentation.Scalar : PlanPresentation.Table,
            new PlanFinancialOperation(FinancialValueOperation.DirectValueRetrieval, PlanFinancialOperationKind.DirectValue,
                PlanComparisonMode.None, [], false), true, false, false, false, []);
    }

    [Fact]
    public void RuntimeCatalogSnapshotBuilder_RejectsCanonicalPackageDrift()
    {
        var root = FindRepositoryRoot();
        var sourceCatalog = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var hierarchy = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var tempCatalog = Path.Combine(Path.GetTempPath(), "feature141-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var directory in Directory.GetDirectories(sourceCatalog, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(directory.Replace(sourceCatalog, tempCatalog, StringComparison.Ordinal));
            foreach (var file in Directory.GetFiles(sourceCatalog, "*", SearchOption.AllDirectories))
            {
                var target = file.Replace(sourceCatalog, tempCatalog, StringComparison.Ordinal);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var relationship = Path.Combine(tempCatalog, "relationships", "rel_2588cf95d01a651a.yaml");
            File.WriteAllText(relationship, File.ReadAllText(relationship).Replace(
                "\"runtimeJoinable\": false", "\"runtimeJoinable\": true", StringComparison.Ordinal));

            var exception = Assert.Throws<SchemaNavigationOutputException>(() =>
                new StructuredQueryCatalogSnapshotBuilder().Build(tempCatalog, hierarchy));

            Assert.Equal("catalog_source_hash_mismatch", exception.Message);
        }
        finally
        {
            if (Directory.Exists(tempCatalog)) Directory.Delete(tempCatalog, recursive: true);
        }
    }

    [Fact]
    public void Compiler_HandlesAnotherProviderStatementSourceThroughTheSameCatalogPath()
    {
        var root = FindRepositoryRoot();
        var catalog = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var hierarchy = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var snapshot = new StructuredQueryCatalogSnapshotBuilder().Build(catalog, hierarchy);
        var source = Assert.Single(snapshot.DirectSources, item => item.ProviderName == "NoavaranCurrentApi"
            && item.StatementType == "BalanceSheet" && item.SourceItemId == 1);
        var (template, _) = Scenario();
        var plan = template with
        {
            CatalogVersion = snapshot.CatalogVersion,
            CatalogHash = snapshot.CatalogHash,
            Sources = [template.Sources[0] with
            {
                CatalogId = source.CatalogId,
                SourceItemCatalogId = source.SourceItemCatalogId,
                ProviderName = source.ProviderName,
                StatementType = source.StatementType,
                SourceItemId = source.SourceItemId,
                Unit = source.SourceUnit,
                UnitStatus = source.SourceUnitStatus,
                MetricCode = source.MetricCode
            }],
            RelationshipIds = source.RelationshipIds,
            Projection = [template.Projection[0] with { CatalogId = source.CatalogId }],
            Statement = template.Statement with
            {
                Providers = [source.ProviderName],
                StatementTypes = [source.StatementType]
            }
        };

        var compiled = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(compiled.Success, string.Join(",", compiled.Diagnostics));
        Assert.Contains(compiled.Query!.Parameters, parameter => parameter.Name == "source_item_id" && Equals(parameter.Value, 1));
    }

    [Fact]
    public void Compiles_catalog_identified_direct_source_item_without_metric_code_using_parameters()
    {
        var (plan, snapshot) = Scenario();

        var result = new StructuredQueryCompiler().Compile(plan, snapshot);

        Assert.True(result.Success, string.Join(",", result.Diagnostics));
        var query = Assert.IsType<CompiledReadOnlyQuery>(result.Query);
        Assert.Contains("\"public\".\"FinancialStatementLineItems\"", query.Sql);
        Assert.Contains("\"public\".\"FinancialStatementSourceItems\"", query.Sql);
        Assert.Contains("JOIN", query.Sql);
        Assert.Contains("LEFT JOIN", query.Sql);
        Assert.Contains("WITH latest_period", query.Sql);
        Assert.DoesNotContain("LIMIT 1", query.Sql);
        Assert.Contains("@source_item_catalog_id", query.Sql);
        Assert.Contains("@provider_name", query.Sql);
        Assert.Contains("@is_composing", query.Sql);
        Assert.Contains("AS \"CompanyId\"", query.Sql);
        Assert.Contains("AS \"PeriodType\"", query.Sql);
        Assert.DoesNotContain("NoavaranCurrentApi", query.Sql);
        Assert.DoesNotContain("161", query.Sql);
        Assert.DoesNotContain("MetricCode", query.Sql);
        Assert.Contains(query.Parameters, x => x.Name == "source_item_catalog_id" && Equals(x.Value, ItemId));
        Assert.Contains(query.Parameters, x => x.Name == "provider_name" && Equals(x.Value, "NoavaranCurrentApi"));
        Assert.Contains(query.Parameters, x => x.Name == "source_item_id" && Equals(x.Value, 161));
        Assert.Contains(query.Parameters, x => x.Name == "is_composing" && Equals(x.Value, false));
        Assert.Equal("IRRPerShare", query.EffectiveUnit);
        Assert.Equal("owner-approved", query.EffectiveUnitStatus);
        Assert.StartsWith("business-owner-confirmed:", query.EffectiveUnitProvenance);
        Assert.Equal(64, query.QueryFingerprint.Length);
    }

    [Fact]
    public void ScalarTieResolution_DeduplicatesEqualValuesAndRetainsAllStatementIdentities()
    {
        var rows = new[] { EvidenceRow(1, 42m), EvidenceRow(2, 42m), EvidenceRow(3, 42m) };

        var result = NpgsqlReadOnlyStructuredQueryExecutor.ResolveStatementTie(rows);

        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, result.Status);
        var row = Assert.Single(result.Rows);
        Assert.Equal(42m, row["Value"]);
        Assert.True((bool)row["SameValueTieResolved"]!);
        Assert.Equal(3, row["MatchingStatementCount"]);
        Assert.False(row.ContainsKey("StatementId"));
        Assert.Equal(new object?[] { 1, 2, 3 }, (object?[])row["StatementIdentities"]!);
    }

    [Fact]
    public void ScalarTieResolution_DifferentValuesRemainAmbiguousWithCompetingEvidence()
    {
        var rows = new[] { EvidenceRow(1, 42m), EvidenceRow(2, 43m) };

        var result = NpgsqlReadOnlyStructuredQueryExecutor.ResolveStatementTie(rows);

        Assert.Equal(StructuredQueryExecutionStatus.Ambiguous, result.Status);
        Assert.Equal("requested_item_values_conflict", result.DiagnosticCode);
        Assert.Equal(new object?[] { 42m, 43m }, result.Rows.Select(row => row["Value"]).ToArray());
        Assert.Equal(new object?[] { 1, 2 }, result.Rows.Select(row => row["StatementId"]).ToArray());
    }

    [Fact]
    public void ScalarTieResolution_TwoEqualAndOneDifferentIsAmbiguous()
    {
        var result = NpgsqlReadOnlyStructuredQueryExecutor.ResolveStatementTie(
            [EvidenceRow(1, 42m), EvidenceRow(2, 42m), EvidenceRow(3, 43m)]);

        Assert.Equal(StructuredQueryExecutionStatus.Ambiguous, result.Status);
        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void ScalarTieResolution_DoesNotLetOlderPeriodOverrideLatestEligibleValues()
    {
        var older = new Dictionary<string, object?>(EvidenceRow(9, 10m))
        {
            ["PeriodEnd"] = new DateOnly(2025, 12, 31)
        };

        var result = NpgsqlReadOnlyStructuredQueryExecutor.ResolveStatementTie(
            [older, EvidenceRow(1, 42m), EvidenceRow(2, 42m)]);

        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, result.Status);
        var resolved = Assert.Single(result.Rows);
        Assert.Equal(42m, resolved["Value"]);
        Assert.Equal(new object?[] { 1, 2 }, (object?[])resolved["StatementIdentities"]!);
    }

    [Fact]
    public void ScalarTieResolution_DoesNotMergeDifferentConsolidationScopes()
    {
        var standalone = EvidenceRow(1, 42m);
        var consolidated = new Dictionary<string, object?>(EvidenceRow(2, 42m)) { ["IsComposing"] = true };

        var result = NpgsqlReadOnlyStructuredQueryExecutor.ResolveStatementTie([standalone, consolidated]);

        Assert.Equal(StructuredQueryExecutionStatus.Ambiguous, result.Status);
        Assert.Equal("different_reporting_period_scopes", result.DiagnosticCode);
    }

    [Fact]
    public void ScalarTieResolution_MissingObservationIsIncompleteRatherThanEqual()
    {
        var result = NpgsqlReadOnlyStructuredQueryExecutor.ResolveStatementTie(
            [EvidenceRow(1, 42m), EvidenceRow(2, null)]);

        Assert.Equal(StructuredQueryExecutionStatus.Incomplete, result.Status);
        Assert.Equal("eligible_statement_observation_missing", result.DiagnosticCode);
    }

    [Theory]
    [InlineData("2026-03-20", "Quarterly", "2026-03-20", "Annual")]
    [InlineData("2026-03-20", "Quarterly", "2026-03-19", "Quarterly")]
    public void ScalarTieResolution_DoesNotMergeDifferentReportingPeriodScopes(
        string firstStart, string firstType, string secondStart, string secondType)
    {
        var first = EvidenceRow(1, 42m, DateOnly.Parse(firstStart), firstType);
        var second = EvidenceRow(2, 42m, DateOnly.Parse(secondStart), secondType);

        var result = NpgsqlReadOnlyStructuredQueryExecutor.ResolveStatementTie([first, second]);

        Assert.Equal(StructuredQueryExecutionStatus.Ambiguous, result.Status);
        Assert.Equal("different_reporting_period_scopes", result.DiagnosticCode);
    }

    [Fact]
    public void Rejects_unvalidated_snapshot_and_catalog_hash_mismatch()
    {
        var (plan, snapshot) = Scenario();

        var unvalidated = new StructuredQueryCompiler().Compile(plan, new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion,
            snapshot.CatalogHash, false, snapshot.Tables, snapshot.Columns, snapshot.Relationships, snapshot.DirectSources));
        var mismatched = new StructuredQueryCompiler().Compile(plan, new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion,
            "other-hash", true, snapshot.Tables, snapshot.Columns, snapshot.Relationships, snapshot.DirectSources));

        Assert.Contains("catalog_snapshot_not_integrity_validated", unvalidated.Diagnostics);
        Assert.Contains("catalog_snapshot_mismatch", mismatched.Diagnostics);
    }

    [Fact]
    public void Compiles_pending_relationships_for_offline_inspection_but_rejects_invented_source_identity()
    {
        var (plan, snapshot) = Scenario();
        var blockedSnapshot = new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion, snapshot.CatalogHash, true,
            snapshot.Tables, snapshot.Columns,
            snapshot.Relationships.Select(x => x.CatalogId == "rel_cd8a77a2717ac71f" ? x with { Queryable = false } : x).ToArray(),
            snapshot.DirectSources);
        var inventedPlan = plan with { Sources = [plan.Sources[0] with { SourceItemId = 999 }] };

        Assert.True(new StructuredQueryCompiler().Compile(plan, blockedSnapshot).Success);
        Assert.Contains("direct_source_identity_mismatch",
            new StructuredQueryCompiler().Compile(inventedPlan, snapshot).Diagnostics);
        var inventedJoin = new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion, snapshot.CatalogHash, true,
            snapshot.Tables, snapshot.Columns,
            snapshot.Relationships.Select(x => x.CatalogId == "rel_cd8a77a2717ac71f" ? x with { ToColumnCatalogId = "tbl_36036602dbf76362_SourceItemId" } : x).ToArray(),
            snapshot.DirectSources);
        Assert.Contains("direct_source_relationship_column_binding_invalid",
            new StructuredQueryCompiler().Compile(plan, inventedJoin).Diagnostics);
    }

    [Fact]
    public async Task Executor_and_development_authorizer_are_disabled_without_explicit_opt_in()
    {
        var (plan, snapshot) = Scenario();
        var query = Assert.IsType<CompiledReadOnlyQuery>(new StructuredQueryCompiler().Compile(plan, snapshot).Query);
        var options = Options.Create(new StructuredQueryExecutionOptions { Enabled = false, EnvironmentName = "Development" });
        var authorizer = new DevelopmentStructuredQueryAuthorizer(options, new TestEnvironment("Production"));
        var executor = new NpgsqlReadOnlyStructuredQueryExecutor(options, NullLogger<NpgsqlReadOnlyStructuredQueryExecutor>.Instance,
            new TestEnvironment("Production"));

        Assert.Null(authorizer.Authorize(query, TenantId));
        var result = await executor.ExecuteAsync(query,
            new StructuredQueryExecutionGrant(TenantId, "Development", "test", "readonly", query.CatalogHash,
                new HashSet<string> { "source_item_682beceadddc1458" }, new HashSet<string>(query.RelationshipCatalogIds),
                new HashSet<string>(query.PhysicalObjectCatalogIds), new HashSet<Guid> { query.CompanyId },
                new HashSet<FinancialValueOperation> { query.Operation }, "test-fixture", DateTimeOffset.UtcNow.AddMinutes(1)),
            CancellationToken.None);
        Assert.Equal(StructuredQueryExecutionStatus.Unauthorized, result.Status);
    }

    [Fact]
    public void Development_test_scope_requires_exact_company_operation_and_catalog_approval()
    {
        var (plan, snapshot) = Scenario();
        var pendingPlan = plan with { SemanticReviewApproved = false, QueryAuthorized = false };
        var compiler = new StructuredQueryCompiler();
        var pendingQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(pendingPlan, snapshot).Query);
        var approvedSnapshot = new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion, snapshot.CatalogHash, true,
            snapshot.Tables.Select(x => x with { Queryable = true, SemanticApproved = true }).ToArray(),
            snapshot.Columns.Select(x => x with { Queryable = true }).ToArray(),
            snapshot.Relationships.Select(x => x with { Queryable = true, SemanticApproved = true, QueryAuthorized = true }).ToArray(),
            snapshot.DirectSources.Select(x => x with { Queryable = true, SemanticApproved = true }).ToArray());
        Assert.False(pendingQuery.SemanticReviewApproved);
        Assert.False(pendingQuery.QueryAuthorized);
        var query = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(plan, approvedSnapshot).Query);
        var fieldPendingSnapshot = new StructuredQueryCatalogSnapshot(approvedSnapshot.CatalogVersion,
            approvedSnapshot.CatalogHash, true, approvedSnapshot.Tables,
            approvedSnapshot.Columns.Select(x => x.CatalogId == "tbl_4f2ef2d8dd59b46a_Value" ? x with { Queryable = false } : x).ToArray(),
            approvedSnapshot.Relationships, approvedSnapshot.DirectSources);
        var fieldPendingQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(plan, fieldPendingSnapshot).Query);
        Assert.False(fieldPendingQuery.QueryAuthorized);
        var options = Options.Create(new StructuredQueryExecutionOptions
        {
            Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
            DesignatedDatabase = "feature141_test", ReadOnlyRole = "feature141_reader",
            DevelopmentScopeId = "feature141-reviewed-fixture", AuthorizationProvenance = "review-record-2026-10-08",
            ReviewedSourceCatalogIds = ["source_item_682beceadddc1458"],
            ReviewedRelationshipCatalogIds = query.RelationshipCatalogIds.ToArray(),
            ReviewedPhysicalObjectCatalogIds = query.PhysicalObjectCatalogIds.ToArray(),
            ReviewedTenantCompanyBindings = [$"{TenantId:D}:{query.CompanyId:D}"],
            ReviewedOperations = [nameof(FinancialValueOperation.DirectValueRetrieval)]
        });

        var grant = new DevelopmentStructuredQueryAuthorizer(options, new TestEnvironment("Development"))
            .Authorize(query, TenantId);

        Assert.NotNull(grant);
        Assert.Equal(query.CatalogHash, grant.CatalogHash);
        Assert.Contains("review-record-2026-10-08", grant.Provenance);
        Assert.Contains(query.CompanyId, grant.AllowedCompanyIds);
        Assert.Contains(query.Operation, grant.AllowedOperations);
        Assert.True(query.QueryAuthorized);
        Assert.Null(new DevelopmentStructuredQueryAuthorizer(options, new TestEnvironment("Development"))
            .Authorize(pendingQuery, TenantId));
        var incompleteScope = Options.Create(new StructuredQueryExecutionOptions
        {
            Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
            DesignatedDatabase = "feature141_test", ReadOnlyRole = "feature141_reader",
            DevelopmentScopeId = "feature141-reviewed-fixture", AuthorizationProvenance = "review-record-2026-10-08",
            ReviewedSourceCatalogIds = ["source_item_682beceadddc1458"],
            ReviewedRelationshipCatalogIds = query.RelationshipCatalogIds.Skip(1).ToArray(),
            ReviewedPhysicalObjectCatalogIds = query.PhysicalObjectCatalogIds.ToArray(),
            ReviewedTenantCompanyBindings = [$"{TenantId:D}:{query.CompanyId:D}"],
            ReviewedOperations = [nameof(FinancialValueOperation.DirectValueRetrieval)]
        });
        Assert.Null(new DevelopmentStructuredQueryAuthorizer(incompleteScope, new TestEnvironment("Development"))
            .Authorize(query, TenantId));
        var wrongCompanyScope = Options.Create(new StructuredQueryExecutionOptions
        {
            Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
            DesignatedDatabase = "feature141_test", ReadOnlyRole = "feature141_reader",
            DevelopmentScopeId = "feature141-reviewed-fixture", AuthorizationProvenance = "review-record-2026-10-08",
            ReviewedSourceCatalogIds = ["source_item_682beceadddc1458"],
            ReviewedRelationshipCatalogIds = query.RelationshipCatalogIds.ToArray(),
            ReviewedPhysicalObjectCatalogIds = query.PhysicalObjectCatalogIds.ToArray(),
            ReviewedTenantCompanyBindings = [$"{TenantId:D}:{Guid.NewGuid():D}"],
            ReviewedOperations = [nameof(FinancialValueOperation.DirectValueRetrieval)]
        });
        Assert.Null(new DevelopmentStructuredQueryAuthorizer(wrongCompanyScope, new TestEnvironment("Development"))
            .Authorize(query, TenantId));
        var wrongTenantPlan = plan with { TenantId = Guid.NewGuid() };
        var wrongTenantQuery = Assert.IsType<CompiledReadOnlyQuery>(compiler.Compile(wrongTenantPlan, approvedSnapshot).Query);
        Assert.Null(new DevelopmentStructuredQueryAuthorizer(options, new TestEnvironment("Development"))
            .Authorize(wrongTenantQuery, wrongTenantPlan.TenantId));
    }

    [Fact]
    public void Enabled_executor_configuration_requires_valid_development_database_scope()
    {
        var validator = new StructuredQueryExecutionOptionsValidator(new TestEnvironment("Development"));
        var valid = new StructuredQueryExecutionOptions
        {
            Enabled = true,
            ExplicitDevelopmentOptIn = true,
            EnvironmentName = "Development",
            ConnectionString = "Host=localhost;Database=feature141_test;Username=feature141_reader;Password=test-only",
            DesignatedDatabase = "feature141_test",
            ReadOnlyRole = "feature141_reader",
            DevelopmentScopeId = "reviewed-test-scope",
            AuthorizationProvenance = "review-record",
            ReviewedSourceCatalogIds = ["source"],
            ReviewedRelationshipCatalogIds = ["relationship"],
            ReviewedPhysicalObjectCatalogIds = ["column"],
            ReviewedTenantCompanyBindings = [$"{TenantId:D}:{TenantId:D}"],
            ReviewedOperations = [nameof(FinancialValueOperation.DirectValueRetrieval)]
        };

        Assert.True(validator.Validate(null, valid).Succeeded);
        Assert.False(new StructuredQueryExecutionOptionsValidator(new TestEnvironment("Production"))
            .Validate(null, valid).Succeeded);
        Assert.False(validator.Validate(null, new StructuredQueryExecutionOptions { Enabled = true }).Succeeded);
    }

    private static (StructuredQueryPlan Plan, StructuredQueryCatalogSnapshot Snapshot) Scenario()
    {
        const string hash = "catalog-integrity-hash";
        var plan = new StructuredQueryPlan(1, "catalog-v1", hash, TenantId,
            [new PlanEntityReference("Company", "کگل", Guid.Parse("4843ec8e-d35a-4f87-98bb-152702dd6f4f"))],
            [new PlanSourceReference(FinancialValueReferenceType.DirectSourceItem, "source_item_682beceadddc1458", ItemId,
                "NoavaranCurrentApi", "IncomeStatement", 161, null, "N/A", "unknown",
                "verified-persisted-source-key", "pending", false)],
            ["rel_cd8a77a2717ac71f", "rel_2588cf95d01a651a", "rel_e4fa0fe989156533"],
            [new PlanProjection("source_item_682beceadddc1458", PlanProjectionRole.Value)], [],
            new PlanPeriodSelection(["latest"], [], true, false),
            new PlanStatementSelection([], ["NoavaranCurrentApi"], ["IncomeStatement"],
                [PlanStatementSelectionKey.ReportingPeriodDescending, PlanStatementSelectionKey.PeriodType,
                    PlanStatementSelectionKey.ProviderIdentity, PlanStatementSelectionKey.StatementType,
                    PlanStatementSelectionKey.StatementScope, PlanStatementSelectionKey.AuditState,
                    PlanStatementSelectionKey.RestatementState, PlanStatementSelectionKey.RevisionIdentity,
                    PlanStatementSelectionKey.Availability], false, true, true, true, true, true),
            [], [], new PlanPagination(0, 10), PlanPresentation.Scalar,
            new PlanFinancialOperation(FinancialValueOperation.DirectValueRetrieval, PlanFinancialOperationKind.DirectValue, PlanComparisonMode.None, [], false),
            true, true, true, false, []);

        var tables = new[]
        {
            new StructuredSqlTable("tbl_36036602dbf76362", "public", "FinancialStatementSourceItems", false),
            new StructuredSqlTable("tbl_4f2ef2d8dd59b46a", "public", "FinancialStatementLineItems", false),
            new StructuredSqlTable("tbl_53d51e26e1ca6929", "public", "FinancialStatements", false),
            new StructuredSqlTable("tbl_8646d83cdb350f92", "public", "Companies", false)
        };
        var columns = new List<StructuredSqlColumn>();
        AddColumns("tbl_36036602dbf76362", "Id", "ProviderName", "StatementType", "SourceItemId", "Unit");
        AddColumns("tbl_4f2ef2d8dd59b46a", "Id", "FinancialStatementId", "SourceItemCatalogId", "Value");
        AddColumns("tbl_53d51e26e1ca6929", "Id", "CompanyId", "ProviderName", "StatementType", "PeriodType", "PeriodStart", "PeriodEnd",
            "SourcePayloadChecksum", "LastSynchronizedAt", "WarningsJson", "LogicalVendor", "SourceMode", "IsAudited", "IsRepresented", "IsComposing", "ExternalStatementId", "PublishedAt");
        AddColumns("tbl_8646d83cdb350f92", "Id", "Ticker");
        var relationships = new[]
        {
            new StructuredSqlRelationship("rel_cd8a77a2717ac71f", "tbl_4f2ef2d8dd59b46a", "tbl_4f2ef2d8dd59b46a_SourceItemCatalogId", "tbl_36036602dbf76362", "tbl_36036602dbf76362_Id", "many-to-one", false, Verified: true, PhysicalConstraintPresent: true),
            new StructuredSqlRelationship("rel_2588cf95d01a651a", "tbl_4f2ef2d8dd59b46a", "tbl_4f2ef2d8dd59b46a_FinancialStatementId", "tbl_53d51e26e1ca6929", "tbl_53d51e26e1ca6929_Id", "many-to-one", false, Verified: true, PhysicalConstraintPresent: false),
            new StructuredSqlRelationship("rel_e4fa0fe989156533", "tbl_53d51e26e1ca6929", "tbl_53d51e26e1ca6929_CompanyId", "tbl_8646d83cdb350f92", "tbl_8646d83cdb350f92_Id", "many-to-one", false, Verified: true, PhysicalConstraintPresent: true)
        };
        var directSources = new[]
        {
            new StructuredSqlDirectSource("source_item_682beceadddc1458", ItemId, "NoavaranCurrentApi", "IncomeStatement", 161,
                "tbl_36036602dbf76362", "tbl_4f2ef2d8dd59b46a", "tbl_53d51e26e1ca6929", "tbl_8646d83cdb350f92",
                relationships.Select(x => x.CatalogId).ToArray(), false, SourceUnit: "N/A", SourceUnitStatus: "unknown",
                IdentityVerified: true, EffectiveUnit: "IRRPerShare", EffectiveUnitStatus: "owner-approved",
                EffectiveUnitProvenance: "business-owner-confirmed:Feature141-2026-10-08")
        };
        return (plan, new StructuredQueryCatalogSnapshot("catalog-v1", hash, true, tables, columns, relationships, directSources));

        void AddColumns(string table, params string[] names)
        {
            foreach (var name in names) columns.Add(new StructuredSqlColumn($"{table}_{name}", table, name, false));
        }
    }

    private static IReadOnlyDictionary<string, object?> EvidenceRow(int id, decimal? value,
        DateOnly? periodStart = null, string periodType = "Quarterly") => new Dictionary<string, object?>
    {
        ["Value"] = value, ["StatementId"] = id, ["PeriodEnd"] = new DateOnly(2026, 3, 31),
        ["PeriodStart"] = periodStart ?? new DateOnly(2026, 1, 1), ["PeriodType"] = periodType,
        ["IsComposing"] = false
    };

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md"))
                && Directory.Exists(Path.Combine(current.FullName, "specs", "141-schema-aware-semantic-query-engine")))
                return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root for the canonical catalog test.");
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "unit-tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}


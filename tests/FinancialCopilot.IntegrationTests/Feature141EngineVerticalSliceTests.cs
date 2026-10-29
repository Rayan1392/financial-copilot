using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Semantics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Real Feature 141 engine vertical slices. One Persian question travels through the production
/// components only: live LLM canonical intent extraction, approved catalog binding, live LLM navigator,
/// deterministic planner/validator, SQL compiler, catalog authorizer, bounded read-only Npgsql executor
/// and the Persian response generator. The database is a disposable isolated PostgreSQL instance; the
/// catalog is the real packaged catalog with no governance flags flipped by the test. Chat/API routing
/// is not touched; the chat orchestrator is only invoked in-process for its pre-model authorization gate.
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class Feature141EngineVerticalSliceTests(
    FinancialStatementValueSearch131PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string LiveOptInVariable = "FEATURE141_RUN_LIVE_LLM_POSTGRES";

    private static void RequireLiveOptIn() =>
        Skip.IfNot(string.Equals(Environment.GetEnvironmentVariable(LiveOptInVariable), "1", StringComparison.Ordinal),
            $"Set {LiveOptInVariable}=1 (plus the isolated PostgreSQL connection) to run the live vertical slice.");

    [SkippableFact]
    public async Task OperatingProfitPerShareQuestionRunsLiveLlmToIsolatedPostgreSqlAndPersianAnswer()
    {
        RequireLiveOptIn();
        await using var database = await fixture.CreateDatabaseAsync();

        var testCase = Feature141VerticalSliceCases.OperatingProfitPerShareKgehr;
        var runner = new Feature141EngineVerticalSliceRunner(database, output);
        var trace = await runner.RunAsync(testCase, CancellationToken.None);
        var artifactPath = await Feature141EngineVerticalSliceRunner.WriteArtifactAsync(trace);
        output.WriteLine($"Trace artifact: {artifactPath}");

        // 1. Live canonical intent.
        Assert.True(trace.IntentExtractionSucceeded, $"intent extraction failed: {trace.IntentFailureCategory}");
        var intent = Assert.IsType<CanonicalSemanticIntent>(trace.CanonicalIntent);
        var measure = Assert.Single(intent.Measures);
        Assert.Equal(testCase.ExpectedConceptId, measure.ConceptId);
        Assert.Equal(MeasureCanonicalizationStatus.Canonicalized, measure.CanonicalizationStatus);
        Assert.Equal(MeasureConceptKind.FinancialStatementItem, measure.Kind);
        Assert.Equal(BusinessMeasureDimension.CurrencyPerShare, measure.Dimension);
        Assert.Equal(MeasureValueForm.ReportedValue, measure.Qualifiers.ValueForm);
        Assert.Equal(EntityScopeKind.NamedCompanies, intent.EntityScope.Kind);
        Assert.Equal([testCase.ExpectedCompanyLabel], intent.EntityScope.CompanyLabels);
        Assert.Equal(AnalyticalOperationKind.Retrieve, intent.Operation.Kind);
        Assert.Equal(ComparisonMode.None, intent.Comparison.Mode);
        Assert.Contains(intent.Period.Kind, new[] { ReportingPeriodKind.LatestAvailable, ReportingPeriodKind.Unspecified });
        Assert.False(intent.Ambiguity.RequiresClarification);

        // 2. Approved catalog binding (typed binding and the production chat-path resolver agree).
        Assert.Equal(CanonicalCatalogBindingStatus.Bound, trace.TypedBindingStatus);
        Assert.Equal(testCase.ExpectedSourceCatalogId, trace.BoundSourceCatalogId);
        Assert.Equal(CanonicalSourceItemBindingStatus.Bound, trace.ChatPathBindingStatus);
        Assert.Equal(FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation, trace.HandoffStatus);
        Assert.Equal("source_identity_ready_for_planner_validation", trace.HandoffDiagnosticCode);

        // 3. Live navigator + deterministic planner, eligible under the real catalog governance.
        Assert.True(trace.PlanStructurallyValid, string.Join("|", trace.PlanDiagnostics));
        Assert.True(trace.PlanExecutionEligible, string.Join("|", trace.PlanDiagnostics));
        Assert.Equal(StructuredCompanyBindingStatus.Found, trace.CompanyBindingStatus);
        Assert.Equal(trace.Fixture.CompanyId, trace.BoundCompanyId);
        Assert.DoesNotContain("consolidated", trace.PlanStatementScopes);

        // 4. SQL compilation and parameters.
        Assert.True(trace.CompilationSucceeded, string.Join("|", trace.CompilationDiagnostics));
        Assert.Equal(trace.Fixture.CompanyId, trace.SqlParameters["company_id"]);
        Assert.Equal(testCase.ExpectedSourceItemCatalogId, trace.SqlParameters["source_item_catalog_id"]);
        Assert.Equal("NoavaranCurrentApi", trace.SqlParameters["provider_name"]);
        Assert.Equal("IncomeStatement", trace.SqlParameters["statement_type"]);
        Assert.Equal(161, trace.SqlParameters["source_item_id"]);
        Assert.Equal(testCase.ExpectedUnit, trace.CompiledEffectiveUnit);
        Assert.Equal(FinancialValueOperation.DirectValueRetrieval, trace.CompiledOperation);
        Assert.Equal([trace.Fixture.CompanyId], trace.CompiledCompanyIds);

        // 5. Catalog authorization against the isolated restricted role.
        Assert.True(trace.GrantIssued, "catalog authorizer refused the compiled query");
        Assert.Equal(trace.Fixture.DatabaseName, trace.GrantDatabase);
        Assert.Equal(trace.Fixture.RestrictedRole, trace.GrantRole);

        // 6. Actual PostgreSQL execution and the exact expected value.
        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, trace.ExecutionStatus);
        Assert.Equal(1, trace.ExecutionRowCount);
        Assert.Equal(testCase.ExpectedValue, trace.ActualValue);
        Assert.Equal(testCase.ExpectedValue, trace.IndependentDatabaseValue);
        Assert.Equal(testCase.ExpectedPeriodEnd, trace.ActualPeriodEnd);
        Assert.Equal(testCase.ExpectedPeriodStart, trace.ActualPeriodStart);
        Assert.Equal(testCase.ExpectedPersianPeriodEnd, trace.ActualPersianPeriodEnd);
        Assert.Equal(testCase.ExpectedPersianPeriodStart, trace.ActualPersianPeriodStart);
        Assert.Equal(testCase.ExpectedUnit, trace.ExecutionEffectiveUnit);
        Assert.Equal(trace.Fixture.CompanyId, trace.ActualRowCompanyId);
        Assert.Equal(161, trace.ActualRowSourceItemId);
        Assert.Equal(Feature141EngineVerticalSliceRunner.FixtureAuthorizationProvenance, trace.ExecutionAuthorizationProvenance);

        // 7. Persian answer rendered only from executor evidence.
        Assert.Equal(StructuredAnswerStatus.Answered, trace.PersianAnswerStatus);
        Assert.Contains(testCase.ExpectedPersianValueDigits!, trace.PersianAnswer, StringComparison.Ordinal);
        Assert.Contains(testCase.ExpectedUnit!, trace.PersianAnswer, StringComparison.Ordinal);
        // The reporting period is stated in Jalali only ("دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵").
        Assert.Contains(PersianReportingPeriodText.StatementPeriod("ThreeMonths", testCase.ExpectedPeriodStart, testCase.ExpectedPeriodEnd),
            trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain(Feature141EngineVerticalSliceRunner.PersianDigits(testCase.ExpectedPeriodEnd!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            trace.PersianAnswer, StringComparison.Ordinal);
        Assert.Equal(testCase.ExpectedUnit, trace.PersianAnswerUnit);

        // 8. No unauthorized access: the role is SELECT-only on the four reviewed tables, decoy rows were not
        //    returned, and the executor refuses the writable administrative principal.
        Assert.True(trace.RestrictedRoleIsSelectOnly);
        Assert.False(trace.RestrictedRoleCanReadOtherDomainTable);
        Assert.Equal(trace.Fixture.SeededLineItemCount, trace.IndependentLineItemCount);
        Assert.Equal(StructuredQueryExecutionStatus.Unauthorized, trace.WritablePrincipalExecutionStatus);
        Assert.Equal("database_read_only_identity_check_failed", trace.WritablePrincipalDiagnostic);
        Assert.True(trace.LlmCalls > 0, "no live model call was made");
        Assert.Equal(trace.LlmCalls, trace.ObservedModelCalls);
        Assert.Equal(1, trace.ObservedExecutorCalls);
    }

    [SkippableFact]
    public async Task MonthlyProductionQuestionRunsLiveLlmToIsolatedPostgreSqlAndPersianAnswer()
    {
        RequireLiveOptIn();
        await using var database = await fixture.CreateDatabaseAsync();

        var testCase = Feature141VerticalSliceCases.MonthlyProductionKgol;
        var runner = new Feature141EngineVerticalSliceRunner(database, output);
        var trace = await runner.RunAsync(testCase, CancellationToken.None);
        var artifactPath = await Feature141EngineVerticalSliceRunner.WriteArtifactAsync(trace);
        output.WriteLine($"Trace artifact: {artifactPath}");

        // 1. Live canonical intent names the company, the product and the Persian month.
        Assert.True(trace.IntentExtractionSucceeded, $"intent extraction failed: {trace.IntentFailureCategory}");
        var intent = Assert.IsType<CanonicalSemanticIntent>(trace.CanonicalIntent);
        var measure = Assert.Single(intent.Measures);
        Assert.Equal(MeasureConceptKind.ProductionQuantity, measure.Kind);
        Assert.Equal("productionQuantity", measure.ConceptId);
        Assert.Equal([testCase.ExpectedCompanyLabel], intent.EntityScope.CompanyLabels);
        Assert.Contains(intent.Products, product => product.UserLabel.Contains(testCase.ExpectedProductLabel!, StringComparison.Ordinal));
        Assert.Equal(ReportingPeriodKind.ExplicitMonth, intent.Period.Kind);
        Assert.False(intent.Ambiguity.RequiresClarification);

        // 2. Deterministic Persian month: model bounds are never authoritative.
        Assert.Equal(testCase.ExpectedPersianMonth, trace.NormalizedPersianMonth);
        Assert.Equal(testCase.ExpectedPeriodStart, trace.NormalizedPeriodStart);
        Assert.Equal(testCase.ExpectedPeriodEnd, trace.NormalizedPeriodEnd);
        Assert.Contains(trace.RequirementsNormalizationDiagnostics, d => d.StartsWith("persian_period_normalized:1405-06:", StringComparison.Ordinal));
        Assert.Equal([testCase.ExpectedMeasureCatalogId!], trace.RequirementsMeasureIds);

        // 3. Governed identities and eligibility from the real catalog (no flags forced).
        Assert.Equal(StructuredCompanyBindingStatus.Found, trace.CompanyBindingStatus);
        Assert.Equal(trace.Fixture.CompanyId, trace.BoundCompanyId);
        Assert.Equal(StructuredProductBindingStatus.Found, trace.ProductBindingStatus);
        Assert.Equal(trace.Fixture.ProductKey, trace.ProductKey);
        Assert.True(trace.PlanStructurallyValid, string.Join("|", trace.PlanDiagnostics));
        Assert.True(trace.PlanExecutionEligible, string.Join("|", trace.PlanDiagnostics));

        // 4. SQL parameters: provider, report type, accepted revision, company, product and the normalized window.
        Assert.True(trace.CompilationSucceeded, string.Join("|", trace.CompilationDiagnostics));
        Assert.Equal("NoavaranCurrentApi", trace.SqlParameters["provider_name"]);
        Assert.Equal("ProductSales", trace.SqlParameters["report_type"]);
        Assert.Equal(0, trace.SqlParameters["output_type"]);
        Assert.Equal(true, trace.SqlParameters["is_accepted"]);
        Assert.Equal("Accepted", trace.SqlParameters["revision_status"]);
        Assert.Equal(trace.Fixture.CompanyId, trace.SqlParameters["company_id"]);
        Assert.Equal(trace.Fixture.ProductKey, trace.SqlParameters["product_code"]);
        Assert.Equal(testCase.ExpectedPeriodStart, trace.SqlParameters["period_start"]);
        Assert.Equal(testCase.ExpectedPeriodEnd, trace.SqlParameters["period_end"]);
        Assert.True(trace.CompiledQueryAuthorized && trace.CompiledSemanticReviewApproved);
        Assert.Equal([trace.Fixture.CompanyId], trace.CompiledCompanyIds);

        // 5 and 6. Authorization and actual PostgreSQL execution with the exact fixture value.
        Assert.True(trace.GrantIssued, "catalog authorizer refused the compiled query");
        Assert.Equal(trace.Fixture.RestrictedRole, trace.GrantRole);
        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, trace.ExecutionStatus);
        Assert.Equal(1, trace.ExecutionRowCount);
        Assert.Equal(testCase.ExpectedValue, trace.ActualValue);
        Assert.Equal(testCase.ExpectedValue, trace.IndependentDatabaseValue);
        Assert.Equal(trace.Fixture.SeededLineItemCount, trace.IndependentLineItemCount);
        Assert.Equal(testCase.ExpectedPeriodStart, trace.ActualPeriodStart);
        Assert.Equal(testCase.ExpectedPeriodEnd, trace.ActualPeriodEnd);
        Assert.Equal("1405-06-01", trace.ActualPersianPeriodStart);
        Assert.Equal("1405-06-31", trace.ActualPersianPeriodEnd);
        Assert.Equal(trace.Fixture.ProductKey, trace.ActualProductCode);
        Assert.Equal(testCase.ExpectedUnit, trace.ActualSourceUnit);
        Assert.Equal(trace.Fixture.CompanyId, trace.ActualRowCompanyId);

        // 7. Persian answer with value, unit and month window.
        Assert.Equal(StructuredAnswerStatus.Answered, trace.PersianAnswerStatus);
        Assert.Contains(testCase.ExpectedPersianValueDigits!, trace.PersianAnswer, StringComparison.Ordinal);
        Assert.Contains("تن", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.Equal("تن", trace.PersianAnswerUnit);
        // The month window is rendered in Jalali only (Shahrivar 1405 = 2026-08-23..2026-09-22).
        Assert.Contains("شهریور ۱۴۰۵", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("۲۰۲۶", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("۹۹", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("۴۷", trace.PersianAnswer, StringComparison.Ordinal);

        // 8. Least privilege and no unauthorized access.
        Assert.True(trace.RestrictedRoleIsSelectOnly);
        Assert.False(trace.RestrictedRoleCanReadOtherDomainTable);
        Assert.Equal(StructuredQueryExecutionStatus.Unauthorized, trace.WritablePrincipalExecutionStatus);
        Assert.Equal(1, trace.ObservedExecutorCalls);
        // Intent extraction plus the navigator's requirements and navigation calls; the navigation result
        // also counts its own requirements call, so the component total may exceed the observed count.
        Assert.InRange(trace.ObservedModelCalls, 2, trace.LlmCalls);
    }

    [SkippableFact]
    public async Task CompanyDiscoveryQuestionRunsLiveLlmToIsolatedPostgreSqlAndReturnsTheProducerSet()
    {
        RequireLiveOptIn();
        await using var database = await fixture.CreateDatabaseAsync();

        var testCase = Feature141VerticalSliceCases.CompanyDiscoveryPellets;
        var runner = new Feature141EngineVerticalSliceRunner(database, output);
        var trace = await runner.RunAsync(testCase, CancellationToken.None);
        var artifactPath = await Feature141EngineVerticalSliceRunner.WriteArtifactAsync(trace);
        output.WriteLine($"Trace artifact: {artifactPath}");

        // 1. Live intent: unbound company population, one product, explicit Persian month.
        Assert.True(trace.IntentExtractionSucceeded, $"intent extraction failed: {trace.IntentFailureCategory}");
        var intent = Assert.IsType<CanonicalSemanticIntent>(trace.CanonicalIntent);
        Assert.Equal(EntityScopeKind.UnboundCompanyPopulation, intent.EntityScope.Kind);
        Assert.Empty(intent.EntityScope.CompanyLabels);
        Assert.Contains(intent.Products, product => product.UserLabel.Contains(testCase.ExpectedProductLabel!, StringComparison.Ordinal));
        Assert.Equal(ReportingPeriodKind.ExplicitMonth, intent.Period.Kind);
        Assert.Equal(testCase.ExpectedPersianMonth, trace.NormalizedPersianMonth);
        Assert.Equal(testCase.ExpectedPeriodStart, trace.NormalizedPeriodStart);
        Assert.Equal(testCase.ExpectedPeriodEnd, trace.NormalizedPeriodEnd);

        // 2. Approved product equivalence and the fixture-scoped discovery authorization.
        Assert.Equal("product_iron_ore_pellets", trace.ProductConceptCatalogId);
        Assert.Equal(["گندله", "گندله سنگ‌آهن"], trace.ProductConceptAliases.OrderBy(a => a.Length).ToArray());
        Assert.Equal("approved-aliases-only", trace.ProductMappingCoverageStatus);
        Assert.Equal(Feature141EngineVerticalSliceRunner.FixtureDiscoveryScopeId, trace.CompanyDiscoveryScopeId);
        Assert.Equal(StructuredProductBindingStatus.Found, trace.ProductBindingStatus);
        Assert.Null(trace.CompanyBindingStatus);
        Assert.True(trace.PlanStructurallyValid, string.Join("|", trace.PlanDiagnostics));
        Assert.True(trace.PlanExecutionEligible, string.Join("|", trace.PlanDiagnostics));

        // 3. SQL and authorization evidence: no company list in the plan, the grant carries only the reviewed universe.
        Assert.True(trace.CompilationSucceeded, string.Join("|", trace.CompilationDiagnostics));
        Assert.True(trace.CompiledWithFixtureOnlyColumnApproval);
        Assert.Empty(trace.CompiledCompanyIds);
        Assert.DoesNotContain("company_id", trace.SqlParameters.Keys);
        Assert.Equal(["گندله", "گندله سنگ‌آهن"], ((string[])trace.SqlParameters["product_aliases"]!).OrderBy(a => a.Length).ToArray());
        Assert.Equal(testCase.ExpectedPeriodStart, trace.SqlParameters["period_start"]);
        Assert.Equal(testCase.ExpectedPeriodEnd, trace.SqlParameters["period_end"]);
        Assert.Equal(true, trace.SqlParameters["is_accepted"]);
        Assert.Contains("> 0", trace.Sql!);
        Assert.Contains("= ANY(@company_ids)", trace.Sql!);
        Assert.True(trace.GrantIssued, "catalog authorizer refused the discovery query");
        Assert.Equal(trace.Fixture.ReviewedCompanyUniverse!.OrderBy(id => id), trace.GrantAllowedCompanyIds);
        Assert.DoesNotContain(trace.Fixture.UnauthorizedProducerId!.Value, trace.GrantAllowedCompanyIds);

        // 4. PostgreSQL result: exactly the expected producers, distinct, with provenance; coverage disclosed as partial.
        // The fixture scope is declared CompanyCoverageComplete=false (the unauthorized producer is outside the
        // reviewed universe), so the executor discloses partial company coverage first; alias coverage is
        // the second, catalog-level reason the list stays partial.
        Assert.Equal(StructuredQueryExecutionStatus.Incomplete, trace.ExecutionStatus);
        Assert.Equal("population_discovery_authorized_company_coverage_incomplete", trace.ExecutionDiagnostic);
        Assert.False(trace.CompanyDiscoveryCoverageComplete);
        Assert.Equal(testCase.ExpectedCompanySymbols!.OrderBy(s => s, StringComparer.Ordinal), trace.ActualCompanySymbols.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(trace.ActualCompanyIds.Count, trace.ActualCompanyIds.Distinct().Count());
        Assert.DoesNotContain(trace.Fixture.UnauthorizedProducerId.Value, trace.ActualCompanyIds);
        Assert.Equal(3, trace.IndependentDatabaseValue); // کگل, کگهر and the unauthorized producer have positive production in the window
        Assert.Equal(2, trace.ActualCompanyIds.Count);     // the unauthorized producer is excluded by the grant
        Assert.All(trace.ExecutionRows, row => Assert.True(Convert.ToDecimal(row["Value"], CultureInfo.InvariantCulture) > 0));
        Assert.All(trace.ExecutionRows, row => Assert.Equal(testCase.ExpectedPeriodEnd, row["PeriodEnd"] is DateTime d ? DateOnly.FromDateTime(d) : (DateOnly?)row["PeriodEnd"]));

        // 5. Persian answer lists both companies with their own product title and unit, and discloses partial coverage.
        Assert.Equal(StructuredAnswerStatus.Incomplete, trace.PersianAnswerStatus);
        Assert.Contains("کگل", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.Contains("کگهر", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.Contains("جزئی", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("UNAUTH", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("فملی", trace.PersianAnswer, StringComparison.Ordinal);

        // 6. Access boundaries.
        Assert.True(trace.RestrictedRoleIsSelectOnly);
        Assert.False(trace.RestrictedRoleCanReadOtherDomainTable);
        Assert.Equal(StructuredQueryExecutionStatus.Unauthorized, trace.WritablePrincipalExecutionStatus);
        Assert.Equal(1, trace.ObservedExecutorCalls);
        Assert.InRange(trace.ObservedModelCalls, 2, trace.LlmCalls);
    }

    /// <summary>
    /// Generic live assertion for every additional answered case: metric interpretation, company/product
    /// resolution, period, numeric value, unit, no decoy leakage and the Persian answer.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(AdditionalAnsweredCases))]
    public async Task AdditionalQuestionsRunLiveEndToEnd(string caseId)
    {
        RequireLiveOptIn();
        var testCase = Feature141VerticalSliceCases.All.Single(item => item.CaseId == caseId);
        await using var database = await fixture.CreateDatabaseAsync();
        var runner = new Feature141EngineVerticalSliceRunner(database, output);
        var trace = await runner.RunAsync(testCase, CancellationToken.None);
        var artifactPath = await Feature141EngineVerticalSliceRunner.WriteArtifactAsync(trace);
        output.WriteLine($"Trace artifact: {artifactPath}");

        Assert.True(trace.IntentExtractionSucceeded, $"intent extraction failed: {trace.IntentFailureCategory}");
        var intent = Assert.IsType<CanonicalSemanticIntent>(trace.CanonicalIntent);
        Assert.Equal([testCase.ExpectedCompanyLabel], intent.EntityScope.CompanyLabels);
        if (testCase.ExpectedConceptId is not null) Assert.Equal(testCase.ExpectedConceptId, Assert.Single(intent.Measures).ConceptId);
        if (testCase.Kind == Feature141CaseKind.MonthlyMeasure)
        {
            Assert.Equal(testCase.ExpectedPersianMonth, trace.NormalizedPersianMonth);
            Assert.Equal([testCase.ExpectedMeasureCatalogId!], trace.RequirementsMeasureIds);
            Assert.Equal(StructuredProductBindingStatus.Found, trace.ProductBindingStatus);
            Assert.Equal(trace.Fixture.ProductKey, trace.SqlParameters["product_code"]);
            Assert.Equal(testCase.ExpectedPeriodStart, trace.SqlParameters["period_start"]);
            Assert.Equal(testCase.ExpectedPeriodEnd, trace.SqlParameters["period_end"]);
        }
        else
        {
            Assert.Equal(testCase.ExpectedSourceCatalogId, trace.BoundSourceCatalogId ?? trace.Plan?.Sources.Single().CatalogId);
            Assert.Equal(testCase.ExpectedSourceItemId, trace.SqlParameters["source_item_id"]);
            Assert.Equal(testCase.ExpectedUnit, trace.CompiledEffectiveUnit);
        }
        Assert.Equal(StructuredCompanyBindingStatus.Found, trace.CompanyBindingStatus);
        Assert.Equal(trace.Fixture.CompanyId, trace.BoundCompanyId);
        Assert.True(trace.PlanExecutionEligible, string.Join("|", trace.PlanDiagnostics));
        Assert.True(trace.CompilationSucceeded, string.Join("|", trace.CompilationDiagnostics));
        Assert.True(trace.GrantIssued);
        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, trace.ExecutionStatus);
        Assert.Equal(1, trace.ExecutionRowCount);
        Assert.Equal(testCase.ExpectedValue, trace.ActualValue);
        Assert.Equal(testCase.ExpectedValue, trace.IndependentDatabaseValue);
        Assert.Equal(testCase.ExpectedPeriodStart, trace.ActualPeriodStart);
        Assert.Equal(testCase.ExpectedPeriodEnd, trace.ActualPeriodEnd);
        Assert.Equal(trace.Fixture.CompanyId, trace.ActualRowCompanyId);
        if (testCase.Kind == Feature141CaseKind.MonthlyMeasure && testCase.MonthlyValueColumn != Feature141MonthlyValueColumn.SalesAmount)
            Assert.Equal(testCase.ExpectedUnit, trace.ActualSourceUnit);
        Assert.Equal(StructuredAnswerStatus.Answered, trace.PersianAnswerStatus);
        Assert.Contains(testCase.ExpectedPersianValueDigits!, trace.PersianAnswer, StringComparison.Ordinal);
        var expectedUnitLabel = testCase.ExpectedUnit switch { "MillionIRR" => "میلیون ریال", "ton" => "تن", var other => other! };
        Assert.Contains(expectedUnitLabel, trace.PersianAnswer, StringComparison.Ordinal);
        Assert.Equal(StructuredQueryExecutionStatus.Unauthorized, trace.WritablePrincipalExecutionStatus);
        Assert.Equal(1, trace.ObservedExecutorCalls);
    }

    /// <summary>
    /// Slice 3a: cross-company comparison and ranking of a statement item, plus the negative scenarios (missing company,
    /// missing metric, different periods, incompatible unit, conflicting duplicate statements, consolidated versus
    /// standalone, equal values, empty result set), each through the live model and the isolated database.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(CrossCompanyCases))]
    public async Task CrossCompanyComparisonRunsLiveEndToEnd(string caseId)
    {
        RequireLiveOptIn();
        await RunCrossCompanyCaseAsync(Feature141VerticalSliceCases.CrossCompany.Single(item => item.CaseId == caseId));
    }

    /// <summary>
    /// Slice 3b: numeric threshold filters (one predicate, or an AND of two on items of the same statement) over the
    /// authorized population, plus the essential negatives (unit conversion, missing filter value, OR), each through
    /// the live model and the isolated database.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(FilteringCases))]
    public async Task ThresholdFilterRunsLiveEndToEnd(string caseId)
    {
        RequireLiveOptIn();
        await RunCrossCompanyCaseAsync(Feature141VerticalSliceCases.Filtering.Single(item => item.CaseId == caseId));
    }

    public static IEnumerable<object[]> FilteringCases() =>
        Feature141VerticalSliceCases.Filtering.Select(item => new object[] { item.CaseId });

    private async Task RunCrossCompanyCaseAsync(Feature141VerticalSliceCase testCase)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var runner = new Feature141EngineVerticalSliceRunner(database, output);
        var trace = await runner.RunAsync(testCase, CancellationToken.None);
        var artifactPath = await Feature141EngineVerticalSliceRunner.WriteArtifactAsync(trace);
        output.WriteLine($"Trace artifact: {artifactPath}");

        Assert.True(trace.IntentExtractionSucceeded, $"intent extraction failed: {trace.IntentFailureCategory} {trace.IntentFailureDiagnostic}");
        var intent = Assert.IsType<CanonicalSemanticIntent>(trace.CanonicalIntent);
        Assert.Equal(testCase.ExpectedOutcome, trace.TypedOutcome);
        Assert.True(trace.ObservedModelCalls > 0);
        Assert.Equal(trace.LlmCalls, trace.ObservedModelCalls);
        if (testCase.ExpectedDiagnostic is { } diagnostic)
        {
            var observed = new[] { trace.TypedOutcomeReason, trace.ExecutionDiagnostic, trace.HandoffDiagnosticCode }
                .Concat(trace.TypedBindingDiagnostics).Concat(trace.PlanDiagnostics).Concat(trace.CompilationDiagnostics)
                .Where(value => value is not null).ToArray();
            Assert.True(observed.Any(value => value!.Contains(diagnostic, StringComparison.Ordinal)),
                $"expected diagnostic {diagnostic}; observed {string.Join(" | ", observed)}");
        }
        if (trace.ExecutionStatus is null)
        {
            // Refused before SQL: no statement was compiled or executed and no number was presented.
            Assert.Null(trace.Sql);
            Assert.Equal(0, trace.ObservedExecutorCalls);
            Assert.NotEqual(Feature141ExpectedOutcome.Answered, trace.TypedOutcome);
            return;
        }

        // Executed: production chain with the cross-company handoff, the catalog-approved snapshot and the restricted role.
        Assert.Equal(testCase.ExpectedConceptId, intent.Measures[0].ConceptId);
        Assert.Equal(CanonicalCatalogBindingStatus.Bound, trace.TypedBindingStatus);
        Assert.Contains(trace.StatementQueryShape, new[] { "MultiCompanyComparison", "Ranking", "Filter" });
        // Threshold predicates are typed plan filters compiled into bound SQL parameters; the model wrote no SQL.
        Assert.Equal(testCase.ExpectedFilterCount, trace.Plan!.Filters.Count);
        for (var index = 0; index < testCase.ExpectedFilterCount; index++)
            Assert.IsType<decimal>(trace.SqlParameters[$"filter_threshold_{index}"]);
        Assert.Equal("cross_company_source_identity_ready_for_planner_validation", trace.HandoffDiagnosticCode);
        Assert.True(trace.PlanExecutionEligible, string.Join("|", trace.PlanDiagnostics));
        Assert.True(trace.CompilationSucceeded, string.Join("|", trace.CompilationDiagnostics));
        Assert.False(trace.CompiledWithFixtureOnlyColumnApproval);
        Assert.Contains("common_period", trace.Sql!, StringComparison.Ordinal);
        Assert.Equal(testCase.ExpectedSourceItemId, trace.SqlParameters["source_item_id"]);
        Assert.Equal(testCase.ExpectedUnit, trace.CompiledEffectiveUnit);
        Assert.True(trace.GrantIssued);
        Assert.Equal(Feature141EngineVerticalSliceRunner.FixtureAuthorizationProvenance, trace.ExecutionAuthorizationProvenance);
        if (testCase.PopulationUniverse is { } universe)
        {
            // The population is the reviewed universe from the grant; no company id is a compiled parameter.
            Assert.False(trace.SqlParameters.ContainsKey("company_ids"));
            Assert.Empty(trace.CompiledCompanyIds);
            Assert.Equal(universe.Select(ticker => trace.Fixture.CompaniesBySymbol![ticker]).Order(), trace.GrantAllowedCompanyIds);
        }
        else
        {
            var named = intent.EntityScope.CompanyLabels.Select(label => trace.Fixture.CompaniesBySymbol![label]).Order().ToArray();
            Assert.Equal(named, ((Guid[])trace.SqlParameters["company_ids"]!).Order().ToArray());
        }
        Assert.Equal(StructuredQueryExecutionStatus.Unauthorized, trace.WritablePrincipalExecutionStatus);
        Assert.Equal("database_read_only_identity_check_failed", trace.WritablePrincipalDiagnostic);
        Assert.Equal(1, trace.ObservedExecutorCalls);
        if (testCase.ExpectedOutcome != Feature141ExpectedOutcome.Answered)
        {
            Assert.NotEqual(StructuredAnswerStatus.Answered, trace.PersianAnswerStatus);
            Assert.DoesNotContain("بیشترین", trace.PersianAnswer, StringComparison.Ordinal);
            return;
        }

        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, trace.ExecutionStatus);
        var expected = testCase.ExpectedComparison!;
        if (testCase.ExpectedComparisonPrefixAllowed)
        {
            // "Which one is larger": a comparison lists both, a top-1 ranking lists the winner; both start with the winner.
            Assert.InRange(trace.ComparisonRows.Count, 1, expected.Count);
            Assert.Equal(expected.Take(trace.ComparisonRows.Count), trace.ComparisonRows);
        }
        else Assert.Equal(expected, trace.ComparisonRows);
        foreach (var row in trace.ComparisonRows)
            Assert.Equal(row.Value, trace.IndependentComparisonValues[row.Label]);
        Assert.Equal(testCase.ExpectedPeriodStart, trace.ActualPeriodStart);
        Assert.Equal(testCase.ExpectedPeriodEnd, trace.ActualPeriodEnd);
        if (testCase.ExpectedWarningPrefix is { } warning)
            Assert.Contains(trace.ExecutionWarnings, item => item.StartsWith(warning, StringComparison.Ordinal));
        Assert.Equal(StructuredAnswerStatus.Answered, trace.PersianAnswerStatus);
        foreach (var row in trace.ComparisonRows)
        {
            Assert.Contains(row.Label, trace.PersianAnswer, StringComparison.Ordinal);
            Assert.Contains(PersianStructuredQueryResponseGenerator.FormatValue(row.Value), trace.PersianAnswer, StringComparison.Ordinal);
        }
        Assert.Contains(testCase.ExpectedUnit == "IRRPerShare" ? "ریال به ازای هر سهم" : "میلیون ریال", trace.PersianAnswer, StringComparison.Ordinal);
        foreach (var excluded in testCase.ExpectedExcludedCompanies ?? [])
            Assert.DoesNotContain(excluded, trace.PersianAnswer, StringComparison.Ordinal);
        // The shared period is stated in Jalali only ("… منتهی به ۳۱ خرداد ۱۴۰۵"); no Gregorian date reaches the answer.
        Assert.Contains(PersianReportingPeriodText.Date(testCase.ExpectedPeriodEnd!.Value), trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain(Feature141EngineVerticalSliceRunner.PersianDigits(testCase.ExpectedPeriodEnd!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("میلادی", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("فولاد", trace.PersianAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain(PersianStructuredQueryResponseGenerator.FormatValue(77_777m), trace.PersianAnswer, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> CrossCompanyCases() =>
        Feature141VerticalSliceCases.CrossCompany.Select(item => new object[] { item.CaseId });

    public static IEnumerable<object[]> AdditionalAnsweredCases() =>
        Feature141VerticalSliceCases.AdditionalAnswered.Select(item => new object[] { item.CaseId });

    [SkippableTheory]
    [MemberData(nameof(TypedRejectionCases))]
    public async Task TypedRejectionsStopBeforeSqlAndRecordTheirOutcome(string caseId)
    {
        RequireLiveOptIn();
        var testCase = Feature141VerticalSliceCases.All.Single(item => item.CaseId == caseId);
        await using var database = await fixture.CreateDatabaseAsync();
        var runner = new Feature141EngineVerticalSliceRunner(database, output);
        var trace = await runner.RunAsync(testCase, CancellationToken.None);
        var artifactPath = await Feature141EngineVerticalSliceRunner.WriteArtifactAsync(trace);
        output.WriteLine($"Trace artifact: {artifactPath}");

        Assert.Equal(0, trace.ObservedExecutorCalls);
        Assert.Null(trace.Sql);
        Assert.Null(trace.ExecutionStatus);
        Assert.False(trace.GrantIssued);
        Assert.Equal(testCase.ExpectedOutcome, trace.TypedOutcome);
        if (testCase.ExpectedOutcome == Feature141ExpectedOutcome.Unauthorized)
        {
            // Pre-model rejection: the production gate refused the request before any provider call.
            Assert.Equal(0, trace.ObservedModelCalls);
            Assert.False(trace.ChatGateEnabled);
            Assert.NotNull(trace.ChatGateMessage);
            Assert.Null(trace.CanonicalIntent);
        }
        else
        {
            // Either the extractor's validator rejected the intent (deterministic, no catalog work) or the
            // typed catalog binding rejected it; in both cases exactly the model was consulted and nothing else.
            Assert.True(trace.ObservedModelCalls > 0);
            Assert.True(trace.IntentExtractionSucceeded || trace.IntentFailureCategory == "unsupported_semantic_intent",
                $"intent extraction failed: {trace.IntentFailureCategory}");
            if (trace.IntentExtractionSucceeded)
                Assert.NotEqual(CanonicalCatalogBindingStatus.Bound, trace.TypedBindingStatus);
            else
                Assert.Null(trace.TypedBindingStatus);
        }
    }

    public static IEnumerable<object[]> TypedRejectionCases() => Feature141VerticalSliceCases.All
        .Where(item => item.ExpectedOutcome != Feature141ExpectedOutcome.Answered && item.Kind == Feature141CaseKind.StatementItem)
        .Select(item => new object[] { item.CaseId });
}

public enum Feature141ExpectedOutcome { Answered, Ambiguous, Unsupported, Incomplete, Unauthorized }

/// <summary>Which governed retrieval path the question exercises and which fixture block is seeded.</summary>
public enum Feature141CaseKind { StatementItem, MonthlyMeasure, CompanyDiscovery, StatementComparison }

/// <summary>One seeded statement of a cross-company comparison fixture (the case's statement item).</summary>
public sealed record Feature141ComparisonStatement(string Ticker, decimal? Value, bool Older = false, bool Composing = false,
    bool HasLine = true, string Suffix = "a", bool Audited = false, IReadOnlyDictionary<int, decimal?>? OtherItems = null);

/// <summary>An expected policy-resolved comparison row: company label, value, competition rank and tie flag.</summary>
public sealed record Feature141ComparisonExpectation(string Label, decimal Value, int Rank, bool Tied = false);

/// <summary>Which monthly line-item column a monthly case seeds its expected value into (the others get decoy values).</summary>
public enum Feature141MonthlyValueColumn { ProductionQuantity, SalesQuantity, SalesAmount }

/// <summary>One governed question with independently fixed expectations; the fixture is seeded from it.</summary>
public sealed record Feature141VerticalSliceCase(
    string CaseId,
    string Question,
    Feature141ExpectedOutcome ExpectedOutcome,
    string? ExpectedConceptId = null,
    string ExpectedCompanyLabel = "کگهر",
    string? ExpectedSourceCatalogId = null,
    Guid ExpectedSourceItemCatalogId = default,
    decimal? ExpectedValue = null,
    string? ExpectedUnit = null,
    DateOnly? ExpectedPeriodStart = null,
    DateOnly? ExpectedPeriodEnd = null,
    string? ExpectedPersianValueDigits = null,
    bool TenantScopeMissing = false,
    Feature141CaseKind Kind = Feature141CaseKind.StatementItem,
    string? ExpectedMeasureCatalogId = null,
    string? ExpectedProductLabel = null,
    string? ExpectedPersianMonth = null,
    IReadOnlyList<string>? ExpectedCompanySymbols = null,
    string ExpectedStatementType = "IncomeStatement",
    int ExpectedSourceItemId = 161,
    string ExpectedTitleFa = "سود عملیاتی هر سهم",
    Feature141MonthlyValueColumn MonthlyValueColumn = Feature141MonthlyValueColumn.ProductionQuantity,
    IReadOnlyList<string>? ComparisonCompanies = null,
    IReadOnlyList<Feature141ComparisonStatement>? ComparisonStatements = null,
    IReadOnlyList<Feature141ComparisonExpectation>? ExpectedComparison = null,
    IReadOnlyList<string>? PopulationUniverse = null,
    string? ExpectedDiagnostic = null,
    bool ExpectedComparisonPrefixAllowed = false,
    string? ExpectedWarningPrefix = null,
    int ExpectedFilterCount = 0,
    IReadOnlyList<string>? ExpectedExcludedCompanies = null)
{
    public string? ExpectedPersianPeriodStart => ExpectedPeriodStart is { } d ? Feature141EngineVerticalSliceRunner.ToPersianCalendar(d) : null;
    public string? ExpectedPersianPeriodEnd => ExpectedPeriodEnd is { } d ? Feature141EngineVerticalSliceRunner.ToPersianCalendar(d) : null;
}

public static class Feature141VerticalSliceCases
{
    public static readonly Guid OperatingProfitPerShareSourceItemCatalogId = Guid.Parse("3cafc7d3-b0ab-4341-85ef-ebcbc9d60528");

    /// <summary>
    /// The only canonical concept with an approved catalog binding in the current catalog is
    /// operatingProfitPerShare (IncomeStatement/161). Monthly sales measures are still
    /// pending-owner-review, so the monthly sales question is substituted by this one.
    /// </summary>
    public static readonly Feature141VerticalSliceCase OperatingProfitPerShareKgehr = new(
        CaseId: "F141-E04",
        Question: "سود عملیاتی هر سهم کگهر چقدر است؟",
        ExpectedOutcome: Feature141ExpectedOutcome.Answered,
        ExpectedConceptId: "operatingProfitPerShare",
        ExpectedCompanyLabel: "کگهر",
        ExpectedSourceCatalogId: "source_item_682beceadddc1458",
        ExpectedSourceItemCatalogId: OperatingProfitPerShareSourceItemCatalogId,
        ExpectedValue: 3_753m,
        ExpectedUnit: "IRRPerShare",
        ExpectedPeriodStart: new DateOnly(2026, 3, 21),
        ExpectedPeriodEnd: new DateOnly(2026, 6, 21),
        ExpectedPersianValueDigits: "۳٬۷۵۳");

    /// <summary>"فروش" names neither the amount/quantity measure nor an approved binding; it must not execute.</summary>
    public static readonly Feature141VerticalSliceCase AmbiguousSales = new(
        "F141-E18", "فروش کگل چقدر است؟", Feature141ExpectedOutcome.Ambiguous, ExpectedCompanyLabel: "کگل");

    /// <summary>Forecasting intrinsic value is not an analytical operation the engine supports.</summary>
    public static readonly Feature141VerticalSliceCase UnsupportedForecast = new(
        "F141-E19", "ارزش ذاتی کگل در سال آینده چقدر می‌شود؟", Feature141ExpectedOutcome.Unsupported, ExpectedCompanyLabel: "کگل");

    /// <summary>A request without tenant/actor scope is refused by the production gate before any model call.</summary>
    public static readonly Feature141VerticalSliceCase UnauthorizedWithoutTenant = new(
        "F141-E20", "سود عملیاتی هر سهم کگهر چقدر است؟", Feature141ExpectedOutcome.Unauthorized, TenantScopeMissing: true);

    /// <summary>
    /// Monthly production quantity of one product for one company and one Persian month, governed by the
    /// owner-approved metric_monthly_production_quantity (decision record 2026-10-28, applied 2026-10-29).
    /// Shahrivar 1405 = 2026-08-23..2026-09-22 by deterministic PersianCalendar normalization.
    /// </summary>
    public static readonly Feature141VerticalSliceCase MonthlyProductionKgol = new(
        CaseId: "F141-E07",
        Question: "تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟",
        ExpectedOutcome: Feature141ExpectedOutcome.Answered,
        ExpectedCompanyLabel: "کگل",
        ExpectedValue: 25m,
        ExpectedUnit: "ton",
        ExpectedPeriodStart: new DateOnly(2026, 8, 23),
        ExpectedPeriodEnd: new DateOnly(2026, 9, 22),
        ExpectedPersianValueDigits: "۲۵",
        Kind: Feature141CaseKind.MonthlyMeasure,
        ExpectedMeasureCatalogId: "metric_monthly_production_quantity",
        ExpectedProductLabel: "گندله",
        ExpectedPersianMonth: "1405-06");

    /// <summary>
    /// Governed cross-company discovery: companies with positive recorded production of the approved
    /// product concept (aliases گندله / گندله سنگ‌آهن) in the named Persian month, selected from persisted
    /// observations inside the tenant's reviewed company universe. Expected set is fixed by the fixture:
    /// کگل (گندله, ton) and کگهر (گندله سنگ‌آهن, ton); zero, NULL, other-month and unauthorized producers excluded.
    /// </summary>
    public static readonly Feature141VerticalSliceCase CompanyDiscoveryPellets = new(
        CaseId: "F141-E10",
        Question: "کدام شرکت‌ها در شهریور ۱۴۰۵ گندله تولید کرده‌اند؟",
        ExpectedOutcome: Feature141ExpectedOutcome.Incomplete,
        ExpectedCompanyLabel: "",
        ExpectedUnit: "ton",
        ExpectedPeriodStart: new DateOnly(2026, 8, 23),
        ExpectedPeriodEnd: new DateOnly(2026, 9, 22),
        Kind: Feature141CaseKind.CompanyDiscovery,
        ExpectedMeasureCatalogId: "metric_monthly_production_quantity",
        ExpectedProductLabel: "گندله",
        ExpectedPersianMonth: "1405-06",
        ExpectedCompanySymbols: ["کگل", "کگهر"]);

    /// <summary>Net profit (IncomeStatement/143, canonical netProfit, MillionIRR) — latest standalone statement.</summary>
    public static readonly Feature141VerticalSliceCase NetProfitKgol = new(
        CaseId: "F141-E01", Question: "سود خالص کگل چقدر است؟", ExpectedOutcome: Feature141ExpectedOutcome.Answered,
        ExpectedConceptId: "netProfit", ExpectedCompanyLabel: "کگل", ExpectedSourceCatalogId: "source_item_66fdf1090cbf96d3",
        ExpectedValue: 1_200m, ExpectedUnit: "MillionIRR", ExpectedPeriodStart: new DateOnly(2026, 3, 21), ExpectedPeriodEnd: new DateOnly(2026, 6, 21),
        ExpectedPersianValueDigits: "۱٬۲۰۰", ExpectedSourceItemId: 143, ExpectedTitleFa: "سود خالص");

    /// <summary>Operating profit (IncomeStatement/140, canonical operatingProfit, MillionIRR).</summary>
    public static readonly Feature141VerticalSliceCase OperatingProfitKgehr = new(
        CaseId: "F141-E02", Question: "سود عملیاتی کگهر چقدر است؟", ExpectedOutcome: Feature141ExpectedOutcome.Answered,
        ExpectedConceptId: "operatingProfit", ExpectedCompanyLabel: "کگهر", ExpectedSourceCatalogId: "source_item_9c617846fc7c5c13",
        ExpectedValue: 1_100m, ExpectedUnit: "MillionIRR", ExpectedPeriodStart: new DateOnly(2026, 3, 21), ExpectedPeriodEnd: new DateOnly(2026, 6, 21),
        ExpectedPersianValueDigits: "۱٬۱۰۰", ExpectedSourceItemId: 140, ExpectedTitleFa: "سود عملیاتی");

    /// <summary>Earnings per share (IncomeStatement/160 سود خالص هر سهم, canonical earningsPerShare, IRRPerShare).</summary>
    public static readonly Feature141VerticalSliceCase EarningsPerShareFameli = new(
        CaseId: "F141-E03", Question: "سود هر سهم فملی چقدر است؟", ExpectedOutcome: Feature141ExpectedOutcome.Answered,
        ExpectedConceptId: "earningsPerShare", ExpectedCompanyLabel: "فملی", ExpectedSourceCatalogId: "source_item_bb4b63de3c1ecfe9",
        ExpectedValue: 3_200m, ExpectedUnit: "IRRPerShare", ExpectedPeriodStart: new DateOnly(2026, 3, 21), ExpectedPeriodEnd: new DateOnly(2026, 6, 21),
        ExpectedPersianValueDigits: "۳٬۲۰۰", ExpectedSourceItemId: 160, ExpectedTitleFa: "سود خالص هر سهم");

    /// <summary>Monthly sales amount of a product for one company and month (metric_monthly_sales, MillionIRR).</summary>
    public static readonly Feature141VerticalSliceCase MonthlySalesAmountKgehr = new(
        CaseId: "F141-E08", Question: "مبلغ فروش گندله کگهر در شهریور ۱۴۰۵ چقدر بود؟", ExpectedOutcome: Feature141ExpectedOutcome.Answered,
        ExpectedCompanyLabel: "کگهر", ExpectedValue: 700m, ExpectedUnit: "MillionIRR",
        ExpectedPeriodStart: new DateOnly(2026, 8, 23), ExpectedPeriodEnd: new DateOnly(2026, 9, 22), ExpectedPersianValueDigits: "۷۰۰",
        Kind: Feature141CaseKind.MonthlyMeasure, ExpectedMeasureCatalogId: "metric_monthly_sales", ExpectedProductLabel: "گندله",
        ExpectedPersianMonth: "1405-06", MonthlyValueColumn: Feature141MonthlyValueColumn.SalesAmount);

    /// <summary>Monthly sales quantity of a product for one company and month (metric_monthly_sales_quantity, row unit).</summary>
    public static readonly Feature141VerticalSliceCase MonthlySalesQuantityKgol = new(
        CaseId: "F141-E09", Question: "مقدار فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟", ExpectedOutcome: Feature141ExpectedOutcome.Answered,
        ExpectedCompanyLabel: "کگل", ExpectedValue: 20m, ExpectedUnit: "ton",
        ExpectedPeriodStart: new DateOnly(2026, 8, 23), ExpectedPeriodEnd: new DateOnly(2026, 9, 22), ExpectedPersianValueDigits: "۲۰",
        Kind: Feature141CaseKind.MonthlyMeasure, ExpectedMeasureCatalogId: "metric_monthly_sales_quantity", ExpectedProductLabel: "گندله",
        ExpectedPersianMonth: "1405-06", MonthlyValueColumn: Feature141MonthlyValueColumn.SalesQuantity);

    /// <summary>Latest common reporting period of the comparison fixture (1405-01-01..1405-03-31) and the prior quarter.</summary>
    public static readonly DateOnly ComparisonLatestStart = new(2026, 3, 21);
    public static readonly DateOnly ComparisonLatestEnd = new(2026, 6, 21);
    public static readonly DateOnly ComparisonPriorStart = new(2025, 12, 21);
    public static readonly DateOnly ComparisonPriorEnd = new(2026, 3, 20);

    /// <summary>Net profit (IncomeStatement/143, MillionIRR) of three peers; older quarters carry different values as decoys.</summary>
    private static readonly Feature141ComparisonStatement[] NetProfitPeers =
    [
        new("کگل", 1_200m), new("کگهر", 900m), new("فملی", 1_600m),
        new("کگل", 1_000m, Older: true), new("کگهر", 2_000m, Older: true), new("فملی", 1_400m, Older: true)
    ];
    private static readonly string[] Peers = ["کگل", "کگهر", "فملی"];

    private static Feature141VerticalSliceCase Comparison(string caseId, string question, Feature141ExpectedOutcome outcome,
        IReadOnlyList<Feature141ComparisonStatement> statements, IReadOnlyList<Feature141ComparisonExpectation>? expected = null,
        string? diagnostic = null, IReadOnlyList<string>? companies = null, IReadOnlyList<string>? universe = null,
        bool prefixAllowed = false, bool prior = false, string? warning = null) => new(
        CaseId: caseId, Question: question, ExpectedOutcome: outcome, ExpectedConceptId: "netProfit", ExpectedCompanyLabel: "",
        ExpectedSourceCatalogId: "source_item_66fdf1090cbf96d3", ExpectedUnit: "MillionIRR",
        ExpectedPeriodStart: prior ? ComparisonPriorStart : ComparisonLatestStart, ExpectedPeriodEnd: prior ? ComparisonPriorEnd : ComparisonLatestEnd,
        Kind: Feature141CaseKind.StatementComparison, ExpectedSourceItemId: 143, ExpectedTitleFa: "سود خالص",
        ComparisonCompanies: companies ?? Peers, ComparisonStatements: statements, ExpectedComparison: expected,
        PopulationUniverse: universe, ExpectedDiagnostic: diagnostic, ExpectedComparisonPrefixAllowed: prefixAllowed,
        ExpectedWarningPrefix: warning);

    /// <summary>F141-E05: comparison of two explicitly named companies at their latest common period.</summary>
    public static readonly Feature141VerticalSliceCase CompareNetProfitKgolKgehr = Comparison("F141-E05",
        "سود خالص کگل و کگهر را مقایسه کن", Feature141ExpectedOutcome.Answered, NetProfitPeers,
        [new("کگل", 1_200m, 1), new("کگهر", 900m, 2)]);

    /// <summary>F141-E06: which named company has the larger net profit (winner کگل, 1,200 MillionIRR).</summary>
    public static readonly Feature141VerticalSliceCase LargerNetProfitKgolOrKgehr = Comparison("F141-E06",
        "کدام یک سود خالص بیشتری دارد، کگل یا کگهر؟", Feature141ExpectedOutcome.Answered, NetProfitPeers,
        [new("کگل", 1_200m, 1), new("کگهر", 900m, 2)], prefixAllowed: true);

    /// <summary>F141-E14: descending ranking of the tenant's authorized company universe; فولاد is outside it.</summary>
    public static readonly Feature141VerticalSliceCase RankAuthorizedPopulationByNetProfit = Comparison("F141-E14",
        "شرکت‌ها را بر اساس سود خالص از بیشترین به کمترین مرتب کن", Feature141ExpectedOutcome.Answered,
        [.. NetProfitPeers, new("فولاد", 9_999m)],
        [new("فملی", 1_600m, 1), new("کگل", 1_200m, 2), new("کگهر", 900m, 3)],
        companies: [.. Peers, "فولاد"], universe: Peers);

    public static readonly Feature141VerticalSliceCase CompareWithMissingCompany = Comparison("F141-E21",
        "سود خالص کگل و زاگرس را مقایسه کن", Feature141ExpectedOutcome.Incomplete, NetProfitPeers,
        diagnostic: "statement_comparison_company_binding_unresolved");

    public static readonly Feature141VerticalSliceCase CompareMissingMetric = Comparison("F141-E22",
        "هزینه استهلاک کگل و کگهر را مقایسه کن", Feature141ExpectedOutcome.Incomplete, NetProfitPeers,
        diagnostic: "approved_catalog_binding_missing:depreciationExpense");

    public static readonly Feature141VerticalSliceCase CompareDifferentReportingPeriods = Comparison("F141-E23",
        "سود خالص کگل و کگهر را مقایسه کن", Feature141ExpectedOutcome.Answered,
        [new("کگل", 1_300m), new("کگل", 1_100m, Older: true), new("کگهر", 800m, Older: true)],
        [new("کگل", 1_100m, 1), new("کگهر", 800m, 2)], prior: true,
        warning: CrossCompanyStatementComparisonPolicy.WarningNewerPeriodAvailable + ":کگل");

    public static readonly Feature141VerticalSliceCase CompareIncompatibleUnit = Comparison("F141-E24",
        "سود خالص کگل و کگهر را به میلیارد ریال مقایسه کن", Feature141ExpectedOutcome.Unsupported, NetProfitPeers,
        diagnostic: "requested_result_unit_conversion_not_supported");

    public static readonly Feature141VerticalSliceCase CompareConflictingDuplicateStatements = Comparison("F141-E25",
        "سود خالص کگل و کگهر را مقایسه کن", Feature141ExpectedOutcome.Ambiguous,
        [new("کگل", 1_200m, Suffix: "a"), new("کگل", 1_250m, Suffix: "b", Audited: true), new("کگهر", 900m)],
        diagnostic: "requested_item_values_conflict");

    public static readonly Feature141VerticalSliceCase CompareConsolidatedWithStandalone = Comparison("F141-E26",
        "سود خالص کگل و کگهر را مقایسه کن", Feature141ExpectedOutcome.Ambiguous,
        [new("کگل", 1_500m, Composing: true), new("کگهر", 900m)],
        diagnostic: "cross_company_consolidation_basis_mismatch");

    public static readonly Feature141VerticalSliceCase LargerNetProfitWithEqualValues = Comparison("F141-E27",
        "کدام یک سود خالص بیشتری دارد، کگل یا کگهر؟", Feature141ExpectedOutcome.Answered,
        [new("کگل", 1_200m), new("کگهر", 1_200m)], [new("کگل", 1_200m, 1, true), new("کگهر", 1_200m, 1, true)]);

    public static readonly Feature141VerticalSliceCase CompareWithoutStatements = Comparison("F141-E28",
        "سود خالص کگل و کگهر را مقایسه کن", Feature141ExpectedOutcome.Incomplete, [],
        diagnostic: "comparison_companies_without_statements");

    /// <summary>
    /// Threshold-filter fixture: net profit (143), operating profit (140) and EPS (160) of the three peers on the same latest
    /// statement (fixture proposed.companies); the prior quarter carries values that would flip every predicate, and فولاد,
    /// outside the authorized universe, passes every predicate.
    /// </summary>
    private static Feature141ComparisonStatement[] FilterPeers(int listedItem, string? epsMissingFor)
    {
        (string Ticker, decimal NetProfit, decimal OperatingProfit, decimal Eps, bool Older)[] values =
        [
            ("کگل", 1_200m, 1_500m, 2_400m, false), ("کگهر", 900m, 1_100m, 1_800m, false), ("فملی", 1_600m, 2_000m, 3_200m, false),
            ("کگل", 500m, 600m, 100m, true), ("کگهر", 2_000m, 5_000m, 5_000m, true), ("فملی", 400m, 300m, 200m, true),
            ("فولاد", 9_999m, 9_999m, 9_999m, false)
        ];
        return values.Select(item =>
        {
            var items = new Dictionary<int, decimal?>
            {
                [143] = item.NetProfit, [140] = item.OperatingProfit, [160] = !item.Older && item.Ticker == epsMissingFor ? null : item.Eps
            };
            var listed = items[listedItem];
            items.Remove(listedItem);
            return new Feature141ComparisonStatement(item.Ticker, listed, item.Older, OtherItems: items);
        }).ToArray();
    }

    private static Feature141VerticalSliceCase Filter(string caseId, string question, Feature141ExpectedOutcome outcome, int listedItem,
        IReadOnlyList<Feature141ComparisonExpectation>? expected = null, IReadOnlyList<string>? excluded = null, int filters = 1,
        string? diagnostic = null, string? epsMissingFor = null, string? warning = null)
    {
        var (concept, catalogId, unit, title) = listedItem switch
        {
            140 => ("operatingProfit", "source_item_9c617846fc7c5c13", "MillionIRR", "سود عملیاتی"),
            160 => ("earningsPerShare", "source_item_bb4b63de3c1ecfe9", "IRRPerShare", "سود خالص هر سهم"),
            _ => ("netProfit", "source_item_66fdf1090cbf96d3", "MillionIRR", "سود خالص")
        };
        return new(CaseId: caseId, Question: question, ExpectedOutcome: outcome, ExpectedConceptId: concept, ExpectedCompanyLabel: "",
            ExpectedSourceCatalogId: catalogId, ExpectedUnit: unit, ExpectedPeriodStart: ComparisonLatestStart, ExpectedPeriodEnd: ComparisonLatestEnd,
            Kind: Feature141CaseKind.StatementComparison, ExpectedSourceItemId: listedItem, ExpectedTitleFa: title,
            ComparisonCompanies: [.. Peers, "فولاد"], ComparisonStatements: FilterPeers(listedItem, epsMissingFor), ExpectedComparison: expected,
            PopulationUniverse: Peers, ExpectedDiagnostic: diagnostic, ExpectedWarningPrefix: warning,
            ExpectedFilterCount: outcome == Feature141ExpectedOutcome.Answered ? filters : 0, ExpectedExcludedCompanies: excluded);
    }

    /// <summary>F141-E15: one threshold on the listed measure (net profit above 1,000 MillionIRR).</summary>
    public static readonly Feature141VerticalSliceCase FilterNetProfitAbove = Filter("F141-E15",
        "شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال کدام‌اند؟", Feature141ExpectedOutcome.Answered, 143,
        [new("فملی", 1_600m, 1), new("کگل", 1_200m, 2)], ["کگهر"]);

    /// <summary>F141-E17: AND of net profit and EPS thresholds, both read from the same statement of each company.</summary>
    public static readonly Feature141VerticalSliceCase FilterNetProfitAndEps = Filter("F141-E17",
        "شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال و EPS بالای ۲۰۰۰ ریال را نشان بده", Feature141ExpectedOutcome.Answered, 143,
        [new("فملی", 1_600m, 1), new("کگل", 1_200m, 2)], ["کگهر"], filters: 2);

    /// <summary>Same operator on operating profit.</summary>
    public static readonly Feature141VerticalSliceCase FilterOperatingProfitAbove = Filter("F141-E29",
        "شرکت‌هایی با سود عملیاتی بیشتر از ۱۲۰۰ میلیون ریال کدام‌اند؟", Feature141ExpectedOutcome.Answered, 140,
        [new("فملی", 2_000m, 1), new("کگل", 1_500m, 2)], ["کگهر"]);

    /// <summary>Less-than on earnings per share (IRRPerShare).</summary>
    public static readonly Feature141VerticalSliceCase FilterEpsBelow = Filter("F141-E30",
        "شرکت‌هایی با سود هر سهم کمتر از ۲۵۰۰ ریال کدام‌اند؟", Feature141ExpectedOutcome.Answered, 160,
        [new("کگل", 2_400m, 1), new("کگهر", 1_800m, 2)], ["فملی"]);

    /// <summary>The AND is evaluated: کگل passes net profit but fails EPS above 2,500.</summary>
    public static readonly Feature141VerticalSliceCase FilterNetProfitAndHigherEps = Filter("F141-E31",
        "شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال و سود هر سهم بالای ۲۵۰۰ ریال کدام‌اند؟", Feature141ExpectedOutcome.Answered, 143,
        [new("فملی", 1_600m, 1)], ["کگل", "کگهر"], filters: 2);

    /// <summary>A threshold in another unit than the item's effective unit is refused before SQL (no conversion).</summary>
    public static readonly Feature141VerticalSliceCase FilterIncompatibleUnit = Filter("F141-E32",
        "شرکت‌هایی با سود خالص بالای ۱ میلیارد ریال کدام‌اند؟", Feature141ExpectedOutcome.Unsupported, 143,
        diagnostic: "unit_conversion_not_supported");

    /// <summary>A missing EPS value excludes the company with a disclosure; it is never treated as zero.</summary>
    public static readonly Feature141VerticalSliceCase FilterMissingEpsValue = Filter("F141-E33",
        "شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال و EPS بالای ۲۰۰۰ ریال را نشان بده", Feature141ExpectedOutcome.Answered, 143,
        [new("فملی", 1_600m, 1)], ["کگهر"], filters: 2, epsMissingFor: "کگل",
        warning: CrossCompanyStatementComparisonPolicy.WarningFilterValueMissing + ":کگل");

    /// <summary>OR between conditions is outside the slice and is refused before SQL.</summary>
    public static readonly Feature141VerticalSliceCase FilterOrUnsupported = Filter("F141-E34",
        "شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال یا EPS بالای ۲۰۰۰ ریال کدام‌اند؟", Feature141ExpectedOutcome.Unsupported, 143,
        diagnostic: "statement_filter_expression_not_supported");

    /// <summary>Slice 3b threshold-filter cases: two required scenarios, three metric variations and three negatives.</summary>
    public static readonly IReadOnlyList<Feature141VerticalSliceCase> Filtering =
        [FilterNetProfitAbove, FilterNetProfitAndEps, FilterOperatingProfitAbove, FilterEpsBelow, FilterNetProfitAndHigherEps,
         FilterIncompatibleUnit, FilterMissingEpsValue, FilterOrUnsupported];

    /// <summary>Slice 3a cross-company cases: three required scenarios and eight negative scenarios.</summary>
    public static readonly IReadOnlyList<Feature141VerticalSliceCase> CrossCompany =
        [CompareNetProfitKgolKgehr, LargerNetProfitKgolOrKgehr, RankAuthorizedPopulationByNetProfit, CompareWithMissingCompany,
         CompareMissingMetric, CompareDifferentReportingPeriods, CompareIncompatibleUnit, CompareConflictingDuplicateStatements,
         CompareConsolidatedWithStandalone, LargerNetProfitWithEqualValues, CompareWithoutStatements];

    public static readonly IReadOnlyList<Feature141VerticalSliceCase> All =
        [NetProfitKgol, OperatingProfitKgehr, EarningsPerShareFameli, OperatingProfitPerShareKgehr, MonthlyProductionKgol,
         MonthlySalesAmountKgehr, MonthlySalesQuantityKgol, CompanyDiscoveryPellets, AmbiguousSales, UnsupportedForecast, UnauthorizedWithoutTenant,
         .. CrossCompany, .. Filtering];

    /// <summary>Answered cases beyond the two original single-metric slices; each runs through the generic live assertion.</summary>
    public static readonly IReadOnlyList<Feature141VerticalSliceCase> AdditionalAnswered =
        [NetProfitKgol, OperatingProfitKgehr, EarningsPerShareFameli, MonthlySalesAmountKgehr, MonthlySalesQuantityKgol];
}

public sealed record Feature141VerticalSliceFixtureInfo(
    string DatabaseName, string RestrictedRole, Guid CompanyId, Guid DecoyCompanyId,
    Guid StatementId, Guid OlderStatementId, int SeededLineItemCount,
    Guid? MonthlyReportId = null, string? ProductKey = null,
    IReadOnlyList<Guid>? ReviewedCompanyUniverse = null, Guid? UnauthorizedProducerId = null,
    IReadOnlyDictionary<string, Guid>? CompaniesBySymbol = null,
    Feature141MonthlyValueColumn MonthlyValueColumn = Feature141MonthlyValueColumn.ProductionQuantity,
    Guid? SeededSourceItemCatalogId = null);

/// <summary>Stage-by-stage trace of one run. Null means the stage was not reached.</summary>
public sealed class Feature141VerticalSliceTrace
{
    public required string CaseId { get; init; }
    public required string Question { get; init; }
    public required Feature141VerticalSliceFixtureInfo Fixture { get; init; }
    public Feature141ExpectedOutcome ExpectedOutcome { get; init; }
    public Feature141ExpectedOutcome? TypedOutcome { get; set; }
    public string? TypedOutcomeReason { get; set; }
    public string? ProviderKey { get; set; }
    public string? ModelKey { get; set; }
    public string? CatalogVersion { get; set; }
    public string? CatalogHash { get; set; }
    public bool? ChatGateEnabled { get; set; }
    public string? ChatGateMessage { get; set; }
    public bool IntentExtractionSucceeded { get; set; }
    public string? IntentFailureCategory { get; set; }
    public string? IntentFailureDiagnostic { get; set; }
    public CanonicalSemanticIntent? CanonicalIntent { get; set; }
    public CanonicalCatalogBindingStatus? TypedBindingStatus { get; set; }
    public IReadOnlyList<string> TypedBindingDiagnostics { get; set; } = [];
    public string? BoundSourceCatalogId { get; set; }
    public CanonicalSourceItemBindingStatus? ChatPathBindingStatus { get; set; }
    public string? ChatPathBindingDiagnostic { get; set; }
    public FinancialSourcePlannerHandoffStatus? HandoffStatus { get; set; }
    public string? HandoffDiagnosticCode { get; set; }
    public QuestionSchemaRequirements? PlannerRequirements { get; set; }
    public SchemaNavigationResult? Navigation { get; set; }
    public StructuredQueryPlan? Plan { get; set; }
    public bool PlanStructurallyValid { get; set; }
    public bool PlanExecutionEligible { get; set; }
    public IReadOnlyList<string> PlanDiagnostics { get; set; } = [];
    public IReadOnlyList<string> PlanStatementScopes { get; set; } = [];
    public StructuredCompanyBindingStatus? CompanyBindingStatus { get; set; }
    public Guid? BoundCompanyId { get; set; }
    public bool CompilationSucceeded { get; set; }
    public IReadOnlyList<string> CompilationDiagnostics { get; set; } = [];
    public string? Sql { get; set; }
    public Dictionary<string, object?> SqlParameters { get; set; } = new(StringComparer.Ordinal);
    public string? QueryFingerprint { get; set; }
    public string? CompiledEffectiveUnit { get; set; }
    public FinancialValueOperation? CompiledOperation { get; set; }
    public IReadOnlyList<Guid> CompiledCompanyIds { get; set; } = [];
    public bool CompiledSemanticReviewApproved { get; set; }
    public bool CompiledIdentityAuthorized { get; set; }
    public bool CompiledQueryAuthorized { get; set; }
    public bool GrantIssued { get; set; }
    public string? GrantDatabase { get; set; }
    public string? GrantRole { get; set; }
    public string? GrantProvenance { get; set; }
    public StructuredQueryExecutionStatus? ExecutionStatus { get; set; }
    public string? ExecutionDiagnostic { get; set; }
    public int ExecutionRowCount { get; set; }
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ExecutionRows { get; set; } = [];
    public decimal? ActualValue { get; set; }
    public DateOnly? ActualPeriodStart { get; set; }
    public DateOnly? ActualPeriodEnd { get; set; }
    public string? ActualPersianPeriodStart { get; set; }
    public string? ActualPersianPeriodEnd { get; set; }
    public Guid? ActualRowCompanyId { get; set; }
    public int? ActualRowSourceItemId { get; set; }
    public string? ExecutionEffectiveUnit { get; set; }
    public string? ExecutionAuthorizationProvenance { get; set; }
    public double? ExecutionElapsedMs { get; set; }
    public decimal? IndependentDatabaseValue { get; set; }
    public int IndependentLineItemCount { get; set; }
    public StructuredAnswerStatus? PersianAnswerStatus { get; set; }
    public string? PersianAnswer { get; set; }
    public string? PersianAnswerUnit { get; set; }
    public IReadOnlyList<StructuredAnswerEvidence> PersianEvidence { get; set; } = [];
    public bool RestrictedRoleIsSelectOnly { get; set; }
    /// <summary>True when the fixture role can read the other domain's table (statement role → MonthlyReports; monthly role → FinancialStatementLineItems).</summary>
    public bool RestrictedRoleCanReadOtherDomainTable { get; set; }
    public IReadOnlyList<string> RequirementsMeasureIds { get; set; } = [];
    public IReadOnlyList<string> RequirementsNormalizationDiagnostics { get; set; } = [];
    public string? ModelPeriodStart { get; set; }
    public string? ModelPeriodEnd { get; set; }
    public string? NormalizedPersianMonth { get; set; }
    public DateOnly? NormalizedPeriodStart { get; set; }
    public DateOnly? NormalizedPeriodEnd { get; set; }
    public StructuredProductBindingStatus? ProductBindingStatus { get; set; }
    public string? ProductKey { get; set; }
    public string? ActualProductCode { get; set; }
    public string? ActualSourceUnit { get; set; }
    public string? ProductConceptCatalogId { get; set; }
    public IReadOnlyList<string> ProductConceptAliases { get; set; } = [];
    public string? ProductMappingCoverageStatus { get; set; }
    public string? CompanyDiscoveryScopeId { get; set; }
    public bool? CompanyDiscoveryCoverageComplete { get; set; }
    public IReadOnlyList<Guid> GrantAllowedCompanyIds { get; set; } = [];
    public IReadOnlyList<string> ActualCompanySymbols { get; set; } = [];
    public IReadOnlyList<Guid> ActualCompanyIds { get; set; } = [];
    public string? StatementQueryShape { get; set; }
    public IReadOnlyList<Feature141ComparisonExpectation> ComparisonRows { get; set; } = [];
    public IReadOnlyList<string> ExecutionWarnings { get; set; } = [];
    public IReadOnlyDictionary<string, decimal?> IndependentComparisonValues { get; set; } = new Dictionary<string, decimal?>();
    public bool CompiledWithFixtureOnlyColumnApproval { get; set; }
    public IReadOnlyList<string> FixtureOnlyApprovedColumns { get; set; } = [];
    public StructuredQueryExecutionStatus? WritablePrincipalExecutionStatus { get; set; }
    public string? WritablePrincipalDiagnostic { get; set; }
    public int LlmCalls { get; set; }
    public int ObservedModelCalls { get; set; }
    public int ObservedExecutorCalls { get; set; }
    public int? LlmInputTokens { get; set; }
    public int? LlmOutputTokens { get; set; }
    public double IntentLatencyMs { get; set; }
    public double NavigatorLatencyMs { get; set; }
    public double TotalElapsedMs { get; set; }
    public bool SecretValuesLogged => false;
}

/// <summary>
/// Reusable runner: seeds the deterministic fixture into an isolated database, wires the production
/// Feature 141 services from the real composition root, runs the chain and records every stage. It
/// asserts nothing itself so later cases can reuse it and compare their own expectations.
/// </summary>
public sealed class Feature141EngineVerticalSliceRunner(LocalPostgreSqlTestDatabase database, ITestOutputHelper output)
{
    public const string Provider = "NoavaranCurrentApi";
    public const string FixtureAuthorizationProvenance = "feature141-isolated-vertical-slice-fixture";
    public const string FixtureDiscoveryScopeId = "feature141-isolated-fixture-company-discovery-scope";
    public const string FixtureDiscoveryApproval = "fixture-only:feature141-company-discovery:2026-10-29";
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
    private static readonly Guid ActorId = Guid.Parse("8a4f1d8e-1d3b-4a6a-9a0d-6b4b5f3a2c11");
    private static readonly PersianCalendar Persian = new();
    private static readonly JsonSerializerOptions ArtifactJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<Feature141VerticalSliceTrace> RunAsync(Feature141VerticalSliceCase testCase, CancellationToken cancellationToken)
    {
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        var root = FindRepositoryRoot();
        var apiDirectory = Path.Combine(root, "src", "backend", "FinancialCopilot.API");
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var databaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database
            ?? throw new InvalidOperationException("The isolated PostgreSQL test database has no name.");

        var fixtureInfo = await SeedAsync(testCase, databaseName, cancellationToken);
        var restrictedConnection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Username = fixtureInfo.RestrictedRole, Password = _restrictedPassword, Pooling = false
        }.ConnectionString;
        var trace = new Feature141VerticalSliceTrace
            { CaseId = testCase.CaseId, Question = testCase.Question, Fixture = fixtureInfo, ExpectedOutcome = testCase.ExpectedOutcome };
        // Monthly measures and company discovery both take the general planner path.
        var comparison = testCase.Kind == Feature141CaseKind.StatementComparison;
        var monthly = testCase.Kind is Feature141CaseKind.MonthlyMeasure or Feature141CaseKind.CompanyDiscovery;
        var discovery = testCase.Kind == Feature141CaseKind.CompanyDiscovery;
        // A statement-population ranking uses the same fixture-only tenant scope and reviewed universe as discovery.
        var populationScoped = discovery || comparison && testCase.PopulationUniverse is not null;
        (trace.RestrictedRoleIsSelectOnly, trace.RestrictedRoleCanReadOtherDomainTable) = await InspectRestrictedRoleAsync(
            restrictedConnection, fixtureInfo.RestrictedRole,
            monthly ? "MonthlyReportLineItems" : "FinancialStatementLineItems",
            monthly ? "FinancialStatementLineItems" : "MonthlyReports", cancellationToken);
        // Discovery authorization is fixture-scoped: the test tenant's reviewed company universe is the
        // seeded companies except the deliberately unauthorized producer, and the scope id/approval pair
        // exists only in this process. No Development or production scope is configured anywhere.
        var discoveryOverrides = new Dictionary<string, string?>();
        if (populationScoped)
        {
            discoveryOverrides[$"Feature141:QueryExecution:ReviewedTenantCompanyDiscoveryScopes:0"] =
                $"{TenantId:D}|{FixtureDiscoveryScopeId}|{FixtureDiscoveryApproval}";
            var universe = fixtureInfo.ReviewedCompanyUniverse ?? [];
            for (var index = 0; index < universe.Count; index++)
                discoveryOverrides[$"Feature141:QueryExecution:ReviewedTenantCompanyBindings:{index}"] = $"{TenantId:D}:{universe[index]:D}";
        }

        // Production composition root. Both connection strings are redirected to the isolated database so
        // nothing in this graph can reach the Development application database.
        var overrides = new Dictionary<string, string?>
        {
            ["Feature141:Enabled"] = "true",
            ["Feature141:SchemaNavigator:HierarchyPath"] = hierarchyPath,
            ["Feature141:SchemaNavigator:CatalogPath"] = catalogPath,
            ["Feature141:QueryExecution:AuthorizationProvenance"] = FixtureAuthorizationProvenance,
            ["Feature141:QueryExecution:MaximumConcurrency"] = "1",
            ["ConnectionStrings:FinancialCopilot"] = database.ConnectionString,
            ["ConnectionStrings:Feature141ReadOnly"] = restrictedConnection
        };
        foreach (var pair in discoveryOverrides) overrides[pair.Key] = pair.Value;
        var configuration = EvaluationConfigurationLoader.Build(apiDirectory, "Development", configurationOverrides: overrides);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment("Development"));
        services.AddLogging();
        services.AddFinancialCopilotInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IAiProviderCapabilityRegistry>();
        var available = registry.GetAvailableProviders(TenantId)
            .Where(item => (item.Capabilities & AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.SchemaNavigation)) ==
                AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.SchemaNavigation)).ToArray();
        if (available.Length == 0)
            throw new InvalidOperationException("vertical_slice_blocked:no_tenant_eligible_structured_output_provider");
        trace.ProviderKey = available[0].ProviderKey;
        trace.ModelKey = available[0].ModelKey;

        var snapshot = provider.GetRequiredService<StructuredQueryCatalogSnapshot>();
        var hierarchyProvider = provider.GetRequiredService<JsonCatalogNavigationHierarchyProvider>();
        var executionService = new CountingModelExecutionService(provider.GetRequiredService<IAiModelExecutionService>());
        trace.CatalogVersion = snapshot.CatalogVersion;
        trace.CatalogHash = snapshot.CatalogHash;
        using var scope = provider.CreateScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<IStructuredQueryAuthorizer>();
        var executor = new CountingExecutor(scope.ServiceProvider.GetRequiredService<IReadOnlyStructuredQueryExecutor>());

        // Stage 0: the production chat orchestrator's pre-model gate. It is invoked in-process with the
        // counting model service and executor so a refused request provably makes no provider or SQL call.
        var tenantId = testCase.TenantScopeMissing ? Guid.Empty : TenantId;
        var chatService = new Feature141ChatQueryService(configuration, executionService, hierarchyProvider,
            scope.ServiceProvider.GetRequiredService<IOfflineStructuredQueryPlanningService>(),
            provider.GetRequiredService<IStructuredQueryCompiler>(), snapshot, authorizer, executor,
            provider.GetRequiredService<IPersianStructuredQueryResponseGenerator>(),
            NullLogger<Feature141ChatQueryService>.Instance,
            scope.ServiceProvider.GetRequiredService<IQuestionSchemaRequirementsExtractor>());
        var chatRequest = new AiQueryRequest(testCase.Question, tenantId, testCase.TenantScopeMissing ? Guid.Empty : ActorId,
            $"feature141-vertical-slice-{testCase.CaseId}", UserId: testCase.TenantScopeMissing ? null : ActorId,
            ActorType: ActorType.User, AuthenticationMode: AuthenticationMode.WebAppUser);
        trace.ChatGateEnabled = chatService.IsEnabled(chatRequest);
        if (!trace.ChatGateEnabled.Value)
        {
            trace.ChatGateMessage = await chatService.ExecuteAsync(chatRequest, cancellationToken);
            trace.TypedOutcome = Feature141ExpectedOutcome.Unauthorized;
            trace.TypedOutcomeReason = "feature141_chat_gate_refused_missing_tenant_or_actor_scope";
            return Finish(trace, totalTimer, executionService, executor);
        }

        // Stage 1: live canonical intent (identical construction to Feature141ChatQueryService).
        var extractor = new LlmCanonicalSemanticIntentExtractor(executionService, new CanonicalSemanticIntentValidator(),
            configuredDeadline: TimeSpan.FromSeconds(25), maximumOutputTokens: 900, maximumQuestionCharacters: 4000,
            tenantId: TenantId, correlationId: $"feature141-vertical-slice-{testCase.CaseId}");
        var extraction = await extractor.ExtractAsync(testCase.Question, cancellationToken);
        trace.IntentExtractionSucceeded = extraction.Succeeded;
        trace.IntentFailureCategory = extraction.FailureCategory;
        trace.IntentFailureDiagnostic = extraction.SanitizedProviderDiagnostic;
        trace.CanonicalIntent = extraction.Intent;
        trace.LlmCalls += extraction.ModelCalls;
        trace.LlmInputTokens = extraction.InputTokens;
        trace.LlmOutputTokens = extraction.OutputTokens;
        trace.IntentLatencyMs = extraction.Latency.TotalMilliseconds;
        output.WriteLine($"Intent: succeeded={extraction.Succeeded} category={extraction.FailureCategory} diagnostic={extraction.SanitizedProviderDiagnostic} calls={extraction.ModelCalls} latencyMs={extraction.Latency.TotalMilliseconds:F0}");
        if (extraction.Intent is null)
        {
            // The extractor's validator rejects unsupported intents deterministically before any catalog work.
            trace.TypedOutcome = extraction.FailureCategory == "unsupported_semantic_intent"
                ? Feature141ExpectedOutcome.Unsupported : Feature141ExpectedOutcome.Incomplete;
            trace.TypedOutcomeReason = $"intent_extraction_failed:{extraction.FailureCategory}";
            return Finish(trace, totalTimer, executionService, executor);
        }
        var intent = extraction.Intent;

        // Stage 2: approved catalog binding. The typed binding and the chat-path resolver must agree.
        var references = await hierarchyProvider.GetFinancialSourceReferencesAsync(snapshot.CatalogVersion, snapshot.CatalogHash, cancellationToken);
        var typedBinding = CanonicalIntentCatalogBinding.Bind(intent, references);
        trace.TypedBindingStatus = typedBinding.Status;
        trace.TypedBindingDiagnostics = typedBinding.Diagnostics;
        trace.BoundSourceCatalogId = typedBinding.References.FirstOrDefault()?.CatalogId;
        // The typed binding only covers the direct statement-item slice; a monthly measure question takes
        // the general planner path exactly as Feature141ChatQueryService does (requiresGeneralPlanner).
        if (typedBinding.Status != CanonicalCatalogBindingStatus.Bound && !monthly)
        {
            trace.TypedOutcome = typedBinding.Status switch
            {
                CanonicalCatalogBindingStatus.Ambiguous => Feature141ExpectedOutcome.Ambiguous,
                CanonicalCatalogBindingStatus.Unsupported => Feature141ExpectedOutcome.Unsupported,
                CanonicalCatalogBindingStatus.Unauthorized => Feature141ExpectedOutcome.Unauthorized,
                _ => Feature141ExpectedOutcome.Incomplete
            };
            trace.TypedOutcomeReason = string.Join("|", typedBinding.Diagnostics);
            output.WriteLine($"Typed rejection: {typedBinding.Status} ({trace.TypedOutcomeReason})");
            return Finish(trace, totalTimer, executionService, executor);
        }
        // A threshold filter may name a second statement item as a predicate; everything else is one measure.
        if (intent.Measures.Count != 1 && StatementQueryShapeClassifier.Classify(intent).Shape != StatementQueryShape.Filter)
            return Finish(trace, totalTimer, executionService, executor);

        // Stage 3: live navigator and deterministic planner over the real hierarchy provider. Company and
        // product identities come from the production resolvers over the isolated database.
        await using var bindingDb = database.CreateContext();
        var companyBinder = new StructuredQueryCompanyBinder(new CanonicalQueryEntityResolver(bindingDb,
            Options.Create(new CanonicalEntityResolutionOptions(Enabled: true))));
        var productBinder = new StructuredQueryProductBinder(new CompanyScopedProductResolver(
            new CompanyResolverService(bindingDb, NullLogger<CompanyResolverService>.Instance),
            new EfCoreMonthlyProductComparisonRepository(bindingDb)), new EfCoreMonthlyProductComparisonRepository(bindingDb));
        var navigator = new LlmVectorlessSchemaNavigator(executionService, hierarchyProvider, hierarchyProvider, hierarchyProvider,
            provider.GetRequiredService<ISchemaContextCompletenessValidator>(), provider.GetRequiredService<SchemaNavigatorBudgets>());
        var planner = new OfflineStructuredQueryPlanningService(
            navigator, navigator,
            provider.GetRequiredService<ICatalogNavigationHierarchyProvider>(),
            provider.GetRequiredService<ISchemaContextCompletenessValidator>(),
            provider.GetRequiredService<IStructuredQueryPlanBuilder>(),
            provider.GetRequiredService<IStructuredQueryPlanValidator>(),
            companyBinder, productBinder,
            productConceptResolver: provider.GetRequiredService<IReviewedProductConceptResolver>(),
            companyDiscoveryScopeResolver: populationScoped
                // The comparison fixture universe is exactly the seeded authorized companies, so its coverage is complete
                // by construction; the discovery fixture keeps the owner-pending incomplete coverage (decision D2-D4).
                ? new AuthorizedCompanyDiscoveryScopeResolver([new AuthorizedCompanyDiscoveryScope(TenantId,
                    FixtureDiscoveryScopeId, FixtureDiscoveryApproval, CompanyCoverageComplete: comparison)])
                : provider.GetRequiredService<IAuthorizedCompanyDiscoveryScopeResolver>());
        var navigationRequest = new SchemaNavigationRequest(1, testCase.Question, snapshot.CatalogVersion, snapshot.CatalogHash,
            TenantId: TenantId, CorrelationId: $"feature141-vertical-slice-{testCase.CaseId}");
        QuestionSchemaRequirements plannerRequirements;
        if (monthly)
        {
            // General planner path (Feature141ChatQueryService.requiresGeneralPlanner): the navigator extracts
            // requirements; the Persian month named by the model is normalized deterministically and the
            // model's Gregorian bounds are replaced before planning.
            var extracted = await navigator.ExtractAsync(navigationRequest, cancellationToken);
            trace.LlmCalls += extracted.ModelCalls;
            trace.LlmInputTokens = Sum(trace.LlmInputTokens, extracted.InputTokens);
            trace.LlmOutputTokens = Sum(trace.LlmOutputTokens, extracted.OutputTokens);
            trace.ModelPeriodStart = extracted.StructuredShape?.ReportingPeriodStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            trace.ModelPeriodEnd = extracted.StructuredShape?.ReportingPeriodEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (intent.Period.Kind != ReportingPeriodKind.ExplicitMonth)
            {
                trace.TypedOutcome = Feature141ExpectedOutcome.Incomplete;
                trace.TypedOutcomeReason = $"period_kind_not_explicit_month:{intent.Period.Kind}";
                return Finish(trace, totalTimer, executionService, executor);
            }
            var period = PersianReportingPeriodNormalizer.Normalize(intent.Period);
            trace.NormalizedPersianMonth = period.PersianLabel;
            trace.NormalizedPeriodStart = period.Start;
            trace.NormalizedPeriodEnd = period.End;
            if (period.Status != PersianReportingPeriodStatus.Normalized)
            {
                trace.TypedOutcome = Feature141ExpectedOutcome.Incomplete;
                trace.TypedOutcomeReason = $"persian_period_not_normalized:{period.DiagnosticCode}";
                return Finish(trace, totalTimer, executionService, executor);
            }
            plannerRequirements = ReportingPeriodRequirementsNormalizer.Apply(extracted, period);
            output.WriteLine($"Period: model={trace.ModelPeriodStart}..{trace.ModelPeriodEnd} normalized={period.PersianLabel}={period.Start:yyyy-MM-dd}..{period.End:yyyy-MM-dd}");
        }
        else
        {
            // Identical to Feature141ChatQueryService: approved canonical binding first, otherwise the exact
            // registered title of the measure phrase (identity-authorized retrieval).
            var canonical = CanonicalSourceItemBindingResolver.Resolve(intent.Measures[0], references, Provider);
            trace.ChatPathBindingStatus = canonical.Status;
            trace.ChatPathBindingDiagnostic = canonical.DiagnosticCode;
            var resolution = canonical.Status == CanonicalSourceItemBindingStatus.Bound && canonical.Reference is { } bound
                ? new FinancialSourceTitleResolution(FinancialSourceTitleResolutionStatus.Resolved,
                    canonical.DiagnosticCode, bound, [bound], new Dictionary<string, string?>(), bound.UnitClassification,
                    bound.QueryAuthorizationStatus ?? (bound.Queryable ? "authorized" : "not-authorized"))
                : FinancialSourceItemTitleResolver.Resolve(new FinancialSourceTitleResolutionRequest(intent.Measures[0].UserPhrase, Provider), references);
            if (resolution.Status != FinancialSourceTitleResolutionStatus.Resolved || resolution.Reference is not { } resolvedReference)
            {
                trace.TypedOutcome = resolution.Status == FinancialSourceTitleResolutionStatus.Ambiguous
                    ? Feature141ExpectedOutcome.Ambiguous : Feature141ExpectedOutcome.Incomplete;
                trace.TypedOutcomeReason = $"source_identity_{resolution.Status.ToString().ToLowerInvariant()}:{resolution.DiagnosticCode}";
                return Finish(trace, totalTimer, executionService, executor);
            }
            bound = resolvedReference;
            var directSource = snapshot.DirectSources.SingleOrDefault(source =>
                string.Equals(source.CatalogId, bound.CatalogId, StringComparison.Ordinal));
            // The intent's shape (not the test case) selects the single-company or the cross-company handoff.
            var shape = StatementQueryShapeClassifier.Classify(intent);
            trace.StatementQueryShape = shape.Shape?.ToString();
            var handoff = shape.Shape is StatementQueryShape.MultiCompanyComparison or StatementQueryShape.Ranking or StatementQueryShape.Filter
                ? FinancialSourcePlannerHandoff.CreateCrossCompany(intent, resolution, snapshot.CatalogVersion, snapshot.CatalogHash, directSource,
                    references)
                : FinancialSourcePlannerHandoff.Create(intent, resolution, snapshot.CatalogVersion, snapshot.CatalogHash, directSource);
            trace.HandoffStatus = handoff.Status;
            trace.HandoffDiagnosticCode = handoff.DiagnosticCode;
            if (handoff.Binding is null)
            {
                trace.TypedOutcome = handoff.Status == FinancialSourcePlannerHandoffStatus.Ambiguous
                    ? Feature141ExpectedOutcome.Ambiguous : Feature141ExpectedOutcome.Incomplete;
                trace.TypedOutcomeReason = handoff.DiagnosticCode;
                return Finish(trace, totalTimer, executionService, executor);
            }
            plannerRequirements = handoff.Binding.PlannerRequirements;
        }
        trace.PlannerRequirements = plannerRequirements;
        trace.RequirementsMeasureIds = plannerRequirements.MeasureIds;
        trace.RequirementsNormalizationDiagnostics = plannerRequirements.NormalizationDiagnostics;
        var planning = await planner.PlanAsync(navigationRequest, plannerRequirements, cancellationToken);
        trace.ProductBindingStatus = planning.ProductBinding?.Status;
        trace.ProductKey = planning.ProductBinding?.ProductKey;
        trace.ProductConceptCatalogId = planning.Plan.ProductConcept?.CatalogId;
        trace.ProductConceptAliases = planning.Plan.ProductConcept?.ApprovedAliases ?? [];
        trace.ProductMappingCoverageStatus = planning.Plan.ProductConcept?.MappingCoverageStatus;
        trace.CompanyDiscoveryScopeId = planning.Plan.CompanyDiscoveryScope?.ScopeId;
        trace.CompanyDiscoveryCoverageComplete = planning.Plan.CompanyDiscoveryScope?.CompanyCoverageComplete;
        trace.Navigation = planning.Navigation;
        trace.Plan = planning.Plan;
        trace.PlanStructurallyValid = planning.Validation.IsStructurallyValid;
        trace.PlanExecutionEligible = planning.Validation.ExecutionEligible;
        trace.PlanDiagnostics = planning.Validation.Diagnostics;
        trace.PlanStatementScopes = planning.Plan.Statement.Scopes;
        trace.CompanyBindingStatus = planning.CompanyBinding?.Status;
        trace.BoundCompanyId = planning.CompanyBinding?.CompanyId;
        trace.LlmCalls += planning.Navigation.ModelCalls;
        trace.LlmInputTokens = Sum(trace.LlmInputTokens, planning.Navigation.InputTokens);
        trace.LlmOutputTokens = Sum(trace.LlmOutputTokens, planning.Navigation.OutputTokens);
        trace.NavigatorLatencyMs = planning.Navigation.ModelLatency?.TotalMilliseconds ?? 0;
        output.WriteLine($"Plan: valid={planning.Validation.IsStructurallyValid} eligible={planning.Validation.ExecutionEligible} diagnostics={string.Join('|', planning.Validation.Diagnostics)} company={planning.CompanyBinding?.Status}/{planning.CompanyBinding?.CompanyId}");
        if (!planning.Validation.ExecutionEligible)
        {
            trace.TypedOutcome = Feature141ExpectedOutcome.Incomplete;
            trace.TypedOutcomeReason = string.Join("|", planning.Validation.Diagnostics);
            return Finish(trace, totalTimer, executionService, executor);
        }

        // Stage 4: compile the plan exactly as validated; no governance flag is modified here.
        // Discovery output needs Companies.Name/Ticker, which the canonical catalog does not yet authorize
        // (approvedObjects = Id only). The runner compiles against a fixture-only snapshot that adds those two
        // columns and records that fact in the trace; it is readiness evidence, not a catalog approval.
        var compileSnapshot = discovery ? DiscoveryFixtureSnapshot(snapshot, trace) : snapshot;
        var compilation = provider.GetRequiredService<IStructuredQueryCompiler>().Compile(planning.Plan, compileSnapshot);
        trace.CompilationSucceeded = compilation.Success;
        trace.CompilationDiagnostics = compilation.Diagnostics;
        if (!compilation.Success || compilation.Query is null) return Finish(trace, totalTimer, executionService, executor);
        var query = compilation.Query;
        trace.Sql = query.Sql;
        trace.SqlParameters = query.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.Ordinal);
        trace.QueryFingerprint = query.QueryFingerprint;
        trace.CompiledEffectiveUnit = query.EffectiveUnit;
        trace.CompiledOperation = query.Operation;
        trace.CompiledCompanyIds = query.CompanyIds;
        trace.CompiledSemanticReviewApproved = query.SemanticReviewApproved;
        trace.CompiledIdentityAuthorized = query.IdentityAuthorized;
        trace.CompiledQueryAuthorized = query.QueryAuthorized;

        // Stage 5 and 6: catalog authorizer and bounded read-only executor from the composition root.
        var grant = authorizer.Authorize(query, TenantId);
        trace.GrantIssued = grant is not null;
        trace.GrantDatabase = grant?.DatabaseName;
        trace.GrantRole = grant?.DatabaseRole;
        trace.GrantProvenance = grant?.Provenance;
        trace.GrantAllowedCompanyIds = grant?.AllowedCompanyIds.OrderBy(id => id).ToArray() ?? [];
        if (grant is null)
        {
            trace.TypedOutcome = Feature141ExpectedOutcome.Unauthorized;
            trace.TypedOutcomeReason = "catalog_authorizer_refused_compiled_query";
            return Finish(trace, totalTimer, executionService, executor);
        }
        var execution = await executor.ExecuteAsync(query, grant, cancellationToken);
        trace.ExecutionStatus = execution.Status;
        trace.ExecutionDiagnostic = execution.DiagnosticCode;
        trace.ExecutionRowCount = execution.Rows.Count;
        trace.ExecutionRows = execution.Rows;
        trace.ExecutionEffectiveUnit = execution.EffectiveUnit;
        trace.ExecutionAuthorizationProvenance = execution.AuthorizationProvenance;
        trace.ExecutionElapsedMs = execution.Elapsed?.TotalMilliseconds;
        output.WriteLine($"Execution: status={execution.Status} diagnostic={execution.DiagnosticCode} rows={execution.Rows.Count} unit={execution.EffectiveUnit}");
        if (discovery)
        {
            trace.ActualCompanyIds = execution.Rows.Select(row => row.GetValueOrDefault("CompanyId")).OfType<Guid>().ToArray();
            trace.ActualCompanySymbols = execution.Rows.Select(row => row.GetValueOrDefault("CompanySymbol") as string
                ?? row.GetValueOrDefault("CompanyName") as string ?? string.Empty).ToArray();
        }
        trace.ExecutionWarnings = execution.Warnings;
        if (comparison)
        {
            trace.ComparisonRows = execution.Rows.Where(row => row.ContainsKey("Rank")).Select(row => new Feature141ComparisonExpectation(
                (string)row["CompanyLabel"]!, Convert.ToDecimal(row["Value"], CultureInfo.InvariantCulture), (int)row["Rank"]!,
                (bool)row["TiedWithOthers"]!)).ToArray();
            if (execution.Rows.Count > 0)
            {
                trace.ActualPeriodStart = ToDate(execution.Rows[0].GetValueOrDefault("PeriodStart"));
                trace.ActualPeriodEnd = ToDate(execution.Rows[0].GetValueOrDefault("PeriodEnd"));
                trace.ActualPersianPeriodStart = trace.ActualPeriodStart is { } start ? ToPersianCalendar(start) : null;
                trace.ActualPersianPeriodEnd = trace.ActualPeriodEnd is { } end ? ToPersianCalendar(end) : null;
            }
        }
        else if (execution.Rows.Count == 1)
        {
            var row = execution.Rows[0];
            trace.ActualValue = row.GetValueOrDefault("Value") is { } value ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : null;
            trace.ActualPeriodStart = ToDate(row.GetValueOrDefault("PeriodStart"));
            trace.ActualPeriodEnd = ToDate(row.GetValueOrDefault("PeriodEnd"));
            trace.ActualPersianPeriodStart = trace.ActualPeriodStart is { } start ? ToPersianCalendar(start) : null;
            trace.ActualPersianPeriodEnd = trace.ActualPeriodEnd is { } end ? ToPersianCalendar(end) : null;
            trace.ActualRowCompanyId = row.GetValueOrDefault("CompanyId") as Guid?;
            trace.ActualRowSourceItemId = row.GetValueOrDefault("SourceItemId") is { } item ? Convert.ToInt32(item, CultureInfo.InvariantCulture) : null;
            trace.ActualProductCode = row.GetValueOrDefault("ProductCode") as string;
            trace.ActualSourceUnit = row.GetValueOrDefault("SourceUnit") as string;
        }

        // Independent verification straight from the seeded rows with the administrative connection.
        if (comparison)
            (trace.IndependentComparisonValues, trace.IndependentLineItemCount) = await ReadIndependentComparisonAsync(
                testCase, fixtureInfo, trace.ActualPeriodEnd, cancellationToken);
        else
        (trace.IndependentDatabaseValue, trace.IndependentLineItemCount) = discovery
            ? await ReadIndependentDiscoveryAsync(testCase, cancellationToken)
            : monthly
            ? await ReadIndependentMonthlyValueAsync(fixtureInfo, cancellationToken)
            : await ReadIndependentValueAsync(fixtureInfo, fixtureInfo.SeededSourceItemCatalogId ?? testCase.ExpectedSourceItemCatalogId, cancellationToken);

        // Stage 7: Persian answer.
        var answer = provider.GetRequiredService<IPersianStructuredQueryResponseGenerator>().Generate(
            new StructuredQueryAnswerRequest(planning.Plan, execution, "postgresql_execution",
                planning.Validation.Diagnostics.Concat(compilation.Diagnostics).Distinct(StringComparer.Ordinal).ToArray()));
        trace.PersianAnswerStatus = answer.Status;
        trace.PersianAnswer = answer.TextAnswer;
        trace.PersianAnswerUnit = answer.Unit;
        trace.PersianEvidence = answer.Evidence;
        trace.TypedOutcome = answer.Status == StructuredAnswerStatus.Answered ? Feature141ExpectedOutcome.Answered
            : answer.Status == StructuredAnswerStatus.Ambiguous ? Feature141ExpectedOutcome.Ambiguous
            : Feature141ExpectedOutcome.Incomplete;
        output.WriteLine($"Persian answer: {answer.TextAnswer}");

        // Negative control: the same sealed query under the writable administrative principal is refused.
        var adminBuilder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        var adminOptions = Options.Create(new StructuredQueryExecutionOptions
        {
            Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
            ConnectionString = database.ConnectionString, DesignatedDatabase = databaseName,
            ReadOnlyRole = adminBuilder.Username ?? string.Empty, CommandTimeoutSeconds = 5, MaximumRows = 100,
            MaximumResultBytes = 1_000_000, MaximumConcurrency = 1, AuthorizationProvenance = FixtureAuthorizationProvenance,
            ReviewedTenantCompanyDiscoveryScopes = populationScoped ? [$"{TenantId:D}|{FixtureDiscoveryScopeId}|{FixtureDiscoveryApproval}"] : [],
            ReviewedTenantCompanyBindings = populationScoped
                ? (fixtureInfo.ReviewedCompanyUniverse ?? []).Select(id => $"{TenantId:D}:{id:D}").ToArray() : []
        });
        var host = new TestEnvironment("Development");
        var adminGrant = new CatalogStructuredQueryAuthorizer(adminOptions, host).Authorize(query, TenantId)
            ?? throw new InvalidOperationException("negative control could not obtain a grant for the writable principal");
        var adminExecution = await new NpgsqlReadOnlyStructuredQueryExecutor(adminOptions,
            NullLogger<NpgsqlReadOnlyStructuredQueryExecutor>.Instance, host).ExecuteAsync(query, adminGrant, cancellationToken);
        trace.WritablePrincipalExecutionStatus = adminExecution.Status;
        trace.WritablePrincipalDiagnostic = adminExecution.DiagnosticCode;
        return Finish(trace, totalTimer, executionService, executor);
    }

    private string _restrictedPassword = string.Empty;

    private async Task<Feature141VerticalSliceFixtureInfo> SeedAsync(Feature141VerticalSliceCase testCase, string databaseName,
        CancellationToken cancellationToken)
    {
        if (testCase.Kind == Feature141CaseKind.MonthlyMeasure)
            return await SeedMonthlyAsync(testCase, databaseName, cancellationToken);
        if (testCase.Kind == Feature141CaseKind.CompanyDiscovery)
            return await SeedDiscoveryAsync(testCase, databaseName, cancellationToken);
        if (testCase.Kind == Feature141CaseKind.StatementComparison)
            return await SeedComparisonAsync(testCase, databaseName, cancellationToken);
        // The persisted source-item identity comes from the canonical catalog (never typed into the fixture).
        var catalogRoot = Path.Combine(FindRepositoryRoot(), "specs", "141-schema-aware-semantic-query-engine");
        var catalogSnapshot = new StructuredQueryCatalogSnapshotBuilder().Build(Path.Combine(catalogRoot, "SemanticCatalog"),
            Path.Combine(catalogRoot, "artifacts", "navigation-hierarchy.yaml"));
        var catalogSource = catalogSnapshot.DirectSources.Single(source => source.ProviderName == Provider
            && source.StatementType == testCase.ExpectedStatementType && source.SourceItemId == testCase.ExpectedSourceItemId);
        var expectedSourceItem = catalogSource.SourceItemCatalogId;
        var expectedValue = testCase.ExpectedValue ?? 3_753m;
        var periodStart = testCase.ExpectedPeriodStart ?? new DateOnly(2026, 3, 21);
        var periodEnd = testCase.ExpectedPeriodEnd ?? new DateOnly(2026, 6, 21);
        var marketId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var decoyCompanyId = Guid.NewGuid();
        var statementId = Guid.NewGuid();
        var olderStatementId = Guid.NewGuid();
        var decoyStatementId = Guid.NewGuid();
        var decoySourceItemId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow
            {
                Id = marketId, ProviderName = Provider, ExternalId = "feature141-vertical-slice-market",
                Name = "Feature 141 vertical slice fixture", LastSynchronizedAt = now
            });
            var decoyTicker = testCase.ExpectedCompanyLabel == "کگل" ? "کگهر" : "کگل";
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = companyId, ProviderName = Provider, ExternalCompanyId = "feature141-vs-expected", Name = $"Fixture {testCase.ExpectedCompanyLabel}",
                Ticker = testCase.ExpectedCompanyLabel, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now
            });
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = decoyCompanyId, ProviderName = Provider, ExternalCompanyId = "feature141-vs-decoy", Name = $"Fixture {decoyTicker}",
                Ticker = decoyTicker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now
            });
            db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow
            {
                Id = expectedSourceItem, ProviderName = Provider, StatementType = testCase.ExpectedStatementType,
                SourceItemId = testCase.ExpectedSourceItemId, TitleFa = testCase.ExpectedTitleFa, Unit = "N/A", LastSynchronizedAt = now
            });
            db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow
            {
                Id = decoySourceItemId, ProviderName = Provider, StatementType = testCase.ExpectedStatementType,
                SourceItemId = 999_162, TitleFa = "قلم آزمایشی دیگر", Unit = "N/A", LastSynchronizedAt = now
            });
            NormalizedFinancialStatementRow Statement(Guid id, Guid company, string externalCompany, string externalId,
                DateOnly start, DateOnly end) => new()
            {
                Id = id, CompanyId = company, ProviderName = Provider, ExternalCompanyId = externalCompany,
                ExternalStatementId = externalId, StatementType = testCase.ExpectedStatementType, PeriodType = "ThreeMonths",
                PeriodStart = start, PeriodEnd = end, PublishedAt = end.AddDays(31), IsComposing = false,
                SourcePayloadChecksum = $"feature141-vs-{externalId}", LastSynchronizedAt = now
            };
            db.FinancialStatements.Add(Statement(statementId, companyId, "feature141-vs-kgehr", "vs-kgehr-latest", periodStart, periodEnd));
            db.FinancialStatements.Add(Statement(olderStatementId, companyId, "feature141-vs-kgehr", "vs-kgehr-older",
                periodStart.AddMonths(-3), periodStart.AddDays(-1)));
            db.FinancialStatements.Add(Statement(decoyStatementId, decoyCompanyId, "feature141-vs-kgol", "vs-kgol-latest", periodStart, periodEnd));
            NormalizedFinancialStatementLineItemRow Line(Guid statement, Guid sourceItem, decimal value) => new()
            {
                Id = Guid.NewGuid(), FinancialStatementId = statement, SourceItemCatalogId = sourceItem, Value = value
            };
            // Expected value, plus decoys that a wrong company, wrong item or wrong period would surface.
            db.FinancialStatementLineItems.Add(Line(statementId, expectedSourceItem, expectedValue));
            db.FinancialStatementLineItems.Add(Line(statementId, decoySourceItemId, 5_555m));
            db.FinancialStatementLineItems.Add(Line(olderStatementId, expectedSourceItem, 3_001m));
            db.FinancialStatementLineItems.Add(Line(decoyStatementId, expectedSourceItem, 9_999m));
            await db.SaveChangesAsync(cancellationToken);
        }

        var role = "feature141_vs_reader_" + Guid.NewGuid().ToString("N");
        _restrictedPassword = "F141vs_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{_restrictedPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; " +
            $"GRANT SELECT ON TABLE public.\"FinancialStatements\", public.\"FinancialStatementLineItems\", " +
            $"public.\"FinancialStatementSourceItems\", public.\"Companies\" TO \"{role}\";";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new(databaseName, role, companyId, decoyCompanyId, statementId, olderStatementId, SeededLineItemCount: 4,
            SeededSourceItemCatalogId: expectedSourceItem);
    }

    /// <summary>Seeds the monthly production fixture: one accepted Shahrivar 1405 report for کگل with a گندله line (25 ton)
    /// plus decoys a wrong product, a wrong month, a superseded revision of the same month and a wrong company would surface.</summary>
    private async Task<Feature141VerticalSliceFixtureInfo> SeedMonthlyAsync(Feature141VerticalSliceCase testCase, string databaseName,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var marketId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var decoyCompanyId = Guid.NewGuid();
        const string externalCompany = "feature141-vs-kgol";
        const string decoyExternalCompany = "feature141-vs-kgehr";
        var periodStart = testCase.ExpectedPeriodStart ?? new DateOnly(2026, 8, 23);
        var periodEnd = testCase.ExpectedPeriodEnd ?? new DateOnly(2026, 9, 22);
        // Amount cases keep "ton" as the row (quantity) unit; the MillionIRR amount unit comes from the catalog.
        var rowUnit = testCase.MonthlyValueColumn == Feature141MonthlyValueColumn.SalesAmount ? "ton" : testCase.ExpectedUnit!;
        var productKey = $"{Provider}:{externalCompany}:TITLE:{testCase.ExpectedProductLabel}|UNIT:{rowUnit}";
        var otherProductKey = $"{Provider}:{externalCompany}:TITLE:کنسانتره|UNIT:{rowUnit}";
        var decoyProductKey = $"{Provider}:{decoyExternalCompany}:TITLE:{testCase.ExpectedProductLabel}|UNIT:{rowUnit}";
        Guid reportId;
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "feature141-vs-monthly-market",
                Name = "Feature 141 monthly vertical slice fixture", LastSynchronizedAt = now });
            db.Companies.Add(new NormalizedCompanyRow { Id = companyId, ProviderName = Provider, ExternalCompanyId = externalCompany,
                Name = "Fixture Kgol", Ticker = testCase.ExpectedCompanyLabel, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
            var decoyTicker = testCase.ExpectedCompanyLabel == "کگهر" ? "کگل" : "کگهر";
            db.Companies.Add(new NormalizedCompanyRow { Id = decoyCompanyId, ProviderName = Provider, ExternalCompanyId = decoyExternalCompany,
                Name = $"Fixture {decoyTicker}", Ticker = decoyTicker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
            NormalizedMonthlyReportRow Report(Guid company, string externalCompanyId, string suffix, DateOnly start, DateOnly end,
                bool accepted, string revisionStatus) => new()
            {
                Id = Guid.NewGuid(), ProviderName = Provider, ExternalCompanyId = externalCompanyId, ExternalReportId = $"vs-{suffix}",
                PeriodStart = start, PeriodEnd = end, SourcePayloadChecksum = $"vs-{suffix}", LastSynchronizedAt = now,
                LogicalReportKey = $"vs-{externalCompanyId}-{start:yyyyMM}", RevisionFingerprint = $"vs-{suffix}", IsAccepted = accepted,
                RevisionStatus = revisionStatus, ReportType = "ProductSales", OutputType = 0, CompanyId = company
            };
            // The case's expected value goes into the requested column; the other two columns carry distinct decoy
            // values so a wrong-column projection would surface.
            NormalizedMonthlyReportLineItemRow Line(NormalizedMonthlyReportRow report, string key, string title, decimal value) => new()
            {
                Id = Guid.NewGuid(), MonthlyReportId = report.Id, SourceRowFingerprint = $"row-{report.ExternalReportId}-{key}",
                SourceMultiplicity = 1, ProductCode = key, Title = title, Unit = rowUnit,
                ProductionQuantity = testCase.MonthlyValueColumn == Feature141MonthlyValueColumn.ProductionQuantity ? value : value + 1_000m,
                SalesQuantity = testCase.MonthlyValueColumn == Feature141MonthlyValueColumn.SalesQuantity ? value : value + 2_000m,
                SalesAmount = testCase.MonthlyValueColumn == Feature141MonthlyValueColumn.SalesAmount ? value : value + 3_000m,
                SalesRate = 10m, SourcePayloadChecksum = report.SourcePayloadChecksum
            };
            var expected = Report(companyId, externalCompany, "kgol-1405-06", periodStart, periodEnd, true, "Accepted");
            var superseded = Report(companyId, externalCompany, "kgol-1405-06-superseded", periodStart, periodEnd, false, "Superseded");
            var earlier = Report(companyId, externalCompany, "kgol-1405-05", periodStart.AddMonths(-1), periodStart.AddDays(-1), true, "Accepted");
            var decoy = Report(decoyCompanyId, decoyExternalCompany, "kgehr-1405-06", periodStart, periodEnd, true, "Accepted");
            db.MonthlyReports.AddRange(expected, superseded, earlier, decoy);
            db.MonthlyReportLineItems.AddRange(
                Line(expected, productKey, testCase.ExpectedProductLabel!, testCase.ExpectedValue ?? 25m),
                Line(expected, otherProductKey, "کنسانتره", 60m),
                Line(superseded, productKey, testCase.ExpectedProductLabel!, 99m),
                Line(earlier, productKey, testCase.ExpectedProductLabel!, 22m),
                Line(decoy, decoyProductKey, testCase.ExpectedProductLabel!, 47m));
            reportId = expected.Id;
            await db.SaveChangesAsync(cancellationToken);
        }

        var role = "feature141_vs_monthly_reader_" + Guid.NewGuid().ToString("N");
        _restrictedPassword = "F141vs_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{_restrictedPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; " +
            $"GRANT SELECT ON TABLE public.\"MonthlyReports\", public.\"MonthlyReportLineItems\", public.\"Companies\" TO \"{role}\";";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new(databaseName, role, companyId, decoyCompanyId, Guid.Empty, Guid.Empty, SeededLineItemCount: 5, reportId, productKey,
            MonthlyValueColumn: testCase.MonthlyValueColumn);
    }

    /// <summary>
    /// Seeds the discovery fixture for Shahrivar 1405: کگل (گندله 25 ton) and کگهر (گندله سنگ‌آهن 47 ton) are
    /// the expected producers. Decoys: فملی reports گندله with zero quantity, کچاد with NULL quantity, وبملت
    /// produced گندله only in Mordad, فولاد produces کنسانتره only, and an unauthorized producer (positive
    /// گندله production) is deliberately left out of the reviewed company universe.
    /// </summary>
    private async Task<Feature141VerticalSliceFixtureInfo> SeedDiscoveryAsync(Feature141VerticalSliceCase testCase, string databaseName,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var marketId = Guid.NewGuid();
        var periodStart = testCase.ExpectedPeriodStart ?? new DateOnly(2026, 8, 23);
        var periodEnd = testCase.ExpectedPeriodEnd ?? new DateOnly(2026, 9, 22);
        var tickers = new[] { "کگل", "کگهر", "فملی", "کچاد", "وبملت", "فولاد", "UNAUTH" };
        var companies = tickers.ToDictionary(t => t, _ => Guid.NewGuid(), StringComparer.Ordinal);
        string External(string ticker) => $"feature141-vs-disc-{ticker}";
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "feature141-vs-discovery-market",
                Name = "Feature 141 discovery fixture", LastSynchronizedAt = now });
            foreach (var (ticker, id) in companies)
                db.Companies.Add(new NormalizedCompanyRow { Id = id, ProviderName = Provider, ExternalCompanyId = External(ticker),
                    Name = $"Fixture {ticker}", Ticker = ticker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
            NormalizedMonthlyReportRow Report(string ticker, string suffix, DateOnly start, DateOnly end)
            {
                var report = new NormalizedMonthlyReportRow
                {
                    Id = Guid.NewGuid(), ProviderName = Provider, ExternalCompanyId = External(ticker), ExternalReportId = $"vs-disc-{ticker}-{suffix}",
                    PeriodStart = start, PeriodEnd = end, SourcePayloadChecksum = $"vs-disc-{ticker}-{suffix}", LastSynchronizedAt = now,
                    LogicalReportKey = $"vs-disc-{ticker}-{start:yyyyMM}", RevisionFingerprint = $"vs-disc-{ticker}-{suffix}", IsAccepted = true,
                    RevisionStatus = "Accepted", ReportType = "ProductSales", OutputType = 0, CompanyId = companies[ticker]
                };
                db.MonthlyReports.Add(report);
                return report;
            }
            void Line(NormalizedMonthlyReportRow report, string ticker, string title, decimal? production, string unit = "ton") =>
                db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
                {
                    Id = Guid.NewGuid(), MonthlyReportId = report.Id, SourceRowFingerprint = $"row-{report.ExternalReportId}-{title}-{unit}",
                    SourceMultiplicity = 1, ProductCode = $"{Provider}:{External(ticker)}:TITLE:{title}|UNIT:{unit}", Title = title, Unit = unit,
                    ProductionQuantity = production, SalesQuantity = production, SalesAmount = production * 10, SalesRate = 10m,
                    SourcePayloadChecksum = report.SourcePayloadChecksum
                });
            Line(Report("کگل", "1405-06", periodStart, periodEnd), "کگل", "گندله", 25m);
            Line(Report("کگهر", "1405-06", periodStart, periodEnd), "کگهر", "گندله سنگ‌آهن", 47m);
            Line(Report("فملی", "1405-06", periodStart, periodEnd), "فملی", "گندله", 0m);
            Line(Report("کچاد", "1405-06", periodStart, periodEnd), "کچاد", "گندله", null);
            Line(Report("وبملت", "1405-05", periodStart.AddMonths(-1), periodStart.AddDays(-1)), "وبملت", "گندله", 30m);
            Line(Report("فولاد", "1405-06", periodStart, periodEnd), "فولاد", "کنسانتره", 60m);
            Line(Report("UNAUTH", "1405-06", periodStart, periodEnd), "UNAUTH", "گندله", 80m);
            await db.SaveChangesAsync(cancellationToken);
        }

        var role = "feature141_vs_discovery_reader_" + Guid.NewGuid().ToString("N");
        _restrictedPassword = "F141vs_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{_restrictedPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; " +
            $"GRANT SELECT ON TABLE public.\"MonthlyReports\", public.\"MonthlyReportLineItems\", public.\"Companies\" TO \"{role}\";";
        await command.ExecuteNonQueryAsync(cancellationToken);
        var universe = companies.Where(pair => pair.Key != "UNAUTH").Select(pair => pair.Value).OrderBy(id => id).ToArray();
        return new(databaseName, role, companies["کگل"], companies["کگهر"], Guid.Empty, Guid.Empty, SeededLineItemCount: 7,
            ReviewedCompanyUniverse: universe, UnauthorizedProducerId: companies["UNAUTH"], CompaniesBySymbol: companies);
    }

    /// <summary>
    /// Seeds the cross-company comparison fixture from the case: the companies, their statements of the case's statement
    /// item (source identity read from the catalog), and on every statement another item with a large decoy value.
    /// </summary>
    private async Task<Feature141VerticalSliceFixtureInfo> SeedComparisonAsync(Feature141VerticalSliceCase testCase, string databaseName,
        CancellationToken cancellationToken)
    {
        var catalogRoot = Path.Combine(FindRepositoryRoot(), "specs", "141-schema-aware-semantic-query-engine");
        var catalogSnapshot = new StructuredQueryCatalogSnapshotBuilder().Build(Path.Combine(catalogRoot, "SemanticCatalog"),
            Path.Combine(catalogRoot, "artifacts", "navigation-hierarchy.yaml"));
        var sourceItem = catalogSnapshot.DirectSources.Single(source => source.ProviderName == Provider
            && source.StatementType == testCase.ExpectedStatementType && source.SourceItemId == testCase.ExpectedSourceItemId).SourceItemCatalogId;
        var otherItem = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var marketId = Guid.NewGuid();
        var companies = (testCase.ComparisonCompanies ?? []).ToDictionary(ticker => ticker, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var lineItems = 0;
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "feature141-vs-comparison-market",
                Name = "Feature 141 comparison fixture", LastSynchronizedAt = now });
            foreach (var (ticker, id) in companies)
                db.Companies.Add(new NormalizedCompanyRow { Id = id, ProviderName = Provider, ExternalCompanyId = $"feature141-vs-cmp-{id:N}",
                    Name = $"Fixture {ticker}", Ticker = ticker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
            db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow { Id = sourceItem, ProviderName = Provider,
                StatementType = testCase.ExpectedStatementType, SourceItemId = testCase.ExpectedSourceItemId, TitleFa = testCase.ExpectedTitleFa,
                Unit = "N/A", LastSynchronizedAt = now });
            db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow { Id = otherItem, ProviderName = Provider,
                StatementType = testCase.ExpectedStatementType, SourceItemId = 999_162, TitleFa = "قلم آزمایشی دیگر", Unit = "N/A", LastSynchronizedAt = now });
            // Threshold-filter items of the same statement (catalog identities, never typed into the fixture).
            var filterItems = (testCase.ComparisonStatements ?? []).SelectMany(statement => statement.OtherItems?.Keys ?? [])
                .Distinct().ToDictionary(itemId => itemId, itemId => catalogSnapshot.DirectSources.Single(source => source.ProviderName == Provider
                    && source.StatementType == testCase.ExpectedStatementType && source.SourceItemId == itemId));
            foreach (var (itemId, source) in filterItems)
                db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow { Id = source.SourceItemCatalogId, ProviderName = Provider,
                    StatementType = testCase.ExpectedStatementType, SourceItemId = itemId, TitleFa = source.TitleFa ?? $"item {itemId}",
                    Unit = "N/A", LastSynchronizedAt = now });
            foreach (var statement in testCase.ComparisonStatements ?? [])
            {
                var id = Guid.NewGuid();
                var start = statement.Older ? Feature141VerticalSliceCases.ComparisonPriorStart : Feature141VerticalSliceCases.ComparisonLatestStart;
                var end = statement.Older ? Feature141VerticalSliceCases.ComparisonPriorEnd : Feature141VerticalSliceCases.ComparisonLatestEnd;
                var externalId = $"vs-cmp-{statement.Ticker}-{start:yyyyMMdd}-{(statement.Composing ? "c" : "s")}-{statement.Suffix}";
                db.FinancialStatements.Add(new NormalizedFinancialStatementRow
                {
                    Id = id, CompanyId = companies[statement.Ticker], ProviderName = Provider,
                    ExternalCompanyId = $"feature141-vs-cmp-{companies[statement.Ticker]:N}", ExternalStatementId = externalId,
                    StatementType = testCase.ExpectedStatementType, PeriodType = "ThreeMonths", PeriodStart = start, PeriodEnd = end,
                    PublishedAt = end.AddDays(31), IsComposing = statement.Composing, IsAudited = statement.Audited,
                    SourcePayloadChecksum = externalId, LastSynchronizedAt = now
                });
                db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                    { Id = Guid.NewGuid(), FinancialStatementId = id, SourceItemCatalogId = otherItem, Value = 77_777m });
                lineItems++;
                foreach (var (itemId, itemValue) in statement.OtherItems ?? new Dictionary<int, decimal?>())
                {
                    if (itemValue is null) continue; // a missing filter value is a missing line, never zero
                    db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                        { Id = Guid.NewGuid(), FinancialStatementId = id, SourceItemCatalogId = filterItems[itemId].SourceItemCatalogId, Value = itemValue });
                    lineItems++;
                }
                if (!statement.HasLine) continue;
                db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                    { Id = Guid.NewGuid(), FinancialStatementId = id, SourceItemCatalogId = sourceItem, Value = statement.Value });
                lineItems++;
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        var role = "feature141_vs_cmp_reader_" + Guid.NewGuid().ToString("N"); // PostgreSQL truncates role names beyond 63 bytes
        _restrictedPassword = "F141vs_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{_restrictedPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; " +
            $"GRANT SELECT ON TABLE public.\"FinancialStatements\", public.\"FinancialStatementLineItems\", " +
            $"public.\"FinancialStatementSourceItems\", public.\"Companies\" TO \"{role}\";";
        await command.ExecuteNonQueryAsync(cancellationToken);
        var universe = (testCase.PopulationUniverse ?? []).Select(ticker => companies[ticker]).OrderBy(id => id).ToArray();
        var first = companies.Values.FirstOrDefault();
        return new(databaseName, role, first, Guid.Empty, Guid.Empty, Guid.Empty, SeededLineItemCount: lineItems,
            ReviewedCompanyUniverse: universe.Length > 0 ? universe : null, CompaniesBySymbol: companies,
            SeededSourceItemCatalogId: sourceItem);
    }

    /// <summary>Independent per-company value of the seeded statement item at the period the engine reported (admin read).</summary>
    private async Task<(IReadOnlyDictionary<string, decimal?> Values, int LineItemCount)> ReadIndependentComparisonAsync(
        Feature141VerticalSliceCase testCase, Feature141VerticalSliceFixtureInfo fixtureInfo, DateOnly? periodEnd,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var expected in testCase.ExpectedComparison ?? [])
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT li.\"Value\" FROM public.\"FinancialStatementLineItems\" li " +
                "JOIN public.\"FinancialStatements\" st ON st.\"Id\" = li.\"FinancialStatementId\" " +
                "WHERE st.\"CompanyId\" = @company AND st.\"PeriodEnd\" = @end AND li.\"SourceItemCatalogId\" = @source";
            command.Parameters.AddWithValue("company", fixtureInfo.CompaniesBySymbol![expected.Label]);
            command.Parameters.AddWithValue("end", periodEnd ?? testCase.ExpectedPeriodEnd!.Value);
            command.Parameters.AddWithValue("source", fixtureInfo.SeededSourceItemCatalogId!.Value);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            values[expected.Label] = value is null or DBNull ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM public.\"FinancialStatementLineItems\"";
        return (values, Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture));
    }

    /// <summary>Independent producer set from the seeded rows: positive production of an approved alias in the window.</summary>
    private async Task<(decimal? Value, int LineItemCount)> ReadIndependentDiscoveryAsync(Feature141VerticalSliceCase testCase,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(DISTINCT r.\"CompanyId\") FROM public.\"MonthlyReportLineItems\" li " +
            "JOIN public.\"MonthlyReports\" r ON li.\"MonthlyReportId\" = r.\"Id\" WHERE r.\"IsAccepted\" AND r.\"RevisionStatus\" = 'Accepted' " +
            "AND r.\"PeriodStart\" = @start AND r.\"PeriodEnd\" = @end AND li.\"ProductionQuantity\" > 0 AND BTRIM(li.\"Title\") = ANY(@aliases)), " +
            "(SELECT COUNT(*) FROM public.\"MonthlyReportLineItems\")";
        command.Parameters.AddWithValue("start", testCase.ExpectedPeriodStart!.Value);
        command.Parameters.AddWithValue("end", testCase.ExpectedPeriodEnd!.Value);
        command.Parameters.AddWithValue("aliases", new[] { "گندله", "گندله سنگ‌آهن" });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (null, 0);
        return (reader.GetInt64(0), (int)reader.GetInt64(1));
    }

    /// <summary>Fixture-only copy of the catalog snapshot with Companies.Name and Companies.Ticker query-authorized for discovery output.</summary>
    private static StructuredQueryCatalogSnapshot DiscoveryFixtureSnapshot(StructuredQueryCatalogSnapshot snapshot, Feature141VerticalSliceTrace trace)
    {
        var companies = snapshot.Tables.Single(table => table.Schema == "public" && table.Name == "Companies");
        var approved = new[] { "Name", "Ticker" };
        trace.CompiledWithFixtureOnlyColumnApproval = true;
        trace.FixtureOnlyApprovedColumns = approved.Select(name => $"{companies.CatalogId}_{name}").ToArray();
        return new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion, snapshot.CatalogHash, true, snapshot.Tables,
            snapshot.Columns.Select(column => column.TableCatalogId == companies.CatalogId && approved.Contains(column.Name, StringComparer.Ordinal)
                ? column with { Queryable = true } : column).ToArray(),
            snapshot.Relationships, snapshot.DirectSources, snapshot.MonthlyMeasures);
    }

    private async Task<(decimal? Value, int LineItemCount)> ReadIndependentMonthlyValueAsync(
        Feature141VerticalSliceFixtureInfo fixtureInfo, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var column = fixtureInfo.MonthlyValueColumn switch
        {
            Feature141MonthlyValueColumn.SalesQuantity => "SalesQuantity",
            Feature141MonthlyValueColumn.SalesAmount => "SalesAmount",
            _ => "ProductionQuantity"
        };
        command.CommandText = $"SELECT (SELECT li.\"{column}\" FROM public.\"MonthlyReportLineItems\" li " +
            "WHERE li.\"MonthlyReportId\" = @report AND li.\"ProductCode\" = @product), " +
            "(SELECT COUNT(*) FROM public.\"MonthlyReportLineItems\")";
        command.Parameters.AddWithValue("report", fixtureInfo.MonthlyReportId!.Value);
        command.Parameters.AddWithValue("product", fixtureInfo.ProductKey!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (null, 0);
        return (reader.IsDBNull(0) ? null : reader.GetDecimal(0), (int)reader.GetInt64(1));
    }

    private static async Task<(bool SelectOnly, bool CanReadOtherDomainTable)> InspectRestrictedRoleAsync(
        string restrictedConnection, string role, string primaryTable, string otherDomainTable, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(restrictedConnection);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_user, rolsuper OR rolcreatedb OR rolcreaterole, " +
            $"has_table_privilege(current_user, 'public.\"{primaryTable}\"', 'SELECT'), " +
            $"has_table_privilege(current_user, 'public.\"{primaryTable}\"', 'INSERT, UPDATE, DELETE'), " +
            $"has_table_privilege(current_user, 'public.\"{otherDomainTable}\"', 'SELECT') " +
            "FROM pg_roles WHERE rolname = current_user";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (false, true);
        var selectOnly = reader.GetString(0) == role && !reader.GetBoolean(1) && reader.GetBoolean(2) && !reader.GetBoolean(3);
        return (selectOnly, reader.GetBoolean(4));
    }

    private async Task<(decimal? Value, int LineItemCount)> ReadIndependentValueAsync(
        Feature141VerticalSliceFixtureInfo fixtureInfo, Guid sourceItemCatalogId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT li.\"Value\" FROM public.\"FinancialStatementLineItems\" li " +
            "WHERE li.\"FinancialStatementId\" = @statement AND li.\"SourceItemCatalogId\" = @source), " +
            "(SELECT COUNT(*) FROM public.\"FinancialStatementLineItems\")";
        command.Parameters.AddWithValue("statement", fixtureInfo.StatementId);
        command.Parameters.AddWithValue("source", sourceItemCatalogId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (null, 0);
        return (reader.IsDBNull(0) ? null : reader.GetDecimal(0), (int)reader.GetInt64(1));
    }

    public static async Task<string> WriteArtifactAsync(Feature141VerticalSliceTrace trace)
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "evaluation", "artifacts");
        Directory.CreateDirectory(directory);
        // One file per run so earlier evidence is never overwritten.
        var path = Path.Combine(directory,
            $"engine-e2e-vertical-slice-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}-{trace.CaseId}-{trace.TypedOutcome?.ToString().ToLowerInvariant() ?? "unreached"}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            runAt = DateTimeOffset.UtcNow,
            executionMode = "live-llm-plus-isolated-postgresql-fixture",
            chatIntegrationTouched = false,
            trace
        }, ArtifactJson));
        return path;
    }

    public static string ToPersianCalendar(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return $"{Persian.GetYear(dateTime):0000}-{Persian.GetMonth(dateTime):00}-{Persian.GetDayOfMonth(dateTime):00}";
    }

    public static string PersianDigits(string value) =>
        string.Concat(value.Select(character => character is >= '0' and <= '9' ? (char)('۰' + (character - '0')) : character));

    private static DateOnly? ToDate(object? value) => value switch
    {
        DateOnly date => date,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        DateTimeOffset offset => DateOnly.FromDateTime(offset.UtcDateTime),
        string text when DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
        _ => null
    };

    private static int? Sum(int? left, int? right) => left is null && right is null ? null : (left ?? 0) + (right ?? 0);

    private static Feature141VerticalSliceTrace Finish(Feature141VerticalSliceTrace trace, System.Diagnostics.Stopwatch timer,
        CountingModelExecutionService models, CountingExecutor executor)
    {
        timer.Stop();
        trace.TotalElapsedMs = timer.Elapsed.TotalMilliseconds;
        trace.ObservedModelCalls = models.Calls;
        trace.ObservedExecutorCalls = executor.Calls;
        return trace;
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    /// <summary>Counts provider calls so a pre-model rejection can prove that no model was invoked.</summary>
    private sealed class CountingModelExecutionService(IAiModelExecutionService inner) : IAiModelExecutionService
    {
        private int _calls;
        public int Calls => _calls;

        public Task<AiModelResult> ExecuteAsync(AiModelSelectionRequest selection, AiModelRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.ExecuteAsync(selection, request, cancellationToken);
        }
    }

    /// <summary>Counts executor calls so a typed rejection can prove that no SQL was executed.</summary>
    private sealed class CountingExecutor(IReadOnlyStructuredQueryExecutor inner) : IReadOnlyStructuredQueryExecutor
    {
        private int _calls;
        public int Calls => _calls;

        public Task<StructuredQueryExecutionResult> ExecuteAsync(CompiledReadOnlyQuery query, StructuredQueryExecutionGrant grant,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.ExecuteAsync(query, grant, cancellationToken);
        }
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "feature141-vertical-slice";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

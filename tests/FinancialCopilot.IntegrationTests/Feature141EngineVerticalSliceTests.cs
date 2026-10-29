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
        Assert.Contains(Feature141EngineVerticalSliceRunner.PersianDigits(testCase.ExpectedPeriodEnd!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
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
        Assert.Contains(Feature141EngineVerticalSliceRunner.PersianDigits("2026-09-22"), trace.PersianAnswer, StringComparison.Ordinal);
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
        .Where(item => item.ExpectedOutcome != Feature141ExpectedOutcome.Answered)
        .Select(item => new object[] { item.CaseId });
}

public enum Feature141ExpectedOutcome { Answered, Ambiguous, Unsupported, Incomplete, Unauthorized }

/// <summary>Which governed retrieval path the question exercises and which fixture block is seeded.</summary>
public enum Feature141CaseKind { StatementItem, MonthlyMeasure }

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
    string? ExpectedPersianMonth = null)
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

    public static readonly IReadOnlyList<Feature141VerticalSliceCase> All =
        [OperatingProfitPerShareKgehr, MonthlyProductionKgol, AmbiguousSales, UnsupportedForecast, UnauthorizedWithoutTenant];
}

public sealed record Feature141VerticalSliceFixtureInfo(
    string DatabaseName, string RestrictedRole, Guid CompanyId, Guid DecoyCompanyId,
    Guid StatementId, Guid OlderStatementId, int SeededLineItemCount,
    Guid? MonthlyReportId = null, string? ProductKey = null);

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
        var monthly = testCase.Kind == Feature141CaseKind.MonthlyMeasure;
        (trace.RestrictedRoleIsSelectOnly, trace.RestrictedRoleCanReadOtherDomainTable) = await InspectRestrictedRoleAsync(
            restrictedConnection, fixtureInfo.RestrictedRole,
            monthly ? "MonthlyReportLineItems" : "FinancialStatementLineItems",
            monthly ? "FinancialStatementLineItems" : "MonthlyReports", cancellationToken);

        // Production composition root. Both connection strings are redirected to the isolated database so
        // nothing in this graph can reach the Development application database.
        var configuration = EvaluationConfigurationLoader.Build(apiDirectory, "Development", configurationOverrides:
            new Dictionary<string, string?>
            {
                ["Feature141:Enabled"] = "true",
                ["Feature141:SchemaNavigator:HierarchyPath"] = hierarchyPath,
                ["Feature141:SchemaNavigator:CatalogPath"] = catalogPath,
                ["Feature141:QueryExecution:AuthorizationProvenance"] = FixtureAuthorizationProvenance,
                ["Feature141:QueryExecution:MaximumConcurrency"] = "1",
                ["ConnectionStrings:FinancialCopilot"] = database.ConnectionString,
                ["ConnectionStrings:Feature141ReadOnly"] = restrictedConnection
            });
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
        trace.CanonicalIntent = extraction.Intent;
        trace.LlmCalls += extraction.ModelCalls;
        trace.LlmInputTokens = extraction.InputTokens;
        trace.LlmOutputTokens = extraction.OutputTokens;
        trace.IntentLatencyMs = extraction.Latency.TotalMilliseconds;
        output.WriteLine($"Intent: succeeded={extraction.Succeeded} category={extraction.FailureCategory} calls={extraction.ModelCalls} latencyMs={extraction.Latency.TotalMilliseconds:F0}");
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
        trace.BoundSourceCatalogId = typedBinding.References.SingleOrDefault()?.CatalogId;
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
        if (intent.Measures.Count != 1) return Finish(trace, totalTimer, executionService, executor);

        // Stage 3: live navigator and deterministic planner over the real hierarchy provider. Company and
        // product identities come from the production resolvers over the isolated database.
        await using var bindingDb = database.CreateContext();
        var companyBinder = new StructuredQueryCompanyBinder(new CanonicalQueryEntityResolver(bindingDb,
            Options.Create(new CanonicalEntityResolutionOptions(Enabled: true))));
        var productBinder = new StructuredQueryProductBinder(new CompanyScopedProductResolver(
            new CompanyResolverService(bindingDb, NullLogger<CompanyResolverService>.Instance),
            new EfCoreMonthlyProductComparisonRepository(bindingDb)));
        var navigator = new LlmVectorlessSchemaNavigator(executionService, hierarchyProvider, hierarchyProvider, hierarchyProvider,
            provider.GetRequiredService<ISchemaContextCompletenessValidator>(), provider.GetRequiredService<SchemaNavigatorBudgets>());
        var planner = new OfflineStructuredQueryPlanningService(
            navigator, navigator,
            provider.GetRequiredService<ICatalogNavigationHierarchyProvider>(),
            provider.GetRequiredService<ISchemaContextCompletenessValidator>(),
            provider.GetRequiredService<IStructuredQueryPlanBuilder>(),
            provider.GetRequiredService<IStructuredQueryPlanValidator>(),
            companyBinder, productBinder,
            productConceptResolver: provider.GetRequiredService<IReviewedProductConceptResolver>());
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
            var canonical = CanonicalSourceItemBindingResolver.Resolve(intent.Measures[0], references, Provider);
            trace.ChatPathBindingStatus = canonical.Status;
            trace.ChatPathBindingDiagnostic = canonical.DiagnosticCode;
            if (canonical.Status != CanonicalSourceItemBindingStatus.Bound || canonical.Reference is not { } bound)
                return Finish(trace, totalTimer, executionService, executor);
            var resolution = new FinancialSourceTitleResolution(FinancialSourceTitleResolutionStatus.Resolved,
                canonical.DiagnosticCode, bound, [bound], new Dictionary<string, string?>(), bound.UnitClassification,
                bound.QueryAuthorizationStatus ?? (bound.Queryable ? "authorized" : "not-authorized"));
            var directSource = snapshot.DirectSources.SingleOrDefault(source =>
                string.Equals(source.CatalogId, bound.CatalogId, StringComparison.Ordinal));
            var handoff = FinancialSourcePlannerHandoff.Create(intent, resolution, snapshot.CatalogVersion, snapshot.CatalogHash, directSource);
            trace.HandoffStatus = handoff.Status;
            trace.HandoffDiagnosticCode = handoff.DiagnosticCode;
            if (handoff.Binding is null) return Finish(trace, totalTimer, executionService, executor);
            plannerRequirements = handoff.Binding.PlannerRequirements;
        }
        trace.PlannerRequirements = plannerRequirements;
        trace.RequirementsMeasureIds = plannerRequirements.MeasureIds;
        trace.RequirementsNormalizationDiagnostics = plannerRequirements.NormalizationDiagnostics;
        var planning = await planner.PlanAsync(navigationRequest, plannerRequirements, cancellationToken);
        trace.ProductBindingStatus = planning.ProductBinding?.Status;
        trace.ProductKey = planning.ProductBinding?.ProductKey;
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
        var compilation = provider.GetRequiredService<IStructuredQueryCompiler>().Compile(planning.Plan, snapshot);
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
        if (execution.Rows.Count == 1)
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
        (trace.IndependentDatabaseValue, trace.IndependentLineItemCount) = monthly
            ? await ReadIndependentMonthlyValueAsync(fixtureInfo, cancellationToken)
            : await ReadIndependentValueAsync(fixtureInfo, testCase.ExpectedSourceItemCatalogId, cancellationToken);

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
            MaximumResultBytes = 1_000_000, MaximumConcurrency = 1, AuthorizationProvenance = FixtureAuthorizationProvenance
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
        var expectedSourceItem = testCase.ExpectedSourceItemCatalogId == Guid.Empty
            ? Feature141VerticalSliceCases.OperatingProfitPerShareSourceItemCatalogId : testCase.ExpectedSourceItemCatalogId;
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
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = companyId, ProviderName = Provider, ExternalCompanyId = "feature141-vs-kgehr", Name = "Fixture Kgehr",
                Ticker = "کگهر", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now
            });
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = decoyCompanyId, ProviderName = Provider, ExternalCompanyId = "feature141-vs-kgol", Name = "Fixture Kgol",
                Ticker = "کگل", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now
            });
            db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow
            {
                Id = expectedSourceItem, ProviderName = Provider, StatementType = "IncomeStatement",
                SourceItemId = 161, TitleFa = "سود عملیاتی هر سهم", Unit = "N/A", LastSynchronizedAt = now
            });
            db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow
            {
                Id = decoySourceItemId, ProviderName = Provider, StatementType = "IncomeStatement",
                SourceItemId = 162, TitleFa = "سود هر سهم قبل از کسر مالیات", Unit = "N/A", LastSynchronizedAt = now
            });
            NormalizedFinancialStatementRow Statement(Guid id, Guid company, string externalCompany, string externalId,
                DateOnly start, DateOnly end) => new()
            {
                Id = id, CompanyId = company, ProviderName = Provider, ExternalCompanyId = externalCompany,
                ExternalStatementId = externalId, StatementType = "IncomeStatement", PeriodType = "ThreeMonths",
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
        return new(databaseName, role, companyId, decoyCompanyId, statementId, olderStatementId, SeededLineItemCount: 4);
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
        var productKey = $"{Provider}:{externalCompany}:TITLE:{testCase.ExpectedProductLabel}|UNIT:{testCase.ExpectedUnit}";
        var otherProductKey = $"{Provider}:{externalCompany}:TITLE:کنسانتره|UNIT:{testCase.ExpectedUnit}";
        var decoyProductKey = $"{Provider}:{decoyExternalCompany}:TITLE:{testCase.ExpectedProductLabel}|UNIT:{testCase.ExpectedUnit}";
        Guid reportId;
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "feature141-vs-monthly-market",
                Name = "Feature 141 monthly vertical slice fixture", LastSynchronizedAt = now });
            db.Companies.Add(new NormalizedCompanyRow { Id = companyId, ProviderName = Provider, ExternalCompanyId = externalCompany,
                Name = "Fixture Kgol", Ticker = testCase.ExpectedCompanyLabel, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
            db.Companies.Add(new NormalizedCompanyRow { Id = decoyCompanyId, ProviderName = Provider, ExternalCompanyId = decoyExternalCompany,
                Name = "Fixture Kgehr", Ticker = "کگهر", MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
            NormalizedMonthlyReportRow Report(Guid company, string externalCompanyId, string suffix, DateOnly start, DateOnly end,
                bool accepted, string revisionStatus) => new()
            {
                Id = Guid.NewGuid(), ProviderName = Provider, ExternalCompanyId = externalCompanyId, ExternalReportId = $"vs-{suffix}",
                PeriodStart = start, PeriodEnd = end, SourcePayloadChecksum = $"vs-{suffix}", LastSynchronizedAt = now,
                LogicalReportKey = $"vs-{externalCompanyId}-{start:yyyyMM}", RevisionFingerprint = $"vs-{suffix}", IsAccepted = accepted,
                RevisionStatus = revisionStatus, ReportType = "ProductSales", OutputType = 0, CompanyId = company
            };
            NormalizedMonthlyReportLineItemRow Line(NormalizedMonthlyReportRow report, string key, string title, decimal production) => new()
            {
                Id = Guid.NewGuid(), MonthlyReportId = report.Id, SourceRowFingerprint = $"row-{report.ExternalReportId}-{key}",
                SourceMultiplicity = 1, ProductCode = key, Title = title, Unit = testCase.ExpectedUnit!, ProductionQuantity = production,
                SalesQuantity = production - 5, SalesAmount = production * 10, SalesRate = 10m, SourcePayloadChecksum = report.SourcePayloadChecksum
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
        return new(databaseName, role, companyId, decoyCompanyId, Guid.Empty, Guid.Empty, SeededLineItemCount: 5, reportId, productKey);
    }

    private async Task<(decimal? Value, int LineItemCount)> ReadIndependentMonthlyValueAsync(
        Feature141VerticalSliceFixtureInfo fixtureInfo, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT li.\"ProductionQuantity\" FROM public.\"MonthlyReportLineItems\" li " +
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

using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Semantics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Latest-period selection semantics of the direct statement-item query, verified against an isolated
/// PostgreSQL database with the real catalog, the production handoff/planner/validator/compiler/
/// authorizer/executor and no LLM. Each scenario is a separate company in one database so the
/// company-scoped query isolates it. Every execution is run twice and must be identical.
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class Feature141LatestPeriodSemanticsPostgreSqlTests(
    FinancialStatementValueSearch131PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string Provider = "NoavaranCurrentApi";
    private const string OtherProvider = "OtherStatementProvider";
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
    private static readonly Guid SourceItem161 = Guid.Parse("3cafc7d3-b0ab-4341-85ef-ebcbc9d60528");
    private static readonly DateOnly T2Start = new(2026, 3, 21);
    private static readonly DateOnly T2End = new(2026, 6, 21);
    private static readonly DateOnly T1Start = new(2025, 12, 21);
    private static readonly DateOnly T1End = new(2026, 3, 20);
    private static readonly DateOnly T0Start = new(2025, 9, 22);
    private static readonly DateOnly T0End = new(2025, 12, 20);

    public sealed record Scenario(string Id, string Ticker, string Description, FinancialStatementScope RequestedScope,
        StructuredQueryExecutionStatus ExpectedStatus, decimal? ExpectedValue, DateOnly? ExpectedPeriodEnd,
        string? ExpectedDiagnostic, bool? ExpectedIsComposing = null);

    [SkippableFact]
    public async Task LatestPeriodSelectionIsSemanticallyCorrectDeterministicAndFailsClosed()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var databaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
        var scenarios = new[]
        {
            new Scenario("S1", "کگهر", "latest statement contains the requested item", FinancialStatementScope.Unspecified,
                StructuredQueryExecutionStatus.Succeeded, 3_753m, T2End, null, false),
            new Scenario("S2", "کگل", "latest statement exists but the requested item is absent; older period has it",
                FinancialStatementScope.Unspecified, StructuredQueryExecutionStatus.NoData, null, T2End,
                "eligible_statement_observation_missing"),
            new Scenario("S3a", "فملی", "consolidated and standalone statements share the latest PeriodEnd; scope unspecified",
                FinancialStatementScope.Unspecified, StructuredQueryExecutionStatus.Ambiguous, null, null,
                "different_reporting_period_scopes"),
            new Scenario("S3b", "فولاد", "consolidated statement is newer than the standalone one; scope unspecified",
                FinancialStatementScope.Unspecified, StructuredQueryExecutionStatus.Ambiguous, null, null,
                "statement_scope_unspecified_across_consolidation_bases"),
            new Scenario("S3c", "فولاد", "same data, standalone explicitly requested", FinancialStatementScope.Standalone,
                StructuredQueryExecutionStatus.Succeeded, 1_100m, T1End, null, false),
            new Scenario("S3d", "فولاد", "same data, consolidated explicitly requested", FinancialStatementScope.Consolidated,
                StructuredQueryExecutionStatus.Succeeded, 2_200m, T2End, null, true),
            new Scenario("S4a", "کچاد", "two statements share the latest PeriodEnd with the same value",
                FinancialStatementScope.Unspecified, StructuredQueryExecutionStatus.Succeeded, 4_000m, T2End, null, false),
            new Scenario("S4b", "شپنا", "two statements share the latest PeriodEnd with different values",
                FinancialStatementScope.Unspecified, StructuredQueryExecutionStatus.Ambiguous, null, null,
                "requested_item_values_conflict"),
            new Scenario("S5", "وبملت", "older periods exist with different values", FinancialStatementScope.Unspecified,
                StructuredQueryExecutionStatus.Succeeded, 5_300m, T2End, null, false),
            new Scenario("S6", "شستا", "requested line item exists on the latest statement with NULL value",
                FinancialStatementScope.Unspecified, StructuredQueryExecutionStatus.NoData, null, T2End,
                "eligible_statement_observation_missing"),
            new Scenario("S7", "خودرو", "another provider has a newer statement for the same company",
                FinancialStatementScope.Unspecified, StructuredQueryExecutionStatus.Succeeded, 7_100m, T2End, null, false)
        };

        var companies = await SeedAsync(database, scenarios.Select(s => s.Ticker).Distinct().ToArray());
        var role = "feature141_lp_reader_" + Guid.NewGuid().ToString("N");
        var password = "F141lp_" + Guid.NewGuid().ToString("N");
        await CreateRestrictedRoleAsync(database.ConnectionString, role, password);
        var restrictedConnection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            { Username = role, Password = password, Pooling = false }.ConnectionString;

        var (snapshot, provider) = Catalog();
        var references = await provider.GetFinancialSourceReferencesAsync(snapshot.CatalogVersion, snapshot.CatalogHash, CancellationToken.None);
        var resolution = FinancialSourceItemTitleResolver.Resolve(new("سود عملیاتی هر سهم", Provider, "IncomeStatement"), references);
        Assert.Equal(FinancialSourceTitleResolutionStatus.Resolved, resolution.Status);
        var directSource = snapshot.DirectSources.Single(s => s.CatalogId == resolution.Reference!.CatalogId);
        await using var bindingDb = database.CreateContext();
        var companyBinder = new StructuredQueryCompanyBinder(new CanonicalQueryEntityResolver(bindingDb,
            Options.Create(new CanonicalEntityResolutionOptions(Enabled: true))));
        var host = new TestEnvironment("Development");
        var results = new List<object>();
        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            var intent = Intent(scenario.Ticker, scenario.RequestedScope);
            var handoff = FinancialSourcePlannerHandoff.Create(intent, resolution, snapshot.CatalogVersion, snapshot.CatalogHash, directSource);
            Assert.Equal(FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation, handoff.Status);
            var requirements = handoff.Binding!.PlannerRequirements;
            var required = requirements.DimensionIds.Concat(requirements.RelationshipIds)
                .Concat(requirements.FinancialValueReferences!.Select(r => r.CatalogId)).ToArray();
            var seedContext = await provider.GetContextAsync(new SchemaContextExpansionRequest(1, snapshot.CatalogVersion,
                snapshot.CatalogHash, [], 0, RequiredCatalogIds: required), CancellationToken.None);
            Assert.NotNull(seedContext);
            var navigation = new SchemaNavigationResult(1, snapshot.CatalogVersion, snapshot.CatalogHash, [], seedContext.NodeIds,
                [], requirements.DimensionIds, requirements.EntityKinds, requirements.RelationshipIds, requirements.SourcePolicies,
                requirements.PeriodPolicies, [], [], SelectedPresentationRequirements: requirements.PresentationRequirements,
                SelectedStatementScopes: requirements.StatementScopes, SelectedEntityReferences: requirements.EntityReferences,
                SelectedFinancialValueReferences: requirements.FinancialValueReferences,
                FinancialValueOperation: requirements.RequestedFinancialValueOperation);
            var planner = new OfflineStructuredQueryPlanningService(new CapturedRequirementsExtractor(requirements),
                new CapturedNavigator(navigation), provider, new SchemaContextCompletenessValidator(),
                new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(executionEnabled: true), companyBinder);
            var request = new SchemaNavigationRequest(1, $"سود عملیاتی هر سهم {scenario.Ticker} چقدر است؟", snapshot.CatalogVersion,
                snapshot.CatalogHash, TenantId: TenantId, CorrelationId: $"feature141-latest-period-{scenario.Id}");
            var planning = await planner.PlanAsync(request, requirements, CancellationToken.None);
            Assert.True(planning.Validation.ExecutionEligible, $"{scenario.Id}: {string.Join("|", planning.Validation.Diagnostics)}");
            Assert.Equal(companies[scenario.Ticker], planning.CompanyBinding?.CompanyId);
            var compilation = new StructuredQueryCompiler().Compile(planning.Plan, snapshot);
            Assert.True(compilation.Success, $"{scenario.Id}: {string.Join("|", compilation.Diagnostics)}");
            var query = compilation.Query!;
            var options = Options.Create(new StructuredQueryExecutionOptions
            {
                Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
                ConnectionString = restrictedConnection, DesignatedDatabase = databaseName, ReadOnlyRole = role,
                CommandTimeoutSeconds = 5, MaximumRows = 100, MaximumResultBytes = 1_000_000, MaximumConcurrency = 1,
                AuthorizationProvenance = "feature141-latest-period-semantics-fixture"
            });
            var grant = new CatalogStructuredQueryAuthorizer(options, host).Authorize(query, TenantId);
            Assert.NotNull(grant);
            var executor = new NpgsqlReadOnlyStructuredQueryExecutor(options, NullLogger<NpgsqlReadOnlyStructuredQueryExecutor>.Instance, host);
            var first = await executor.ExecuteAsync(query, grant, CancellationToken.None);
            var second = await executor.ExecuteAsync(query, grant, CancellationToken.None);
            var firstRows = Render(first.Rows);
            var secondRows = Render(second.Rows);
            var observedValue = first.Rows.Count == 1 && first.Rows[0].GetValueOrDefault("Value") is { } v
                ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : (decimal?)null;
            var observedEnd = first.Rows.Count == 1 ? ToDate(first.Rows[0].GetValueOrDefault("PeriodEnd")) : null;
            var observedComposing = first.Rows.Count == 1 ? first.Rows[0].GetValueOrDefault("IsComposing") as bool? : null;
            var answer = new PersianStructuredQueryResponseGenerator().Generate(new(planning.Plan, first));
            output.WriteLine($"{scenario.Id} [{scenario.Description}] status={first.Status} diag={first.DiagnosticCode} value={observedValue} end={observedEnd} composing={observedComposing} rows={first.Rows.Count} answer={answer.TextAnswer}");
            results.Add(new
            {
                scenario.Id, scenario.Description, scenario.Ticker, requestedScope = scenario.RequestedScope.ToString(),
                expectedStatus = scenario.ExpectedStatus.ToString(), scenario.ExpectedValue, scenario.ExpectedPeriodEnd, scenario.ExpectedDiagnostic,
                observedStatus = first.Status.ToString(), observedDiagnostic = first.DiagnosticCode, observedValue, observedPeriodEnd = observedEnd,
                observedIsComposing = observedComposing, observedRowCount = first.Rows.Count, deterministic = firstRows == secondRows && first.Status == second.Status,
                sqlParameters = query.Parameters.ToDictionary(p => p.Name, p => p.Value), persianAnswerStatus = answer.Status.ToString(),
                persianAnswer = answer.TextAnswer, rows = first.Rows
            });
            void Check<T>(string what, T expected, T actual)
            {
                if (!Equals(expected, actual)) failures.Add($"{scenario.Id} {what}: expected {expected}, actual {actual}");
            }
            Check("status", scenario.ExpectedStatus, first.Status);
            Check("diagnostic", scenario.ExpectedDiagnostic, first.DiagnosticCode);
            Check("value", scenario.ExpectedValue, observedValue);
            if (scenario.ExpectedPeriodEnd is not null) Check("periodEnd", scenario.ExpectedPeriodEnd, observedEnd);
            if (scenario.ExpectedIsComposing is not null) Check("isComposing", scenario.ExpectedIsComposing, observedComposing);
            Check("determinism", true, firstRows == secondRows && first.Status == second.Status);
            // A non-answer must never carry a number into the Persian response.
            if (scenario.ExpectedStatus != StructuredQueryExecutionStatus.Succeeded)
                Check("answerStatusNotAnswered", true, answer.Status != StructuredAnswerStatus.Answered);
        }

        var root = FindRepositoryRoot();
        var artifact = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "evaluation", "artifacts",
            $"latest-period-semantics-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.json");
        await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new { runAt = DateTimeOffset.UtcNow, scenarios = results, failures },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        output.WriteLine($"Artifact: {artifact}");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task<Dictionary<string, Guid>> SeedAsync(LocalPostgreSqlTestDatabase database, string[] tickers)
    {
        var now = DateTimeOffset.UtcNow;
        var marketId = Guid.NewGuid();
        var companies = tickers.ToDictionary(t => t, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var otherItem = Guid.NewGuid();
        await using var db = database.CreateContext();
        db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "feature141-latest-period-market",
            Name = "Feature 141 latest-period fixture", LastSynchronizedAt = now });
        foreach (var (ticker, id) in companies)
            db.Companies.Add(new NormalizedCompanyRow { Id = id, ProviderName = Provider, ExternalCompanyId = $"lp-{id:N}", Name = $"Fixture {ticker}",
                Ticker = ticker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
        db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow { Id = SourceItem161, ProviderName = Provider,
            StatementType = "IncomeStatement", SourceItemId = 161, TitleFa = "سود عملیاتی هر سهم", Unit = "N/A", LastSynchronizedAt = now });
        db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow { Id = otherItem, ProviderName = Provider,
            StatementType = "IncomeStatement", SourceItemId = 162, TitleFa = "سود هر سهم قبل از کسر مالیات", Unit = "N/A", LastSynchronizedAt = now });
        Guid Statement(string ticker, string suffix, DateOnly start, DateOnly end, bool composing = false, string providerName = Provider)
        {
            var id = Guid.NewGuid();
            db.FinancialStatements.Add(new NormalizedFinancialStatementRow
            {
                Id = id, CompanyId = companies[ticker], ProviderName = providerName, ExternalCompanyId = $"lp-{companies[ticker]:N}",
                ExternalStatementId = $"lp-{ticker}-{suffix}", StatementType = "IncomeStatement", PeriodType = "ThreeMonths",
                PeriodStart = start, PeriodEnd = end, PublishedAt = end.AddDays(31), IsComposing = composing,
                SourcePayloadChecksum = $"lp-{ticker}-{suffix}", LastSynchronizedAt = now
            });
            return id;
        }
        void Line(Guid statement, decimal? value, Guid? item = null) =>
            db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                { Id = Guid.NewGuid(), FinancialStatementId = statement, SourceItemCatalogId = item ?? SourceItem161, Value = value });

        // S1: latest has the item (3753), older has 3001.
        Line(Statement("کگهر", "t2", T2Start, T2End), 3_753m);
        Line(Statement("کگهر", "t1", T1Start, T1End), 3_001m);
        // S2: latest statement has only another item; the older statement has the requested item.
        Line(Statement("کگل", "t2", T2Start, T2End), 5_555m, otherItem);
        Line(Statement("کگل", "t1", T1Start, T1End), 3_001m);
        // S3a: consolidated and standalone share the latest PeriodEnd with different values.
        Line(Statement("فملی", "t2-standalone", T2Start, T2End), 1_500m);
        Line(Statement("فملی", "t2-consolidated", T2Start, T2End, composing: true), 2_500m);
        // S3b/c/d: consolidated newer than standalone.
        Line(Statement("فولاد", "t1-standalone", T1Start, T1End), 1_100m);
        Line(Statement("فولاد", "t2-consolidated", T2Start, T2End, composing: true), 2_200m);
        // S4a: two statements, same PeriodEnd, same value (original and re-presented).
        Line(Statement("کچاد", "t2-a", T2Start, T2End), 4_000m);
        Line(Statement("کچاد", "t2-b", T2Start, T2End), 4_000m);
        // S4b: two statements, same PeriodEnd, different values.
        Line(Statement("شپنا", "t2-a", T2Start, T2End), 4_100m);
        Line(Statement("شپنا", "t2-b", T2Start, T2End), 4_200m);
        // S5: three periods with different values.
        Line(Statement("وبملت", "t0", T0Start, T0End), 5_100m);
        Line(Statement("وبملت", "t1", T1Start, T1End), 5_200m);
        Line(Statement("وبملت", "t2", T2Start, T2End), 5_300m);
        // S6: latest line item exists with NULL value; older has a value.
        Line(Statement("شستا", "t2", T2Start, T2End), null);
        Line(Statement("شستا", "t1", T1Start, T1End), 6_001m);
        // S7: another provider has a newer statement with a different value.
        Line(Statement("خودرو", "t2", T2Start, T2End), 7_100m);
        Line(Statement("خودرو", "t3-other", T2End.AddDays(1), T2End.AddMonths(3), providerName: OtherProvider), 9_900m);
        await db.SaveChangesAsync();
        return companies;
    }

    private static CanonicalSemanticIntent Intent(string ticker, FinancialStatementScope scope) => new(2,
        [new RequestedMeasure("سود عملیاتی هر سهم", "operatingProfitPerShare", MeasureCanonicalizationStatus.Canonicalized,
            ["operatingProfitPerShare"], MeasureConceptKind.FinancialStatementItem, BusinessMeasureDimension.CurrencyPerShare,
            new(StatementScope: scope), SemanticUnit.Unspecified, null, EffectiveUnitEvidence.Unresolved)],
        new(AnalyticalOperationKind.Retrieve), new(ComparisonMode.None), new(ReportingPeriodKind.Unspecified),
        new(EntityScopeKind.NamedCompanies, [ticker]), [], [], null, null, null, new(RequestedOutputKind.Scalar, []), [], new(false, [], []));

    private static (StructuredQueryCatalogSnapshot Snapshot, JsonCatalogNavigationHierarchyProvider Provider) Catalog()
    {
        var root = FindRepositoryRoot();
        var catalog = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var hierarchy = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        return (new StructuredQueryCatalogSnapshotBuilder().Build(catalog, hierarchy), new JsonCatalogNavigationHierarchyProvider(hierarchy, catalog));
    }

    private static async Task CreateRestrictedRoleAsync(string adminConnectionString, string role, string password)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; " +
            $"GRANT SELECT ON TABLE public.\"FinancialStatements\", public.\"FinancialStatementLineItems\", " +
            $"public.\"FinancialStatementSourceItems\", public.\"Companies\" TO \"{role}\";";
        await command.ExecuteNonQueryAsync();
    }

    private static string Render(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        JsonSerializer.Serialize(rows.Select(row => row.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value?.ToString())));

    private static DateOnly? ToDate(object? value) => value switch
    {
        DateOnly date => date,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        _ => null
    };

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "feature141-latest-period-semantics";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturedRequirementsExtractor(QuestionSchemaRequirements captured) : IQuestionSchemaRequirementsExtractor
    {
        public Task<QuestionSchemaRequirements> ExtractAsync(SchemaNavigationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(captured);
    }

    private sealed class CapturedNavigator(SchemaNavigationResult captured) : IVectorlessSchemaNavigator
    {
        public Task<SchemaNavigationResult> NavigateQuestionAsync(SchemaNavigationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(captured);

        public Task<SchemaNavigationResult> NavigateAsync(SchemaNavigationRequest request,
            QuestionSchemaRequirements independentlyExtractedRequirements, CancellationToken cancellationToken) =>
            Task.FromResult(captured);
    }
}

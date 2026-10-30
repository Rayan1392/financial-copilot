using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
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
/// Governed company discovery scenarios without an LLM: real catalog (plus the fixture-only approval of the
/// two Companies display columns the canonical catalog does not yet authorize), production planner with the
/// approved product concept, compiler, catalog authorizer with a fixture-only scope and reviewed company
/// universe, and the restricted executor on the isolated database. Each scenario is a separate reporting
/// window or scope so results cannot leak between them.
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class Feature141CompanyDiscoverySemanticsPostgreSqlTests(
    FinancialStatementValueSearch131PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string Provider = "NoavaranCurrentApi";
    private const string Measure = "metric_monthly_production_quantity";
    private const string ScopeId = "feature141-isolated-fixture-company-discovery-scope";
    private const string Approval = "fixture-only:feature141-company-discovery:2026-10-29";
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
    private static readonly DateOnly ShahrivarStart = new(2026, 8, 23);
    private static readonly DateOnly ShahrivarEnd = new(2026, 9, 22);
    private static readonly DateOnly MordadStart = new(2026, 7, 23);
    private static readonly DateOnly MordadEnd = new(2026, 8, 22);
    private static readonly DateOnly MehrStart = new(2026, 9, 23);
    private static readonly DateOnly MehrEnd = new(2026, 10, 22);

    private enum Expect { Producers, Ambiguous, NoScope, NoMatch, ProductNotApproved }

    private sealed record Scenario(string Id, string ProductLabel, string MonthLabel, string Description, Expect Expected,
        string[]? ExpectedSymbols = null, string? ExpectedDiagnostic = null, bool ExcludeUnauthorizedFromUniverse = true, bool WithScope = true);

    [SkippableFact]
    public async Task CompanyDiscoveryIsGovernedUnitSafeAndFailsClosed()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var databaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
        var companies = await SeedAsync(database);
        var role = "feature141_cd_reader_" + Guid.NewGuid().ToString("N");
        var password = "F141cd_" + Guid.NewGuid().ToString("N");
        await CreateRoleAsync(database.ConnectionString, role, password);
        var restrictedConnection = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Username = role, Password = password, Pooling = false }.ConnectionString;

        var scenarios = new[]
        {
            new Scenario("D1", "گندله", "شهریور ۱۴۰۵", "exact label; multiple producers; zero, NULL, other-month, other-product and unauthorized producers excluded",
                Expect.Producers, ["کگل", "کگهر", "شپنا"], "population_discovery_authorized_company_coverage_incomplete"),
            new Scenario("D2", "گندله سنگ‌آهن", "شهریور ۱۴۰۵", "equivalent label resolves to the same approved concept and producer set",
                Expect.Producers, ["کگل", "کگهر", "شپنا"], "population_discovery_authorized_company_coverage_incomplete"),
            // Company identity is (ProviderName, ExternalCompanyId) and IX_MonthlyReports_LogicalPeriod forbids two accepted reports
            // of one identity for the same month, so the former "company alias" duplicate cannot exist; a second report of the same
            // company whose window is not the requested month must not be counted as that month.
            new Scenario("D3", "گندله", "مهر ۱۴۰۵", "a second accepted report of the producer with a different window is not counted as the month",
                Expect.Producers, ["شستا"], "population_discovery_authorized_company_coverage_incomplete"),
            new Scenario("D4", "گندله", "آبان ۱۴۰۵", "no matching companies in the requested month", Expect.NoMatch),
            new Scenario("D5", "گندله", "اسفند ۱۴۰۴", "incorrect reporting period (no reports at all)", Expect.NoMatch),
            new Scenario("D6", "گندله", "شهریور ۱۴۰۵", "no approved discovery scope for the tenant: plan refused, no SQL", Expect.NoScope, WithScope: false),
            new Scenario("D7", "گندله", "شهریور ۱۴۰۵", "unauthorized producer inside the universe would be listed; outside it is not (scope boundary)",
                Expect.Producers, ["کگل", "کگهر", "شپنا", "UNAUTH"], "population_discovery_authorized_company_coverage_incomplete", ExcludeUnauthorizedFromUniverse: false),
            new Scenario("D8", "کنسانتره", "شهریور ۱۴۰۵", "product without an approved concept cannot be discovered", Expect.ProductNotApproved)
        };

        var (snapshot, provider) = Catalog();
        var compileSnapshot = WithFixtureOnlyCompanyDisplayColumns(snapshot);
        await using var bindingDb = database.CreateContext();
        var companyBinder = new StructuredQueryCompanyBinder(new CanonicalQueryEntityResolver(bindingDb,
            Options.Create(new CanonicalEntityResolutionOptions(Enabled: true))));
        var productBinder = new StructuredQueryProductBinder(new CompanyScopedProductResolver(
            new CompanyResolverService(bindingDb, NullLogger<CompanyResolverService>.Instance),
            new EfCoreMonthlyProductComparisonRepository(bindingDb)), new EfCoreMonthlyProductComparisonRepository(bindingDb));
        var conceptResolver = new ReviewedProductConceptResolver([new ReviewedProductConcept("product_iron_ore_pellets", "product-catalog-v1",
            "گندله سنگ‌آهن", ["گندله", "گندله سنگ‌آهن"], "approved", "owner-confirmed:feature141-product-equivalence:2026-10-09")]);
        var host = new TestEnvironment("Development");
        var results = new List<object>();
        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            void Check<T>(string what, T expected, T actual)
            {
                if (!Equals(expected, actual)) failures.Add($"{scenario.Id} {what}: expected {expected}, actual {actual}");
            }
            var period = PersianReportingPeriodNormalizer.Parse(scenario.MonthLabel);
            Assert.Equal(PersianReportingPeriodStatus.Normalized, period.Status);
            // The model-side requirements for an unbound population question: product only, no company, table.
            var requirements = ReportingPeriodRequirementsNormalizer.Apply(new QuestionSchemaRequirements(1, snapshot.CatalogVersion, snapshot.CatalogHash,
                [], ["dim_company"], ["company", "product"], [], [], [], [], ["table"], EntityReferences: [])
            { ProductReferences = [scenario.ProductLabel] }, period);
            var seedContext = await provider.GetContextAsync(new SchemaContextExpansionRequest(1, snapshot.CatalogVersion, snapshot.CatalogHash,
                [], 0, RequiredCatalogIds: ["dim_company"]), CancellationToken.None);
            Assert.NotNull(seedContext);
            var navigation = new SchemaNavigationResult(1, snapshot.CatalogVersion, snapshot.CatalogHash, [], seedContext.NodeIds,
                [], requirements.DimensionIds, requirements.EntityKinds, [], [], requirements.PeriodPolicies, [], [],
                SelectedPresentationRequirements: requirements.PresentationRequirements, SelectedEntityReferences: [scenario.ProductLabel]);
            var universe = companies.Where(pair => !scenario.ExcludeUnauthorizedFromUniverse || pair.Key != "UNAUTH").Select(pair => pair.Value).ToArray();
            var planner = new OfflineStructuredQueryPlanningService(new CapturedRequirementsExtractor(requirements),
                new CapturedNavigator(navigation), provider, new SchemaContextCompletenessValidator(),
                new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(executionEnabled: true), companyBinder, productBinder,
                productConceptResolver: conceptResolver,
                companyDiscoveryScopeResolver: scenario.WithScope
                    ? new AuthorizedCompanyDiscoveryScopeResolver([new AuthorizedCompanyDiscoveryScope(TenantId, ScopeId, Approval, CompanyCoverageComplete: false)])
                    : new AuthorizedCompanyDiscoveryScopeResolver([]));
            var request = new SchemaNavigationRequest(1, $"کدام شرکت‌ها در {scenario.MonthLabel} {scenario.ProductLabel} تولید کرده‌اند؟",
                snapshot.CatalogVersion, snapshot.CatalogHash, TenantId: TenantId, CorrelationId: $"feature141-discovery-{scenario.Id}");
            var planning = await planner.PlanAsync(request, requirements, CancellationToken.None);
            var record = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = scenario.Id, ["description"] = scenario.Description, ["product"] = scenario.ProductLabel, ["month"] = scenario.MonthLabel,
                ["window"] = $"{period.Start:yyyy-MM-dd}..{period.End:yyyy-MM-dd}", ["expected"] = scenario.Expected.ToString(),
                ["expectedSymbols"] = scenario.ExpectedSymbols, ["expectedDiagnostic"] = scenario.ExpectedDiagnostic,
                ["productConcept"] = planning.Plan.ProductConcept?.CatalogId, ["scope"] = planning.Plan.CompanyDiscoveryScope?.ScopeId,
                ["planEligible"] = planning.Validation.ExecutionEligible, ["planDiagnostics"] = planning.Validation.Diagnostics,
                ["normalization"] = planning.Requirements.NormalizationDiagnostics
            };
            if (scenario.Expected is Expect.NoScope or Expect.ProductNotApproved)
            {
                var compilation = planning.Validation.ExecutionEligible ? new StructuredQueryCompiler().Compile(planning.Plan, compileSnapshot) : null;
                record["compiled"] = compilation?.Success ?? false;
                record["compileDiagnostics"] = compilation?.Diagnostics;
                record["sqlExecuted"] = false;
                if (scenario.Expected == Expect.NoScope)
                {
                    Check("scope", null, planning.Plan.CompanyDiscoveryScope?.ScopeId);
                    Check("diagnostic", true, planning.Requirements.NormalizationDiagnostics.Contains("company_discovery_scope_not_authorized"));
                    Check("noSql", true, compilation is null || !compilation.Success);
                }
                else
                {
                    Check("concept", null, planning.Plan.ProductConcept?.CatalogId);
                    Check("notEligible", false, planning.Validation.ExecutionEligible && (compilation?.Success ?? false));
                }
                results.Add(record);
                output.WriteLine($"{scenario.Id} [{scenario.Description}] eligible={planning.Validation.ExecutionEligible} compiled={compilation?.Success}");
                continue;
            }
            Check("eligible", true, planning.Validation.ExecutionEligible);
            if (!planning.Validation.ExecutionEligible) { results.Add(record); continue; }
            var compiled = new StructuredQueryCompiler().Compile(planning.Plan, compileSnapshot);
            Assert.True(compiled.Success, $"{scenario.Id}: {string.Join("|", compiled.Diagnostics)}");
            var query = compiled.Query!;
            var options = Options.Create(new StructuredQueryExecutionOptions
            {
                Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
                ConnectionString = restrictedConnection, DesignatedDatabase = databaseName, ReadOnlyRole = role,
                CommandTimeoutSeconds = 5, MaximumRows = 100, MaximumResultBytes = 1_000_000, MaximumConcurrency = 1,
                AuthorizationProvenance = "feature141-company-discovery-semantics-fixture",
                ReviewedTenantCompanyDiscoveryScopes = [$"{TenantId:D}|{ScopeId}|{Approval}"],
                ReviewedTenantCompanyBindings = universe.Select(id => $"{TenantId:D}:{id:D}").ToArray()
            });
            var grant = new CatalogStructuredQueryAuthorizer(options, host).Authorize(query, TenantId);
            Assert.NotNull(grant);
            Assert.Empty(query.CompanyIds);
            Assert.Equal(universe.OrderBy(id => id), grant.AllowedCompanyIds.OrderBy(id => id));
            var executor = new NpgsqlReadOnlyStructuredQueryExecutor(options, NullLogger<NpgsqlReadOnlyStructuredQueryExecutor>.Instance, host);
            var first = await executor.ExecuteAsync(query, grant, CancellationToken.None);
            var second = await executor.ExecuteAsync(query, grant, CancellationToken.None);
            var symbols = first.Rows.Select(row => row.GetValueOrDefault("CompanySymbol") as string ?? string.Empty).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var answer = new PersianStructuredQueryResponseGenerator().Generate(new(planning.Plan, first));
            record["sqlExecuted"] = true; record["status"] = first.Status.ToString(); record["diagnostic"] = first.DiagnosticCode;
            record["symbols"] = symbols; record["rows"] = first.Rows.Select(row => new { CompanySymbol = row.GetValueOrDefault("CompanySymbol"), ProductTitle = row.GetValueOrDefault("ProductTitle"), Value = row.GetValueOrDefault("Value"), SourceUnit = row.GetValueOrDefault("SourceUnit"), PeriodEnd = row.GetValueOrDefault("PeriodEnd")?.ToString() }).ToArray();
            record["grantAllowedCompanies"] = grant.AllowedCompanyIds.Count; record["persianAnswerStatus"] = answer.Status.ToString(); record["persianAnswer"] = answer.TextAnswer;
            record["deterministic"] = Render(first.Rows) == Render(second.Rows) && first.Status == second.Status;
            results.Add(record);
            output.WriteLine($"{scenario.Id} [{scenario.Description}] status={first.Status} diag={first.DiagnosticCode} symbols={string.Join(',', symbols)} answer={answer.TextAnswer}");
            Check("determinism", true, Render(first.Rows) == Render(second.Rows) && first.Status == second.Status);
            switch (scenario.Expected)
            {
                case Expect.Producers:
                    Check("status", StructuredQueryExecutionStatus.Incomplete, first.Status);
                    Check("diagnostic", scenario.ExpectedDiagnostic, first.DiagnosticCode);
                    Check("symbols", string.Join(",", scenario.ExpectedSymbols!.OrderBy(s => s, StringComparer.Ordinal)), string.Join(",", symbols));
                    Check("distinct", symbols.Length, symbols.Distinct().Count());
                    Check("allPositive", true, first.Rows.All(row => Convert.ToDecimal(row["Value"], CultureInfo.InvariantCulture) > 0));
                    if (scenario.Id == "D3") // the Mehr report only: 5, never 6 (other window) or 11 (both)
                        Check("value", 5m, Convert.ToDecimal(first.Rows.Single()["Value"], CultureInfo.InvariantCulture));
                    Check("answerLists", true, scenario.ExpectedSymbols!.All(s => answer.TextAnswer.Contains(s, StringComparison.Ordinal)));
                    Check("answerPartial", true, answer.TextAnswer.Contains("جزئی", StringComparison.Ordinal));
                    Check("noSumAcrossUnits", true, !answer.TextAnswer.Contains("مجموع", StringComparison.Ordinal));
                    break;
                case Expect.Ambiguous:
                    Check("status", StructuredQueryExecutionStatus.Ambiguous, first.Status);
                    Check("diagnostic", scenario.ExpectedDiagnostic, first.DiagnosticCode);
                    Check("answerNotAnswered", true, answer.Status != StructuredAnswerStatus.Answered);
                    break;
                case Expect.NoMatch:
                    Check("rows", 0, first.Rows.Count);
                    Check("status", StructuredQueryExecutionStatus.Incomplete, first.Status);
                    Check("answerNotAnswered", true, answer.Status != StructuredAnswerStatus.Answered);
                    break;
            }
        }

        var root = FindRepositoryRoot();
        var artifact = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "evaluation", "artifacts",
            $"company-discovery-semantics-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.json");
        await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new { runAt = DateTimeOffset.UtcNow, catalogHash = snapshot.CatalogHash,
                fixtureOnlyColumnApproval = new[] { "Companies.Name", "Companies.Ticker" }, scenarios = results, failures },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        output.WriteLine($"Artifact: {artifact}");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task<Dictionary<string, Guid>> SeedAsync(LocalPostgreSqlTestDatabase database)
    {
        var now = DateTimeOffset.UtcNow;
        var marketId = Guid.NewGuid();
        var tickers = new[] { "کگل", "کگهر", "شپنا", "فملی", "کچاد", "وبملت", "فولاد", "UNAUTH", "شستا" };
        var companies = tickers.ToDictionary(t => t, _ => Guid.NewGuid(), StringComparer.Ordinal);
        await using var db = database.CreateContext();
        db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "feature141-discovery-semantics-market",
            Name = "Feature 141 discovery semantics fixture", LastSynchronizedAt = now });
        string External(string ticker) => $"cd-{companies[ticker]:N}";
        foreach (var (ticker, id) in companies)
            db.Companies.Add(new NormalizedCompanyRow { Id = id, ProviderName = Provider, ExternalCompanyId = External(ticker), Name = $"Fixture {ticker}",
                Ticker = ticker, CompanySymbol = ticker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
        NormalizedMonthlyReportRow Report(string ticker, string suffix, DateOnly start, DateOnly end, string? externalCompany = null)
        {
            var report = new NormalizedMonthlyReportRow
            {
                Id = Guid.NewGuid(), ProviderName = Provider, ExternalCompanyId = externalCompany ?? External(ticker), ExternalReportId = $"cd-{ticker}-{suffix}",
                PeriodStart = start, PeriodEnd = end, SourcePayloadChecksum = $"cd-{ticker}-{suffix}", LastSynchronizedAt = now,
                LogicalReportKey = $"cd-{ticker}-{start:yyyyMM}-{suffix}", RevisionFingerprint = $"cd-{ticker}-{suffix}", IsAccepted = true,
                RevisionStatus = "Accepted", ReportType = "ProductSales", OutputType = 0, CompanyId = null
            };
            db.MonthlyReports.Add(report);
            return report;
        }
        void Line(NormalizedMonthlyReportRow report, string ticker, string title, decimal? production, string unit = "ton") =>
            db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
            {
                Id = Guid.NewGuid(), MonthlyReportId = report.Id, SourceRowFingerprint = $"row-{report.ExternalReportId}-{title}-{unit}",
                // Production-like ProductCode: a per-row hash reused across companies, never a product identity.
                SourceMultiplicity = 1, ProductCode = "PRODUCT:NATURAL:22d58f29e3fedf1e", Title = title, Unit = unit,
                ProductionQuantity = production, SalesQuantity = production, SalesAmount = production * 10, SalesRate = 10m,
                SourcePayloadChecksum = report.SourcePayloadChecksum
            });

        // Shahrivar 1405 (D1, D2, D7): producers کگل (ton), کگهر (equivalent title, ton), شپنا (kg: same product, different unit);
        // decoys فملی zero, کچاد NULL, وبملت Mordad only, فولاد other product, UNAUTH positive but outside the reviewed universe.
        Line(Report("کگل", "1405-06", ShahrivarStart, ShahrivarEnd), "کگل", "گندله", 25m);
        Line(Report("کگهر", "1405-06", ShahrivarStart, ShahrivarEnd), "کگهر", "گندله سنگ‌آهن", 47m);
        Line(Report("شپنا", "1405-06", ShahrivarStart, ShahrivarEnd), "شپنا", "گندله", 12_000m, unit: "kg");
        Line(Report("فملی", "1405-06", ShahrivarStart, ShahrivarEnd), "فملی", "گندله", 0m);
        Line(Report("کچاد", "1405-06", ShahrivarStart, ShahrivarEnd), "کچاد", "گندله", null);
        Line(Report("وبملت", "1405-05", MordadStart, MordadEnd), "وبملت", "گندله", 30m);
        Line(Report("فولاد", "1405-06", ShahrivarStart, ShahrivarEnd), "فولاد", "کنسانتره", 60m);
        Line(Report("UNAUTH", "1405-06", ShahrivarStart, ShahrivarEnd), "UNAUTH", "گندله", 80m);
        // Mehr 1405 (D3): شستا has two accepted reports ending in the month (same company identity, different PeriodStart).
        Line(Report("شستا", "1405-07-a", MehrStart, MehrEnd), "شستا", "گندله", 5m);
        Line(Report("شستا", "1405-07-b", MehrStart.AddDays(1), MehrEnd), "شستا", "گندله", 6m);
        // Aban 1405 (D4): a report exists but no pellet line.
        Line(Report("فولاد", "1405-08", new(2026, 10, 23), new(2026, 11, 21)), "فولاد", "کنسانتره", 61m);
        await db.SaveChangesAsync();
        return companies;
    }

    private static (StructuredQueryCatalogSnapshot Snapshot, JsonCatalogNavigationHierarchyProvider Provider) Catalog()
    {
        var root = FindRepositoryRoot();
        var catalog = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var hierarchy = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        return (new StructuredQueryCatalogSnapshotBuilder().Build(catalog, hierarchy), new JsonCatalogNavigationHierarchyProvider(hierarchy, catalog));
    }

    /// <summary>Fixture-only: Companies.Name and Companies.Ticker are not query-authorized in the canonical catalog yet (decision D1).</summary>
    private static StructuredQueryCatalogSnapshot WithFixtureOnlyCompanyDisplayColumns(StructuredQueryCatalogSnapshot snapshot)
    {
        var companies = snapshot.Tables.Single(table => table.Schema == "public" && table.Name == "Companies");
        return new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion, snapshot.CatalogHash, true, snapshot.Tables,
            snapshot.Columns.Select(column => column.TableCatalogId == companies.CatalogId && column.Name is "Name" or "Ticker"
                ? column with { Queryable = true } : column).ToArray(),
            snapshot.Relationships, snapshot.DirectSources, snapshot.MonthlyMeasures);
    }

    private static async Task CreateRoleAsync(string adminConnectionString, string role, string password)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; GRANT SELECT ON TABLE public.\"MonthlyReports\", public.\"MonthlyReportLineItems\", public.\"Companies\" TO \"{role}\";";
        await command.ExecuteNonQueryAsync();
    }

    private static string Render(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        JsonSerializer.Serialize(rows.Select(row => row.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value?.ToString())));

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "feature141-company-discovery-semantics";
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

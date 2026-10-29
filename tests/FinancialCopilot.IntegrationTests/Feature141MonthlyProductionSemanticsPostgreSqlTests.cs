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
/// Negative and boundary semantics of the owner-approved monthly production quantity retrieval, run
/// against an isolated PostgreSQL database with the real catalog, the production company/product
/// resolvers, planner, validator, compiler, authorizer and executor, and no LLM. Every scenario is
/// its own company so the company-scoped query isolates it; nothing is substituted or converted.
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class Feature141MonthlyProductionSemanticsPostgreSqlTests(
    FinancialStatementValueSearch131PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string Provider = "NoavaranCurrentApi";
    private const string Measure = "metric_monthly_production_quantity";
    private const string Pellet = "گندله";
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
    private static readonly DateOnly ShahrivarStart = new(2026, 8, 23);
    private static readonly DateOnly ShahrivarEnd = new(2026, 9, 22);
    private static readonly DateOnly MordadStart = new(2026, 7, 23);
    private static readonly DateOnly MordadEnd = new(2026, 8, 22);

    private enum Expect { Answered, ProductNotFound, ProductAmbiguous, NoData, Ambiguous, Incomplete, CatalogRefused, DatabaseDenied, Observed }

    private sealed record Scenario(string Id, string Ticker, string Product, string MonthLabel, string Description, Expect Expected,
        decimal? ExpectedValue = null, string? ExpectedDiagnostic = null, string? ExpectedUnit = null);

    [SkippableFact]
    public async Task MonthlyProductionRetrievalFailsClosedOnEveryNegativeScenario()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var databaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
        var scenarios = new[]
        {
            new Scenario("M1", "کگل", Pellet, "شهریور ۱۴۰۵", "accepted report with the product in the requested month (decoy company and product present)", Expect.Answered, 25m, null, "ton"),
            new Scenario("M2", "کگل", "فولاد", "شهریور ۱۴۰۵", "missing product for the company", Expect.ProductNotFound),
            new Scenario("M3", "فملی", Pellet, "شهریور ۱۴۰۵", "missing report for the requested month (only Mordad exists)", Expect.NoData),
            new Scenario("M4", "کچاد", Pellet, "شهریور ۱۴۰۵", "wrong company: the product belongs to another company only", Expect.ProductNotFound),
            new Scenario("M5", "کگل", Pellet, "مهر ۱۴۰۵", "wrong period: no report for Mehr", Expect.NoData),
            new Scenario("M6", "فولاد", Pellet, "شهریور ۱۴۰۵", "two accepted revisions for the same month", Expect.Ambiguous, null, "monthly_product_period_has_multiple_source_rows"),
            new Scenario("M7", "شپنا", Pellet, "شهریور ۱۴۰۵", "superseded revision (99) and accepted revision (30)", Expect.Answered, 30m, null, "ton"),
            new Scenario("M8", "وبملت", Pellet, "شهریور ۱۴۰۵", "accepted report, line present, NULL quantity", Expect.NoData, null, "monthly_observation_missing"),
            new Scenario("M9", "شستا", Pellet, "شهریور ۱۴۰۵", "accepted report, line present, missing unit", Expect.Incomplete, null, "monthly_quantity_unit_missing"),
            // Owner rules (2026-10-29): product identity is unit independent, but a value is never taken from one
            // of several units silently. The Feature 141 binder detects the multi-unit title through the product
            // catalog and returns Ambiguous; the Feature 129 resolver itself is unchanged.
            new Scenario("M10", "خودرو", Pellet, "شهریور ۱۴۰۵", "same product title reported in two incompatible units (ton and kg)", Expect.ProductAmbiguous),
            new Scenario("M11", "کگل", Pellet, "شهریور ۱۴۰۵", "catalog with the measure unapproved: authorizer refuses, no SQL", Expect.CatalogRefused),
            new Scenario("M12", "کگل", Pellet, "شهریور ۱۴۰۵", "database role without SELECT on MonthlyReportLineItems", Expect.DatabaseDenied, null, "database_query_failed")
        };
        var companies = await SeedAsync(database);
        var role = "feature141_mp_reader_" + Guid.NewGuid().ToString("N");
        var password = "F141mp_" + Guid.NewGuid().ToString("N");
        await CreateRoleAsync(database.ConnectionString, role, password, "\"MonthlyReports\", \"MonthlyReportLineItems\", \"Companies\"");
        var deniedRole = "feature141_mp_denied_" + Guid.NewGuid().ToString("N");
        await CreateRoleAsync(database.ConnectionString, deniedRole, password, "\"MonthlyReports\", \"Companies\"");
        string Connection(string user) => new NpgsqlConnectionStringBuilder(database.ConnectionString) { Username = user, Password = password, Pooling = false }.ConnectionString;

        var (snapshot, provider) = Catalog();
        var unapprovedSnapshot = new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion, snapshot.CatalogHash, true,
            snapshot.Tables, snapshot.Columns, snapshot.Relationships, snapshot.DirectSources,
            snapshot.MonthlyMeasures.Select(m => m.CatalogId == Measure ? m with { Queryable = false, SemanticApproved = false } : m).ToArray());
        await using var bindingDb = database.CreateContext();
        var companyBinder = new StructuredQueryCompanyBinder(new CanonicalQueryEntityResolver(bindingDb,
            Options.Create(new CanonicalEntityResolutionOptions(Enabled: true))));
        var productBinder = new StructuredQueryProductBinder(new CompanyScopedProductResolver(
            new CompanyResolverService(bindingDb, NullLogger<CompanyResolverService>.Instance),
            new EfCoreMonthlyProductComparisonRepository(bindingDb)), new EfCoreMonthlyProductComparisonRepository(bindingDb));
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
            var requirements = ReportingPeriodRequirementsNormalizer.Apply(new QuestionSchemaRequirements(1, snapshot.CatalogVersion, snapshot.CatalogHash,
                [Measure], ["dim_company", "dim_monthly_period", "dim_product_identity"], ["company", "product"],
                ["rel_31e410fb3ae11956"], [], [], [], ["scalar"], EntityReferences: [scenario.Ticker],
                FinancialValueReferences: [new(nameof(FinancialValueReferenceType.MonthlyMeasure), Measure)])
            { ProductReferences = [scenario.Product] }, period);
            var required = requirements.MeasureIds.Concat(requirements.DimensionIds).Concat(requirements.RelationshipIds).ToArray();
            var seedContext = await provider.GetContextAsync(new SchemaContextExpansionRequest(1, snapshot.CatalogVersion, snapshot.CatalogHash,
                [], 0, RequiredCatalogIds: required), CancellationToken.None);
            Assert.NotNull(seedContext);
            var navigation = new SchemaNavigationResult(1, snapshot.CatalogVersion, snapshot.CatalogHash, [], seedContext.NodeIds,
                requirements.MeasureIds, requirements.DimensionIds, requirements.EntityKinds, requirements.RelationshipIds, [],
                requirements.PeriodPolicies, [], [], SelectedPresentationRequirements: requirements.PresentationRequirements,
                SelectedEntityReferences: (requirements.EntityReferences ?? []).Concat(requirements.ProductReferences ?? []).ToArray(),
                SelectedFinancialValueReferences: requirements.FinancialValueReferences,
                FinancialValueOperation: requirements.RequestedFinancialValueOperation);
            var planner = new OfflineStructuredQueryPlanningService(new CapturedRequirementsExtractor(requirements),
                new CapturedNavigator(navigation), provider, new SchemaContextCompletenessValidator(),
                new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(executionEnabled: true), companyBinder, productBinder);
            var request = new SchemaNavigationRequest(1, $"تولید {scenario.Product} {scenario.Ticker} در {scenario.MonthLabel} چقدر بود؟",
                snapshot.CatalogVersion, snapshot.CatalogHash, TenantId: TenantId, CorrelationId: $"feature141-monthly-{scenario.Id}");
            var planning = await planner.PlanAsync(request, requirements, CancellationToken.None);
            var record = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = scenario.Id, ["description"] = scenario.Description, ["ticker"] = scenario.Ticker, ["product"] = scenario.Product,
                ["month"] = scenario.MonthLabel, ["normalizedWindow"] = $"{period.Start:yyyy-MM-dd}..{period.End:yyyy-MM-dd}",
                ["expected"] = scenario.Expected.ToString(), ["expectedValue"] = scenario.ExpectedValue, ["expectedDiagnostic"] = scenario.ExpectedDiagnostic,
                ["companyBinding"] = planning.CompanyBinding?.Status.ToString(), ["productBinding"] = planning.ProductBinding?.Status.ToString(),
                ["productKey"] = planning.ProductBinding?.ProductKey, ["planEligible"] = planning.Validation.ExecutionEligible,
                ["planDiagnostics"] = planning.Validation.Diagnostics
            };
            if (scenario.Expected is Expect.ProductNotFound or Expect.ProductAmbiguous)
            {
                Check("productBinding", scenario.Expected == Expect.ProductNotFound ? StructuredProductBindingStatus.NotFound : StructuredProductBindingStatus.Ambiguous,
                    planning.ProductBinding?.Status ?? StructuredProductBindingStatus.Missing);
                Check("eligible", false, planning.Validation.ExecutionEligible);
                Check("diagnostic", true, planning.Validation.Diagnostics.Contains("monthly_product_binding_required"));
                record["sqlExecuted"] = false;
                results.Add(record);
                output.WriteLine($"{scenario.Id} [{scenario.Description}] productBinding={planning.ProductBinding?.Status} eligible={planning.Validation.ExecutionEligible}");
                continue;
            }
            Check("company", companies[scenario.Ticker], planning.CompanyBinding?.CompanyId);
            Check("eligible", true, planning.Validation.ExecutionEligible);
            if (!planning.Validation.ExecutionEligible)
            {
                failures.Add($"{scenario.Id} plan not eligible: {string.Join("|", planning.Validation.Diagnostics)}");
                results.Add(record);
                continue;
            }
            var compilation = new StructuredQueryCompiler().Compile(planning.Plan, scenario.Expected == Expect.CatalogRefused ? unapprovedSnapshot : snapshot);
            Assert.True(compilation.Success, $"{scenario.Id}: {string.Join("|", compilation.Diagnostics)}");
            var query = compilation.Query!;
            var executionRole = scenario.Expected == Expect.DatabaseDenied ? deniedRole : role;
            var options = Options.Create(new StructuredQueryExecutionOptions
            {
                Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
                ConnectionString = Connection(executionRole), DesignatedDatabase = databaseName, ReadOnlyRole = executionRole,
                CommandTimeoutSeconds = 5, MaximumRows = 100, MaximumResultBytes = 1_000_000, MaximumConcurrency = 1,
                AuthorizationProvenance = "feature141-monthly-production-semantics-fixture"
            });
            var grant = new CatalogStructuredQueryAuthorizer(options, host).Authorize(query, TenantId);
            record["compiledQueryAuthorized"] = query.QueryAuthorized;
            record["compiledSemanticReviewApproved"] = query.SemanticReviewApproved;
            record["grantIssued"] = grant is not null;
            record["sqlParameters"] = query.Parameters.ToDictionary(p => p.Name, p => p.Value);
            if (scenario.Expected == Expect.CatalogRefused)
            {
                Check("compiledQueryAuthorized", false, query.QueryAuthorized);
                Check("grant", false, grant is not null);
                record["sqlExecuted"] = false;
                results.Add(record);
                output.WriteLine($"{scenario.Id} [{scenario.Description}] authorized={query.QueryAuthorized} grant={grant is not null}");
                continue;
            }
            Assert.NotNull(grant);
            var executor = new NpgsqlReadOnlyStructuredQueryExecutor(options, NullLogger<NpgsqlReadOnlyStructuredQueryExecutor>.Instance, host);
            var first = await executor.ExecuteAsync(query, grant, CancellationToken.None);
            var second = await executor.ExecuteAsync(query, grant, CancellationToken.None);
            var value = first.Rows.Count == 1 && first.Rows[0].GetValueOrDefault("Value") is { } v ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : (decimal?)null;
            var unit = first.Rows.Count == 1 ? first.Rows[0].GetValueOrDefault("SourceUnit") as string : null;
            var productCode = first.Rows.Count == 1 ? first.Rows[0].GetValueOrDefault("ProductCode") as string : null;
            var answer = new PersianStructuredQueryResponseGenerator().Generate(new(planning.Plan, first));
            record["sqlExecuted"] = true;
            record["status"] = first.Status.ToString(); record["diagnostic"] = first.DiagnosticCode; record["value"] = value; record["unit"] = unit;
            record["productCode"] = productCode; record["rows"] = first.Rows.Count; record["persianAnswerStatus"] = answer.Status.ToString();
            record["persianAnswer"] = answer.TextAnswer; record["deterministic"] = Render(first.Rows) == Render(second.Rows) && first.Status == second.Status;
            results.Add(record);
            output.WriteLine($"{scenario.Id} [{scenario.Description}] status={first.Status} diag={first.DiagnosticCode} value={value} unit={unit} rows={first.Rows.Count} answer={answer.TextAnswer}");
            Check("determinism", true, Render(first.Rows) == Render(second.Rows) && first.Status == second.Status);
            switch (scenario.Expected)
            {
                case Expect.Answered:
                    Check("status", StructuredQueryExecutionStatus.Succeeded, first.Status);
                    Check("value", scenario.ExpectedValue, value);
                    Check("unit", scenario.ExpectedUnit, unit);
                    Check("productKey", planning.ProductBinding?.ProductKey, productCode);
                    Check("answer", StructuredAnswerStatus.Answered, answer.Status);
                    Check("answerUnit", "تن", answer.Unit);
                    break;
                case Expect.NoData:
                    Check("status", StructuredQueryExecutionStatus.NoData, first.Status);
                    if (scenario.ExpectedDiagnostic is not null) Check("diagnostic", scenario.ExpectedDiagnostic, first.DiagnosticCode);
                    Check("value", null, value);
                    Check("answerNotAnswered", true, answer.Status != StructuredAnswerStatus.Answered);
                    break;
                case Expect.Ambiguous:
                    Check("status", StructuredQueryExecutionStatus.Ambiguous, first.Status);
                    Check("diagnostic", scenario.ExpectedDiagnostic, first.DiagnosticCode);
                    Check("answerNotAnswered", true, answer.Status != StructuredAnswerStatus.Answered);
                    break;
                case Expect.Incomplete:
                    Check("status", StructuredQueryExecutionStatus.Incomplete, first.Status);
                    Check("diagnostic", scenario.ExpectedDiagnostic, first.DiagnosticCode);
                    Check("answerNotAnswered", true, answer.Status != StructuredAnswerStatus.Answered);
                    break;
                case Expect.DatabaseDenied:
                    Check("status", StructuredQueryExecutionStatus.Failed, first.Status);
                    Check("diagnostic", scenario.ExpectedDiagnostic, first.DiagnosticCode);
                    Check("rows", 0, first.Rows.Count);
                    Check("answerNotAnswered", true, answer.Status != StructuredAnswerStatus.Answered);
                    break;
            }
        }

        var root = FindRepositoryRoot();
        var artifact = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "evaluation", "artifacts",
            $"monthly-production-semantics-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.json");
        await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new { runAt = DateTimeOffset.UtcNow, catalogHash = snapshot.CatalogHash, scenarios = results, failures },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        output.WriteLine($"Artifact: {artifact}");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task<Dictionary<string, Guid>> SeedAsync(LocalPostgreSqlTestDatabase database)
    {
        var now = DateTimeOffset.UtcNow;
        var marketId = Guid.NewGuid();
        var tickers = new[] { "کگل", "کگهر", "فملی", "کچاد", "فولاد", "شپنا", "وبملت", "شستا", "خودرو" };
        var companies = tickers.ToDictionary(t => t, _ => Guid.NewGuid(), StringComparer.Ordinal);
        await using var db = database.CreateContext();
        db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "feature141-monthly-semantics-market",
            Name = "Feature 141 monthly semantics fixture", LastSynchronizedAt = now });
        foreach (var (ticker, id) in companies)
            db.Companies.Add(new NormalizedCompanyRow { Id = id, ProviderName = Provider, ExternalCompanyId = $"mp-{id:N}", Name = $"Fixture {ticker}",
                Ticker = ticker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
        NormalizedMonthlyReportRow Report(string ticker, string suffix, DateOnly start, DateOnly end, bool accepted = true, string revision = "Accepted")
        {
            var report = new NormalizedMonthlyReportRow
            {
                Id = Guid.NewGuid(), ProviderName = Provider, ExternalCompanyId = $"mp-{companies[ticker]:N}", ExternalReportId = $"mp-{ticker}-{suffix}",
                PeriodStart = start, PeriodEnd = end, SourcePayloadChecksum = $"mp-{ticker}-{suffix}", LastSynchronizedAt = now,
                LogicalReportKey = $"mp-{ticker}-{start:yyyyMM}", RevisionFingerprint = $"mp-{ticker}-{suffix}", IsAccepted = accepted,
                RevisionStatus = revision, ReportType = "ProductSales", OutputType = 0, CompanyId = companies[ticker]
            };
            db.MonthlyReports.Add(report);
            return report;
        }
        string Key(string ticker, string title, string unit) => $"{Provider}:mp-{companies[ticker]:N}:TITLE:{title}|UNIT:{unit}";
        void Line(NormalizedMonthlyReportRow report, string ticker, string title, decimal? production, string unit = "ton", string? code = null) =>
            db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
            {
                Id = Guid.NewGuid(), MonthlyReportId = report.Id, SourceRowFingerprint = $"row-{report.ExternalReportId}-{title}-{unit}-{Guid.NewGuid():N}",
                SourceMultiplicity = 1, ProductCode = code ?? Key(ticker, title, unit), Title = title, Unit = unit, ProductionQuantity = production,
                SalesQuantity = production, SalesAmount = production * 10, SalesRate = 10m, SourcePayloadChecksum = report.SourcePayloadChecksum
            });

        // M1/M2/M5/M11/M12: کگل Shahrivar accepted with گندله 25 and کنسانتره 60; کگهر decoy with گندله 47.
        var kgol = Report("کگل", "1405-06", ShahrivarStart, ShahrivarEnd);
        Line(kgol, "کگل", Pellet, 25m); Line(kgol, "کگل", "کنسانتره", 60m);
        Line(Report("کگهر", "1405-06", ShahrivarStart, ShahrivarEnd), "کگهر", Pellet, 47m);
        // M3: فملی has only Mordad.
        Line(Report("فملی", "1405-05", MordadStart, MordadEnd), "فملی", Pellet, 33m);
        // M4: کچاد has a report without گندله.
        Line(Report("کچاد", "1405-06", ShahrivarStart, ShahrivarEnd), "کچاد", "کنسانتره", 70m);
        // M6: فولاد two accepted reports for the same month. The schema's IX_MonthlyReports_LogicalPeriod unique
        // index forbids two accepted rows with the same provider/external company/period, so the realistic
        // duplicate is a provider company alias (second ExternalCompanyId mapped to the same CompanyId).
        Line(Report("فولاد", "1405-06-a", ShahrivarStart, ShahrivarEnd), "فولاد", Pellet, 40m);
        var alias = Report("فولاد", "1405-06-b", ShahrivarStart, ShahrivarEnd);
        alias.ExternalCompanyId = $"mp-{companies["فولاد"]:N}-alias";
        Line(alias, "فولاد", Pellet, 41m);
        // M7: شپنا superseded (99) then accepted (30).
        Line(Report("شپنا", "1405-06-old", ShahrivarStart, ShahrivarEnd, accepted: false, revision: "Superseded"), "شپنا", Pellet, 99m);
        Line(Report("شپنا", "1405-06", ShahrivarStart, ShahrivarEnd), "شپنا", Pellet, 30m);
        // M8: وبملت NULL quantity.
        Line(Report("وبملت", "1405-06", ShahrivarStart, ShahrivarEnd), "وبملت", Pellet, null);
        // M9: شستا missing unit.
        Line(Report("شستا", "1405-06", ShahrivarStart, ShahrivarEnd), "شستا", Pellet, 50m, unit: "");
        // M10: خودرو reports the same product title in two incompatible units (two company-scoped product keys).
        var khodro = Report("خودرو", "1405-06", ShahrivarStart, ShahrivarEnd);
        Line(khodro, "خودرو", Pellet, 10m, unit: "ton");
        Line(khodro, "خودرو", Pellet, 10_000m, unit: "kg");
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

    private static async Task CreateRoleAsync(string adminConnectionString, string role, string password, string tables)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"; GRANT SELECT ON TABLE {string.Join(", ", tables.Split(',').Select(t => "public." + t.Trim()))} TO \"{role}\";";
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
        public string ApplicationName { get; set; } = "feature141-monthly-production-semantics";
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

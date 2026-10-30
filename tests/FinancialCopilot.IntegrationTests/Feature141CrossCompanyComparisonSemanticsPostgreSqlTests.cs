using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Cross-company comparison and ranking of a statement item (net profit, IncomeStatement/143) verified
/// against isolated PostgreSQL with the real catalog and the production cross-company handoff, planner,
/// validator, compiler, catalog authorizer, restricted executor and Persian renderer, without an LLM.
/// Every scenario gets its own database, is executed twice and must be identical (determinism).
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class Feature141CrossCompanyComparisonSemanticsPostgreSqlTests(
    FinancialStatementValueSearch131PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string Provider = "NoavaranCurrentApi";
    private const string ScopeId = "feature141-cross-company-fixture-scope";
    private const string ScopeApproval = "fixture-only:feature141-cross-company-comparison:2026-10-29";
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
    private static readonly DateOnly T2Start = new(2026, 3, 21);
    private static readonly DateOnly T2End = new(2026, 6, 21);
    private static readonly DateOnly T1Start = new(2025, 12, 21);
    private static readonly DateOnly T1End = new(2026, 3, 20);

    public sealed record Statement(string Ticker, decimal? Value, DateOnly Start, DateOnly End, bool Composing = false,
        string PeriodType = "ThreeMonths", bool HasLine = true, string Suffix = "a", bool Audited = false);

    public sealed record Expected(string Label, decimal Value, int Rank, bool Tied);

    public sealed record Scenario(string Id, string Description, string[] Companies, Statement[] Statements,
        CanonicalSemanticIntent Intent, StructuredQueryExecutionStatus? ExpectedStatus, string? ExpectedDiagnostic,
        Expected[] ExpectedRows, string[]? Universe = null, bool CoverageComplete = true, bool ScopeConfigured = true,
        string? ExpectedRefusal = null, bool UnresolvedUnitSnapshot = false, string[]? ExpectedWarningPrefixes = null);

    public static IEnumerable<object[]> ScenarioIds() => Scenarios().Select(scenario => new object[] { scenario.Id });

    [SkippableTheory]
    [MemberData(nameof(ScenarioIds))]
    public async Task CrossCompanyComparisonIsCorrectDeterministicAndFailsClosed(string scenarioId)
    {
        var scenario = Scenarios().Single(item => item.Id == scenarioId);
        await using var database = await fixture.CreateDatabaseAsync();
        var databaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
        var (snapshot, provider) = Catalog();
        var source = snapshot.DirectSources.Single(item => item.ProviderName == Provider
            && item.StatementType == "IncomeStatement" && item.SourceItemId == 143);
        var companies = await SeedAsync(database, scenario, source.SourceItemCatalogId);
        var role = "feature141_cc_reader_" + Guid.NewGuid().ToString("N");
        var password = "F141cc_" + Guid.NewGuid().ToString("N");
        await CreateRestrictedRoleAsync(database.ConnectionString, role, password);
        var restrictedConnection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            { Username = role, Password = password, Pooling = false }.ConnectionString;

        var record = new Dictionary<string, object?>
        {
            ["id"] = scenario.Id, ["description"] = scenario.Description,
            ["expectedStatus"] = scenario.ExpectedStatus?.ToString(), ["expectedDiagnostic"] = scenario.ExpectedDiagnostic,
            ["expectedRefusal"] = scenario.ExpectedRefusal, ["expectedRows"] = scenario.ExpectedRows
        };
        try
        {
            // Typed binding and the cross-company handoff (same entry point as the engine runner).
            var references = await provider.GetFinancialSourceReferencesAsync(snapshot.CatalogVersion, snapshot.CatalogHash, CancellationToken.None);
            var binding = CanonicalIntentCatalogBinding.Bind(scenario.Intent, references);
            record["bindingStatus"] = binding.Status.ToString();
            Assert.Equal(CanonicalCatalogBindingStatus.Bound, binding.Status);
            var resolution = CanonicalSourceItemBindingResolver.Resolve(scenario.Intent.Measures[0], references, Provider);
            var bound = Assert.IsType<FinancialValueCatalogReference>(resolution.Reference);
            var handoff = FinancialSourcePlannerHandoff.CreateCrossCompany(scenario.Intent,
                new FinancialSourceTitleResolution(FinancialSourceTitleResolutionStatus.Resolved, resolution.DiagnosticCode, bound, [bound],
                    new Dictionary<string, string?>(), bound.UnitClassification, bound.QueryAuthorizationStatus ?? "authorized"),
                snapshot.CatalogVersion, snapshot.CatalogHash, source);
            Assert.Equal(FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation, handoff.Status);
            var requirements = handoff.Binding!.PlannerRequirements;

            var required = requirements.DimensionIds.Concat(requirements.RelationshipIds)
                .Concat(requirements.FinancialValueReferences!.Select(r => r.CatalogId)).ToArray();
            var seedContext = await provider.GetContextAsync(new SchemaContextExpansionRequest(1, snapshot.CatalogVersion,
                snapshot.CatalogHash, [], 0, RequiredCatalogIds: required), CancellationToken.None);
            Assert.NotNull(seedContext);
            var navigation = new SchemaNavigationResult(1, snapshot.CatalogVersion, snapshot.CatalogHash, [], seedContext.NodeIds,
                [], requirements.DimensionIds, requirements.EntityKinds, requirements.RelationshipIds, requirements.SourcePolicies,
                requirements.PeriodPolicies, [], [], SelectedComparisonScopes: requirements.ComparisonScopes,
                SelectedPresentationRequirements: requirements.PresentationRequirements,
                SelectedStatementScopes: requirements.StatementScopes, SelectedEntityReferences: requirements.EntityReferences,
                SelectedFinancialValueReferences: requirements.FinancialValueReferences,
                FinancialValueOperation: requirements.RequestedFinancialValueOperation);
            await using var bindingDb = database.CreateContext();
            var companyBinder = new StructuredQueryCompanyBinder(new CanonicalQueryEntityResolver(bindingDb,
                Options.Create(new CanonicalEntityResolutionOptions(Enabled: true))));
            var scopeResolver = new AuthorizedCompanyDiscoveryScopeResolver(scenario.ScopeConfigured
                ? [new AuthorizedCompanyDiscoveryScope(TenantId, ScopeId, ScopeApproval, scenario.CoverageComplete)]
                : []);
            var planner = new OfflineStructuredQueryPlanningService(new CapturedRequirementsExtractor(requirements),
                new CapturedNavigator(navigation), provider, new SchemaContextCompletenessValidator(),
                new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(executionEnabled: true), companyBinder,
                companyDiscoveryScopeResolver: scopeResolver);
            var request = new SchemaNavigationRequest(1, scenario.Description, snapshot.CatalogVersion, snapshot.CatalogHash,
                TenantId: TenantId, CorrelationId: $"feature141-cross-company-{scenario.Id}");
            var planning = await planner.PlanAsync(request, requirements, CancellationToken.None);
            record["planEligible"] = planning.Validation.ExecutionEligible;
            record["planDiagnostics"] = planning.Validation.Diagnostics.Concat(planning.Plan.Diagnostics).Distinct().ToArray();
            record["companyBindings"] = (planning.CompanyBindings ?? []).Select(item => $"{item.Status}:{item.CompanyId}").ToArray();
            if (!planning.Validation.ExecutionEligible)
            {
                Assert.NotNull(scenario.ExpectedRefusal);
                Assert.Contains(scenario.ExpectedRefusal!, planning.Plan.Diagnostics.Concat(planning.Validation.Diagnostics));
                record["outcome"] = "refused_before_sql";
                return;
            }

            var compileSnapshot = scenario.UnresolvedUnitSnapshot ? WithoutEffectiveUnit(snapshot, source.CatalogId) : snapshot;
            var compilation = new StructuredQueryCompiler().Compile(planning.Plan, compileSnapshot);
            record["compilationDiagnostics"] = compilation.Diagnostics;
            if (!compilation.Success)
            {
                Assert.NotNull(scenario.ExpectedRefusal);
                Assert.Contains(scenario.ExpectedRefusal!, compilation.Diagnostics);
                record["outcome"] = "refused_at_compilation";
                return;
            }
            Assert.Null(scenario.ExpectedRefusal);
            var query = compilation.Query!;
            record["sql"] = query.Sql;
            record["parameters"] = query.Parameters.ToDictionary(p => p.Name, p => p.Value is Guid[] ids
                ? string.Join(",", ids.Select(id => companies.Single(pair => pair.Value == id).Key)) : p.Value);
            record["effectiveUnit"] = query.EffectiveUnit;
            var universe = (scenario.Universe ?? []).Select(ticker => companies[ticker]).ToArray();
            var options = Options.Create(new StructuredQueryExecutionOptions
            {
                Enabled = true, ExplicitDevelopmentOptIn = true, EnvironmentName = "Development",
                ConnectionString = restrictedConnection, DesignatedDatabase = databaseName, ReadOnlyRole = role,
                CommandTimeoutSeconds = 5, MaximumRows = 100, MaximumResultBytes = 1_000_000, MaximumConcurrency = 1,
                AuthorizationProvenance = "feature141-cross-company-semantics-fixture",
                ReviewedTenantCompanyDiscoveryScopes = universe.Length > 0 ? [$"{TenantId:D}|{ScopeId}|{ScopeApproval}"] : [],
                ReviewedTenantCompanyBindings = universe.Select(id => $"{TenantId:D}:{id:D}").ToArray()
            });
            var host = new TestEnvironment("Development");
            var grant = new CatalogStructuredQueryAuthorizer(options, host).Authorize(query, TenantId);
            Assert.NotNull(grant);
            var executor = new NpgsqlReadOnlyStructuredQueryExecutor(options, NullLogger<NpgsqlReadOnlyStructuredQueryExecutor>.Instance, host);
            var first = await executor.ExecuteAsync(query, grant, CancellationToken.None);
            var second = await executor.ExecuteAsync(query, grant, CancellationToken.None);
            var answer = new PersianStructuredQueryResponseGenerator().Generate(new(planning.Plan, first, "postgresql_execution"));
            var observed = first.Rows.Select(row => new Expected((string)row["CompanyLabel"]!,
                Convert.ToDecimal(row["Value"], CultureInfo.InvariantCulture), (int)row["Rank"]!, (bool)row["TiedWithOthers"]!)).ToArray();
            record["observedStatus"] = first.Status.ToString();
            record["observedDiagnostic"] = first.DiagnosticCode;
            record["observedRows"] = observed;
            record["warnings"] = first.Warnings;
            record["deterministic"] = Render(first) == Render(second);
            record["persianAnswerStatus"] = answer.Status.ToString();
            record["persianAnswer"] = answer.TextAnswer;
            output.WriteLine($"{scenario.Id} status={first.Status} diag={first.DiagnosticCode} rows={string.Join(" | ", observed.Select(o => $"{o.Rank}:{o.Label}={o.Value}{(o.Tied ? "*" : "")}"))} warnings={string.Join(",", first.Warnings)}");
            output.WriteLine(answer.TextAnswer);

            Assert.Equal(Render(first), Render(second));
            Assert.Equal(scenario.ExpectedStatus, first.Status);
            Assert.Equal(scenario.ExpectedDiagnostic, first.DiagnosticCode);
            Assert.Equal(scenario.ExpectedRows, observed);
            foreach (var prefix in scenario.ExpectedWarningPrefixes ?? [])
                Assert.Contains(first.Warnings, warning => warning.StartsWith(prefix, StringComparison.Ordinal));
            if (first.Status == StructuredQueryExecutionStatus.Succeeded)
            {
                Assert.Equal(StructuredAnswerStatus.Answered, answer.Status);
                foreach (var row in scenario.ExpectedRows)
                {
                    Assert.Contains(row.Label, answer.TextAnswer, StringComparison.Ordinal);
                    // Million IRR amounts are displayed in Toman.
                    Assert.Contains(PersianStructuredQueryResponseGenerator.Amount(row.Value, PersianStructuredQueryResponseGenerator.MillionIrrLabel),
                        answer.TextAnswer, StringComparison.Ordinal);
                }
                Assert.Contains("تومان", answer.TextAnswer, StringComparison.Ordinal);
            }
            else if (first.DiagnosticCode != "population_ranking_authorized_company_coverage_incomplete")
            {
                // A non-answer never presents a comparison verdict or ranking.
                Assert.NotEqual(StructuredAnswerStatus.Answered, answer.Status);
                Assert.DoesNotContain("رتبه‌بندی", answer.TextAnswer, StringComparison.Ordinal);
                Assert.DoesNotContain("بیشترین", answer.TextAnswer, StringComparison.Ordinal);
            }
            else
            {
                // Partial population coverage is rendered as an explicitly partial ranking, never as Answered.
                Assert.Equal(StructuredAnswerStatus.Incomplete, answer.Status);
                Assert.Contains("جزئی", answer.TextAnswer, StringComparison.Ordinal);
            }
            record["outcome"] = "executed";
        }
        finally
        {
            await WriteArtifactAsync(record);
        }
    }

    private static IEnumerable<Scenario> Scenarios()
    {
        var two = new[] { "کگل", "کگهر" };
        var three = new[] { "فملی", "کگل", "کگهر" };
        Statement S(string ticker, decimal? value, bool older = false, string suffix = "a", bool composing = false,
            bool hasLine = true, string periodType = "ThreeMonths", bool audited = false) =>
            new(ticker, value, older ? T1Start : T2Start, older ? T1End : T2End, composing, periodType, hasLine, suffix, audited);
        Expected E(string label, decimal value, int rank, bool tied = false) => new(label, value, rank, tied);
        var e05Data = new[] { S("کگل", 1_200m), S("کگهر", 900m), S("فملی", 1_600m), S("کگل", 1_000m, older: true), S("کگهر", 2_000m, older: true) };
        var e14Data = new[] { S("کگل", 1_200m), S("کگهر", 900m), S("فملی", 1_600m) };

        yield return new("X01", "named comparison of two companies at the shared latest period; a third company and older periods are decoys",
            three, e05Data, Compare(two), StructuredQueryExecutionStatus.Succeeded, null, [E("کگل", 1_200m, 1), E("کگهر", 900m, 2)]);
        yield return new("X02", "descending ranking of three named companies", three, e05Data, Rank(three, "descending"),
            StructuredQueryExecutionStatus.Succeeded, null, [E("فملی", 1_600m, 1), E("کگل", 1_200m, 2), E("کگهر", 900m, 3)]);
        yield return new("X03", "equal values share a competition rank and are ordered by company label", three,
            [S("کگل", 1_200m), S("کگهر", 1_200m), S("فملی", 900m)], Rank(three, "descending"),
            StructuredQueryExecutionStatus.Succeeded, null, [E("کگل", 1_200m, 1, true), E("کگهر", 1_200m, 1, true), E("فملی", 900m, 3)]);
        yield return new("X04", "ascending ranking limited to the top one", three, e05Data, Rank(three, "ascending", limit: 1),
            StructuredQueryExecutionStatus.Succeeded, null, [E("کگهر", 900m, 1)]);
        yield return new("X05", "a tie at the top-N boundary keeps every tied company", three,
            [S("فملی", 1_600m), S("کگل", 1_200m), S("کگهر", 1_200m)], Rank(three, "descending", limit: 2),
            StructuredQueryExecutionStatus.Succeeded, null, [E("فملی", 1_600m, 1), E("کگل", 1_200m, 2, true), E("کگهر", 1_200m, 2, true)],
            ExpectedWarningPrefixes: [CrossCompanyStatementComparisonPolicy.WarningTopNExtendedByTies]);
        yield return new("X06", "missing metric value for one company is not zero; the comparison is incomplete", two,
            [S("کگل", 1_200m), S("کگهر", null, hasLine: false)], Compare(two),
            StructuredQueryExecutionStatus.Incomplete, "comparison_company_value_missing", [E("کگل", 1_200m, 1)],
            ExpectedWarningPrefixes: [CrossCompanyStatementComparisonPolicy.WarningCompanyValueMissing + ":کگهر"]);
        yield return new("X07", "different latest periods: both companies are read at the latest common period", two,
            [S("کگل", 1_300m), S("کگل", 1_100m, older: true), S("کگهر", 800m, older: true)], Compare(two),
            StructuredQueryExecutionStatus.Succeeded, null, [E("کگل", 1_100m, 1), E("کگهر", 800m, 2)],
            ExpectedWarningPrefixes: [CrossCompanyStatementComparisonPolicy.WarningNewerPeriodAvailable + ":کگل"]);
        yield return new("X08", "no common reporting period: nothing is compared", two,
            [S("کگل", 1_300m), S("کگهر", 800m, older: true)], Compare(two),
            StructuredQueryExecutionStatus.Incomplete, "no_common_reporting_period", [],
            ExpectedWarningPrefixes: [CrossCompanyStatementComparisonPolicy.WarningCompanyLatestPeriod]);
        yield return new("X09", "same period end but different period types is not a common period", two,
            [S("کگل", 1_300m), S("کگهر", 3_800m, periodType: "TwelveMonths")], Compare(two),
            StructuredQueryExecutionStatus.Incomplete, "no_common_reporting_period", []);
        yield return new("X10", "duplicate statements with the same value resolve to one observation", two,
            [S("کگل", 1_200m, suffix: "a"), S("کگل", 1_200m, suffix: "b", audited: true), S("کگهر", 900m)], Compare(two),
            StructuredQueryExecutionStatus.Succeeded, null, [E("کگل", 1_200m, 1), E("کگهر", 900m, 2)]);
        yield return new("X11", "duplicate statements with conflicting values are never chosen arbitrarily", two,
            [S("کگل", 1_200m, suffix: "a"), S("کگل", 1_250m, suffix: "b", audited: true), S("کگهر", 900m)], Compare(two),
            StructuredQueryExecutionStatus.Ambiguous, "requested_item_values_conflict", []);
        yield return new("X12", "consolidated versus standalone companies are not compared without a basis", two,
            [S("کگل", 1_500m, composing: true), S("کگهر", 900m)], Compare(two),
            StructuredQueryExecutionStatus.Ambiguous, "cross_company_consolidation_basis_mismatch", []);
        yield return new("X13", "a company reporting on both bases needs an explicit basis", two,
            [S("کگل", 1_200m), S("کگل", 1_500m, composing: true), S("کگهر", 900m)], Compare(two),
            StructuredQueryExecutionStatus.Ambiguous, "statement_scope_unspecified_across_consolidation_bases", []);
        yield return new("X14", "explicit consolidated basis compares the consolidated statements only", two,
            [S("کگل", 1_200m), S("کگل", 1_500m, composing: true), S("کگهر", 950m, composing: true), S("کگهر", 900m)],
            Compare(two, FinancialStatementScope.Consolidated),
            StructuredQueryExecutionStatus.Succeeded, null, [E("کگل", 1_500m, 1), E("کگهر", 950m, 2)]);
        yield return new("X15", "empty result set: the named companies have no statements", two, [], Compare(two),
            StructuredQueryExecutionStatus.Incomplete, "comparison_companies_without_statements", []);
        yield return new("X16", "population ranking over the authorized universe; an unauthorized company is never read",
            [.. three, "فولاد"], [.. e14Data, S("فولاد", 9_999m)], RankPopulation("descending"),
            StructuredQueryExecutionStatus.Succeeded, null, [E("فملی", 1_600m, 1), E("کگل", 1_200m, 2), E("کگهر", 900m, 3)],
            Universe: three);
        yield return new("X17", "population member without statements is excluded and disclosed",
            [.. three, "شستا"], e14Data, RankPopulation("descending"),
            StructuredQueryExecutionStatus.Succeeded, null, [E("فملی", 1_600m, 1), E("کگل", 1_200m, 2), E("کگهر", 900m, 3)],
            Universe: [.. three, "شستا"], ExpectedWarningPrefixes: [CrossCompanyStatementComparisonPolicy.WarningPopulationMembersWithoutStatements]);
        yield return new("X18", "incomplete population coverage yields a partial, explicitly incomplete ranking", three, e14Data,
            RankPopulation("descending"), StructuredQueryExecutionStatus.Incomplete, "population_ranking_authorized_company_coverage_incomplete",
            [E("فملی", 1_600m, 1), E("کگل", 1_200m, 2), E("کگهر", 900m, 3)], Universe: three, CoverageComplete: false);
        yield return new("X19", "population without an authorized discovery scope is refused before SQL", three, e14Data,
            RankPopulation("descending"), null, null, [], Universe: three, ScopeConfigured: false,
            ExpectedRefusal: "company_discovery_scope_not_authorized");
        yield return new("X20", "empty population: no member has statements", three, [], RankPopulation("descending"),
            StructuredQueryExecutionStatus.NoData, "comparison_population_without_statements", [], Universe: three);
        yield return new("X21", "missing company: a named company that does not exist is refused before SQL", ["کگل"],
            [S("کگل", 1_200m)], Compare(["کگل", "زاگرس"]), null, null, [], ExpectedRefusal: "statement_comparison_company_binding_unresolved");
        yield return new("X22", "incompatible units: a source without a resolved effective unit is not compared", three, e05Data,
            Compare(two), null, null, [], UnresolvedUnitSnapshot: true,
            ExpectedRefusal: "statement_comparison_requires_resolved_effective_unit");
    }

    private static CanonicalSemanticIntent Compare(IReadOnlyList<string> companies,
        FinancialStatementScope scope = FinancialStatementScope.Unspecified) =>
        Intent(new(AnalyticalOperationKind.Compare), new(ComparisonMode.MultiCompany), new(EntityScopeKind.NamedCompanies, companies),
            null, RequestedOutputKind.Comparison, scope);

    private static CanonicalSemanticIntent Rank(IReadOnlyList<string> companies, string direction, int? limit = null) =>
        Intent(new(AnalyticalOperationKind.Rank, direction), new(ComparisonMode.MultiCompany),
            new(EntityScopeKind.NamedCompanies, companies), new(direction, "netProfit", limit), RequestedOutputKind.RankedList);

    private static CanonicalSemanticIntent RankPopulation(string direction) =>
        Intent(new(AnalyticalOperationKind.Rank, direction), new(ComparisonMode.None),
            new(EntityScopeKind.UnboundCompanyPopulation, []), new(direction, "netProfit"), RequestedOutputKind.RankedList);

    private static CanonicalSemanticIntent Intent(AnalyticalOperation operation, ComparisonIntent comparison, EntityScope scope,
        RankingIntent? ranking, RequestedOutputKind output, FinancialStatementScope statementScope = FinancialStatementScope.Unspecified) => new(2,
        [new RequestedMeasure("سود خالص", "netProfit", MeasureCanonicalizationStatus.Canonicalized, ["netProfit"],
            MeasureConceptKind.FinancialStatementItem, BusinessMeasureDimension.CurrencyAmount, new(StatementScope: statementScope),
            SemanticUnit.Unspecified, null, EffectiveUnitEvidence.Unresolved)],
        operation, comparison, new(ReportingPeriodKind.Unspecified), scope, [], [], null, null, ranking,
        new(output, []), [], new(false, [], []));

    private static async Task<Dictionary<string, Guid>> SeedAsync(LocalPostgreSqlTestDatabase database, Scenario scenario, Guid sourceItem)
    {
        var now = DateTimeOffset.UtcNow;
        var marketId = Guid.NewGuid();
        var companies = scenario.Companies.ToDictionary(ticker => ticker, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var otherItem = Guid.NewGuid();
        await using var db = database.CreateContext();
        db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "feature141-cross-company-market",
            Name = "Feature 141 cross-company fixture", LastSynchronizedAt = now });
        foreach (var (ticker, id) in companies)
            db.Companies.Add(new NormalizedCompanyRow { Id = id, ProviderName = Provider, ExternalCompanyId = $"cc-{id:N}",
                Name = $"Fixture {ticker}", Ticker = ticker, CompanySymbol = ticker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
        db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow { Id = sourceItem, ProviderName = Provider,
            StatementType = "IncomeStatement", SourceItemId = 143, TitleFa = "سود خالص", Unit = "N/A", LastSynchronizedAt = now });
        db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow { Id = otherItem, ProviderName = Provider,
            StatementType = "IncomeStatement", SourceItemId = 140, TitleFa = "سود عملیاتی", Unit = "N/A", LastSynchronizedAt = now });
        foreach (var statement in scenario.Statements)
        {
            var id = Guid.NewGuid();
            var externalId = $"cc-{statement.Ticker}-{statement.Start:yyyyMMdd}-{statement.PeriodType}-{(statement.Composing ? "c" : "s")}-{statement.Suffix}";
            db.FinancialStatements.Add(new NormalizedFinancialStatementRow
            {
                Id = id, CompanyId = companies[statement.Ticker], ProviderName = Provider, ExternalCompanyId = $"cc-{companies[statement.Ticker]:N}",
                ExternalStatementId = externalId, StatementType = "IncomeStatement", PeriodType = statement.PeriodType,
                PeriodStart = statement.PeriodType == "TwelveMonths" ? statement.End.AddYears(-1).AddDays(1) : statement.Start,
                PeriodEnd = statement.End, PublishedAt = statement.End.AddDays(31), IsComposing = statement.Composing,
                IsAudited = statement.Audited, SourcePayloadChecksum = externalId, LastSynchronizedAt = now
            });
            // Every statement also carries another item with a large value that a wrong item would surface.
            db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                { Id = Guid.NewGuid(), FinancialStatementId = id, SourceItemCatalogId = otherItem, Value = 77_777m });
            if (statement.HasLine)
                db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                    { Id = Guid.NewGuid(), FinancialStatementId = id, SourceItemCatalogId = sourceItem, Value = statement.Value });
        }
        await db.SaveChangesAsync();
        return companies;
    }

    private static StructuredQueryCatalogSnapshot WithoutEffectiveUnit(StructuredQueryCatalogSnapshot snapshot, string catalogId) =>
        new(snapshot.CatalogVersion, snapshot.CatalogHash, snapshot.IntegrityValidated, snapshot.Tables, snapshot.Columns,
            snapshot.Relationships,
            snapshot.DirectSources.Select(source => source.CatalogId == catalogId
                ? source with { EffectiveUnit = null, EffectiveUnitStatus = "requires-semantic-review", SourceUnitStatus = "unresolved" }
                : source).ToArray(),
            snapshot.MonthlyMeasures);

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

    private static string Render(StructuredQueryExecutionResult result) =>
        result.Status + "|" + result.DiagnosticCode + "|" + string.Join(",", result.Warnings) + "|" +
        JsonSerializer.Serialize(result.Rows.Select(row => row.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value is Array array
                ? string.Join(",", array.Cast<object?>().Select(item => item?.ToString()).Order(StringComparer.Ordinal))
                : pair.Value?.ToString())));

    private static async Task WriteArtifactAsync(Dictionary<string, object?> record)
    {
        var directory = Path.Combine(FindRepositoryRoot(), "specs", "141-schema-aware-semantic-query-engine", "evaluation", "artifacts",
            $"cross-company-comparison-semantics-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        Directory.CreateDirectory(directory);
        record["runAt"] = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(directory, $"{record["id"]}.json"), JsonSerializer.Serialize(record,
            new JsonSerializerOptions
            {
                WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Converters = { new JsonStringEnumConverter() }
            }));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "feature141-cross-company-semantics";
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

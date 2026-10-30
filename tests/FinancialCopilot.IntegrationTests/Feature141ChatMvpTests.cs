using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;
using FinancialCopilot.Infrastructure.Memory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 141 chat MVP: Persian questions through the production chat orchestration service
/// (<see cref="IAiQueryOrchestrationService"/>, the service behind <c>POST api/ai/v1/query</c>) composed from the real
/// infrastructure registration, the live model providers and an isolated PostgreSQL database holding the chat, billing and
/// memory schemas plus a deterministic financial fixture. The pilot configuration is the existing <c>Feature141</c> section:
/// the flag, the read-only connection, and one tenant's discovery scope and reviewed company universe.
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class Feature141ChatMvpTests(FinancialStatementValueSearch131PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string Provider = "NoavaranCurrentApi";
    private const string ScopeId = "feature141-chat-mvp-pilot-scope";
    private const string ScopeApproval = "fixture-only:feature141-chat-mvp-pilot:2026-10-29";
    private static readonly Guid TenantId = Guid.Parse("7d2a4c1e-5b3f-4e8a-9c6d-1f2e3a4b5c6d");
    private static readonly Guid UserId = Guid.Parse("3b9e8f7a-6c5d-4b3a-8f2e-1d0c9b8a7f6e");
    private static readonly DateOnly StatementStart = new(2026, 3, 21), StatementEnd = new(2026, 6, 21);
    private static readonly DateOnly PriorStart = new(2025, 12, 21), PriorEnd = new(2026, 3, 20);
    private static readonly DateOnly MonthStart = new(2026, 8, 23), MonthEnd = new(2026, 9, 22);

    /// <summary>
    /// One chat question; <c>Contains</c>/<c>Excludes</c> are checked against the persisted assistant answer. When an existing
    /// specialist answers correctly it keeps priority: <c>SpecialistContains</c> is the same fact in that specialist's rendering.
    /// </summary>
    public sealed record ChatCase(string Id, string Question, bool Refusal, string[] Contains, string[] Excludes,
        string[]? SpecialistContains = null, string ExpectedQueryStatus = "Succeeded");

    public static readonly ChatCase[] Cases =
    [
        new("C1", "سود خالص کگل چقدر است؟", false, ["۱٬۲۰۰", "میلیون ریال", "دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵"], ["۱٬۰۰۰"]),
        new("C2", "تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟", false, ["۲۵ تن", "در شهریور ۱۴۰۵"], ["۲۲ تن", "۹۹۹"]),
        new("C3", "مبلغ فروش گندله کگهر در شهریور ۱۴۰۵ چقدر بود؟", false, ["۷۰۰", "میلیون ریال"], ["۶۵۰"],
            SpecialistContains: ["۰٫۰۷", "میلیارد تومان", "۱۴۰۵/۰۶"]), // 700 million rial = 0.07 billion toman
        new("C4", "سود خالص کگل و کگهر را مقایسه کن.", false, ["کگل", "۱٬۲۰۰", "کگهر", "۹۰۰", "دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵"], ["فملی", "فولاد"]),
        new("C5", "شرکت‌ها را بر اساس سود خالص رتبه‌بندی کن.", false, ["فملی", "۱٬۶۰۰", "کگل", "۱٬۲۰۰", "کگهر", "۹۰۰"], ["فولاد", "۹٬۹۹۹"]),
        new("C6", "کدام شرکت‌ها سود خالص بالای ۱۰۰۰ میلیون ریال دارند؟", false, ["فملی", "۱٬۶۰۰", "کگل", "۱٬۲۰۰"], ["کگهر", "فولاد"]),
        new("C7", "شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال و EPS بالای ۲۰۰۰ ریال را نشان بده.", false,
            ["فملی", "۳٬۲۰۰", "کگل", "۲٬۴۰۰"], ["کگهر", "فولاد"]),
        // Product alias coverage is "approved-aliases-only" in the shipped catalog (owner decision D4): the producer set is
        // returned with an explicit partial-coverage disclosure and the engine status Incomplete.
        new("C8", "کدام شرکت‌ها در شهریور ۱۴۰۵ گندله تولید کرده‌اند؟", false, ["کگل", "۲۵ تن", "کگهر", "۴۰ تن", "جزئی"], ["فملی", "فولاد"],
            ExpectedQueryStatus: "Incomplete"),
        new("C9", "کدام شرکت‌ها تولید گندله بالای ۵۰۰ تن داشته‌اند؟", true, ["نمی‌توانم"], ["فولاد", "۹۹۹", "کگل", "کگهر"]),
        // OutputType: only single-month reports (0) are monthly values. A fiscal-year-to-date request is refused, never
        // answered with one month; a month whose only report is OutputType 3 has no monthly value (the 777 decoy).
        new("C10", "تولید تجمعی گندله کگل از ابتدای سال مالی تا پایان شهریور ۱۴۰۵ چقدر بود؟", true, ["نمی‌توانم"], ["۲۵ تن", "۲۲ تن", "۴۷"]),
        new("C11", "تولید گندله کگهر در مرداد ۱۴۰۵ چقدر بود؟", false, ["مرداد ۱۴۰۵"], ["۷۷۷", "۴۰ تن"], ExpectedQueryStatus: "NoData")
    ];

    public static IEnumerable<object[]> CaseIds() => Cases.Select(item => new object[] { item.Id });

    [SkippableTheory]
    [MemberData(nameof(CaseIds))]
    public async Task SupportedQuestionsAnswerThroughTheChatOrchestrationService(string caseId)
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("FEATURE141_RUN_LIVE_LLM_POSTGRES") == "1",
            "Set FEATURE141_RUN_LIVE_LLM_POSTGRES=1 to run the live chat MVP verification.");
        var testCase = Cases.Single(item => item.Id == caseId);
        await using var database = await fixture.CreateDatabaseAsync();
        var run = await RunChatAsync(database, testCase.Question, enabled: true);
        await WriteArtifactAsync(testCase, run);

        if (!run.Feature141Invoked && testCase.SpecialistContains is { } specialist)
        {
            // Existing specialist priority: the routed capability answered the same fact without Feature 141.
            Assert.Equal("Answered", run.Outcome);
            foreach (var expected in specialist) Assert.Contains(expected, run.Answer, StringComparison.Ordinal);
            foreach (var excluded in testCase.Excludes) Assert.DoesNotContain(excluded, run.Answer, StringComparison.Ordinal);
            Assert.Equal(run.Answer, run.PersistedAssistantMessage);
            return;
        }
        Assert.True(run.Feature141Invoked, $"Feature 141 was not invoked; route: {run.RouteLog}; answer: {run.Answer}");
        if (testCase.Refusal)
        {
            // Never an unfiltered retrieval: no query ran, and the turn ends with the existing typed Unsupported outcome.
            Assert.Equal(0, run.QueriesExecuted);
            Assert.Contains("Outcome=Unsupported", run.TypedOutcome ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal("Unsupported", run.Outcome);
        }
        else
        {
            Assert.Equal("Answered", run.Outcome);
            Assert.Equal(1, run.QueriesExecuted);
            Assert.Contains($"Status={testCase.ExpectedQueryStatus}", run.QueryStatus ?? string.Empty, StringComparison.Ordinal);
        }
        foreach (var expected in testCase.Contains)
            Assert.Contains(expected, run.Answer, StringComparison.Ordinal);
        foreach (var excluded in testCase.Excludes)
            Assert.DoesNotContain(excluded, run.Answer, StringComparison.Ordinal);
        // Feature 141 states reporting periods in Jalali only.
        foreach (var gregorian in new[] { "۲۰۲۵", "۲۰۲۶", "2025", "2026", "میلادی" })
            Assert.DoesNotContain(gregorian, run.Answer, StringComparison.Ordinal);
        // Existing chat persistence and billing: the user and assistant messages are stored and the turn is settled.
        Assert.Equal(run.Answer, run.PersistedAssistantMessage);
        Assert.Equal(testCase.Question, run.PersistedUserMessage);
        Assert.NotNull(run.Usage);
        Assert.Equal("MicrosoftAgentFrameworkV2", run.Mode);
    }

    [SkippableFact]
    public async Task DisablingTheFlagRestoresTheExistingChatRouting()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("FEATURE141_RUN_LIVE_LLM_POSTGRES") == "1",
            "Set FEATURE141_RUN_LIVE_LLM_POSTGRES=1 to run the live chat MVP verification.");
        var testCase = Cases.Single(item => item.Id == "C4");
        await using var database = await fixture.CreateDatabaseAsync();
        var run = await RunChatAsync(database, testCase.Question, enabled: false);
        await WriteArtifactAsync(testCase with { Id = "C4-flag-off" }, run);

        Assert.False(run.Feature141Invoked);
        Assert.Equal(0, run.QueriesExecuted);
        Assert.False(string.IsNullOrWhiteSpace(run.Answer));
        Assert.Equal(run.Answer, run.PersistedAssistantMessage);
    }

    public sealed record ChatRun(string Question, bool Enabled, string Answer, string? PersistedUserMessage, string? PersistedAssistantMessage,
        bool Feature141Invoked, int QueriesExecuted, string? QueryStatus, string? TypedOutcome, string RouteLog,
        string? SemanticCapability, string Intent, string Outcome, string? Mode, object? Usage, IReadOnlyList<string> Feature141Log,
        double ElapsedMs, IReadOnlyList<string> WorkflowLog);

    private async Task<ChatRun> RunChatAsync(LocalPostgreSqlTestDatabase database, string question, bool enabled)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var root = FindRepositoryRoot();
        var (restrictedConnection, universe) = await SeedAsync(database);
        var overrides = new Dictionary<string, string?>
        {
            ["Feature141:Enabled"] = enabled ? "true" : "false",
            ["Feature141:SchemaNavigator:HierarchyPath"] = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml"),
            ["Feature141:SchemaNavigator:CatalogPath"] = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog"),
            ["Feature141:QueryExecution:MaximumConcurrency"] = "1",
            ["Feature141:CompanyDiscoveryScopes:0:TenantId"] = TenantId.ToString("D"),
            ["Feature141:CompanyDiscoveryScopes:0:ScopeId"] = ScopeId,
            ["Feature141:CompanyDiscoveryScopes:0:ApprovalReference"] = ScopeApproval,
            ["Feature141:CompanyDiscoveryScopes:0:CompanyCoverageComplete"] = "true",
            ["Feature141:QueryExecution:ReviewedTenantCompanyDiscoveryScopes:0"] = $"{TenantId:D}|{ScopeId}|{ScopeApproval}",
            ["ConnectionStrings:FinancialCopilot"] = database.ConnectionString,
            ["ConnectionStrings:Feature141ReadOnly"] = restrictedConnection
        };
        for (var index = 0; index < universe.Count; index++)
            overrides[$"Feature141:QueryExecution:ReviewedTenantCompanyBindings:{index}"] = $"{TenantId:D}:{universe[index]:D}";
        var configuration = EvaluationConfigurationLoader.Build(Path.Combine(root, "src", "backend", "FinancialCopilot.API"), "Development",
            configurationOverrides: overrides);
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new ChatTestEnvironment("Development"));
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Information));
        // Registered by the API host (Program.cs) before the infrastructure.
        services.AddSingleton(new BankingFinancialReportsConfiguration(
            configuration.GetValue<Guid>(BankingFinancialReportsConfiguration.ConfigurationKey)));
        services.AddFinancialCopilotInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        using (var migrationScope = provider.CreateScope())
            foreach (var contextType in new[] { typeof(AuthDbContext), typeof(BillingDbContext), typeof(ConversationDbContext),
                         typeof(MemoryDbContext), typeof(SemanticCatalogDbContext) })
                await ((DbContext)migrationScope.ServiceProvider.GetRequiredService(contextType)).Database.MigrateAsync();

        using var scope = provider.CreateScope();
        // The pilot user is provisioned exactly like a newly registered web user (Free plan, initial wallet).
        await scope.ServiceProvider.GetRequiredService<OwnedIdentityBillingProvisioner>()
            .EnsureProvisionedAsync(TenantId, UserId, CancellationToken.None);
        var orchestration = scope.ServiceProvider.GetRequiredService<IAiQueryOrchestrationService>();
        var correlationId = $"feature141-chat-mvp-{Guid.NewGuid():N}";
        // The same request the AI facade controller builds for an authenticated web-app user.
        var response = await orchestration.ExecuteAsync(new AiQueryRequest(question, TenantId, UserId, correlationId, null, UserId, null,
            ScannerPage: 1, ScannerPageSize: 20, DisclosurePage: 1, ActorType: ActorType.User,
            AuthenticationMode: AuthenticationMode.WebAppUser), CancellationToken.None);
        var messages = await scope.ServiceProvider.GetRequiredService<IMessageRepository>()
            .ListByConversationAsync(response.ConversationId, CancellationToken.None);
        var entries = logs.Entries.ToArray();
        var feature141 = entries.Where(entry => entry.Category.EndsWith("Feature141ChatQueryService", StringComparison.Ordinal))
            .Select(entry => entry.Message).ToArray();
        var answer = response.TextAnswer ?? response.ClarificationMessage ?? string.Empty;
        var run = new ChatRun(question, enabled, answer,
            messages.FirstOrDefault(message => message.Role == MessageRole.User)?.Content,
            messages.LastOrDefault(message => message.Role == MessageRole.Assistant)?.Content,
            feature141.Any(message => message.StartsWith("Feature141 registered financial source route selected", StringComparison.Ordinal)),
            feature141.Count(message => message.StartsWith("Feature141 query completed", StringComparison.Ordinal)),
            feature141.FirstOrDefault(message => message.StartsWith("Feature141 query completed", StringComparison.Ordinal)),
            feature141.FirstOrDefault(message => message.StartsWith("Feature141 typed outcome", StringComparison.Ordinal)),
            string.Join(" | ", entries.Where(entry => entry.Message.StartsWith("Chat ", StringComparison.Ordinal)).Select(entry => entry.Message)),
            response.SemanticCapabilityCode, response.Intent.ToString(), response.Outcome.ToString(), response.AiOrchestrationMode,
            response.Usage, feature141, timer.Elapsed.TotalMilliseconds,
            entries.Where(entry => entry.Category.Contains("OrchestrationV2", StringComparison.Ordinal)
                    && !entry.Category.EndsWith("Feature141ChatQueryService", StringComparison.Ordinal))
                .Select(entry => entry.Message.Length > 400 ? entry.Message[..400] : entry.Message).Take(40).ToArray());
        output.WriteLine($"Q: {question}\nRoute: {run.RouteLog}\nFeature141: {string.Join(" || ", feature141)}\nA: {answer}");
        return run;
    }

    /// <summary>
    /// One deterministic fixture for every question: net profit (143), operating profit (140) and EPS (160) of three peers on the latest
    /// quarter, a prior quarter with different values, and monthly product reports for Shahrivar 1405 with decoys (earlier month, another
    /// product, a producer outside the reviewed universe). Source identities come from the packaged catalog.
    /// </summary>
    private static async Task<(string RestrictedConnection, IReadOnlyList<Guid> Universe)> SeedAsync(LocalPostgreSqlTestDatabase database)
    {
        var root = FindRepositoryRoot();
        var snapshot = new StructuredQueryCatalogSnapshotBuilder().Build(
            Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog"),
            Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml"));
        var items = new[] { 143, 140, 160 }.ToDictionary(id => id, id => snapshot.DirectSources.Single(source =>
            source.ProviderName == Provider && source.StatementType == "IncomeStatement" && source.SourceItemId == id));
        var now = DateTimeOffset.UtcNow;
        var marketId = Guid.NewGuid();
        var companies = new[] { ("کگل", "kgol"), ("کگهر", "kgehr"), ("فملی", "fameli"), ("فولاد", "foolad") }
            .ToDictionary(item => item.Item1, item => (Id: Guid.NewGuid(), External: $"f141chat-{item.Item2}"));
        await using (var db = database.CreateContext())
        {
            db.Markets.Add(new NormalizedMarketRow { Id = marketId, ProviderName = Provider, ExternalId = "f141chat-market",
                Name = "Feature 141 chat MVP fixture", LastSynchronizedAt = now });
            foreach (var (ticker, company) in companies)
                db.Companies.Add(new NormalizedCompanyRow { Id = company.Id, ProviderName = Provider, ExternalCompanyId = company.External,
                    Name = $"Fixture {ticker}", Ticker = ticker, CompanySymbol = ticker, MarketId = marketId, PrecedencyRight = 0, LastSynchronizedAt = now });
            foreach (var (id, source) in items)
                db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow { Id = source.SourceItemCatalogId,
                    ProviderName = Provider, StatementType = "IncomeStatement", SourceItemId = id, TitleFa = source.TitleFa ?? $"item {id}",
                    Unit = "N/A", LastSynchronizedAt = now });
            (string Ticker, bool Prior, decimal NetProfit, decimal OperatingProfit, decimal Eps)[] statements =
            [
                ("کگل", false, 1_200m, 1_500m, 2_400m), ("کگهر", false, 900m, 1_100m, 1_800m), ("فملی", false, 1_600m, 2_000m, 3_200m),
                ("کگل", true, 1_000m, 600m, 100m), ("کگهر", true, 2_000m, 5_000m, 5_000m), ("فملی", true, 400m, 300m, 200m),
                ("فولاد", false, 9_999m, 9_999m, 9_999m)
            ];
            foreach (var statement in statements)
            {
                var id = Guid.NewGuid();
                var company = companies[statement.Ticker];
                var externalId = $"f141chat-{company.External}-{(statement.Prior ? "prior" : "latest")}";
                db.FinancialStatements.Add(new NormalizedFinancialStatementRow
                {
                    Id = id, CompanyId = null, ProviderName = Provider, ExternalCompanyId = company.External, ExternalStatementId = externalId,
                    StatementType = "IncomeStatement", PeriodType = "ThreeMonths",
                    PeriodStart = statement.Prior ? PriorStart : StatementStart, PeriodEnd = statement.Prior ? PriorEnd : StatementEnd,
                    PublishedAt = (statement.Prior ? PriorEnd : StatementEnd).AddDays(31), IsComposing = false,
                    SourcePayloadChecksum = externalId, LastSynchronizedAt = now
                });
                foreach (var (itemId, value) in new[] { (143, statement.NetProfit), (140, statement.OperatingProfit), (160, statement.Eps) })
                    db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                        { Id = Guid.NewGuid(), FinancialStatementId = id, SourceItemCatalogId = items[itemId].SourceItemCatalogId, Value = value });
            }

            NormalizedMonthlyReportRow Report(string ticker, string suffix, DateOnly start, DateOnly end, int outputType = 0) => new()
            {
                Id = Guid.NewGuid(), ProviderName = Provider, ExternalCompanyId = companies[ticker].External, ExternalReportId = $"f141chat-{suffix}",
                PeriodStart = start, PeriodEnd = end, SourcePayloadChecksum = $"f141chat-{suffix}", LastSynchronizedAt = now,
                LogicalReportKey = $"f141chat-{companies[ticker].External}-{start:yyyyMM}", RevisionFingerprint = $"f141chat-{suffix}",
                IsAccepted = true, RevisionStatus = "Accepted", ReportType = "ProductSales", OutputType = outputType, CompanyId = null
            };
            NormalizedMonthlyReportLineItemRow Line(NormalizedMonthlyReportRow report, string ticker, string title, decimal production,
                decimal salesQuantity, decimal salesAmount) => new()
            {
                Id = Guid.NewGuid(), MonthlyReportId = report.Id, SourceRowFingerprint = $"row-{report.ExternalReportId}-{title}", SourceMultiplicity = 1,
                // Production-like ProductCode: a per-row hash reused across companies, never a product identity.
                ProductCode = "PRODUCT:NATURAL:22d58f29e3fedf1e", Title = title, Unit = "ton",
                ProductionQuantity = production, SalesQuantity = salesQuantity, SalesAmount = salesAmount, SalesRate = 10m,
                SourcePayloadChecksum = report.SourcePayloadChecksum
            };
            var kgol = Report("کگل", "kgol-1405-06", MonthStart, MonthEnd);
            var kgolEarlier = Report("کگل", "kgol-1405-05", MonthStart.AddMonths(-1), MonthStart.AddDays(-1));
            var kgehr = Report("کگهر", "kgehr-1405-06", MonthStart, MonthEnd);
            var fameli = Report("فملی", "fameli-1405-06", MonthStart, MonthEnd);
            var foolad = Report("فولاد", "foolad-1405-06", MonthStart, MonthEnd);
            // Corrected fiscal-year-to-previous-month (OutputType 3) is the only کگهر report for Mordad: not a monthly value.
            var kgehrMordadCorrected = Report("کگهر", "kgehr-1405-05-corrected", MonthStart.AddMonths(-1), MonthStart.AddDays(-1), outputType: 3);
            db.MonthlyReports.AddRange(kgol, kgolEarlier, kgehr, fameli, foolad, kgehrMordadCorrected);
            db.MonthlyReportLineItems.AddRange(
                Line(kgol, "کگل", "گندله", 25m, 20m, 650m), Line(kgol, "کگل", "کنسانتره", 60m, 55m, 300m),
                Line(kgolEarlier, "کگل", "گندله", 22m, 18m, 610m),
                Line(kgehr, "کگهر", "گندله", 40m, 30m, 700m),
                Line(fameli, "فملی", "کنسانتره", 80m, 70m, 400m),
                Line(foolad, "فولاد", "گندله", 999m, 900m, 9_000m),
                Line(kgehrMordadCorrected, "کگهر", "گندله", 777m, 700m, 7_000m));
            await db.SaveChangesAsync();
        }

        var role = "f141_chat_reader_" + Guid.NewGuid().ToString("N"); // PostgreSQL truncates role names beyond 63 bytes
        var password = "F141chat_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT; " +
                $"GRANT USAGE ON SCHEMA public TO \"{role}\"; " +
                "GRANT SELECT ON TABLE public.\"FinancialStatements\", public.\"FinancialStatementLineItems\", public.\"FinancialStatementSourceItems\", " +
                $"public.\"Companies\", public.\"MonthlyReports\", public.\"MonthlyReportLineItems\" TO \"{role}\";";
            await command.ExecuteNonQueryAsync();
        }
        var restricted = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Username = role, Password = password, Pooling = false };
        return (restricted.ConnectionString, new[] { "کگل", "کگهر", "فملی" }.Select(ticker => companies[ticker].Id).ToArray());
    }

    private static async Task WriteArtifactAsync(ChatCase testCase, ChatRun run)
    {
        var directory = Path.Combine(FindRepositoryRoot(), "specs", "141-schema-aware-semantic-query-engine", "evaluation", "artifacts",
            "feature141-chat-mvp");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyy-MM-dd-HHmmss}-{testCase.Id}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { testCase, run }, new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class ChatTestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Feature141ChatMvpTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record LogEntry(string Category, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new(category, formatter(state, exception) + (exception is null ? string.Empty : $" [{exception.GetType().Name}: {exception.Message}]")));
    }
}

using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;
using FinancialCopilot.Infrastructure.Financial.Semantics;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
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
/// Feature 141 production-data pilot: real persisted financial data through the production chat orchestration service.
/// Feature 141 reads the real application database only through the read-only principal given in
/// <c>FEATURE141_PILOT_READONLY_CONNECTION_STRING</c>. Chat, billing and memory writes go to a disposable database whose
/// company, statement and monthly tables are a copy of the same real rows read through that principal, so specialists see real data and
/// no real customer account is touched. Each question runs as a fresh pilot user on the Free plan. Records artifacts only;
/// correctness is judged against independent read-only SQL in the pilot report.
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class Feature141ProductionDataPilotTests(FinancialStatementValueSearch131PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string ScopeId = "feature141-production-pilot-scope";
    private const string ScopeApproval = "pilot-only:feature141-production-data-pilot:2026-10-29";
    private static readonly Guid TenantId = Guid.Parse("5e1c9a2b-7d3f-4c6e-8a1b-2f3e4d5c6b7a");
    // The production shared public scope: every eligible listed company (NoavaranEligibleCompanies), as generated for
    // production by tools/SemanticCatalog/sql/feature141-public-company-scope-env.sql.
    // The monthly tables are copied too: the company-scoped product resolver binds products from the application database.
    private static readonly string[] CopiedTables = ["Companies", "FinancialStatementSourceItems", "FinancialStatements", "FinancialStatementLineItems",
        "MonthlyReports", "MonthlyReportLineItems"];

    // Required columns of the copied monthly tables that the read-only principal cannot read get disposable-only defaults.
    private const string DisposableMonthlyDefaults =
        "ALTER TABLE public.\"MonthlyReportLineItems\" ALTER COLUMN \"Id\" SET DEFAULT gen_random_uuid(); " +
        "ALTER TABLE public.\"MonthlyReports\" ALTER COLUMN \"LastSynchronizedAt\" SET DEFAULT now(); " +
        "ALTER TABLE public.\"MonthlyReports\" ALTER COLUMN \"SourcePayloadChecksum\" SET DEFAULT ''; " +
        // Accepted and pending revisions share an ExternalReportId; the unreadable fingerprint keeps them distinct.
        "ALTER TABLE public.\"MonthlyReports\" ALTER COLUMN \"RevisionFingerprint\" SET DEFAULT gen_random_uuid()::text";
    private static readonly SemaphoreSlim PilotDatabaseGate = new(1, 1);
    private static (LocalPostgreSqlTestDatabase Database, IReadOnlyList<Guid> Universe)? pilotDatabase;

    public static readonly (string Id, string Question)[] Questions =
    [
        ("P01", "سود خالص کگل در آخرین دوره چقدر است؟"),
        ("P02", "سود عملیاتی کگهر در آخرین دوره چقدر است؟"),
        ("P03", "سود هر سهم فملی چقدر است؟"),
        ("P04", "تولید گندله کگل در آخرین ماه گزارش‌شده چقدر بوده؟"),
        ("P05", "مبلغ فروش گندله کگهر در آخرین ماه چقدر بوده؟"),
        ("P06", "سود خالص کگل و کگهر را در دوره مشترک مقایسه کن."),
        ("P07", "شرکت‌ها را بر اساس سود خالص رتبه‌بندی کن."),
        ("P08", "شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیارد ریال را پیدا کن."),
        ("P09", "کدام شرکت‌ها گندله تولید می‌کنند؟"),
        ("P10", "کدام شرکت‌ها سود خالص بالای ۱۰۰۰ میلیارد ریال و EPS بالای ۲۰۰۰ ریال دارند؟"),
        // Noavaran company mapping verification (owner example: وبملت, ExternalCompanyId 452, SixMonths ending 2026-09-22).
        ("P11", "سود خالص وبملت در آخرین دوره چقدر است؟"),
        ("P12", "سود عملیاتی وبملت در آخرین دوره چقدر است؟"),
        ("P13", "سود هر سهم وبملت در آخرین دوره چقدر است؟"),
        ("P14", "سود عملیاتی هر سهم وبملت در آخرین دوره چقدر است؟"),
        ("P15", "سود خالص کگل و کگهر را مقایسه کن."),
        // The same facts with the statement basis stated (both bases exist for these companies).
        ("P16", "سود خالص وبملت در آخرین دوره بر اساس صورت‌های مالی جداگانه چقدر است؟"),
        ("P17", "سود عملیاتی وبملت در آخرین دوره بر اساس صورت‌های مالی جداگانه چقدر است؟"),
        ("P18", "سود هر سهم وبملت در آخرین دوره بر اساس صورت‌های مالی جداگانه چقدر است؟"),
        ("P19", "سود عملیاتی هر سهم وبملت در آخرین دوره بر اساس صورت‌های مالی جداگانه چقدر است؟"),
        ("P20", "سود خالص کگل در آخرین دوره بر اساس صورت‌های مالی جداگانه چقدر است؟"),
        ("P21", "سود خالص کگل و کگهر را بر اساس صورت‌های مالی جداگانه مقایسه کن."),
        // Real monthly production and sales (accepted OutputType 0 reports; OutputType 1-4 must never be answered as one month).
        ("P22", "تولید گندله کگل در شهریور ۱۴۰۵ چقدر بوده؟"),
        ("P23", "مقدار فروش گندله کگل در شهریور ۱۴۰۵ چند تن بوده؟"),
        ("P24", "مبلغ فروش گندله کگل در شهریور ۱۴۰۵ چقدر بوده؟"),
        ("P25", "تولید گندله کگهر در مرداد ۱۴۰۵ چقدر بوده؟"),
        ("P26", "روند تولید ماهانه گندله کگل از فروردین تا شهریور ۱۴۰۵ را نشان بده."),
        ("P27", "کدام شرکت‌ها در مرداد ۱۴۰۵ گندله تولید کرده‌اند؟"),
        ("P28", "شرکت‌های تولیدکننده گندله را بر اساس مبلغ فروش گندله در مرداد ۱۴۰۵ رتبه‌بندی کن."),
        ("P29", "تولید تجمعی گندله کگل از ابتدای سال مالی تا شهریور ۱۴۰۵ چقدر بوده؟"),
        // Measure routing: production quantity, sales quantity and sales amount stay distinct; P33 is an existing specialist trend.
        ("P30", "مقدار تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟"),
        ("P31", "مقدار فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟"),
        ("P32", "مبلغ فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟"),
        ("P33", "روند فروش گندله کگل در ۱۲ ماه اخیر چطور بوده؟"),
        ("P34", "میزان فروش گندله کگل در شهریور ۱۴۰۵ چند تن بود؟"),
        // Explicit-month routing: the latest-month sales value specialist must not answer a named month; P37 names none.
        ("P35", "مبلغ فروش گندله کگهر در تیر ۱۴۰۵ چقدر بود؟"),
        ("P36", "مبلغ فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟"),
        ("P37", "مبلغ فروش گندله کگهر چقدر است؟"),
        // The same with the product marker, which the deterministic interpreter routes to the sales value specialist.
        ("P38", "مبلغ فروش محصول گندله کگهر در تیر ۱۴۰۵ چقدر بود؟"),
        ("P39", "مبلغ فروش محصول گندله کگهر چقدر است؟"),
        // Partial-coverage product ranking (owner decision 2026-10-30): amount and physical quantity.
        ("P40", "شرکت‌ها را بر اساس مبلغ فروش گندله در مرداد ۱۴۰۵ رتبه‌بندی کن."),
        ("P41", "شرکت‌ها را بر اساس مقدار فروش گندله در مرداد ۱۴۰۵ رتبه‌بندی کن."),
        // Manual chat acceptance (2026-10-30): explicit period and standalone basis.
        ("A1", "سود خالص وبملت در دوره شش‌ماهه منتهی به شهریور ۱۴۰۵ بر اساس صورت مالی جداگانه چقدر بوده؟"),
        ("A2", "سود عملیاتی وبملت در دوره شش‌ماهه منتهی به شهریور ۱۴۰۵ بر اساس صورت مالی جداگانه چقدر بوده؟"),
        ("A3", "EPS وبملت در دوره شش‌ماهه منتهی به شهریور ۱۴۰۵ بر اساس صورت مالی جداگانه چقدر بوده؟"),
        ("A4", "سود خالص کگل و کگهر را در دوره مشترک، بر اساس صورت‌های مالی جداگانه مقایسه کن.")
    ];

    public static IEnumerable<object[]> QuestionIds() => Questions.Select(item => new object[] { item.Id });

    [SkippableTheory]
    [MemberData(nameof(QuestionIds))]
    public async Task RealDataQuestionThroughTheChatOrchestrationService(string questionId)
    {
        var readOnly = RequirePilot();
        var (id, question) = Questions.Single(item => item.Id == questionId);
        var run = await RunChatAsync(readOnly, question, enabled: true);
        await WriteArtifactAsync(id, run);

        Assert.Equal(run.Answer, run.PersistedAssistantMessage);
        Assert.Equal(question, run.PersistedUserMessage);
        Assert.All(run.Reservations, reservation => Assert.NotEqual("Reserved", reservation.Status));
        Assert.Equal(0m, run.WalletReservedAfter);
    }

    /// <summary>
    /// The same question asked after an unrelated product-sales question in one conversation: the answer must be about
    /// the current question, never the previous turn's result.
    /// </summary>
    [SkippableTheory]
    [InlineData("P33", "P27")]
    [InlineData("P33", "P40")]
    public async Task RealDataQuestionAfterAnotherQuestionInTheSameConversation(string previousId, string questionId)
    {
        var readOnly = RequirePilot();
        var previous = Questions.Single(item => item.Id == previousId).Question;
        var (id, question) = Questions.Single(item => item.Id == questionId);
        var run = await RunChatAsync(readOnly, question, enabled: true, previousQuestion: previous);
        await WriteArtifactAsync($"{id}-after-{previousId}", run);

        Assert.Equal(run.Answer, run.PersistedAssistantMessage);
        Assert.Equal(question, run.PersistedUserMessage);
    }

    /// <summary>
    /// The Feature 141 service the chat fallback calls, without the specialist that may answer first: verifies Feature 141
    /// itself on real data for questions the existing specialists usually take (the chat test shows what the user receives).
    /// </summary>
    [SkippableTheory]
    [InlineData("P23")]
    [InlineData("P24")]
    [InlineData("P05")]
    public async Task RealDataQuestionThroughFeature141Directly(string questionId)
    {
        var readOnly = RequirePilot();
        var (id, question) = Questions.Single(item => item.Id == questionId);
        var run = await RunChatAsync(readOnly, question, enabled: true, directFeature141: true);
        await WriteArtifactAsync($"{id}-feature141-direct", run);

        Assert.True(run.Feature141Invoked);
    }

    // Legacy routing regression check: existing specialist questions asked with the Feature 141 flag off and on.
    public static readonly (string Id, string Question)[] LegacyQuestions =
    [
        ("L1", "روند فروش کگهر"),
        ("L2", "روند فروش ماهانه کگل"),
        ("L3", "روند فروش گندله کگل در ۱۲ ماه اخیر"),
        ("L4", "قیمت فعلی کگل چقدر است؟"),
        ("L5", "P/E کگل چقدر است؟"),
        ("L6", "فروش ماهانه کگهر را نشان بده"),
        ("L7", "مبلغ فروش گندله کگهر در تیر ۱۴۰۵ چقدر بود؟"),
        // MVP release smoke test (2026-10-30), exact phrasings.
        ("S1", "روند فروش کگهر"),
        ("S2", "روند فروش گندله کگل در ۱۲ ماه اخیر"),
        ("S3", "سود خالص وبملت در آخرین دوره"),
        ("S4", "مقدار تولید گندله کگل در شهریور ۱۴۰۵"),
        ("S5", "مبلغ فروش گندله کگهر در تیر ۱۴۰۵"),
        ("S6", "رتبه‌بندی مبلغ فروش گندله در مرداد ۱۴۰۵"),
        // Non-answers must cost nothing: a month without reports (NoData) and a cumulative request (typed Unsupported).
        ("S7", "مقدار تولید گندله کگل در فروردین ۱۳۹۰"),
        ("S8", "تولید تجمعی گندله کگل از ابتدای سال مالی تا شهریور ۱۴۰۵ چقدر بوده؟")
    ];

    public static IEnumerable<object[]> LegacyQuestionIds() => LegacyQuestions.Select(item => new object[] { item.Id });

    [SkippableTheory]
    [MemberData(nameof(LegacyQuestionIds))]
    public async Task LegacyQuestionWithTheFlagOffAndOn(string questionId)
    {
        var readOnly = RequirePilot();
        var (id, question) = LegacyQuestions.Single(item => item.Id == questionId);
        var off = await RunChatAsync(readOnly, question, enabled: false);
        await WriteArtifactAsync($"{id}-flag-off", off);
        var on = await RunChatAsync(readOnly, question, enabled: true);
        await WriteArtifactAsync($"{id}-flag-on", on);
        foreach (var (label, run) in new[] { ("OFF", off), ("ON", on) })
            output.WriteLine($"[{id} {label}] Capability={run.SemanticCapability} Outcome={run.Outcome} Feature141={run.Feature141Invoked} " +
                $"Reservations={string.Join(",", run.Reservations.Select(item => $"{item.IdempotencyKey[^20..]}:{item.Status}:{item.CommittedCredits}"))} " +
                $"Ledger={run.LedgerEntries}/{run.LedgerCreditsCharged}\n[{id} {label}] A: {run.Answer}");

        Assert.False(off.Feature141Invoked);
        Assert.All(new[] { off, on }, run => Assert.All(run.Reservations, item => Assert.NotEqual("Reserved", item.Status)));
        // At most one billing settlement per chat turn (each run is a fresh pilot user).
        Assert.All(new[] { off, on }, run => Assert.True(run.LedgerEntries <= 1, $"{run.LedgerEntries} settlements in one turn"));
    }

    [SkippableFact]
    public async Task DisablingTheFlagRestoresTheExistingChatRouting()
    {
        var readOnly = RequirePilot();
        var question = Questions.Single(item => item.Id == "P06").Question;
        var run = await RunChatAsync(readOnly, question, enabled: false);
        await WriteArtifactAsync("P06-flag-off", run);

        Assert.False(run.Feature141Invoked);
        Assert.Equal(0, run.QueriesExecuted);
        Assert.Equal(run.Answer, run.PersistedAssistantMessage);
    }

    private static string RequirePilot()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("FEATURE141_RUN_PRODUCTION_PILOT") == "1",
            "Set FEATURE141_RUN_PRODUCTION_PILOT=1 to run the production-data pilot.");
        var readOnly = Environment.GetEnvironmentVariable("FEATURE141_PILOT_READONLY_CONNECTION_STRING");
        Skip.If(string.IsNullOrWhiteSpace(readOnly), "Set FEATURE141_PILOT_READONLY_CONNECTION_STRING to the read-only Feature 141 principal.");
        return readOnly!;
    }

    public sealed record ReservationSnapshot(string IdempotencyKey, string Status, decimal ReservedCredits, decimal? CommittedCredits);

    /// <summary>The compiled SQL, its bound parameters and the rows the read-only executor returned.</summary>
    public sealed record ExecutedQuery(string Sql, IReadOnlyDictionary<string, string?> Parameters, string Status, string? DiagnosticCode,
        string? EffectiveUnit, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

    public sealed record PilotRun(string Question, bool Enabled, string Answer, string? PersistedUserMessage, string? PersistedAssistantMessage,
        bool Feature141Invoked, int QueriesExecuted, string? QueryStatus, string? TypedOutcome, string RouteLog, string? SemanticCapability,
        string Intent, string Outcome, string? Mode, UsageAccountingResult? Usage, IReadOnlyList<ReservationSnapshot> Reservations,
        int LedgerEntries, decimal LedgerCreditsCharged, decimal WalletBalanceBefore, decimal WalletBalanceAfter, decimal WalletReservedAfter,
        IReadOnlyList<string> Feature141Log, double ElapsedMs, IReadOnlyList<string> WorkflowLog, IReadOnlyList<ExecutedQuery> ExecutedQueries);

    private async Task<PilotRun> RunChatAsync(string readOnlyConnection, string question, bool enabled, bool directFeature141 = false,
        string? previousQuestion = null)
    {
        var (database, universe) = await GetPilotDatabaseAsync(readOnlyConnection);
        var root = FindRepositoryRoot();
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
            ["ConnectionStrings:Feature141ReadOnly"] = readOnlyConnection
        };
        for (var index = 0; index < universe.Count; index++)
            overrides[$"Feature141:QueryExecution:ReviewedTenantCompanyBindings:{index}"] = $"{TenantId:D}:{universe[index]:D}";
        var configuration = EvaluationConfigurationLoader.Build(Path.Combine(root, "src", "backend", "FinancialCopilot.API"), "Development",
            configurationOverrides: overrides);
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new PilotEnvironment("Development"));
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(new BankingFinancialReportsConfiguration(
            configuration.GetValue<Guid>(BankingFinancialReportsConfiguration.ConfigurationKey)));
        services.AddFinancialCopilotInfrastructure(configuration);
        var executed = new ConcurrentQueue<ExecutedQuery>();
        services.AddScoped<IReadOnlyStructuredQueryExecutor>(serviceProvider => new RecordingExecutor(
            ActivatorUtilities.CreateInstance<NpgsqlReadOnlyStructuredQueryExecutor>(serviceProvider), executed));
        await using var provider = services.BuildServiceProvider();
        using (var migrationScope = provider.CreateScope())
            foreach (var contextType in new[] { typeof(AuthDbContext), typeof(BillingDbContext), typeof(ConversationDbContext),
                         typeof(MemoryDbContext), typeof(SemanticCatalogDbContext) })
                await ((DbContext)migrationScope.ServiceProvider.GetRequiredService(contextType)).Database.MigrateAsync();

        var userId = Guid.NewGuid();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OwnedIdentityBillingProvisioner>()
            .EnsureProvisionedAsync(TenantId, userId, CancellationToken.None);
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var walletBefore = await billing.WalletProjections.AsNoTracking().SumAsync(wallet => wallet.Balance);
        var orchestration = scope.ServiceProvider.GetRequiredService<IAiQueryOrchestrationService>();
        Guid? conversationId = null;
        if (previousQuestion is not null)
        {
            var previousResponse = await orchestration.ExecuteAsync(new AiQueryRequest(previousQuestion, TenantId, userId,
                $"feature141-pilot-{Guid.NewGuid():N}", null, userId, null, ScannerPage: 1, ScannerPageSize: 20, DisclosurePage: 1,
                ActorType: ActorType.User, AuthenticationMode: AuthenticationMode.WebAppUser), CancellationToken.None);
            conversationId = previousResponse.ConversationId;
            output.WriteLine($"Previous Q: {previousQuestion}\nPrevious A: {previousResponse.TextAnswer}");
        }
        var correlationId = $"feature141-pilot-{Guid.NewGuid():N}";
        logs.Entries.Clear();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var request = new AiQueryRequest(question, TenantId, userId, correlationId, conversationId, userId, null,
            ScannerPage: 1, ScannerPageSize: 20, DisclosurePage: 1, ActorType: ActorType.User,
            AuthenticationMode: AuthenticationMode.WebAppUser);
        var directAnswer = directFeature141
            ? (await scope.ServiceProvider.GetRequiredService<Feature141ChatQueryService>()
                .AnswerAsync(request, null, CancellationToken.None)).Text
            : null;
        var response = directFeature141 ? null : await orchestration.ExecuteAsync(request, CancellationToken.None);
        timer.Stop();

        var messages = response is null ? []
            : await scope.ServiceProvider.GetRequiredService<IMessageRepository>()
                .ListByConversationAsync(response.ConversationId, CancellationToken.None);
        billing.ChangeTracker.Clear();
        var reservations = await billing.UsageReservations.AsNoTracking()
            .Where(row => row.IdempotencyKey.StartsWith(correlationId))
            .Select(row => new ReservationSnapshot(row.IdempotencyKey, row.Status, row.ReservedCredits, row.CommittedCredits)).ToListAsync();
        var ledger = await billing.UsageLedgerEntries.AsNoTracking().Where(row => row.ActorId == userId).ToListAsync();
        // One pilot user per question: every wallet in the disposable database except this user's is unchanged by the turn.
        var walletAfter = await billing.WalletProjections.AsNoTracking().SumAsync(wallet => wallet.Balance);
        var reservedAfter = await billing.WalletProjections.AsNoTracking().SumAsync(wallet => wallet.ReservedAmount);

        var entries = logs.Entries.ToArray();
        var feature141 = entries.Where(entry => entry.Category.EndsWith("Feature141ChatQueryService", StringComparison.Ordinal))
            .Select(entry => entry.Message).ToArray();
        var answer = directAnswer ?? response?.TextAnswer ?? response?.ClarificationMessage ?? string.Empty;
        var run = new PilotRun(question, enabled, answer,
            messages.LastOrDefault(message => message.Role == MessageRole.User)?.Content,
            messages.LastOrDefault(message => message.Role == MessageRole.Assistant)?.Content,
            feature141.Any(message => message.StartsWith("Feature141 registered financial source route selected", StringComparison.Ordinal)),
            feature141.Count(message => message.StartsWith("Feature141 query completed", StringComparison.Ordinal)),
            feature141.FirstOrDefault(message => message.StartsWith("Feature141 query completed", StringComparison.Ordinal)),
            feature141.FirstOrDefault(message => message.StartsWith("Feature141 typed outcome", StringComparison.Ordinal)),
            string.Join(" | ", entries.Where(entry => entry.Message.StartsWith("Chat ", StringComparison.Ordinal)).Select(entry => entry.Message)),
            response?.SemanticCapabilityCode, response?.Intent.ToString() ?? "Feature141Direct", response?.Outcome.ToString() ?? "Feature141Direct",
            response?.AiOrchestrationMode, response?.Usage, reservations, ledger.Count, ledger.Sum(row => row.CreditsCharged), walletBefore, walletAfter, reservedAfter,
            feature141, timer.Elapsed.TotalMilliseconds,
            entries.Where(entry => entry.Category.Contains("OrchestrationV2", StringComparison.Ordinal)
                    && !entry.Category.EndsWith("Feature141ChatQueryService", StringComparison.Ordinal))
                .Select(entry => entry.Message.Length > 400 ? entry.Message[..400] : entry.Message).Take(40).ToArray(),
            executed.ToArray());
        output.WriteLine($"Q: {question}\nRoute: {run.RouteLog}\nFeature141: {string.Join(" || ", feature141)}\nA: {answer}");
        return run;
    }

    /// <summary>
    /// One disposable chat database for the pilot run, holding a copy of the real company and statement rows that the read-only
    /// principal can read (same ids, so the reviewed company universe is the real company identity).
    /// </summary>
    private async Task<(LocalPostgreSqlTestDatabase Database, IReadOnlyList<Guid> Universe)> GetPilotDatabaseAsync(string readOnlyConnection)
    {
        await PilotDatabaseGate.WaitAsync();
        try
        {
            if (pilotDatabase is { } existing) return existing;
            var database = await fixture.CreateDatabaseAsync();
            await using var source = new NpgsqlConnection(readOnlyConnection);
            await source.OpenAsync();
            await using var target = new NpgsqlConnection(database.ConnectionString);
            await target.OpenAsync();
            await using (var replica = new NpgsqlCommand("SET session_replication_role = replica", target))
                await replica.ExecuteNonQueryAsync();
            await using (var defaults = new NpgsqlCommand(DisposableMonthlyDefaults, target))
                await defaults.ExecuteNonQueryAsync();
            foreach (var table in CopiedTables)
            {
                var readable = await ColumnsAsync(source,
                    "SELECT column_name FROM information_schema.column_privileges WHERE grantee = current_user AND privilege_type = 'SELECT' " +
                    "AND table_schema = 'public' AND table_name = @table", table);
                var targetColumns = await ColumnsAsync(target,
                    "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table", table);
                var copied = string.Join(", ", targetColumns.Intersect(readable).Select(column => $"\"{column}\""));
                await using var reader = await source.BeginRawBinaryCopyAsync($"COPY (SELECT {copied} FROM public.\"{table}\") TO STDOUT (FORMAT BINARY)");
                await using var writer = await target.BeginRawBinaryCopyAsync($"COPY public.\"{table}\" ({copied}) FROM STDIN (FORMAT BINARY)");
                await reader.CopyToAsync(writer);
            }
            var universe = new List<Guid>();
            await using (var lookup = new NpgsqlCommand("SELECT \"Id\" FROM public.\"NoavaranEligibleCompanies\" ORDER BY \"Id\"", target))
            await using (var eligible = await lookup.ExecuteReaderAsync())
                while (await eligible.ReadAsync()) universe.Add(eligible.GetGuid(0));
            if (universe.Count == 0) throw new InvalidOperationException("No eligible companies were copied.");
            pilotDatabase = (database, universe);
            return pilotDatabase.Value;
        }
        finally
        {
            PilotDatabaseGate.Release();
        }
    }

    private static async Task<HashSet<string>> ColumnsAsync(NpgsqlConnection connection, string sql, string table)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return columns;
    }

    private static async Task WriteArtifactAsync(string id, PilotRun run)
    {
        var directory = Path.Combine(FindRepositoryRoot(), "specs", "141-schema-aware-semantic-query-engine", "evaluation", "artifacts",
            "feature141-production-pilot");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyy-MM-dd-HHmmss}-{id}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { id, run }, new JsonSerializerOptions
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

    private sealed class RecordingExecutor(IReadOnlyStructuredQueryExecutor inner, ConcurrentQueue<ExecutedQuery> executed)
        : IReadOnlyStructuredQueryExecutor
    {
        public async Task<StructuredQueryExecutionResult> ExecuteAsync(CompiledReadOnlyQuery query, StructuredQueryExecutionGrant grant,
            CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(query, grant, cancellationToken);
            executed.Enqueue(new(query.Sql, query.Parameters.ToDictionary(parameter => parameter.Name, parameter => Text(parameter.Value)),
                result.Status.ToString(), result.DiagnosticCode, result.EffectiveUnit,
                result.Rows.Select(row => (IReadOnlyDictionary<string, string?>)row.ToDictionary(cell => cell.Key, cell => Text(cell.Value)))
                    .ToArray()));
            return result;
        }

        private static string? Text(object? value) => value switch
        {
            null => null,
            Guid[] ids => string.Join(",", ids),
            string[] values => "[" + string.Join(", ", values.Select(item => $"\"{item}\"")) + "]",
            DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private sealed class PilotEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Feature141ProductionDataPilotTests";
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

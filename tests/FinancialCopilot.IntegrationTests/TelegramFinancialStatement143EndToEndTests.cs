extern alias TelegramGateway;

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Gateway = TelegramGateway::FinancialCopilot.TelegramGateway;

namespace FinancialCopilot.IntegrationTests;

public sealed class TelegramFinancialStatement143EndToEndTests
{
    private const long CodalMirrorChatId = -100123;
    private const string TelegramPost = """
        {"update_id":143701,"channel_post":{"message_id":9701,"date":1760227200,"chat":{"id":-100123,"type":"channel","title":"CodalMirror"},"text":"\u0634\u0631\u06a9\u062a \u0641\u0648\u0644\u0627\u062f \u0645\u0628\u0627\u0631\u06a9\u0647 \u0627\u0635\u0641\u0647\u0627\u0646 (#\u0641\u0648\u0644\u0627\u062f)\n\u0635\u0648\u0631\u062a\u200c\u0647\u0627\u06cc \u0645\u0627\u0644\u06cc \u0633\u0627\u0644 \u0645\u0627\u0644\u06cc \u0645\u0646\u062a\u0647\u06cc \u0628\u0647 1403/12/30 (\u062d\u0633\u0627\u0628\u0631\u0633\u06cc \u0646\u0634\u062f\u0647)"}}
        """;

    [Fact]
    public async Task Gateway_channel_post_imports_all_four_categories_once_and_duplicate_replays()
    {
        using var api = new Feature143ApiFactory(autoBackfillEnabled: true, allowedChannelId: CodalMirrorChatId.ToString());
        api.Seed();
        var telegram = new TelegramUpdateScript(Envelope(TelegramPost, TelegramPost));
        await using var gateway = new GatewayHarness(api, telegram);
        await gateway.StartAsync();

        await gateway.PrimaryResponses.WaitForAsync(2);
        await gateway.StopAsync();

        using (var mapped = JsonDocument.Parse(gateway.PrimaryResponses.RequestBodies.First()))
        {
            var body = mapped.RootElement;
            Assert.Equal((int)TelegramAssistantUpdateKind.ChannelPost, body.GetProperty("kind").GetInt32());
            Assert.Equal(0, body.GetProperty("telegramUserId").GetInt64());
            Assert.Equal(CodalMirrorChatId, body.GetProperty("telegramChatId").GetInt64());
            Assert.Equal(9701, body.GetProperty("telegramMessageId").GetInt64());
            Assert.Contains("صورت‌های مالی", body.GetProperty("text").GetString());
        }
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["BalanceSheet"] = 1,
                ["IncomeStatement"] = 1,
                ["CashFlow"] = 1,
                ["FinancialRatios"] = 1
            },
            api.Noavaran.RequestedCategories.GroupBy(category => category).ToDictionary(group => group.Key, group => group.Count()));
        using var scope = api.Services.CreateScope();
        var financial = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(3, await financial.FinancialStatements.CountAsync());
        Assert.Equal(
            new[] { "BalanceSheet", "CashFlow", "IncomeStatement" },
            await financial.FinancialStatements.Select(row => row.StatementType).OrderBy(value => value).ToArrayAsync());
        Assert.Single(await financial.DerivedMetrics.ToListAsync());
        Assert.Equal(4, await financial.SyncRuns.CountAsync(x => x.Status == "Completed"));
        var checkpoint = await auth.TelegramProcessedUpdates.SingleAsync();
        Assert.Equal("Feature143:Completed", checkpoint.Status);
        using var result = JsonDocument.Parse(checkpoint.ResponseJson);
        Assert.All(new[] { "BalanceSheet", "IncomeStatement", "CashFlow", "FinancialRatios" }, category =>
            Assert.Equal("Persisted", result.RootElement.GetProperty(category).GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task Amendment_post_keeps_review_required_after_matching_reports_are_persisted()
    {
        using var api = new Feature143ApiFactory(autoBackfillEnabled: true, allowedChannelId: CodalMirrorChatId.ToString());
        api.Seed();
        var amendment = JsonNode.Parse(TelegramPost)!;
        amendment["channel_post"]!["text"] = amendment["channel_post"]!["text"]!.GetValue<string>() + " (اصلاحیه)";
        var telegram = new TelegramUpdateScript(Envelope(amendment.ToJsonString()));
        await using var gateway = new GatewayHarness(api, telegram);
        await gateway.StartAsync();

        await gateway.PrimaryResponses.WaitForAsync(1);
        await gateway.StopAsync();

        Assert.Equal(4, api.Noavaran.RequestedCategories.Count);
        using var scope = api.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var financial = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        Assert.Equal(3, await financial.FinancialStatements.CountAsync());
        Assert.Single(await financial.DerivedMetrics.ToListAsync());
        var checkpoint = await auth.TelegramProcessedUpdates.SingleAsync();
        Assert.Equal("Feature143:ReviewRequired", checkpoint.Status);
        using var result = JsonDocument.Parse(checkpoint.ResponseJson);
        Assert.All(new[] { "BalanceSheet", "IncomeStatement", "CashFlow", "FinancialRatios" }, category =>
            Assert.Equal("ReviewRequired", result.RootElement.GetProperty(category).GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task Gateway_post_from_channel_outside_allowlist_never_reaches_provider()
    {
        using var api = new Feature143ApiFactory(autoBackfillEnabled: true, allowedChannelId: "-100999");
        api.Seed();
        var telegram = new TelegramUpdateScript(Envelope(TelegramPost));
        await using var gateway = new GatewayHarness(api, telegram);
        await gateway.StartAsync();

        await gateway.PrimaryResponses.WaitForAsync(1);
        await gateway.StopAsync();

        Assert.Empty(api.Noavaran.RequestedCategories);
        using var scope = api.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().TelegramProcessedUpdates.ToListAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>().FinancialStatements.ToListAsync());
    }

    [Fact]
    public async Task Gateway_post_with_auto_backfill_disabled_is_claimed_without_provider_requests()
    {
        using var api = new Feature143ApiFactory(autoBackfillEnabled: false, allowedChannelId: CodalMirrorChatId.ToString());
        api.Seed();
        var telegram = new TelegramUpdateScript(Envelope(TelegramPost));
        await using var gateway = new GatewayHarness(api, telegram);
        await gateway.StartAsync();

        await gateway.PrimaryResponses.WaitForAsync(1);
        await gateway.StopAsync();

        Assert.Empty(api.Noavaran.RequestedCategories);
        using var scope = api.Services.CreateScope();
        var claim = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().TelegramProcessedUpdates.SingleAsync();
        Assert.Equal("Feature143:Disabled", claim.Status);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>().FinancialStatements.ToListAsync());
    }

    [Fact]
    public async Task Partial_failure_is_checkpointed_and_redelivery_retries_only_failed_category()
    {
        using var api = new Feature143ApiFactory(
            autoBackfillEnabled: true,
            allowedChannelId: CodalMirrorChatId.ToString(),
            failFirstIncomeStatement: true);
        api.Seed();
        var telegram = new TelegramUpdateScript(
            Envelope(TelegramPost),
            Envelope(TelegramPost, TelegramPost)) { HoldSecondPoll = true };
        await using var gateway = new GatewayHarness(api, telegram);
        await gateway.StartAsync();

        await gateway.PrimaryResponses.WaitForAsync(1);
        await telegram.SecondPollStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (var scope = api.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var partial = await auth.TelegramProcessedUpdates.SingleAsync();
            Assert.Equal("Feature143:Partial", partial.Status);
            using var checkpoint = JsonDocument.Parse(partial.ResponseJson);
            Assert.Equal("Persisted", checkpoint.RootElement.GetProperty("BalanceSheet").GetProperty("outcome").GetString());
            Assert.Equal("PersistenceFailed", checkpoint.RootElement.GetProperty("IncomeStatement").GetProperty("outcome").GetString());
            Assert.Equal("Persisted", checkpoint.RootElement.GetProperty("CashFlow").GetProperty("outcome").GetString());
            Assert.Equal("Persisted", checkpoint.RootElement.GetProperty("FinancialRatios").GetProperty("outcome").GetString());
        }

        telegram.ReleaseSecondPoll.TrySetResult();
        await gateway.PrimaryResponses.WaitForAsync(3);
        await gateway.StopAsync();

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["BalanceSheet"] = 1,
                ["IncomeStatement"] = 2,
                ["CashFlow"] = 1,
                ["FinancialRatios"] = 1
            },
            api.Noavaran.RequestedCategories.GroupBy(category => category).ToDictionary(group => group.Key, group => group.Count()));
        using (var scope = api.Services.CreateScope())
        {
            var financial = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            Assert.Equal(3, await financial.FinancialStatements.CountAsync());
            Assert.Equal(
                new[] { "BalanceSheet", "CashFlow", "IncomeStatement" },
                await financial.FinancialStatements.Select(row => row.StatementType).OrderBy(value => value).ToArrayAsync());
            Assert.Single(await financial.DerivedMetrics.ToListAsync());
            Assert.Equal(4, await financial.SyncRuns.CountAsync(run => run.Status == "Completed"));
            Assert.Single(await financial.SyncRuns.Where(run => run.Status == "Failed").ToListAsync());
            var completed = await auth.TelegramProcessedUpdates.SingleAsync();
            Assert.Equal("Feature143:Completed", completed.Status);
            using var result = JsonDocument.Parse(completed.ResponseJson);
            Assert.All(new[] { "BalanceSheet", "IncomeStatement", "CashFlow", "FinancialRatios" }, category =>
                Assert.Equal("Persisted", result.RootElement.GetProperty(category).GetProperty("outcome").GetString()));
            Assert.Equal(2, result.RootElement.GetProperty("IncomeStatement").GetProperty("attempts").GetInt32());
        }
    }

    private static string Envelope(params string[] updates) =>
        $"{{\"ok\":true,\"result\":[{string.Join(',', updates)}]}}";
}

internal sealed class Feature143ApiFactory : AuthenticationApiFactory
{
    private readonly SqliteConnection authConnection = new("Data Source=:memory:");
    private readonly SqliteConnection financialConnection = new("Data Source=:memory:");
    private readonly string providerDatabaseName = $"feature143-provider-{Guid.NewGuid():N}";
    private readonly bool autoBackfillEnabled;
    private readonly string allowedChannelId;
    private readonly bool failFirstIncomeStatement;
    public ControlledNoavaranProvider Noavaran { get; } = new();

    public Feature143ApiFactory(bool autoBackfillEnabled, string allowedChannelId, bool failFirstIncomeStatement = false)
    {
        this.autoBackfillEnabled = autoBackfillEnabled;
        this.allowedChannelId = allowedChannelId;
        this.failFirstIncomeStatement = failFirstIncomeStatement;
        authConnection.Open();
        financialConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ChannelFinancialStatements:AutoBackfillEnabled"] = autoBackfillEnabled.ToString(),
                ["ChannelMonthlyReports:AllowedChannelIds:0"] = allowedChannelId,
                ["NadpcoApi:BaseAddress"] = "https://noavaran.test/",
                ["NadpcoApi:UserName"] = "integration-test",
                ["NadpcoApi:Password"] = "integration-test"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseSqlite(authConnection));

            services.RemoveAll<FinancialIngestionDbContext>();
            services.RemoveAll<DbContextOptions<FinancialIngestionDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinancialIngestionDbContext>>();
            services.AddDbContext<FinancialIngestionDbContext>(options => options.UseSqlite(financialConnection));

            services.RemoveAll<FinancialProviderDbContext>();
            services.RemoveAll<DbContextOptions<FinancialProviderDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinancialProviderDbContext>>();
            services.AddDbContext<FinancialProviderDbContext>(options => options.UseInMemoryDatabase(providerDatabaseName));

            Noavaran.FailFirstIncomeStatement = failFirstIncomeStatement;
            services.AddSingleton(Noavaran);
            services.RemoveAll<INadpcoApiTokenProvider>();
            services.AddSingleton<INadpcoApiTokenProvider, StaticNoavaranTokenProvider>();
            services.AddSingleton<IHttpMessageHandlerBuilderFilter, NoavaranHttpMessageHandlerBuilderFilter>();
        });
    }

    public void Seed()
    {
        using var scope = Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        auth.Database.EnsureCreated();
        auth.Tenants.Add(new TenantRow { Id = TenantId, Name = "Feature 143 integration test" });
        auth.SaveChanges();

        var financial = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        financial.Database.EnsureCreated();
        financial.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalCompanyId = "19",
            Name = "شرکت فولاد مبارکه اصفهان",
            CompanySymbol = "فولاد",
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        financial.SaveChanges();
        financial.Database.ExecuteSqlRaw("""
            CREATE VIEW NoavaranEligibleCompanies AS
            SELECT Id, ProviderName, ExternalCompanyId, Name, IndustryId, GroupId, MarketId,
                   PrecedencyRight, CompanySymbol, SymbolIsin, TseSymbol
            FROM Companies
            WHERE ExternalCompanyId = '19'
            """);
        scope.ServiceProvider.GetRequiredService<FinancialProviderDbContext>().Database.EnsureCreated();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            authConnection.Dispose();
            financialConnection.Dispose();
        }
    }
}

internal sealed class GatewayHarness : IAsyncDisposable
{
    private readonly Gateway.TelegramGatewayPollingWorker worker;
    private readonly CancellationTokenSource cancellation = new();
    private readonly string stateDirectory = Path.Combine(Path.GetTempPath(), $"feature143-gateway-{Guid.NewGuid():N}");
    public PrimaryApiResponseObserver PrimaryResponses { get; } = new();

    public GatewayHarness(Feature143ApiFactory api, TelegramUpdateScript telegram)
    {
        _ = api.CreateClient();
        var settings = new Gateway.TelegramGatewaySettings
        {
            Enabled = true,
            BotToken = "integration-test-token",
            PrimaryApiBaseUrl = "http://localhost/",
            PrimaryApiKey = AuthenticationApiFactory.ApiKey,
            PollIntervalSeconds = 1,
            LongPollTimeoutSeconds = 5,
            RequestTimeoutSeconds = 15,
            DeleteWebhookOnStart = false,
            OffsetFilePath = Path.Combine(stateDirectory, "offset.txt"),
            IdempotencyFilePath = Path.Combine(stateDirectory, "idempotency.json")
        };
        var clientFactory = new GatewayHttpClientFactory(name => name == "TelegramGateway.PrimaryApi"
            ? new PrimaryApiObservingHandler(api.Server.CreateHandler(), PrimaryResponses)
            : new TelegramBotApiHandler(telegram));
        var options = Options.Create(settings);
        var telegramApi = new Gateway.TelegramApiClient(clientFactory, options);
        var primaryApi = new Gateway.PrimaryApiClient(clientFactory, options);
        var idempotency = new Gateway.GatewayIdempotencyStore(options, NullLogger<Gateway.GatewayIdempotencyStore>.Instance);
        worker = new Gateway.TelegramGatewayPollingWorker(
            telegramApi, primaryApi, idempotency, options, NullLogger<Gateway.TelegramGatewayPollingWorker>.Instance);
    }

    public Task StartAsync() => worker.StartAsync(cancellation.Token);

    public Task StopAsync() => worker.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        catch (OperationCanceledException) { }
        worker.Dispose();
        cancellation.Dispose();
        if (Directory.Exists(stateDirectory)) Directory.Delete(stateDirectory, recursive: true);
    }
}

internal sealed class TelegramUpdateScript(params string[] responses)
{
    private int pollCount;
    private readonly string[] responses = responses;
    public bool HoldSecondPoll { get; init; }
    public TaskCompletionSource SecondPollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseSecondPoll { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<string> GetUpdatesAsync(CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref pollCount);
        if (current == 2 && HoldSecondPoll)
        {
            SecondPollStarted.TrySetResult();
            await ReleaseSecondPoll.Task.WaitAsync(cancellationToken);
        }
        return current <= responses.Length ? responses[current - 1] : "{\"ok\":true,\"result\":[]}";
    }
}

internal sealed class GatewayHttpClientFactory(Func<string, HttpMessageHandler> createHandler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(createHandler(name), disposeHandler: true);
}

internal sealed class TelegramBotApiHandler(TelegramUpdateScript script) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("getUpdates", request.RequestUri!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("channel_post", request.RequestUri.OriginalString, StringComparison.Ordinal);
        var response = await script.GetUpdatesAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }
}

internal sealed class PrimaryApiObservingHandler(HttpMessageHandler inner, PrimaryApiResponseObserver observer) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/telegram/assistant/updates", request.RequestUri!.AbsolutePath);
        var requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);
        observer.RecordResponse(requestBody);
        return response;
    }
}

internal sealed class PrimaryApiResponseObserver
{
    private int responseCount;
    private readonly ConcurrentDictionary<int, TaskCompletionSource> waiters = new();
    private readonly ConcurrentQueue<string> requestBodies = new();

    public IReadOnlyCollection<string> RequestBodies => requestBodies.ToArray();

    public Task WaitForAsync(int count) =>
        waiters.GetOrAdd(count, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .Task.WaitAsync(TimeSpan.FromSeconds(20));

    public void RecordResponse(string requestBody)
    {
        requestBodies.Enqueue(requestBody);
        var count = Interlocked.Increment(ref responseCount);
        foreach (var waiter in waiters.Where(item => item.Key <= count)) waiter.Value.TrySetResult();
    }
}

internal sealed class ControlledNoavaranProvider
{
    private int failIncomeStatement;
    private readonly ConcurrentQueue<string> requestedCategories = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource> waiters = new();
    private int requestCount;

    public bool FailFirstIncomeStatement
    {
        set => failIncomeStatement = value ? 1 : 0;
    }

    public IReadOnlyCollection<string> RequestedCategories => requestedCategories.ToArray();

    public Task WaitForRequestCountAsync(int count) =>
        waiters.GetOrAdd(count, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .Task.WaitAsync(TimeSpan.FromSeconds(20));

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var category = path switch
        {
            var value when value.Contains("BalanceSheet", StringComparison.Ordinal) => "BalanceSheet",
            var value when value.Contains("IncomeStatement", StringComparison.Ordinal) => "IncomeStatement",
            var value when value.Contains("CashFlow", StringComparison.Ordinal) => "CashFlow",
            var value when value.Contains("CompanyFundamentalIndex", StringComparison.Ordinal) => "FinancialRatios",
            _ => throw new InvalidOperationException($"Unexpected Noavaran endpoint: {path}")
        };
        requestedCategories.Enqueue(category);
        var count = Interlocked.Increment(ref requestCount);
        foreach (var waiter in waiters.Where(item => item.Key <= count)) waiter.Value.TrySetResult();

        if (category == "IncomeStatement" && Interlocked.Exchange(ref failIncomeStatement, 0) == 1)
            return Json("not-json");

        return Json(category == "FinancialRatios" ? RatioPayload : StatementPayload(category));
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string StatementPayload(string category)
    {
        var statementId = category switch { "BalanceSheet" => 301, "IncomeStatement" => 302, _ => 303 };
        return $$"""
            [{"statementID":{{statementId}},"com_ID":19,"bourseSymbol":"فولاد","fullTitle":"گزارش مالی سالانه","periodType":12,"fiscalYearEnd":"2025-03-20T00:00:00","jalaliFiscalYearEnd":"1403/12/30","periodEnd":"2025-03-20T00:00:00","jalaliPeriodEnd":"1403/12/30","anouncementDate":"2025-04-15T00:00:00","jalaliAnouncementDate":"1404/01/26","isAudited":false,"isRepresented":false,"isComposing":false,"items":[{"itemID":143,"itemTitle":"سود خالص","amount":1000,"amountUnit":"N/A"}]}]
            """;
    }

    private const string RatioPayload = """
        [{"comBS_ID":323928,"comId":19,"comTitle":"شرکت فولاد مبارکه اصفهان","periodType":12,"jalaliFiscalYearEnd":"1403/12/30","jalaliPeriodEnd":"1403/12/30","jalaliAnouncementDate":"1404/01/26","isAudited":false,"isRepresented":false,"isComposing":false,"indexes":[{"companyIndexId":65,"companyIndexTitle":"نسبت جاری","companyIndexGroupId":1156,"companyIndexGroupTitle":"سنجش نقدینگی","companyIndexValue":1.25,"companyIndexUnit":""}]}]
        """;
}

internal sealed class StubNoavaranHttpHandler(ControlledNoavaranProvider provider) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(provider.Respond(request));
}

internal sealed class NoavaranHttpMessageHandlerBuilderFilter(ControlledNoavaranProvider provider) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name == nameof(NadpcoApiDataProviderClient))
            builder.PrimaryHandler = new StubNoavaranHttpHandler(provider);
    };
}

internal sealed class StaticNoavaranTokenProvider : INadpcoApiTokenProvider
{
    public Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken) => Task.FromResult("integration-test-token");
    public void Invalidate() { }
}


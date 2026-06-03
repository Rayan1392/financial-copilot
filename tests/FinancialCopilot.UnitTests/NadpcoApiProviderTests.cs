using System.Net;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class NadpcoApiProviderTests
{
    [Fact]
    public async Task TokenProvider_ReusesCachedTokenUntilExpiry()
    {
        var tokenRequestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            tokenRequestCount++;
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            return JsonResponse(new { access_token = "token-1", expires_in = 120 });
        }))
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };
        var provider = CreateTokenProvider(httpClient);

        var first = await provider.GetTokenAsync(forceRefresh: false, CancellationToken.None);
        var second = await provider.GetTokenAsync(forceRefresh: false, CancellationToken.None);

        Assert.Equal("token-1", first);
        Assert.Equal(first, second);
        Assert.Equal(1, tokenRequestCount);
    }

    [Fact]
    public async Task TokenProvider_RefreshesExpiredToken()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-06-03T10:00:00Z"));
        var tokenRequestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            tokenRequestCount++;
            return JsonResponse(new { access_token = $"token-{tokenRequestCount}", expires_in = 60 });
        }))
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };
        var provider = CreateTokenProvider(httpClient, timeProvider: time);

        var first = await provider.GetTokenAsync(forceRefresh: false, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(2));
        var second = await provider.GetTokenAsync(forceRefresh: false, CancellationToken.None);

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, tokenRequestCount);
    }

    [Fact]
    public async Task TokenProvider_AuthenticationFailure_ReturnsRedactedProviderException()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)))
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };
        var provider = CreateTokenProvider(httpClient, options: new NadpcoApiProviderOptions
        {
            UserName = "vendor-user",
            Password = "vendor-password"
        });

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(
            () => provider.GetTokenAsync(forceRefresh: false, CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.Unauthorized, exception.Code);
        Assert.DoesNotContain("vendor-user", exception.Message);
        Assert.DoesNotContain("vendor-password", exception.Message);
    }

    [Fact]
    public async Task AuthHandler_AddsBearerHeaderAndRetriesOnceAfter401()
    {
        var tokenProvider = new SequenceTokenProvider("expired-token", "fresh-token");
        var dataRequestCount = 0;
        var capturedTokens = new List<string>();
        var inner = new StubHttpMessageHandler(request =>
        {
            dataRequestCount++;
            capturedTokens.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            return new HttpResponseMessage(dataRequestCount == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        });
        using var client = new HttpClient(new NadpcoApiAuthHandler(tokenProvider) { InnerHandler = inner })
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };

        using var response = await client.GetAsync("api/v3/BaseInfo/Companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["expired-token", "fresh-token"], capturedTokens);
        Assert.Equal(1, tokenProvider.Invalidations);
    }

    [Fact]
    public async Task ResilienceHandler_MapsTimeoutAndOpensCircuit()
    {
        var timeoutHandler = new NadpcoApiResilienceHandler(
            Options.Create(new NadpcoApiProviderOptions
            {
                TimeoutSeconds = 0,
                RetryCount = 0,
                CircuitFailureThreshold = 2
            }),
            TimeProvider.System,
            NullLogger<NadpcoApiResilienceHandler>.Instance)
        {
            InnerHandler = new StubHttpMessageHandler(_ =>
                Task.FromException<HttpResponseMessage>(new OperationCanceledException()))
        };
        using var timeoutInvoker = new HttpMessageInvoker(timeoutHandler);

        var timeout = await Assert.ThrowsAsync<FinancialProviderException>(() =>
            timeoutInvoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://data3.nadpco.com/slow"), CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.Timeout, timeout.Code);

        var circuitHandler = new NadpcoApiResilienceHandler(
            Options.Create(new NadpcoApiProviderOptions
            {
                RetryCount = 0,
                CircuitFailureThreshold = 1,
                CircuitBreakSeconds = 60
            }),
            TimeProvider.System,
            NullLogger<NadpcoApiResilienceHandler>.Instance)
        {
            InnerHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        };
        using var circuitInvoker = new HttpMessageInvoker(circuitHandler);
        using var failed = await circuitInvoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://data3.nadpco.com/unavailable"),
            CancellationToken.None);
        var openCircuit = await Assert.ThrowsAsync<FinancialProviderException>(() =>
            circuitInvoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://data3.nadpco.com/unavailable"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(FinancialProviderErrorCode.RemoteUnavailable, openCircuit.Code);
    }

    [Fact]
    public async Task DataProvider_FetchSymbols_StoresRawPayloadWithDeterministicChecksum()
    {
        await using var dbContext = CreateProviderDbContext();
        var store = new ProviderRawPayloadStore(dbContext);
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"companyId":"1","symbol":"ABC"}]""", Encoding.UTF8, "application/json")
            }))
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };
        var client = CreateDataProvider(httpClient, store);

        var first = await client.FetchSymbolsAsync(CancellationToken.None);
        var second = await client.FetchSymbolsAsync(CancellationToken.None);

        Assert.Equal("NadpcoApi", first.ProviderName);
        Assert.Equal(ProviderDataset.Symbols, first.Dataset);
        Assert.Equal(first.Checksum, second.Checksum);
        Assert.Single(dbContext.ProviderRawPayloads);
    }

    [Fact]
    public async Task DataProvider_FetchFinancialStatements_PostsBoundedCompanyAndItemAllowlists()
    {
        await using var dbContext = CreateProviderDbContext();
        var requests = new List<(string Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            requests.Add((
                request.RequestUri!.OriginalString,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync()));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };
        var client = new NadpcoApiDataProviderClient(
            httpClient,
            new ProviderRawPayloadStore(dbContext),
            new SequenceTokenProvider("token"),
            Options.Create(new NadpcoApiProviderOptions
            {
                StatementFromYear = 1401,
                StatementPeriodTypeId = 6,
                StatementIsAudited = false
            }),
            TimeProvider.System,
            NullLogger<NadpcoApiDataProviderClient>.Instance);

        await client.FetchFinancialStatementsAsync("3", CancellationToken.None);

        Assert.Equal(3, requests.Count);
        Assert.All(requests, r => Assert.Contains("\"companyIds\":[3]", r.Body));
        Assert.All(requests, r => Assert.DoesNotContain("\"items\":[]", r.Body));
        Assert.Contains(requests, r => r.Uri.Contains("IncomeStatement") && r.Body.Contains("143"));
        Assert.Contains(requests, r => r.Uri.Contains("BalanceSheet") && r.Body.Contains("147"));
        Assert.Contains(requests, r => r.Uri.Contains("CashFlow") && r.Body.Contains("\"items\":[1]"));
        Assert.All(requests, r => Assert.Contains("fromYear=1401", r.Uri));
        Assert.All(requests, r => Assert.Contains("perTId=6", r.Uri));
        Assert.All(requests, r => Assert.Contains("isAudited=false", r.Uri));
    }

    [Fact]
    public async Task DataProvider_FetchFinancialRatios_PostsBoundedFundamentalIndexAllowlists()
    {
        await using var dbContext = CreateProviderDbContext();
        var requests = new List<(string Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            requests.Add((
                request.RequestUri!.OriginalString,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync()));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };
        var client = new NadpcoApiDataProviderClient(
            httpClient,
            new ProviderRawPayloadStore(dbContext),
            new SequenceTokenProvider("token"),
            Options.Create(new NadpcoApiProviderOptions
            {
                FundamentalIndexFromYear = 1401,
                FundamentalIndexPeriodTypeId = 6,
                FundamentalIndexIsAudited = true
            }),
            TimeProvider.System,
            NullLogger<NadpcoApiDataProviderClient>.Instance);

        var payload = await client.FetchFinancialRatiosAsync("3", CancellationToken.None);

        var request = Assert.Single(requests);
        Assert.Equal(ProviderDataset.FundamentalIndexes, payload.Dataset);
        Assert.Contains("\"companyIds\":[3]", request.Body);
        Assert.Contains("\"companyIndexIds\":[", request.Body);
        Assert.Contains("65", request.Body);
        Assert.DoesNotContain("\"companyIndexIds\":[]", request.Body);
        Assert.Contains("fromYear=1401", request.Uri);
        Assert.Contains("perTId=6", request.Uri);
        Assert.Contains("isAudited=true", request.Uri);
    }

    [Fact]
    public void Router_ResolvesNadpcoApiAlongsideCodalDbByProviderName()
    {
        var codal = new StubSymbolProvider();
        var nadpco = new StubSymbolProvider();
        var codalRatios = new StubRatioProvider();
        var nadpcoRatios = new StubRatioProvider();
        var router = new FinancialDataProviderRouter(
            new Dictionary<string, ISymbolDataProvider>
            {
                ["CodalDb"] = codal,
                ["NadpcoApi"] = nadpco
            },
            new Dictionary<string, IFinancialStatementProvider>(),
            new Dictionary<string, IMonthlyProductionSalesProvider>(),
            new Dictionary<string, IFinancialRatioProvider>
            {
                ["CodalDb"] = codalRatios,
                ["NadpcoApi"] = nadpcoRatios
            });

        Assert.Same(codal, router.ResolveSymbolProvider("codaldb"));
        Assert.Same(nadpco, router.ResolveSymbolProvider("NADPCOAPI"));
        Assert.Same(codalRatios, router.ResolveRatioProvider("codaldb"));
        Assert.Same(nadpcoRatios, router.ResolveRatioProvider("NADPCOAPI"));
    }

    private static NadpcoApiTokenProvider CreateTokenProvider(
        HttpClient httpClient,
        NadpcoApiProviderOptions? options = null,
        TimeProvider? timeProvider = null,
        NadpcoApiTokenCache? cache = null) =>
        new(
            httpClient,
            cache ?? new NadpcoApiTokenCache(),
            Options.Create(options ?? new NadpcoApiProviderOptions
            {
                UserName = "test-user",
                Password = "test-pass"
            }),
            timeProvider ?? TimeProvider.System,
            NullLogger<NadpcoApiTokenProvider>.Instance);

    private static NadpcoApiDataProviderClient CreateDataProvider(
        HttpClient httpClient,
        IProviderRawPayloadStore store) =>
        new(
            httpClient,
            store,
            new SequenceTokenProvider("token"),
            Options.Create(new NadpcoApiProviderOptions()),
            TimeProvider.System,
            NullLogger<NadpcoApiDataProviderClient>.Instance);

    private static FinancialProviderDbContext CreateProviderDbContext()
    {
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FinancialProviderDbContext(options);
    }

    private static HttpResponseMessage JsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            : this(request => Task.FromResult(responseFactory(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request);
    }

    private sealed class SequenceTokenProvider(params string[] tokens) : INadpcoApiTokenProvider
    {
        private readonly Queue<string> _tokens = new(tokens);

        public int Invalidations { get; private set; }

        public Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(_tokens.Count > 1 ? _tokens.Dequeue() : _tokens.Peek());

        public void Invalidate() => Invalidations++;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }

    private sealed class StubSymbolProvider : ISymbolDataProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubRatioProvider : IFinancialRatioProvider
    {
        public Task<ProviderRawPayload> FetchFinancialRatiosAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

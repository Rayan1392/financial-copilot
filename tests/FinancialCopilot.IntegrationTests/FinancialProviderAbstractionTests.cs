using System.Net;
using System.Text;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.IntegrationTests;

public sealed class FinancialProviderAbstractionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-26T09:00:00Z");

    [Fact]
    public async Task RawPayloadStore_PersistsProviderPayloadIdempotentlyByChecksum()
    {
        await using var dbContext = CreateDbContext();
        var store = new ProviderRawPayloadStore(dbContext);
        var payload = new ProviderRawPayload(
            Guid.NewGuid(),
            "ProviderA",
            ProviderDataset.Symbols,
            "/symbols",
            "all",
            "[]",
            "CHECKSUM",
            Now);

        await store.StoreAsync(payload, CancellationToken.None);
        await store.StoreAsync(payload with { Id = Guid.NewGuid() }, CancellationToken.None);

        var restored = await store.FindByChecksumAsync("ProviderA", "CHECKSUM", CancellationToken.None);
        Assert.Single(dbContext.ProviderRawPayloads);
        Assert.Equal(payload.Id, restored!.Id);
    }

    [Fact]
    public async Task MockProvider_ReturnsLiveAndPreviousTradingDayQuotesWithEvidence()
    {
        await using var dbContext = CreateDbContext();
        var provider = new MockFinancialDataProvider(
            new ProviderRawPayloadStore(dbContext),
            new FixedTimeProvider(Now));

        var quotes = await provider.GetLatestQuotesAsync(
            [new SymbolCode("LIVE"), new SymbolCode("FALLBACK"), new SymbolCode("UNKNOWN")],
            CancellationToken.None);
        var health = await provider.CheckAsync(CancellationToken.None);

        Assert.Equal(MarketQuoteSource.LiveQuote, quotes.Observations.Single(item => item.SymbolCode.Value == "LIVE").Source);
        Assert.Equal(
            MarketQuoteSource.PreviousTradingDay,
            quotes.Observations.Single(item => item.SymbolCode.Value == "FALLBACK").Source);
        Assert.Equal("UNKNOWN", quotes.UnavailableSymbols.Single().Value);
        Assert.Equal(MockFinancialDataProvider.ProviderName, quotes.Observations.First().SourceEvidence.SourceProvider);
        Assert.Equal(ProviderHealthStatus.Healthy, health.Status);
    }

    [Fact]
    public async Task MockProvider_StoresRawPayloadBeforeLaterNormalizationConsumerUsesIt()
    {
        await using var dbContext = CreateDbContext();
        var provider = new MockFinancialDataProvider(
            new ProviderRawPayloadStore(dbContext),
            new FixedTimeProvider(Now));

        var payload = await provider.FetchFinancialStatementsAsync("company-42", CancellationToken.None);

        Assert.Equal(ProviderDataset.FinancialStatements, payload.Dataset);
        Assert.NotEmpty(payload.Checksum);
        Assert.Equal(payload.Checksum, dbContext.ProviderRawPayloads.Single().Checksum);
    }

    [Fact]
    public async Task ConfiguredHttpProvider_MapsBatchLiveAndFallbackSourceMetadata()
    {
        await using var dbContext = CreateDbContext();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            JsonResponse(
                """[{"symbol":"AAA","latestPrice":100,"priceChangePercentage":2,"asOf":"2026-05-26T08:00:00Z","isLive":true},{"symbol":"BBB","latestPrice":90,"priceChangePercentage":-1,"asOf":"2026-05-25T09:00:00Z","isLive":false}]""")))
        {
            BaseAddress = new Uri("https://provider.test/")
        };
        var provider = new ConfiguredFinancialDataProviderClient(
            httpClient,
            new ProviderRawPayloadStore(dbContext),
            Options.Create(new FinancialProviderOptions { ProviderName = "ProviderA" }),
            new FixedTimeProvider(Now),
            NullLogger<ConfiguredFinancialDataProviderClient>.Instance);

        var result = await provider.GetLatestQuotesAsync(
            [new SymbolCode("AAA"), new SymbolCode("BBB"), new SymbolCode("CCC")],
            CancellationToken.None);

        Assert.Equal(MarketQuoteSource.LiveQuote, result.Observations.Single(item => item.SymbolCode.Value == "AAA").Source);
        Assert.Equal(
            MarketQuoteSource.PreviousTradingDay,
            result.Observations.Single(item => item.SymbolCode.Value == "BBB").Source);
        Assert.Equal("CCC", result.UnavailableSymbols.Single().Value);
        Assert.All(result.Observations, item => Assert.Equal("ProviderA", item.SourceEvidence.SourceProvider));
    }

    [Fact]
    public async Task ResilienceHandler_RetriesTransientFailureAndOpensCircuitAfterThreshold()
    {
        var retryHandler = new FinancialProviderResilienceHandler(
            Options.Create(new FinancialProviderOptions { RetryCount = 1, CircuitFailureThreshold = 3 }),
            new FixedTimeProvider(Now),
            NullLogger<FinancialProviderResilienceHandler>.Instance)
        {
            InnerHandler = new SequenceHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                new HttpResponseMessage(HttpStatusCode.OK))
        };
        using var retryInvoker = new HttpMessageInvoker(retryHandler);

        using var successful = await retryInvoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://provider.test/symbols"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, successful.StatusCode);

        var circuitHandler = new FinancialProviderResilienceHandler(
            Options.Create(new FinancialProviderOptions { RetryCount = 0, CircuitFailureThreshold = 1 }),
            new FixedTimeProvider(Now),
            NullLogger<FinancialProviderResilienceHandler>.Instance)
        {
            InnerHandler = new SequenceHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        };
        using var circuitInvoker = new HttpMessageInvoker(circuitHandler);

        using var failed = await circuitInvoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://provider.test/symbols"),
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() =>
            circuitInvoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://provider.test/symbols"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(FinancialProviderErrorCode.RemoteUnavailable, exception.Code);
    }

    private static FinancialProviderDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FinancialProviderDbContext(options);
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class SequenceHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
    }
}

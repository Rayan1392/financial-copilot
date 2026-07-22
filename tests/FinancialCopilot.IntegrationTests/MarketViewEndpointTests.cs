using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class MarketViewEndpointTests : IClassFixture<MarketViewApiFactory>
{
    private readonly MarketViewApiFactory _factory;

    public MarketViewEndpointTests(MarketViewApiFactory factory)
    {
        _factory = factory;
        factory.EnsureBillingSeeded();
        factory.EnsureMarketSeeded();
        factory.ResetWatchlists();
    }

    [Fact]
    public async Task Watchlist_UpdateThenRead_ReturnsQuoteAndNoQuoteFallback()
    {
        using var client = UserClient();

        using var updateResponse = await client.PutAsJsonAsync(
            "/api/v1/watchlists/me",
            new { symbols = new[] { "GAIN", "NOQUOTE" } },
            CancellationToken.None);
        using var readResponse = await client.GetAsync("/api/v1/watchlists/me", CancellationToken.None);
        using var document = await ReadJsonAsync(readResponse);
        var symbols = document.RootElement.GetProperty("symbols").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(2, symbols.Length);
        Assert.Equal(5m, symbols[0].GetProperty("changePercent").GetDecimal());
        Assert.Equal(JsonValueKind.Null, symbols[1].GetProperty("latestPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, symbols[1].GetProperty("asOf").ValueKind);
    }

    [Fact]
    public async Task Watchlist_Read_IsScopedToActorWithinTenant()
    {
        using var owner = UserClient();
        using var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var updateResponse = await owner.PutAsJsonAsync(
            "/api/v1/watchlists/me",
            new { symbols = new[] { "GAIN" } },
            CancellationToken.None);
        using var peerResponse = await apiClient.GetAsync("/api/v1/watchlists/me", CancellationToken.None);
        using var document = await ReadJsonAsync(peerResponse);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, peerResponse.StatusCode);
        Assert.Empty(document.RootElement.GetProperty("symbols").EnumerateArray());
    }

    [Fact]
    public async Task Watchlist_Read_PrefersFollowedSymbolsAndReturnsQuotes()
    {
        using var client = UserClient();

        using var legacyResponse = await client.PutAsJsonAsync(
            "/api/v1/watchlists/me",
            new { symbols = new[] { "LOSS" } },
            CancellationToken.None);
        using var followResponse = await client.PostAsync(
            "/api/v1/followed-symbols/me/gain-company",
            null,
            CancellationToken.None);
        using var readResponse = await client.GetAsync("/api/v1/watchlists/me", CancellationToken.None);
        using var document = await ReadJsonAsync(readResponse);
        var symbols = document.RootElement.GetProperty("symbols").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, followResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var symbol = Assert.Single(symbols);
        Assert.Equal("GAIN", symbol.GetProperty("symbol").GetString());
        Assert.Equal(105m, symbol.GetProperty("latestPrice").GetDecimal());
        Assert.Equal(5m, symbol.GetProperty("changePercent").GetDecimal());
    }


    [Fact]
    public async Task Watchlist_Update_RejectsUnknownSymbol()
    {
        using var client = UserClient();

        using var response = await client.PutAsJsonAsync(
            "/api/v1/watchlists/me",
            new { symbols = new[] { "UNKNOWN" } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MarketSummary_ReturnsNormalizedObservationsAndExplicitUnavailableFields()
    {
        using var client = UserClient();

        using var response = await client.GetAsync("/api/v1/market/summary", CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(root.GetProperty("indices").EnumerateArray());
        Assert.Equal("GAIN", Assert.Single(root.GetProperty("topGainers").EnumerateArray()).GetProperty("symbol").GetString());
        Assert.Equal("LOSS", Assert.Single(root.GetProperty("topLosers").EnumerateArray()).GetProperty("symbol").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("realMoneyFlow").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("trendingIndustries").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("insight").ValueKind);
    }

    [Fact]
    public async Task MarketPulse_LatestIsIdempotentAndHistoryExposesEvidenceBoundSnapshot()
    {
        using var client = UserClient();

        using var firstResponse = await client.GetAsync("/api/v1/market-pulse/latest", CancellationToken.None);
        using var first = await ReadJsonAsync(firstResponse);
        using var secondResponse = await client.GetAsync("/api/v1/market-pulse/latest", CancellationToken.None);
        using var second = await ReadJsonAsync(secondResponse);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            var quote = db.LatestMarketQuotes.Single(row => row.PriceChangePercentage == 5m);
            quote.PriceChangePercentage = 6m;
            quote.AsOf = quote.AsOf.AddSeconds(1);
            db.SaveChanges();
        }

        using var correctedResponse = await client.GetAsync("/api/v1/market-pulse/latest", CancellationToken.None);
        using var corrected = await ReadJsonAsync(correctedResponse);
        using var historyResponse = await client.GetAsync("/api/v1/market-pulse/history?page=1&pageSize=10", CancellationToken.None);
        using var history = await ReadJsonAsync(historyResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, correctedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(first.RootElement.GetProperty("id").GetGuid(), second.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(1, first.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(2, corrected.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(first.RootElement.GetProperty("id").GetGuid(),
            corrected.RootElement.GetProperty("supersedesSnapshotId").GetGuid());
        Assert.Contains(first.RootElement.GetProperty("facts").EnumerateArray(), fact =>
            fact.GetProperty("code").GetString() == "SMALL_TRADE_VALUE" &&
            fact.GetProperty("status").GetString() == "Unavailable" &&
            fact.GetProperty("value").ValueKind == JsonValueKind.Null);
        Assert.NotEmpty(first.RootElement.GetProperty("evidence").EnumerateArray());
        Assert.Contains(history.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("id").GetGuid() == corrected.RootElement.GetProperty("id").GetGuid());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            var revisions = db.MarketPulseSnapshots
                .Where(row => row.TradingDate == DateOnly.FromDateTime(DateTime.UtcNow) && row.Segment == "all")
                .OrderBy(row => row.Revision)
                .ToArray();
            Assert.Contains(revisions, row => row.Id == first.RootElement.GetProperty("id").GetGuid() && !row.IsCurrent);
            Assert.Contains(revisions, row => row.Id == corrected.RootElement.GetProperty("id").GetGuid() && row.IsCurrent);
            var quote = db.LatestMarketQuotes.Single(row => row.PriceChangePercentage == 6m);
            quote.PriceChangePercentage = 5m;
            db.SaveChanges();
        }
    }

    [Theory]
    [InlineData("/api/v1/watchlists/me")]
    [InlineData("/api/v1/market/summary")]
    [InlineData("/api/v1/market-pulse/latest")]
    [InlineData("/api/v1/market-pulse/history")]
    public async Task MarketViewEndpoints_WithoutCredentials_ReturnUnauthorized(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient UserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public class MarketViewApiFactory : AiFacadeApiFactory
{
    private bool _marketSeeded;
    private readonly object _marketSeedLock = new();

    public void EnsureMarketSeeded()
    {
        if (_marketSeeded) return;
        lock (_marketSeedLock)
        {
            if (_marketSeeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            db.Database.EnsureCreated();
            var gain = Instrument("GAIN", "Gain Co");
            var loss = Instrument("LOSS", "Loss Co");
            var noQuote = Instrument("NOQUOTE", "No Quote Co");
            // The active direct-feed path persists index rows under TSETMC instrument codes;
            // the summary must still recognize the governed شاخص کل catalog entry.
            var index = Instrument("TEDPIX", "Total Index");
            index.InstrumentCode = 32097828799138957;
            db.TradingInstruments.AddRange(gain, loss, noQuote, index);
            db.Companies.AddRange(
                Company("gain-company", "GAIN", "Gain Co"),
                Company("loss-company", "LOSS", "Loss Co"),
                Company("noquote-company", "NOQUOTE", "No Quote Co"));
            db.LatestMarketQuotes.AddRange(
                Quote(gain.Id, 105m, 5m),
                Quote(loss.Id, 95m, -5m));
            db.DailyIndexSnapshots.Add(new DailyIndexSnapshotRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "StockMarketDb",
                TradingInstrumentId = index.Id,
                TradingDate = new DateOnly(2026, 6, 1),
                Value = 2_500_000m,
                ChangePercent = 1.25m,
                SourceKind = "IntradayClose",
                ObservedAt = DateTimeOffset.Parse("2026-06-01T12:30:00Z")
            });
            db.SaveChanges();
            _marketSeeded = true;
        }
    }

    public void ResetWatchlists()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.WatchlistSymbols.RemoveRange(db.WatchlistSymbols);
        db.FollowedSymbols.RemoveRange(db.FollowedSymbols);
        db.SaveChanges();
    }

    private static TradingInstrumentRow Instrument(string symbol, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = Random.Shared.NextInt64(1, long.MaxValue),
            InstrumentIsin = $"IRO1{symbol}0001",
            Symbol = symbol,
            Name = name,
            MarketCode = "NO",
            InstrumentKind = "A",
            IsActive = true,
            SourceChangedAt = DateTimeOffset.Parse("2026-06-01T12:30:00Z"),
            LastSynchronizedAt = DateTimeOffset.Parse("2026-06-01T12:30:00Z")
        };

    private static NormalizedCompanyRow Company(string externalCompanyId, string symbol, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            ExternalCompanyId = externalCompanyId,
            CompanySymbol = symbol,
            TseSymbol = symbol,
            Ticker = symbol,
            Name = name,
            LastSynchronizedAt = DateTimeOffset.Parse("2026-06-01T12:30:00Z")
        };

    private static LatestMarketQuoteRow Quote(Guid instrumentId, decimal price, decimal change) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            TradingInstrumentId = instrumentId,
            LatestPrice = price,
            PriceChangePercentage = change,
            SourceKind = "Intraday",
            TradingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AsOf = DateTimeOffset.UtcNow
        };
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 159 change «standalone-valuation-gauge-cards»: POST /api/ai/v1/query through the V2 workflow (API-key auth,
/// gate, executor, billing, persistence, history) over an in-memory Feature 148 store. Only the LLM is a fake, and the
/// gauge route never calls it.
/// </summary>
public sealed class ValuationGaugeCardsCyclicalWavesE2ETests : IClassFixture<ValuationGaugeCardsApiFactory>
{
    private readonly ValuationGaugeCardsApiFactory _factory;

    public ValuationGaugeCardsCyclicalWavesE2ETests(ValuationGaugeCardsApiFactory factory)
    {
        _factory = factory;
        factory.EnsureGaugeSeeded();
        factory.ResetRecorders();
    }

    [Fact]
    public async Task SinglePeCard_IsAnsweredFromTheSnapshot_ChargedOnce_AndReplayedFromHistory()
    {
        var key = Guid.NewGuid();
        var root = await ValuationGaugeChat.AskAsync(_factory, "گیج P/E شاراک", key);

        Assert.Equal("valuation_gauges", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal("ValuationGauges", root.GetProperty("intent").GetString());
        var cards = root.GetProperty("valuationGaugeCards");
        var card = Assert.Single(cards.GetProperty("cards").EnumerateArray().ToArray());
        Assert.Equal("Pe", card.GetProperty("kind").GetString());
        Assert.Equal("Available", card.GetProperty("state").GetString());
        Assert.Equal("6.4291", card.GetProperty("ratio").GetProperty("current").GetString());
        Assert.Equal("5.88", card.GetProperty("ratio").GetProperty("average").GetString());
        var average = card.GetProperty("geometry").GetProperty("markers").EnumerateArray()
            .Single(marker => marker.GetProperty("role").GetString() == "average");
        Assert.Equal(90m, average.GetProperty("angle").GetDecimal());
        Assert.Contains("نسبت فعلی ۶٫۴۳", ValuationGaugeChat.Text(root));
        // Visual refresh: derived figures and band ranges travel as optional members of the stored card.
        var insights = card.GetProperty("insights");
        Assert.Equal(JsonValueKind.String, insights.GetProperty("distanceToAveragePercent").ValueKind);
        Assert.InRange(insights.GetProperty("currentBand").GetInt32(), 1, 6);
        Assert.Equal("5.88", card.GetProperty("geometry").GetProperty("sectors")[3].GetProperty("from").GetString());
        Assert.Equal(1m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        var charge = Assert.Single(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge");
        Assert.Equal("AiQuery.Scanner", charge.OperationCode);
        Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
        Feature148Chat.NoOutboundCalls(_factory.Http);
        Assert.Empty(_factory.Fake.ToolCalls);
        Assert.Empty(_factory.Fake.StructuredOutputSchemas);

        // AC-06: history returns the same stored facts and geometry.
        var conversationId = root.GetProperty("conversationId").GetGuid();
        var messages = (await Feature148Chat.MessagesAsync(_factory, conversationId)).GetProperty("messages").EnumerateArray().ToArray();
        var replayed = messages[^1].GetProperty("assistantContent").GetProperty("valuationGaugeCards");
        Assert.Equal(cards.GetRawText(), replayed.GetRawText());
        Assert.Contains("نسبت فعلی ۶٫۴۳", messages[^1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task AllGauges_ComeInFixedOrder_ForOneCreditPerCard()
    {
        var key = Guid.NewGuid();
        var root = await ValuationGaugeChat.AskAsync(_factory, "گیج‌های ارزش‌گذاری شاراک", key);

        var kinds = root.GetProperty("valuationGaugeCards").GetProperty("cards").EnumerateArray()
            .Select(card => (card.GetProperty("kind").GetString(), card.GetProperty("state").GetString())).ToArray();
        // شاراک has no instrument quote in this store, so the equilibrium card is range-only.
        Assert.Equal([("Pe", "Available"), ("Ps", "Available"), ("Equilibrium", "Partial")], kinds);
        // Change «valuation-gauge-per-card-credits» AC-02, AC-04 (OQ-139): three usable cards, the range-only
        // equilibrium card included, cost three credits.
        Assert.Equal(3m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        var charge = Assert.Single(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge");
        Assert.Equal("ValuationGaugesAnswered.3", charge.CompletionStatus);
        Assert.Equal(3m, charge.CreditsCharged);
        Assert.Contains("قیمت بازار در دسترس نیست", ValuationGaugeChat.Text(root));
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task AllGauges_WithAMissingCard_ChargeOnlyTheCardsShown()
    {
        var key = Guid.NewGuid();
        var root = await ValuationGaugeChat.AskAsync(_factory, "گیج‌های ارزش‌گذاری کگهر", key);

        var states = root.GetProperty("valuationGaugeCards").GetProperty("cards").EnumerateArray()
            .Select(card => (card.GetProperty("kind").GetString(), card.GetProperty("state").GetString())).ToArray();
        // کگهر has no P/E snapshot: two usable cards (AC-03, OQ-138).
        Assert.Equal("Ps", states[1].Item1);
        Assert.Equal(2, states.Count(state => state.Item2 is "Available" or "Partial"));
        Assert.Equal(2m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Equal("ValuationGaugesAnswered.2",
            Assert.Single(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge").CompletionStatus);
    }

    [Fact]
    public async Task AllGauges_WithTooSmallABalance_AreRefusedBeforeAnyGaugeRead()
    {
        var key = Guid.NewGuid();
        var balances = _factory.SetWalletBalances(2m);
        try
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
            using var response = await client.PostAsJsonAsync("/api/ai/v1/query",
                new { message = "گیج‌های ارزش‌گذاری شاراک", idempotencyKey = key }, CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

            // AC-05 (OQ-140): the existing insufficient-credit outcome, no card, no gauge read, nothing charged.
            Assert.True(response.StatusCode == HttpStatusCode.PaymentRequired, $"{(int)response.StatusCode}: {body}");
            Assert.DoesNotContain("valuationGaugeCards\":{", body);
            Assert.False(_factory.Reads.Read<CyclicalWavesV2GaugeDetailRow>());
            Assert.False(_factory.Reads.Read<CyclicalWavesV2EquilibriumRangeRow>());
            Assert.DoesNotContain(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge" && entry.CreditsCharged > 0);
            Assert.All(_factory.ReadWallets(), wallet => Assert.Equal((2m, 0m), (wallet.Balance, wallet.ReservedAmount)));
        }
        finally
        {
            _factory.RestoreWalletBalances(balances);
        }
    }

    [Theory]
    [InlineData("بازۀ قیمت تعادلی اخابر")]
    [InlineData("گیج قیمت تعادلی اخابر")]
    public async Task EquilibriumCard_UsesTheCanonicalQuoteWithItsOwnSourceAndTime(string question)
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, question);

        var card = Assert.Single(root.GetProperty("valuationGaugeCards").GetProperty("cards").EnumerateArray().ToArray());
        Assert.Equal("Equilibrium", card.GetProperty("kind").GetString());
        Assert.Equal("Available", card.GetProperty("state").GetString());
        var facts = card.GetProperty("equilibrium");
        Assert.Equal("2488.15", facts.GetProperty("lower").GetString());
        Assert.Equal("4120", facts.GetProperty("quote").GetProperty("price").GetString()); // last traded price
        Assert.Equal("LatestDailyFallback", facts.GetProperty("quote").GetProperty("sourceLabel").GetString());
        Assert.Equal("LowerHalf", facts.GetProperty("position").GetString());
        Assert.NotEqual(card.GetProperty("fetchedAtUtc").GetString(), facts.GetProperty("quote").GetProperty("asOf").GetString());
        Assert.Equal(1m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    [Fact]
    public async Task BareEquilibriumPhrase_KeepsItsCurrentRoute()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "قیمت تعادلی اخابر");

        Assert.NotEqual("valuation_gauges", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(root.TryGetProperty("valuationGaugeCards", out var cards) && cards.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task SinglePsCard_ForKgahar()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "گیج P/S کگهر");

        var card = Assert.Single(root.GetProperty("valuationGaugeCards").GetProperty("cards").EnumerateArray().ToArray());
        Assert.Equal("Ps", card.GetProperty("kind").GetString());
        Assert.Equal("1.93", card.GetProperty("ratio").GetProperty("current").GetString());
    }

    [Theory]
    [InlineData("گیج P/E اخابر")]   // not in the vendor P/E catalog
    [InlineData("گیج P/E کهنه")]    // only a stale snapshot
    public async Task NoUsableCard_IsOneOrdinaryFreeReply(string question)
    {
        var key = Guid.NewGuid();
        var root = await ValuationGaugeChat.AskAsync(_factory, question, key);

        Assert.Equal("valuation_gauges", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.True(!root.TryGetProperty("valuationGaugeCards", out var cards) || cards.ValueKind == JsonValueKind.Null);
        Assert.Contains("دادهٔ قابل نمایش وجود ندارد", ValuationGaugeChat.Text(root));
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.DoesNotContain(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge" && entry.CreditsCharged > 0);
    }

    [Fact]
    public async Task ARetriedRequestSettlesOnce()
    {
        var key = Guid.NewGuid();
        var first = await ValuationGaugeChat.AskAsync(_factory, "گیج P/E شاراک", key);
        await ValuationGaugeChat.AskAsync(_factory, "گیج P/E شاراک", key);

        Assert.Equal(1m, first.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Single(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge");
    }

    [Fact]
    public async Task ARetriedAllGaugesRequestSettlesOnceAtThreeCredits()
    {
        var key = Guid.NewGuid();
        var first = await ValuationGaugeChat.AskAsync(_factory, "گیج‌های ارزش‌گذاری شاراک", key);
        var second = await ValuationGaugeChat.AskAsync(_factory, "گیج‌های ارزش‌گذاری شاراک", key);

        // AC-07: the replay returns the first settlement; the raised hold is never upgraded again.
        Assert.Equal(3m, first.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Equal(3m, second.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Equal(3m, Assert.Single(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge").CreditsCharged);
    }

    [Fact]
    public async Task AGaugeRequestWithoutSymbol_AsksForTheSymbol_AndIsFree()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "گیج‌های ارزش‌گذاری");

        Assert.True(root.GetProperty("clarificationRequired").GetBoolean(), root.ToString());
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    [Fact]
    public async Task AvailabilityEndpoint_ReportsTheMenuShortcut() =>
        Assert.True((await ValuationGaugeChat.AvailabilityAsync(_factory)).GetProperty("valuationGaugeCards").GetBoolean());

    [Fact]
    public async Task OrdinaryRatioLookup_KeepsItsRoute() =>
        Assert.Equal("symbol_metric_lookup",
            (await ValuationGaugeChat.AskAsync(_factory, "P/E فولاد چقدر است؟")).GetProperty("semanticCapabilityCode").GetString());
}

/// <summary>AC-07 / AC-09: with the flag off, today's routes answer and the shortcut is hidden.</summary>
public sealed class ValuationGaugeCardsFlagOffE2ETests : IClassFixture<ValuationGaugeCardsFlagOffApiFactory>
{
    private readonly ValuationGaugeCardsFlagOffApiFactory _factory;

    public ValuationGaugeCardsFlagOffE2ETests(ValuationGaugeCardsFlagOffApiFactory factory)
    {
        _factory = factory;
        factory.EnsureGaugeSeeded();
        factory.ResetRecorders();
    }

    [Theory]
    [InlineData("گیج P/E شاراک")]
    [InlineData("گیج‌های ارزش‌گذاری شاراک")]
    public async Task GaugePhrases_KeepTheExistingRoute(string question)
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, question);

        Assert.NotEqual("valuation_gauges", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.True(!root.TryGetProperty("valuationGaugeCards", out var cards) || cards.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task AvailabilityEndpoint_HidesTheMenuShortcut() =>
        Assert.False((await ValuationGaugeChat.AvailabilityAsync(_factory)).GetProperty("valuationGaugeCards").GetBoolean());
}

/// <summary>AC-07: NoavaranAmin base provider with valuation_gauges mapped to CyclicalWaves; ISIN matching.</summary>
public sealed class ValuationGaugeCardsNoavaranMappedE2ETests : IClassFixture<ValuationGaugeCardsNoavaranMappedApiFactory>
{
    private readonly ValuationGaugeCardsNoavaranMappedApiFactory _factory;

    public ValuationGaugeCardsNoavaranMappedE2ETests(ValuationGaugeCardsNoavaranMappedApiFactory factory)
    {
        _factory = factory;
        factory.EnsureGaugeSeeded();
        factory.ResetRecorders();
    }

    [Fact]
    public async Task TheNoavaranCompanyIsMatchedToTheVendorTickerByIsin()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "گیج P/E شاراک");

        Assert.Equal("valuation_gauges", root.GetProperty("semanticCapabilityCode").GetString());
        var card = Assert.Single(root.GetProperty("valuationGaugeCards").GetProperty("cards").EnumerateArray().ToArray());
        Assert.Equal("6.4291", card.GetProperty("ratio").GetProperty("current").GetString());
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task AnAmbiguousIsinIsACoverageMiss()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "گیج P/E دوقلو");

        Assert.Equal("valuation_gauges", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Contains("تطبیق یکتای نماد", ValuationGaugeChat.Text(root));
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    [Fact]
    public async Task AvailabilityEndpoint_ReportsTheMenuShortcut() =>
        Assert.True((await ValuationGaugeChat.AvailabilityAsync(_factory)).GetProperty("valuationGaugeCards").GetBoolean());
}

internal static class ValuationGaugeChat
{
    public static async Task<JsonElement> AskAsync(WebApplicationFactory<Program> factory, string message, Guid? idempotencyKey = null)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query",
            new { message, idempotencyKey = idempotencyKey ?? Guid.NewGuid() }, CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public static async Task<JsonElement> AvailabilityAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.GetAsync("/api/ai/v1/capabilities/availability", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None)).RootElement.Clone();
    }

    public static string Text(JsonElement root) => root.GetProperty("textAnswer").GetString() ?? string.Empty;
}

public class ValuationGaugeCardsApiFactory : Feature148CyclicalWavesChatApiFactory
{
    private readonly object _gaugeSeedLock = new();
    private bool _gaugeSeeded;

    protected virtual bool CardsEnabled => true;
    protected virtual string? GaugeSource => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Read while services are composed, so they must be host settings.
        builder.UseSetting("ValuationGaugeCards:Enabled", CardsEnabled ? "true" : "false");
        if (GaugeSource is not null)
            builder.UseSetting("FinancialData:CapabilitySources:valuation_gauges", GaugeSource);
    }

    public void EnsureGaugeSeeded()
    {
        EnsureSeeded();
        if (_gaugeSeeded) return;
        lock (_gaugeSeedLock)
        {
            if (_gaugeSeeded) return;
            ValuationGaugeSeed.Seed(Services);
            _gaugeSeeded = true;
        }

        ResetRecorders();
    }

    /// <summary>Sets every wallet to <paramref name="balance"/> and returns the previous balances.</summary>
    public IReadOnlyDictionary<Guid, decimal> SetWalletBalances(decimal balance)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialCopilot.Infrastructure.Billing.Persistence.BillingDbContext>();
        var previous = db.WalletProjections.ToDictionary(row => row.CustomerAccountId, row => row.Balance);
        foreach (var wallet in db.WalletProjections) wallet.Balance = balance;
        db.SaveChanges();
        return previous;
    }

    public void RestoreWalletBalances(IReadOnlyDictionary<Guid, decimal> balances)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialCopilot.Infrastructure.Billing.Persistence.BillingDbContext>();
        foreach (var wallet in db.WalletProjections)
            if (balances.TryGetValue(wallet.CustomerAccountId, out var balance)) wallet.Balance = balance;
        db.SaveChanges();
    }

    public IReadOnlyCollection<FinancialCopilot.Infrastructure.Billing.Persistence.WalletProjectionRow> ReadWallets()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<FinancialCopilot.Infrastructure.Billing.Persistence.BillingDbContext>()
            .WalletProjections.AsNoTracking().ToList();
    }
}

public sealed class ValuationGaugeCardsFlagOffApiFactory : ValuationGaugeCardsApiFactory
{
    protected override bool CardsEnabled => false;
}

public sealed class ValuationGaugeCardsNoavaranMappedApiFactory : ValuationGaugeCardsApiFactory
{
    protected override string ChatProvider => "NoavaranAmin";
    protected override string? GaugeSource => "CyclicalWaves";
}

/// <summary>Gauge fixtures modelled on the local Feature 148 sample (evidence/feature148-local-sample-output.txt).</summary>
internal static class ValuationGaugeSeed
{
    public static void Seed(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        SeedAsync(db).GetAwaiter().GetResult();
    }

    private static async Task SeedAsync(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var store = new EfCoreCyclicalWavesV2Store(db, NullLogger<EfCoreCyclicalWavesV2Store>.Instance);
        await store.UpsertCatalogAsync(["فولاد", "فملی", "شستا", "کگل", "وبملت", "کچاد", "شاراک", "کگهر", "اخابر", "کهنه", "دوقلو", "دوقلوب"], now, CancellationToken.None);
        var tickers = await db.CyclicalWavesV2Tickers.ToDictionaryAsync(row => row.Ticker);
        var isins = new Dictionary<string, string>
        {
            ["شاراک"] = "IRO1PARK0001", ["کگهر"] = "IRO3GZIZ0001", ["اخابر"] = "IRO1MKBT0001", ["کهنه"] = "IRO1OLDD0001",
            ["دوقلو"] = "IRO1TWIN0001", ["دوقلوب"] = " iro1twin0001 "
        };
        foreach (var (ticker, isin) in isins) tickers[ticker].Enticker = isin;

        db.CyclicalWavesV2GaugeAvailability.AddRange(
            Availability(tickers["شاراک"].Id, true, true, now),
            Availability(tickers["کگهر"].Id, true, true, now),
            Availability(tickers["اخابر"].Id, false, false, now),
            Availability(tickers["کهنه"].Id, true, true, now),
            Availability(tickers["دوقلو"].Id, true, true, now),
            Availability(tickers["دوقلوب"].Id, true, true, now));
        db.CyclicalWavesV2GaugeDetails.AddRange(
            Gauge(tickers["شاراک"].Id, "PE", "6.4291", "2.106", "59.61", "3.348", "5.88", "9.696", 3, now.AddHours(-2)),
            Gauge(tickers["شاراک"].Id, "PS", "0.7405", "0.21", "4.9", "0.35", "0.61", "1.42", 2, now.AddHours(-2)),
            Gauge(tickers["کگهر"].Id, "PS", "1.93", "0.4", "9.5", "0.9", "2.2", "3.8", 2, now.AddHours(-2)),
            Gauge(tickers["کهنه"].Id, "PE", "4", "1", "10", "2", "4.5", "8", 2, now.AddDays(-5)),
            Gauge(tickers["دوقلو"].Id, "PE", "4", "1", "10", "2", "4.5", "8", 2, now.AddHours(-1)),
            Gauge(tickers["دوقلوب"].Id, "PE", "5", "1", "10", "2", "4.5", "8", 2, now.AddHours(-1)));
        db.CyclicalWavesV2EquilibriumRanges.AddRange(
            Range(tickers["شاراک"].Id, "15423.41", "44331.97", "73240.53", now.AddHours(-1)),
            Range(tickers["اخابر"].Id, "2488.15", "4713.35", "6938.56", now.AddHours(-1)),
            Range(tickers["کگهر"].Id, "1500", "2000", "2500", now.AddHours(-1)));

        // The canonical quote for اخابر's instrument (same ISIN); the latest daily trade, since nothing traded today.
        var instrumentId = Guid.NewGuid();
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "StockMarketDb",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 159_001,
            Symbol = "اخابر",
            Name = "مخابرات ایران",
            InstrumentIsin = "IRO1MKBT0001",
            IsActive = true,
            SourceChangedAt = now,
            LastSynchronizedAt = now
        });
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3.5)).DateTime);
        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = today.AddDays(-1),
            ClosingPrice = 4_100m,
            LastTradedPrice = 4_120m,
            PriceChange = 20m,
            PriceYesterday = 4_080m,
            SourceInsertedAt = now.AddDays(-1)
        });

        // NoavaranAmin companies for the mapped mode; دوقلو's ISIN matches two active vendor tickers.
        db.Companies.AddRange(
            Company("15900000-0000-0000-0000-000000000001", "شاراک", "پارس", "IRO1PARK0001", now),
            Company("15900000-0000-0000-0000-000000000002", "دوقلو", "دوقلو", "IRO1TWIN0001", now));
        await db.SaveChangesAsync();
    }

    private static CyclicalWavesV2GaugeAvailabilityRow Availability(Guid tickerId, bool pe, bool ps, DateTimeOffset now) =>
        new() { TickerId = tickerId, HasPe = pe, HasPs = ps, LastFetchedAtUtc = now };

    private static CyclicalWavesV2GaugeDetailRow Gauge(Guid tickerId, string type, string? close, string start, string end,
        string min, string avg, string max, int? status, DateTimeOffset fetched) => new()
    {
        TickerId = tickerId, GaugeType = type, A = 120, B = 340, C = 410, D = 260, E = 150, F = 20,
        Close = close is null ? null : ExactDecimal.Parse(close), Start = ExactDecimal.Parse(start), End = ExactDecimal.Parse(end),
        Min = ExactDecimal.Parse(min), Avg = ExactDecimal.Parse(avg), Max = ExactDecimal.Parse(max), Status = status, FetchedAtUtc = fetched
    };

    private static CyclicalWavesV2EquilibriumRangeRow Range(Guid tickerId, string min, string avg, string max, DateTimeOffset fetched) => new()
    {
        TickerId = tickerId, HasRange = true, Min = ExactDecimal.Parse(min), Avg = ExactDecimal.Parse(avg), Max = ExactDecimal.Parse(max),
        DetailsFetchedAtUtc = fetched, FetchedAtUtc = fetched
    };

    private static NormalizedCompanyRow Company(string id, string symbol, string name, string isin, DateTimeOffset now) => new()
    {
        Id = Guid.Parse(id),
        ProviderName = ProviderSources.NoavaranCurrentApiName,
        ExternalCompanyId = "noavaran-" + symbol,
        CompanySymbol = symbol,
        TseSymbol = symbol,
        Name = name,
        SymbolIsin = isin,
        LastSynchronizedAt = now
    };
}

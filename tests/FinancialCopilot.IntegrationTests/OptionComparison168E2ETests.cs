using System.Text.Json;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 168 end to end on the V2 path: an option question is routed by the dialogue gate,
/// answered from stored option data, charged one credit, persisted and replayed. No provider or
/// model call is made on this path.
/// </summary>
public sealed class OptionComparison168E2ETests : IClassFixture<OptionAnswersApiFactory>
{
    private readonly OptionAnswersApiFactory _factory;

    public OptionComparison168E2ETests(OptionAnswersApiFactory factory)
    {
        _factory = factory;
        factory.EnsureOptionsSeeded();
        factory.ResetRecorders();
    }

    [Fact]
    public async Task ACallQuestion_IsAnsweredWithTheComparison_ForOneCredit_AndReplayedFromHistory()
    {
        var key = Guid.NewGuid();
        var root = await ValuationGaugeChat.AskAsync(_factory, "اختیار خرید شاراک", key);

        Assert.Equal("option_comparison", root.GetProperty("semanticCapabilityCode").GetString());
        var comparison = root.GetProperty("optionComparison");
        Assert.Equal("Call", comparison.GetProperty("requestedView").GetString());
        Assert.Equal("شاراک", comparison.GetProperty("underlyingSymbol").GetString());
        Assert.Equal("Share", comparison.GetProperty("underlyingClass").GetString());
        Assert.True(comparison.GetProperty("orderBookIsCurrent").GetBoolean());
        var callRows = Rows(comparison, "Call");
        Assert.Equal(["ضشارا1", "ضشارا2"], callRows.Select(row => row.GetProperty("symbol").GetString()));
        Assert.Equal("Fillable", callRows[0].GetProperty("state").GetString());
        // strike 1,000 × 1.0005 + ask 544 × 1.001
        Assert.Equal(1545.044m, callRows[0].GetProperty("effectivePurchaseCost").GetDecimal());
        Assert.Equal(["طشارا1"], Rows(comparison, "Put").Select(row => row.GetProperty("symbol").GetString()));

        var text = ValuationGaugeChat.Text(root);
        Assert.StartsWith("مقایسهٔ هزینهٔ خرید از مسیر اختیار خرید — شاراک", text);
        Assert.Contains("ضشارا1", text);

        Assert.Equal(1m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        var charge = Assert.Single(_factory.ReadUsageEntries(), entry => entry.IdempotencyKey == $"{key:N}:charge");
        Assert.Equal("OptionComparisonAnswered", charge.CompletionStatus);
        Assert.Empty(_factory.Fake.ToolCalls);
        Feature148Chat.NoOutboundCalls(_factory.Http);

        var conversationId = root.GetProperty("conversationId").GetGuid();
        var messages = (await Feature148Chat.MessagesAsync(_factory, conversationId)).GetProperty("messages").EnumerateArray().ToArray();
        var replayed = messages[^1].GetProperty("assistantContent").GetProperty("optionComparison");
        Assert.Equal(comparison.GetRawText(), replayed.GetRawText());
    }

    [Theory]
    [InlineData("اختیار فروش شاراک", "Put")]
    [InlineData("کاورد کال شاراک", "CoveredCall")]
    [InlineData("زنجیره اختیار شاراک", "Call")]
    public async Task TheWordingSelectsTheView(string question, string view)
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, question);

        Assert.Equal("option_comparison", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal(view, root.GetProperty("optionComparison").GetProperty("requestedView").GetString());
    }

    [Fact]
    public async Task QuantityAndExpiry_ComeFromTheQuestion()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "اختیار خرید شاراک 15 قرارداد نزدیک‌ترین سررسید");

        var comparison = root.GetProperty("optionComparison");
        Assert.Equal(15, comparison.GetProperty("quantityInContracts").GetInt32());
        // asks 544 × 10 and 550 × 10 → 15 contracts fill at 546
        Assert.Equal(546m, Rows(comparison, "Call")[0].GetProperty("fillPrice").GetDecimal());
        Assert.Single(comparison.GetProperty("views").EnumerateArray().First().GetProperty("expiries").EnumerateArray().ToArray());
    }

    [Fact]
    public async Task AQuestionNamingOneContract_IsAnsweredForItsUnderlyingAndExpiry_WithTheRowMarked()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "اختیار خرید ضشارا3");

        Assert.Equal("option_comparison", root.GetProperty("semanticCapabilityCode").GetString());
        var comparison = root.GetProperty("optionComparison");
        Assert.Equal("شاراک", comparison.GetProperty("underlyingSymbol").GetString());
        var row = Assert.Single(Rows(comparison, "Call"));
        Assert.Equal("ضشارا3", row.GetProperty("symbol").GetString());
        Assert.True(row.GetProperty("isRequestedContract").GetBoolean());
        Assert.Contains("ضشارا3", ValuationGaugeChat.Text(root));
    }

    [Fact]
    public async Task AnUnknownSymbol_GetsTheExistingUnknownSymbolAnswer_AndIsNotCharged()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "اختیار خرید ناشناسنماد");

        Assert.True(!root.TryGetProperty("optionComparison", out var payload) || payload.ValueKind == JsonValueKind.Null);
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    [Fact]
    public async Task ASymbolWithoutOptions_GetsOneSentence_AndIsNotCharged()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "اختیار خرید فولاد");

        Assert.Equal("option_comparison", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal("برای فولاد قرارداد اختیار معاملهٔ فعالی ثبت نشده است.", ValuationGaugeChat.Text(root));
        Assert.True(!root.TryGetProperty("optionComparison", out var payload) || payload.ValueKind == JsonValueKind.Null);
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    [Theory]
    [InlineData("اختیار خرید شاراک بخرم؟")]
    [InlineData("شاراک بخرم؟")]
    [InlineData("قیمت شاراک")]
    public async Task OtherQuestions_KeepTheirRoute(string question)
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, question);

        Assert.NotEqual("option_comparison", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.True(!root.TryGetProperty("optionComparison", out var payload) || payload.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task AvailabilityEndpoint_ReportsTheCapability() =>
        Assert.True((await ValuationGaugeChat.AvailabilityAsync(_factory)).GetProperty("optionComparison").GetBoolean());

    private static JsonElement[] Rows(JsonElement comparison, string view) =>
        comparison.GetProperty("views").EnumerateArray()
            .Single(item => item.GetProperty("view").GetString() == view)
            .GetProperty("expiries").EnumerateArray()
            .First()
            .GetProperty("rows").EnumerateArray()
            .ToArray();
}

/// <summary>With the flag off an option question keeps today's routing and the entry points stay hidden.</summary>
public sealed class OptionComparison168FlagOffE2ETests : IClassFixture<OptionAnswersFlagOffApiFactory>
{
    private readonly OptionAnswersFlagOffApiFactory _factory;

    public OptionComparison168FlagOffE2ETests(OptionAnswersFlagOffApiFactory factory)
    {
        _factory = factory;
        factory.EnsureOptionsSeeded();
        factory.ResetRecorders();
    }

    [Fact]
    public async Task OptionPhrases_KeepTheExistingRoute()
    {
        var root = await ValuationGaugeChat.AskAsync(_factory, "اختیار خرید شاراک");

        Assert.NotEqual("option_comparison", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.True(!root.TryGetProperty("optionComparison", out var payload) || payload.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task AvailabilityEndpoint_HidesTheCapability() =>
        Assert.False((await ValuationGaugeChat.AvailabilityAsync(_factory)).GetProperty("optionComparison").GetBoolean());
}

/// <summary>
/// NoavaranAmin chat provider with gauges mapped to CyclicalWaves, the composition the other
/// gauge-based factories use without tripping BUG-053.
/// </summary>
public class OptionAnswersApiFactory : ValuationGaugeCardsApiFactory
{
    private readonly object _optionSeedLock = new();
    private bool _optionsSeeded;

    protected override string ChatProvider => "NoavaranAmin";
    protected override string? GaugeSource => "CyclicalWaves";
    protected virtual bool OptionAnswersEnabled => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Read while services are composed, so it must be a host setting.
        builder.UseSetting("OptionAnswers:Enabled", OptionAnswersEnabled ? "true" : "false");
    }

    public void EnsureOptionsSeeded()
    {
        EnsureGaugeSeeded();
        if (_optionsSeeded) return;
        lock (_optionSeedLock)
        {
            if (_optionsSeeded) return;
            using var scope = Services.CreateScope();
            OptionSeed.Seed(scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>());
            _optionsSeeded = true;
        }

        ResetRecorders();
    }
}

public sealed class OptionAnswersFlagOffApiFactory : OptionAnswersApiFactory
{
    protected override bool OptionAnswersEnabled => false;
}

/// <summary>شاراک at 1,489 with two calls and one put expiring in 11 days, plus a later expiry.</summary>
internal static class OptionSeed
{
    public static void Seed(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3.5)).DateTime);
        var underlying = Instrument(db, 910001, "شاراک", "IRO1PARK0001", now);
        Quote(db, underlying, 1489, today, now);

        Contract(db, 910011, "ضشارا1", "IRO9PARK0A01", "Call", underlying, 1000, today.AddDays(11), now,
            (1, 500m, 10, 544m, 10), (2, 490m, 10, 550m, 10));
        Contract(db, 910012, "ضشارا2", "IRO9PARK0A11", "Call", underlying, 1400, today.AddDays(11), now,
            (1, 170m, 20, 178m, 30));
        Contract(db, 910013, "طشارا1", "IROFPARK0A01", "Put", underlying, 1400, today.AddDays(11), now,
            (1, 15m, 8, 20m, 30));
        Contract(db, 910014, "ضشارا3", "IRO9PARK0B01", "Call", underlying, 1500, today.AddDays(46), now,
            (1, 200m, 10, 230m, 30));

        db.StockMarketSyncStates.Add(new StockMarketSyncStateRow
        {
            Dataset = "Tsetmc_OptionOrderBook", Watermark = now, LastRunStartedAt = now, LastRunCompletedAt = now
        });
        db.SaveChanges();
    }

    private static Guid Instrument(FinancialIngestionDbContext db, long code, string symbol, string isin, DateTimeOffset now)
    {
        var row = new TradingInstrumentRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = code, Symbol = symbol, InstrumentIsin = isin, IsActive = true, LastSynchronizedAt = now
        };
        db.TradingInstruments.Add(row);
        return row.Id;
    }

    private static void Quote(FinancialIngestionDbContext db, Guid instrumentId, decimal price, DateOnly tradingDate, DateTimeOffset now) =>
        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", TradingInstrumentId = instrumentId,
            LatestPrice = price, SourceKind = "Intraday", TradingDate = tradingDate, AsOf = now
        });

    private static void Contract(
        FinancialIngestionDbContext db, long code, string symbol, string isin, string side, Guid underlying,
        decimal strike, DateOnly expiry, DateTimeOffset now,
        params (short Level, decimal Bid, long BidVolume, decimal Ask, long AskVolume)[] levels)
    {
        var instrument = Instrument(db, code, symbol, isin, now);
        db.OptionContracts.Add(new OptionContractRow
        {
            Id = Guid.NewGuid(), TradingInstrumentId = instrument, UnderlyingInstrumentId = underlying, Side = side,
            StrikePrice = strike, ExpiryDate = expiry, BeginDate = expiry.AddDays(-90), ContractSize = 1000,
            OpenInterest = 100, PreviousOpenInterest = 90, OpenInterestObservedAt = now, FirstSeenAt = now, LastSynchronizedAt = now
        });
        foreach (var level in levels)
            db.OptionOrderBookLevels.Add(new OptionOrderBookLevelRow
            {
                TradingInstrumentId = instrument, Level = level.Level, BidPrice = level.Bid, BidVolume = level.BidVolume,
                BidOrderCount = 1, AskPrice = level.Ask, AskVolume = level.AskVolume, AskOrderCount = 1,
                ProviderTime = new TimeOnly(10, 0), ObservedAt = now
            });
    }
}

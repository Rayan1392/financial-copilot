using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class ConditionalTrackerEndpointTests : IClassFixture<FollowedSymbolsApiFactory>
{
    private readonly FollowedSymbolsApiFactory _factory;

    public ConditionalTrackerEndpointTests(FollowedSymbolsApiFactory factory)
    {
        _factory = factory;
        factory.EnsureBillingSeeded();
        factory.EnsureSeeded();
        ResetTrackerData();
    }

    [Fact]
    public async Task StructuredCreateAndList_AreActorScoped_AndPlanGoverned()
    {
        using var owner = UserClient();
        using var create = await owner.PostAsJsonAsync("/api/v1/trackers/me", StructuredRule(), CancellationToken.None);
        using var ownerList = await owner.GetAsync("/api/v1/trackers/me", CancellationToken.None);
        using var ownerJson = await ReadJsonAsync(ownerList);
        var ownerRule = Assert.Single(ownerJson.RootElement.GetProperty("items").EnumerateArray());

        using var peer = _factory.CreateClient();
        peer.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var peerList = await peer.GetAsync("/api/v1/trackers/me", CancellationToken.None);
        using var peerJson = await ReadJsonAsync(peerList);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("Active", ownerRule.GetProperty("state").GetString());
        Assert.Equal("100", ownerRule.GetProperty("externalCompanyId").GetString());
        Assert.Empty(peerJson.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task PersianNaturalLanguageRule_RemainsDraftUntilVersionedConfirmation()
    {
        using var client = UserClient();
        using var create = await client.PostAsJsonAsync(
            "/api/v1/trackers/me",
            new
            {
                externalCompanyId = "100",
                naturalLanguageText = "اگر قیمت کمتر از ۱۲۵۰ تومان شد هشدار بده",
                idempotencyKey = "nl-price-100"
            },
            CancellationToken.None);
        using var draft = await ReadJsonAsync(create);
        var id = draft.RootElement.GetProperty("id").GetGuid();
        var version = draft.RootElement.GetProperty("version").GetInt32();
        var token = draft.RootElement.GetProperty("confirmationToken").GetString();

        using var confirm = await client.PostAsJsonAsync(
            $"/api/v1/trackers/me/{id}/confirm",
            new { expectedVersion = version, confirmationToken = token },
            CancellationToken.None);
        using var confirmed = await ReadJsonAsync(confirm);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("Draft", draft.RootElement.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal("Active", confirmed.RootElement.GetProperty("state").GetString());
        Assert.Equal(GovernedAlertRuleParser.Version, confirmed.RootElement.GetProperty("parserVersion").GetString());
    }

    [Fact]
    public async Task SymbolAlias_IsResolvedToCanonicalExternalCompanyId()
    {
        using var client = UserClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/trackers/me",
            new
            {
                externalCompanyId = "FOO",
                ruleType = "Price",
                metricOrEventCode = "LATEST_PRICE",
                @operator = "GreaterThan",
                threshold = 100m,
                unit = "Rial",
                recurrence = "Recurring",
                cooldownMinutes = 0,
                resetPolicy = "CrossBack",
                sessionPolicy = "Any",
                confirmImmediately = true,
                idempotencyKey = "alias-foo"
            },
            CancellationToken.None);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("100", json.RootElement.GetProperty("externalCompanyId").GetString());
        Assert.Equal("FOO", json.RootElement.GetProperty("symbol").GetString());
    }

    [Fact]
    public async Task DefaultRuleLimit_IsEnforcedWithoutChargingUsageLedger()
    {
        using var client = UserClient();
        for (var index = 0; index < 5; index++)
        {
            using var accepted = await client.PostAsJsonAsync(
                "/api/v1/trackers/me",
                new
                {
                    externalCompanyId = "100",
                    ruleType = "Price",
                    metricOrEventCode = "LATEST_PRICE",
                    @operator = "GreaterThan",
                    threshold = 100m + index,
                    unit = "Rial",
                    recurrence = "Recurring",
                    cooldownMinutes = 0,
                    resetPolicy = "CrossBack",
                    sessionPolicy = "Any",
                    confirmImmediately = true,
                    idempotencyKey = $"free-limit-{index}"
                },
                CancellationToken.None);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync(
            "/api/v1/trackers/me",
            new
            {
                externalCompanyId = "100",
                ruleType = "Price",
                metricOrEventCode = "LATEST_PRICE",
                @operator = "GreaterThan",
                threshold = 200m,
                unit = "Rial",
                recurrence = "Recurring",
                cooldownMinutes = 0,
                resetPolicy = "CrossBack",
                sessionPolicy = "Any",
                confirmImmediately = true,
                idempotencyKey = "free-limit-rejected"
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task PriceCrossing_EvaluatorCreatesOneTriggerAndOneNotificationIntent()
    {
        SeedQuote(110m, DateTimeOffset.Parse("2026-07-10T08:00:00Z"));
        using var client = UserClient();
        using var create = await client.PostAsJsonAsync(
            "/api/v1/trackers/me",
            StructuredRule(sessionPolicy: "Any", cooldownMinutes: 0),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IConditionalTrackerEvaluationProcessor>();
            var first = await processor.EvaluateDueAsync(10, CancellationToken.None);
            Assert.Equal(0, first.Triggered);
        }

        UpdateQuote(90m, DateTimeOffset.Parse("2026-07-10T08:01:00Z"));
        using (var firstScope = _factory.Services.CreateScope())
        using (var secondScope = _factory.Services.CreateScope())
        {
            var firstProcessor = firstScope.ServiceProvider.GetRequiredService<IConditionalTrackerEvaluationProcessor>();
            var secondProcessor = secondScope.ServiceProvider.GetRequiredService<IConditionalTrackerEvaluationProcessor>();
            var concurrent = await Task.WhenAll(
                firstProcessor.EvaluateDueAsync(10, CancellationToken.None),
                secondProcessor.EvaluateDueAsync(10, CancellationToken.None));
            Assert.Equal(1, concurrent.Sum(result => result.Triggered));
        }

        using (var replayScope = _factory.Services.CreateScope())
        {
            var replayProcessor = replayScope.ServiceProvider.GetRequiredService<IConditionalTrackerEvaluationProcessor>();
            var replay = await replayProcessor.EvaluateDueAsync(10, CancellationToken.None);
            Assert.Equal(0, replay.Triggered);
        }

        using var assertionScope = _factory.Services.CreateScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var trigger = Assert.Single(db.AlertRuleTriggers);
        var intent = Assert.Single(db.NotificationIntents.Where(row => row.EventType == "ConditionalTrackerTriggered"));
        Assert.Equal(intent.Id, trigger.NotificationIntentId);
        Assert.Contains("quote:", trigger.EvidenceIdentity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrackersWithoutCredentials_ReturnUnauthorized()
    {
        using var response = await _factory.CreateClient().GetAsync("/api/v1/trackers/me", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private object StructuredRule(string sessionPolicy = "TradingSessionOnly", int cooldownMinutes = 30) => new
    {
        externalCompanyId = "100",
        ruleType = "Price",
        metricOrEventCode = "LATEST_PRICE",
        @operator = "CrossesBelow",
        threshold = 100m,
        unit = "Rial",
        recurrence = "Recurring",
        cooldownMinutes,
        resetPolicy = "CrossBack",
        sessionPolicy,
        confirmImmediately = true,
        idempotencyKey = $"price-cross-{sessionPolicy}-{cooldownMinutes}"
    };

    private HttpClient UserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private void ResetTrackerData()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.AlertRuleTriggers.RemoveRange(db.AlertRuleTriggers);
        db.AlertRuleEvaluationStates.RemoveRange(db.AlertRuleEvaluationStates);
        db.AlertRules.RemoveRange(db.AlertRules);
        db.NotificationIntents.RemoveRange(db.NotificationIntents);
        db.LatestMarketQuotes.RemoveRange(db.LatestMarketQuotes);
        db.TradingInstruments.RemoveRange(db.TradingInstruments);
        db.SaveChanges();
    }

    private void SeedQuote(decimal price, DateTimeOffset asOf)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var companyId = db.Companies.Single(row => row.ExternalCompanyId == "100").Id;
        var instrument = new TradingInstrumentRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 100,
            InstrumentIsin = "IRO1FOO0001",
            Symbol = "FOO",
            Name = "Foo Company",
            MarketCode = "TSE",
            InstrumentKind = "Share",
            NormalizedCompanyId = companyId,
            IsActive = true,
            SourceChangedAt = asOf,
            LastSynchronizedAt = asOf
        };
        db.TradingInstruments.Add(instrument);
        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            TradingInstrumentId = instrument.Id,
            LatestPrice = price,
            PriceChangePercentage = 0m,
            SourceKind = "Intraday",
            TradingDate = DateOnly.FromDateTime(asOf.UtcDateTime),
            AsOf = asOf
        });
        db.SaveChanges();
    }

    private void UpdateQuote(decimal price, DateTimeOffset asOf)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var quote = db.LatestMarketQuotes.Single();
        quote.LatestPrice = price;
        quote.AsOf = asOf;
        quote.TradingDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        db.SaveChanges();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
    }
}

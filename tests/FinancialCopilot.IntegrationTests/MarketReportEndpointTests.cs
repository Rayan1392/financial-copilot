using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class MarketReportEndpointTests : IClassFixture<MarketViewApiFactory>
{
    private readonly MarketViewApiFactory _factory;

    public MarketReportEndpointTests(MarketViewApiFactory factory)
    {
        _factory = factory;
        factory.EnsureBillingSeeded();
        factory.EnsureMarketSeeded();
        ResetFeatureState();
    }

    [Fact]
    public async Task PublicReport_IsAnonymousEvidenceBoundIdempotentAndRevisionedOnCorrection()
    {
        using var user = UserClient();
        using var pulseResponse = await user.GetAsync("/api/v1/market-pulse/latest", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, pulseResponse.StatusCode);

        MarketReportView first;
        using (var scope = _factory.Services.CreateScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<IMarketReportService>().GeneratePublicAsync(
                new GeneratePublicMarketReportCommand("all", "integration-public-1"), CancellationToken.None);
        }

        using var anonymous = _factory.CreateClient();
        using var latestResponse = await anonymous.GetAsync("/api/v1/market-reports/latest", CancellationToken.None);
        using var latest = await ReadJsonAsync(latestResponse);
        Assert.Equal(HttpStatusCode.OK, latestResponse.StatusCode);
        Assert.Equal("Fallback", latest.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(latest.RootElement.GetProperty("evidence").GetProperty("items").EnumerateArray());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            var pulseRow = db.MarketPulseSnapshots.OrderByDescending(row => row.GeneratedAtUtc).First();
            pulseRow.BreadthJson = JsonSerializer.Serialize(new MarketPulseBreadth(
                2, 0, 0, 2, 0, MarketPulseFactStatus.Available, null));
            pulseRow.SourceWatermarkUtc = pulseRow.SourceWatermarkUtc?.AddSeconds(2);
            db.SaveChanges();
        }

        MarketReportView corrected;
        using (var scope = _factory.Services.CreateScope())
        {
            corrected = await scope.ServiceProvider.GetRequiredService<IMarketReportService>().GeneratePublicAsync(
                new GeneratePublicMarketReportCommand("all", "integration-public-2"), CancellationToken.None);
        }

        Assert.Equal(first.Revision + 1, corrected.Revision);
        Assert.Equal(first.Id, corrected.SupersedesReportId);
        Assert.NotEqual(first.EvidenceHash, corrected.EvidenceHash);
    }

    [Fact]
    public async Task PersonalDigest_IsActorScopedIdempotentAndReleasesBillingOnFallback()
    {
        using var owner = UserClient();
        using var watchlist = await owner.PutAsJsonAsync(
            "/api/v1/watchlists/me", new { symbols = new[] { "GAIN" } }, CancellationToken.None);
        using var pulse = await owner.GetAsync("/api/v1/market-pulse/latest", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, watchlist.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pulse.StatusCode);

        using var firstResponse = await owner.PostAsJsonAsync(
            "/api/v1/digests/me/generate", new { publishNotification = true }, CancellationToken.None);
        using var first = await ReadJsonAsync(firstResponse);
        using var replayResponse = await owner.PostAsJsonAsync(
            "/api/v1/digests/me/generate", new { publishNotification = true }, CancellationToken.None);
        using var replay = await ReadJsonAsync(replayResponse);

        using var peer = _factory.CreateClient();
        peer.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var peerResponse = await peer.GetAsync("/api/v1/digests/me/latest", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(first.RootElement.GetProperty("id").GetGuid(), replay.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Fallback", first.RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, peerResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var financial = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        Assert.Single(financial.NotificationIntents.Where(row => row.EventType == "PersonalMarketDigestReady"));
        Assert.Single(billing.UsageReservations.Where(row => row.OperationCode == "AiQuery.PersonalDigest" && row.Status == "Released"));
        Assert.Empty(billing.UsageLedgerEntries.Where(row => row.OperationCode == "AiQuery.PersonalDigest"));
    }

    [Fact]
    public async Task PersonalDigest_FreePlanWithoutCapability_IsForbiddenWithoutReservation()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var account = billing.CustomerAccounts.Single(row => row.UserId == AuthenticationApiFactory.UserId);
            account.SubscriptionPlanCode = "Free";
            billing.SaveChanges();
        }
        try
        {
            using var owner = UserClient();
            using var response = await owner.PostAsJsonAsync(
                "/api/v1/digests/me/generate", new { publishNotification = false }, CancellationToken.None);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using var scope = _factory.Services.CreateScope();
            var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            Assert.Empty(billing.UsageReservations.Where(row => row.OperationCode == "AiQuery.PersonalDigest"));
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var account = billing.CustomerAccounts.Single(row => row.UserId == AuthenticationApiFactory.UserId);
            account.SubscriptionPlanCode = null;
            billing.SaveChanges();
        }
    }

    private HttpClient UserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private void ResetFeatureState()
    {
        using var scope = _factory.Services.CreateScope();
        var financial = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        financial.NotificationIntents.RemoveRange(financial.NotificationIntents.Where(row =>
            row.EventType == "PersonalMarketDigestReady"));
        financial.MarketReports.RemoveRange(financial.MarketReports);
        financial.MarketPulseSnapshots.RemoveRange(financial.MarketPulseSnapshots);
        financial.WatchlistSymbols.RemoveRange(financial.WatchlistSymbols);
        financial.SaveChanges();

        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        billing.UsageLedgerEntries.RemoveRange(billing.UsageLedgerEntries.Where(row => row.OperationCode == "AiQuery.PersonalDigest"));
        billing.UsageReservations.RemoveRange(billing.UsageReservations.Where(row => row.OperationCode == "AiQuery.PersonalDigest"));
        var account = billing.CustomerAccounts.Single(row => row.UserId == AuthenticationApiFactory.UserId);
        account.SubscriptionPlanCode = null;
        billing.SaveChanges();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
    }
}

public sealed class SuccessfulMarketReportEndpointTests : IClassFixture<SuccessfulMarketReportApiFactory>
{
    private readonly SuccessfulMarketReportApiFactory _factory;

    public SuccessfulMarketReportEndpointTests(SuccessfulMarketReportApiFactory factory)
    {
        _factory = factory;
        factory.EnsureBillingSeeded();
        factory.EnsureMarketSeeded();
        using var scope = factory.Services.CreateScope();
        var financial = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        financial.MarketReports.RemoveRange(financial.MarketReports);
        financial.MarketPulseSnapshots.RemoveRange(financial.MarketPulseSnapshots);
        financial.WatchlistSymbols.RemoveRange(financial.WatchlistSymbols);
        financial.SaveChanges();
    }

    [Fact]
    public async Task ValidEvidenceCitedNarrative_IsPublishedAndBillingCommittedOnce()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.CreateWebAppToken(includeTenant: true));
        using var watchlist = await client.PutAsJsonAsync(
            "/api/v1/watchlists/me", new { symbols = new[] { "GAIN" } }, CancellationToken.None);
        using var pulse = await client.GetAsync("/api/v1/market-pulse/latest", CancellationToken.None);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/digests/me/generate", new { publishNotification = false }, CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(CancellationToken.None), cancellationToken: CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, watchlist.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pulse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Generated", document.RootElement.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("generatedAtUtc").ValueKind);
        Assert.Contains("[e:", document.RootElement.GetProperty("narrative").GetString());

        using var scope = _factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        Assert.Single(billing.UsageReservations.Where(row =>
            row.OperationCode == "AiQuery.PersonalDigest" && row.Status == "Committed"));
        var ledger = Assert.Single(billing.UsageLedgerEntries.Where(row => row.OperationCode == "AiQuery.PersonalDigest"));
        Assert.Equal(4m, ledger.CreditsCharged);
    }
}

public sealed class SuccessfulMarketReportApiFactory : MarketViewApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelExecutionService>();
            services.AddSingleton<IAiModelExecutionService, EvidenceCitingAiExecutionService>();
        });
    }

    private sealed class EvidenceCitingAiExecutionService : IAiModelExecutionService
    {
        public Task<AiModelResult> ExecuteAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken)
        {
            using var evidence = JsonDocument.Parse(request.Messages.Last().Content);
            var item = evidence.RootElement.GetProperty("items").EnumerateArray()
                .First(element => element.GetProperty("numericValues").GetArrayLength() > 0);
            var id = item.GetProperty("id").GetString()!;
            var number = item.GetProperty("numericValues")[0].GetString()!;
            var usage = new AiExecutionUsageFacts(
                request.CorrelationId, "evidence-test", "evidence-test-v1", AiExecutionStatus.Completed,
                TimeSpan.FromMilliseconds(1), 1, 20, 10);
            return Task.FromResult(new AiModelResult(
                $"مقدار مستند {number} ثبت شده است. [e:{id}]", null, [], usage));
        }
    }
}

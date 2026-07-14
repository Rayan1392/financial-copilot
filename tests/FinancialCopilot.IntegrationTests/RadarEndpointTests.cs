using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class RadarEndpointTests : IClassFixture<FollowedSymbolsApiFactory>
{
    private readonly FollowedSymbolsApiFactory factory;

    public RadarEndpointTests(FollowedSymbolsApiFactory factory)
    {
        this.factory = factory;
        factory.EnsureBillingSeeded();
        factory.EnsureSeeded();
        Reset();
    }

    [Fact]
    public async Task Preferences_and_followed_symbol_override_are_actor_scoped_and_versioned()
    {
        using var client = UserClient();
        using var follow = await client.PostAsync("/api/v1/followed-symbols/me/100", null, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);

        using var create = await client.PutAsJsonAsync("/api/v1/radar/me/preferences", Preferences(0), CancellationToken.None);
        using var created = await Json(create);
        using var addOverride = await client.PutAsJsonAsync("/api/v1/radar/me/symbols/100", new
        {
            expectedVersion = 0,
            state = "Active",
            eventTypes = new[] { "LargeTradeDetected" },
            minimumSeverity = "Important",
            minimumImportance = 80m,
            sensitivity = "Focused"
        }, CancellationToken.None);
        using var overridden = await Json(addOverride);
        using var stale = await client.PutAsJsonAsync("/api/v1/radar/me/preferences", Preferences(0), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(1, created.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("Active", created.RootElement.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, addOverride.StatusCode);
        var symbolOverride = Assert.Single(overridden.RootElement.GetProperty("symbolOverrides").EnumerateArray());
        Assert.Equal("100", symbolOverride.GetProperty("externalCompanyId").GetString());
        Assert.Equal("Focused", symbolOverride.GetProperty("sensitivity").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
    }

    [Fact]
    public async Task Override_for_unfollowed_symbol_is_rejected_and_test_notification_is_nonbillable()
    {
        using var client = UserClient();
        using var create = await client.PutAsJsonAsync("/api/v1/radar/me/preferences", Preferences(0), CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var rejected = await client.PutAsJsonAsync("/api/v1/radar/me/symbols/100", new
        {
            expectedVersion = 0,
            state = "Active",
            sensitivity = "Focused"
        }, CancellationToken.None);
        using var test = await client.PostAsJsonAsync("/api/v1/radar/me/test-notification", new
        {
            idempotencyKey = "radar-endpoint-test",
            correlationId = "corr-radar-test"
        }, CancellationToken.None);
        using var body = await Json(test);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        Assert.True(body.RootElement.GetProperty("informational").GetBoolean());
        Assert.False(body.RootElement.GetProperty("billable").GetBoolean());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        Assert.Single(db.NotificationIntents.Where(item => item.EventType == "RadarTestNotification"));
    }

    [Fact]
    public async Task Radar_without_credentials_is_unauthorized()
    {
        using var response = await factory.CreateClient().GetAsync("/api/v1/radar/me", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static object Preferences(int expectedVersion) => new
    {
        expectedVersion,
        eventTypes = new[] { "PriceMovement", "LargeTradeDetected" },
        minimumSeverity = "Notice",
        minimumImportance = 50m,
        sensitivity = "Balanced",
        deliveryMode = "Immediate",
        state = "Active"
    };

    private HttpClient UserClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private void Reset()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.RadarEventMatches.RemoveRange(db.RadarEventMatches);
        db.RadarPreferenceAudits.RemoveRange(db.RadarPreferenceAudits);
        db.RadarSymbolOverrides.RemoveRange(db.RadarSymbolOverrides);
        db.RadarProfiles.RemoveRange(db.RadarProfiles);
        db.NotificationIntents.RemoveRange(db.NotificationIntents);
        db.FollowedSymbols.RemoveRange(db.FollowedSymbols);
        db.SaveChanges();
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
    }
}

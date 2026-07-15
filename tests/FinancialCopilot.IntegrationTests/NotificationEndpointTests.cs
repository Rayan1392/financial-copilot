using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class NotificationEndpointTests : IClassFixture<FollowedSymbolsApiFactory>
{
    private readonly FollowedSymbolsApiFactory factory;

    public NotificationEndpointTests(FollowedSymbolsApiFactory factory)
    {
        this.factory = factory;
        factory.EnsureBillingSeeded();
        factory.EnsureSeeded();
        Reset();
    }

    [Fact]
    public async Task Preferences_are_actor_scoped_versioned_and_require_followed_symbol_identity()
    {
        using var client = UserClient();
        using var defaults = await client.GetAsync("/api/v1/notifications/me/preferences", CancellationToken.None);
        using var defaultsBody = await Json(defaults);
        using var invalid = await client.PutAsJsonAsync("/api/v1/notifications/me/preferences",
            Preferences(0, "200"), CancellationToken.None);
        using var follow = await client.PostAsync("/api/v1/followed-symbols/me/100", null, CancellationToken.None);
        using var create = await client.PutAsJsonAsync("/api/v1/notifications/me/preferences",
            Preferences(0, "100"), CancellationToken.None);
        using var created = await Json(create);
        using var stale = await client.PutAsJsonAsync("/api/v1/notifications/me/preferences",
            Preferences(0, "100"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
        Assert.Equal(0, defaultsBody.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(1, created.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("Digest", created.RootElement.GetProperty("deliveryMode").GetString());
        Assert.Single(created.RootElement.GetProperty("symbols").EnumerateArray());
        Assert.Contains("Critical events", created.RootElement.GetProperty("effectivePolicyExplanation").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
    }

    [Fact]
    public async Task History_returns_only_the_canonical_actor_and_exposes_evidence_status()
    {
        SeedHistory();
        using var client = UserClient();
        using var response = await client.GetAsync("/api/v1/notifications/me/history?pageSize=10", CancellationToken.None);
        using var body = await Json(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Delivered", item.GetProperty("status").GetString());
        Assert.Equal("evidence:actor", item.GetProperty("evidenceReference").GetString());
        Assert.False(body.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Notification_controls_without_credentials_are_unauthorized()
    {
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/notifications/me/preferences", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dead_letter_retry_requires_data_admin_and_records_the_operator_action()
    {
        Guid intentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            var row = Intent(AuthenticationApiFactory.UserId, "evidence:dead-letter");
            row.Status = NotificationIntentState.DeadLettered.ToString();
            row.DeliveredAtUtc = null;
            row.DeadLetteredAtUtc = DateTimeOffset.Parse("2026-07-10T08:00:00Z");
            row.LastErrorCode = "Telegram403";
            db.NotificationIntents.Add(row);
            db.SaveChanges();
            intentId = row.Id;
        }
        using var user = UserClient();
        using var forbidden = await user.GetAsync("/api/v1/admin/notifications/dead-letters", CancellationToken.None);
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateWebAppToken(includeTenant: true, dataAdmin: true));
        using var list = await admin.GetAsync("/api/v1/admin/notifications/dead-letters", CancellationToken.None);
        using var retry = await admin.PostAsJsonAsync(
            $"/api/v1/admin/notifications/dead-letters/{intentId}/retry",
            new { correlationId = "admin-retry" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        Assert.Equal(NotificationIntentState.Pending.ToString(),
            verifyDb.NotificationIntents.Single(row => row.Id == intentId).Status);
        Assert.Equal("ManualRetry", Assert.Single(verifyDb.NotificationOperationAudits).Action);
    }

    private static object Preferences(int expectedVersion, string companyId) => new
    {
        expectedVersion,
        timeZoneId = "Asia/Tehran",
        deliveryMode = "Digest",
        quietHoursStart = "23:00:00",
        quietHoursEnd = "07:00:00",
        minimumSeverity = "Important",
        dailyCap = 12,
        digestTime = "18:30:00",
        cooldownMinutes = 45,
        categories = new[] { new { eventType = "PriceMovement", enabled = true, minimumSeverity = "Notice", cooldownMinutes = 15 } },
        symbols = new[] { new { externalCompanyId = companyId, muted = true } },
        correlationId = "notification-endpoint-test"
    };

    private HttpClient UserClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private void SeedHistory()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.NotificationOperationAudits.RemoveRange(db.NotificationOperationAudits);
        db.NotificationIntents.AddRange(
            Intent(AuthenticationApiFactory.UserId, "evidence:actor"),
            Intent(Guid.NewGuid(), "evidence:other"));
        db.SaveChanges();
    }

    private static NotificationIntentRow Intent(Guid actorId, string evidence) => new()
    {
        Id = Guid.NewGuid(), TenantId = AuthenticationApiFactory.TenantId, ActorId = actorId,
        ActorType = "User", Channel = "Telegram", EventType = "PriceMovement",
        Category = "Market", EntityKey = "100", DeduplicationKey = Guid.NewGuid().ToString("N"),
        CooldownKey = "PriceMovement:100", Severity = InsightSeverity.Important.ToString(),
        Status = NotificationIntentState.Delivered.ToString(), PayloadJson = "{}",
        EvidenceReference = evidence, ConcurrencyToken = Guid.NewGuid(),
        CreatedAtUtc = DateTimeOffset.Parse("2026-07-10T07:00:00Z"),
        NotBeforeUtc = DateTimeOffset.Parse("2026-07-10T07:00:00Z"),
        DeliveredAtUtc = DateTimeOffset.Parse("2026-07-10T08:00:00Z"),
        CorrelationId = $"corr-{actorId:N}"
    };

    private void Reset()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.NotificationOutcomeHandoffs.RemoveRange(db.NotificationOutcomeHandoffs);
        db.NotificationDeliveryAttempts.RemoveRange(db.NotificationDeliveryAttempts);
        db.NotificationCategoryPreferences.RemoveRange(db.NotificationCategoryPreferences);
        db.NotificationSymbolPreferences.RemoveRange(db.NotificationSymbolPreferences);
        db.NotificationPreferenceAudits.RemoveRange(db.NotificationPreferenceAudits);
        db.NotificationIntents.RemoveRange(db.NotificationIntents);
        db.NotificationBatches.RemoveRange(db.NotificationBatches);
        db.NotificationPreferences.RemoveRange(db.NotificationPreferences);
        db.FollowedSymbols.RemoveRange(db.FollowedSymbols);
        db.SaveChanges();
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
    }
}

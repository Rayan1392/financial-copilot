using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class TelegramAccountLinking087Tests : IClassFixture<OwnedIdentityApiFactory>
{
    private readonly OwnedIdentityApiFactory _factory;

    public TelegramAccountLinking087Tests(OwnedIdentityApiFactory factory) => _factory = factory;

    [Fact]
    public async Task WebChallenge_AdapterConfirmation_LinksOnceAndUnlinksIdempotently()
    {
        using var web = _factory.CreateClient();
        await AuthenticateNewUserAsync(web);

        using var challengeResponse = await web.PostAsync("/api/v1/telegram/link-token", null);
        using var challenge = await ReadJsonAsync(challengeResponse);
        Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
        var deepLink = challenge.RootElement.GetProperty("deepLink").GetString()!;
        Assert.StartsWith("https://t.me/financial_copilot_test_bot?start=link_", deepLink, StringComparison.Ordinal);
        var startParameter = new Uri(deepLink).Query.Split("start=", 2)[1];

        using var adapter = CreateAdapterClient();
        var request = new
        {
            startParameter,
            telegramUserId = 871001L,
            telegramChatId = 871001L,
            username = "display_only",
            telegramUpdateId = 87001L
        };
        using var confirmation = await adapter.PostAsJsonAsync("/api/v1/telegram/link/confirm", request);
        using var result = await ReadJsonAsync(confirmation);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Equal(0, result.RootElement.GetProperty("outcome").GetInt32());

        using var replay = await adapter.PostAsJsonAsync("/api/v1/telegram/link/confirm", request);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        using var current = await web.GetAsync("/api/v1/telegram/link/me");
        using var currentJson = await ReadJsonAsync(current);
        Assert.Equal(871001L, currentJson.RootElement.GetProperty("telegramUserId").GetInt64());
        Assert.Equal("display_only", currentJson.RootElement.GetProperty("username").GetString());

        using var unlink = await web.DeleteAsync("/api/v1/telegram/link/me");
        using var repeatUnlink = await web.DeleteAsync("/api/v1/telegram/link/me");
        using var missing = await web.GetAsync("/api/v1/telegram/link/me");
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatUnlink.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task TelegramStart_WebConfirmation_UsesCanonicalAuthenticatedActor()
    {
        using var adapter = CreateAdapterClient();
        using var start = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/telegram-start",
            new
            {
                telegramUserId = 872001L,
                telegramChatId = 872001L,
                username = "mutable_name",
                telegramUpdateId = 87201L
            });
        using var startJson = await ReadJsonAsync(start);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var confirmationUrl = new Uri(startJson.RootElement.GetProperty("confirmationUrl").GetString()!);
        var token = confirmationUrl.Query.Split("token=", 2)[1];

        using var web = _factory.CreateClient();
        await AuthenticateNewUserAsync(web);
        using var preview = await web.PostAsJsonAsync(
            "/api/v1/telegram/link/web-preview",
            new { token });
        using var previewJson = await ReadJsonAsync(preview);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("***2001", previewJson.RootElement.GetProperty("maskedTelegramUserId").GetString());
        Assert.Equal("mutable_name", previewJson.RootElement.GetProperty("username").GetString());
        using var confirmation = await web.PostAsJsonAsync(
            "/api/v1/telegram/link/web-confirm",
            new { token });
        using var current = await web.GetAsync("/api/v1/telegram/link/me");

        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);

        using var unlink = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/unlink-from-telegram",
            new { telegramUserId = 872001L, telegramUpdateId = 87202L });
        using var replayUnlink = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/unlink-from-telegram",
            new { telegramUserId = 872001L, telegramUpdateId = 87202L });
        using var missing = await web.GetAsync("/api/v1/telegram/link/me");
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replayUnlink.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task IssuingSecondChallenge_RevokesFirstAndStoresOnlyTokenHashes()
    {
        using var web = _factory.CreateClient();
        await AuthenticateNewUserAsync(web);

        using var firstResponse = await web.PostAsync("/api/v1/telegram/link-token", null);
        using var first = await ReadJsonAsync(firstResponse);
        using var secondResponse = await web.PostAsync("/api/v1/telegram/link-token", null);
        using var second = await ReadJsonAsync(secondResponse);
        var firstStart = new Uri(first.RootElement.GetProperty("deepLink").GetString()!).Query.Split("start=", 2)[1];
        var secondStart = new Uri(second.RootElement.GetProperty("deepLink").GetString()!).Query.Split("start=", 2)[1];

        using var adapter = CreateAdapterClient();
        using var revoked = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/confirm",
            new { startParameter = firstStart, telegramUserId = 873001L, telegramChatId = 873001L, username = "x", telegramUpdateId = 87301L });
        using var valid = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/confirm",
            new { startParameter = secondStart, telegramUserId = 873001L, telegramChatId = 873001L, username = "x", telegramUpdateId = 87302L });

        Assert.Equal(HttpStatusCode.BadRequest, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var rows = await db.TelegramLinkTokens.AsNoTracking().ToArrayAsync();
        Assert.All(rows, row => Assert.DoesNotContain(firstStart[5..], row.TokenHash, StringComparison.OrdinalIgnoreCase));
        Assert.All(rows, row => Assert.Equal(64, row.TokenHash.Length));
    }

    [Fact]
    public async Task LinkEndpoints_EnforceWebUserVersusAdapterAuthenticationAndPrivateChat()
    {
        using var anonymous = _factory.CreateClient();
        using var denied = await anonymous.PostAsync("/api/v1/telegram/link-token", null);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var adapter = CreateAdapterClient();
        using var groupChat = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/telegram-start",
            new { telegramUserId = 874001L, telegramChatId = -100874001L, username = "x", telegramUpdateId = 87401L });
        Assert.Equal(HttpStatusCode.BadRequest, groupChat.StatusCode);

        using var adapterSelfLink = await adapter.PostAsync("/api/v1/telegram/link-token", null);
        Assert.Equal(HttpStatusCode.Forbidden, adapterSelfLink.StatusCode);
    }

    [Fact]
    public async Task ExpiredChallenge_IsRejectedWithoutCreatingLink()
    {
        using var web = _factory.CreateClient();
        await AuthenticateNewUserAsync(web);
        using var challengeResponse = await web.PostAsync("/api/v1/telegram/link-token", null);
        using var challenge = await ReadJsonAsync(challengeResponse);
        var startParameter = new Uri(challenge.RootElement.GetProperty("deepLink").GetString()!).Query.Split("start=", 2)[1];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var latest = await db.TelegramLinkTokens.OrderByDescending(row => row.CreatedAtUtc).FirstAsync();
            latest.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var adapter = CreateAdapterClient();
        using var expired = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/confirm",
            new { startParameter, telegramUserId = 875001L, telegramChatId = 875001L, username = "x", telegramUpdateId = 87501L });
        using var missing = await web.GetAsync("/api/v1/telegram/link/me");

        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task TelegramIdentity_CannotBeLinkedToTwoCanonicalUsers()
    {
        using var firstUser = _factory.CreateClient();
        await AuthenticateNewUserAsync(firstUser);
        var firstStart = await CreateStartParameterAsync(firstUser);
        using var adapter = CreateAdapterClient();
        using var firstConfirm = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/confirm",
            new { startParameter = firstStart, telegramUserId = 876001L, telegramChatId = 876001L, username = "first", telegramUpdateId = 87601L });
        Assert.Equal(HttpStatusCode.OK, firstConfirm.StatusCode);

        using var secondUser = _factory.CreateClient();
        await AuthenticateNewUserAsync(secondUser);
        var secondStart = await CreateStartParameterAsync(secondUser);
        using var conflict = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/confirm",
            new { startParameter = secondStart, telegramUserId = 876001L, telegramChatId = 876001L, username = "renamed", telegramUpdateId = 87602L });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    private HttpClient CreateAdapterClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private static async Task AuthenticateNewUserAsync(HttpClient client)
    {
        using var register = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email = $"telegram-{Guid.NewGuid():N}@example.test", password = "StrongPassword!123" });
        using var session = await ReadJsonAsync(register);
        register.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            session.RootElement.GetProperty("accessToken").GetString());
    }

    private static async Task<string> CreateStartParameterAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/v1/telegram/link-token", null);
        using var json = await ReadJsonAsync(response);
        response.EnsureSuccessStatusCode();
        return new Uri(json.RootElement.GetProperty("deepLink").GetString()!).Query.Split("start=", 2)[1];
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

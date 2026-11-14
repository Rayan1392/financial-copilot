using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Self-service profile: masked by default, full value only on the owner's explicit reveal (Design §10.2).</summary>
public sealed class OwnProfileIntegrationTests(TahlilAppLaunchApiFactory factory) : IClassFixture<TahlilAppLaunchApiFactory>
{
    private const string NationalId = "0012345678";
    private const string Mobile = "+989121234567";

    [Fact]
    public async Task Profile_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/auth/v1/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OwnProfile_ShowsNamesInFull_AndMasksIdentifiers_WithNoStore()
    {
        var token = await LaunchWithProfileAsync();

        using var response = await GetAsync("/api/auth/v1/profile", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());
        var body = await ReadRawAsync(response);
        Assert.Contains("\"firstName\":\"Ali\"", body);
        Assert.Contains("\"nationalIdMasked\":\"••••••5678\"", body);
        Assert.Contains("\"mobileMasked\":\"0912•••4567\"", body);
        Assert.DoesNotContain(NationalId, body);
        Assert.DoesNotContain("+989121234567", body);
    }

    [Fact]
    public async Task OwnProfile_WithoutTahlilAppProfile_ReturnsEmptyNullableProfile_WithNoStore()
    {
        var token = factory.CreateWebAppToken(includeTenant: false);

        using var response = await GetAsync("/api/auth/v1/profile", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var profile = document.RootElement;
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("firstName").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("lastName").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("nationalIdMasked").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("mobileMasked").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("profileSyncedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("displayName").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("contactMobileMasked").ValueKind);
        Assert.False(profile.GetProperty("avatarAvailable").GetBoolean());
    }

    [Fact]
    public async Task OwnProfile_UpdateNames_UsesLocalOverrideWithoutChangingTahlilAppSource()
    {
        var token = await LaunchWithProfileAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/auth/v1/profile")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { firstName = "Sara", lastName = "Karimi" })
        };
        using var update = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var profile = await auth.UserProfiles.SingleAsync(row => row.LocalFirstName == "Sara" && row.LocalLastName == "Karimi");
        Assert.Equal("Sara", profile.LocalFirstName);
        Assert.Equal("Karimi", profile.LocalLastName);

        using var response = await GetAsync("/api/auth/v1/profile", token);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Sara", document.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("Karimi", document.RootElement.GetProperty("lastName").GetString());

        Assert.Equal("Ali", profile.FirstName);
        Assert.Null(profile.LastName);
    }

    [Fact]
    public async Task OwnProfile_VerifiesLocalMobile_WithoutChangingTahlilAppMobile()
    {
        var token = await LaunchWithProfileAsync();
        factory.SmsSender.NextCode = "3741437414";
        using var client = factory.CreateClient();

        using var send = new HttpRequestMessage(HttpMethod.Post, "/api/auth/v1/profile/mobile/request")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { mobile = "09129876543" })
        };
        using var sent = await client.SendAsync(send);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        Assert.Equal("09129876543", factory.SmsSender.LastMobile);

        using var confirm = new HttpRequestMessage(HttpMethod.Post, "/api/auth/v1/profile/mobile/confirm")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { code = factory.SmsSender.NextCode })
        };
        using var confirmed = await client.SendAsync(confirm);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        using var response = await GetAsync("/api/auth/v1/profile", token);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal("0912***6543", document.RootElement.GetProperty("contactMobileMasked").GetString());
        Assert.Equal("0912•••4567", document.RootElement.GetProperty("mobileMasked").GetString());
    }

    [Fact]
    public async Task OwnProfile_AcceptsAndServesNormalizedAvatar()
    {
        var token = await LaunchWithProfileAsync();
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.Red);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var png = encoded.ToArray();
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(png);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(image, "File", "avatar.png");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/auth/v1/profile/avatar")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = content
        };

        using var uploaded = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, uploaded.StatusCode);
        using var get = await GetAsync("/api/auth/v1/profile/avatar", token);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Reveal_ReturnsOwnNationalIdAndMobile_InFull()
    {
        var token = await LaunchWithProfileAsync();

        var nationalId = await RevealAsync(token, "nationalId");
        var mobile = await RevealAsync(token, "mobile");

        Assert.Equal(NationalId, nationalId);
        Assert.Equal(Mobile, mobile);
    }

    [Fact]
    public async Task Reveal_RejectsUnknownFields()
    {
        var token = await LaunchWithProfileAsync();

        using var response = await PostRevealAsync(token, "email");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reveal_ReturnsNotFound_WhenTheFieldWasNeverProvided()
    {
        var token = await LaunchAsync(NewExternalId(), nationalId: null, mobile: null);

        using var response = await PostRevealAsync(token, "nationalId");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reveal_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/v1/profile/reveal", new { field = "mobile" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> LaunchWithProfileAsync() =>
        await LaunchAsync(NewExternalId(), NationalId, Mobile);

    private async Task<string> LaunchAsync(string externalId, string? nationalId, string? mobile)
    {
        var code = NewCode();
        factory.Gateway.Respond(code, new TahlilAppExchangeResult(TahlilAppExchangeOutcome.Succeeded, new TahlilAppExchangeSnapshot(
            ExternalUserId: externalId,
            IsSuspended: false,
            ExternalPlan: "Free",
            IsSubscriptionActive: false,
            SubscriptionExpiresAt: null,
            ExternalRevision: null,
            ExternalPermissionCount: 0,
            FirstName: IncomingProfileField.Valid("Ali"),
            LastName: IncomingProfileField.Absent,
            NationalId: nationalId is null ? IncomingProfileField.Absent : IncomingProfileField.Valid(nationalId),
            Mobile: mobile is null ? IncomingProfileField.Absent : IncomingProfileField.Valid(mobile))));

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/v1/tahlilapp/launch", new { code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<string?> RevealAsync(string token, string field)
    {
        using var response = await PostRevealAsync(token, field);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.GetProperty("value").GetString();
    }

    private async Task<HttpResponseMessage> PostRevealAsync(string token, string field)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/v1/profile/reveal")
        {
            Content = JsonContent.Create(new { field })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task<string> ReadRawAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    private static string NewExternalId() => $"tahlil-profile-{Guid.NewGuid():N}";

    private static string NewCode() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

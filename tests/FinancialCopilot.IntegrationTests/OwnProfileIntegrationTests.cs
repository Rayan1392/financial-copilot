using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;

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

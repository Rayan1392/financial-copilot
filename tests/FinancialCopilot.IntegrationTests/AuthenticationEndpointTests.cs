using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FinancialCopilot.IntegrationTests;

public sealed class AuthenticationEndpointTests : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _factory;

    public AuthenticationEndpointTests(AuthenticationApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AiQuery_WithoutCredentials_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync("/api/ai/v1/query", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/ai/v1/conversations")]
    [InlineData("/api/ai/v1/conversations/6fcebf13-c238-4798-90aa-57d01f778ef0")]
    [InlineData("/api/ai/v1/conversations/6fcebf13-c238-4798-90aa-57d01f778ef0/messages")]
    public async Task ConversationEndpoints_WithoutCredentials_ReturnUnauthorized(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AiQuery_WithValidWebAppJwt_UsesWebAppUserContext()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsync("/api/ai/v1/query", null, CancellationToken.None);
        var problemDetails = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal(
            AuthenticationMode.WebAppUser.ToString(),
            problemDetails.RootElement.GetProperty("authenticationMode").GetString());
    }

    [Fact]
    public async Task AiQuery_WithJwtMissingTenantContext_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: false));

        using var response = await client.PostAsync("/api/ai/v1/query", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AiQuery_WithValidApiKey_UsesApiClientContext()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsync("/api/ai/v1/query", null, CancellationToken.None);
        var problemDetails = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal(
            AuthenticationMode.ApiClient.ToString(),
            problemDetails.RootElement.GetProperty("authenticationMode").GetString());
    }

    [Fact]
    public async Task ConversationHistory_WithValidApiKey_ReachesProtectedCapability()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync("/api/ai/v1/conversations", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task AiQuery_WithInvalidApiKey_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "invalid-key");

        using var response = await client.PostAsync("/api/ai/v1/query", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiClientUsage_WithWebAppJwt_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.GetAsync(
            $"/api/v1/usage/api-client/{AuthenticationApiFactory.ClientId}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiClientUsage_WithMatchingApiKeyActor_ReachesProtectedCapability()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync(
            $"/api/v1/usage/api-client/{AuthenticationApiFactory.ClientId}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task ApiClientUsage_WithDifferentApiClientId_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync(
            $"/api/v1/usage/api-client/{Guid.NewGuid()}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class RateLimitEndpointTests : IClassFixture<RateLimitedAuthenticationApiFactory>
{
    private readonly RateLimitedAuthenticationApiFactory _factory;

    public RateLimitEndpointTests(RateLimitedAuthenticationApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AiQuery_RateLimitIsPartitionedByAuthenticatedActor()
    {
        using var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var firstClientResponse = await apiClient.PostAsync("/api/ai/v1/query", null, CancellationToken.None);
        using var secondClientResponse = await apiClient.PostAsync("/api/ai/v1/query", null, CancellationToken.None);
        using var userResponse = await userClient.PostAsync("/api/ai/v1/query", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotImplemented, firstClientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondClientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, userResponse.StatusCode);
    }
}

public class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "integration-client-secret";
    public static readonly Guid TenantId = Guid.Parse("39fd0553-95fb-4882-ae72-d96e67320611");
    public static readonly Guid ClientId = Guid.Parse("eb93553e-504c-4131-9145-6258b968f1c5");
    private static readonly Guid UserId = Guid.Parse("fc3b4b7b-61cf-4295-aa53-5273e4c0d5a0");
    private const string Issuer = "FinancialCopilot.Tests";
    private const string Audience = "FinancialCopilot.Tests.Web";
    private const string SigningKey = "integration-test-signing-key-with-more-than-32-characters";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ApiKey)));
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:JwtBearer:Issuer"] = Issuer,
                ["Authentication:JwtBearer:Audience"] = Audience,
                ["Authentication:JwtBearer:SigningKey"] = SigningKey,
                ["Authentication:JwtBearer:RequireHttpsMetadata"] = "false",
                ["Authentication:ApiKeys:Clients:0:ClientId"] = ClientId.ToString(),
                ["Authentication:ApiKeys:Clients:0:TenantId"] = TenantId.ToString(),
                ["Authentication:ApiKeys:Clients:0:Name"] = "Integration Client",
                ["Authentication:ApiKeys:Clients:0:KeySha256"] = keyHash,
                ["Authentication:ApiKeys:Clients:0:IsActive"] = "true"
            });
        });
    }

    public string CreateWebAppToken(bool includeTenant)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, UserId.ToString())
        };

        if (includeTenant)
        {
            claims.Add(new Claim(FinancialCopilotClaimTypes.TenantId, TenantId.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class RateLimitedAuthenticationApiFactory : AuthenticationApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AuthenticatedActor:PermitLimit"] = "1",
                ["RateLimiting:AuthenticatedActor:WindowSeconds"] = "60"
            });
        });
    }
}

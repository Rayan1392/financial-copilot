using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.API.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<ApiKeyAuthenticationOptions> apiKeyOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) ||
            string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var apiKey = values.First()!;
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        var credential = apiKeyOptions.CurrentValue.Clients.FirstOrDefault(client =>
            client.IsActive && MatchesHash(presentedHash, client.KeySha256));

        if (credential is null ||
            !Guid.TryParse(credential.ClientId, out var clientId) ||
            !Guid.TryParse(credential.TenantId, out var tenantId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, clientId.ToString()),
            new Claim(ClaimTypes.Name, credential.Name),
            new Claim(FinancialCopilotClaimTypes.ApiClientId, clientId.ToString()),
            new Claim(FinancialCopilotClaimTypes.TenantId, tenantId.ToString()),
            new Claim(FinancialCopilotClaimTypes.AuthenticationMode, AuthenticationMode.ApiClient.ToString())
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool MatchesHash(byte[] presentedHash, string configuredHash)
    {
        try
        {
            var expectedHash = Convert.FromHexString(configuredHash);
            return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CyclicalWavesAuthHandler(
    CyclicalWavesTokenCache tokenCache,
    IOptions<CyclicalWavesProviderOptions> options,
    TimeProvider timeProvider) : DelegatingHandler
{
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsAuthRequest(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        await EnsureTokenAsync(cancellationToken);
        AddBearerHeader(request);

        var response = await base.SendAsync(request, cancellationToken);

        // A 4xx response is a definitive rejection for this symbol/request.
        // Never re-authenticate and replay it.
        return response;
    }

    private async Task EnsureTokenAsync(CancellationToken cancellationToken)
    {
        if (tokenCache.TryGetToken(timeProvider.GetUtcNow(), out _))
        {
            return;
        }

        await LoginAsync(cancellationToken);
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        await _loginGate.WaitAsync(cancellationToken);
        try
        {
            if (tokenCache.TryGetToken(timeProvider.GetUtcNow(), out _))
            {
                return;
            }

            var settings = options.Value;
            var loginUri = new Uri(new Uri(settings.BaseAddress), "auth/login");
            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUri)
            {
                Content = JsonContent.Create(new { user_name = settings.UserName, password = settings.Password })
            };

            using var loginResponse = await base.SendAsync(loginRequest, cancellationToken);

            if (!loginResponse.IsSuccessStatusCode)
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.Unauthorized,
                    $"CyclicalWaves login failed with status {loginResponse.StatusCode}.");
            }

            var authResponse = await loginResponse.Content.ReadFromJsonAsync<CyclicalWavesAuthResponse>(
                JsonOptions,
                cancellationToken) ??
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.InvalidResponse,
                    "CyclicalWaves login response was empty.");

            var expiresAt = timeProvider.GetUtcNow().AddSeconds(authResponse.ExpiresIn);
            tokenCache.SetToken(authResponse.AccessToken, expiresAt);
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private void AddBearerHeader(HttpRequestMessage request)
    {
        request.Headers.Authorization = null;

        if (tokenCache.TryGetToken(timeProvider.GetUtcNow(), out var token))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static bool IsAuthRequest(HttpRequestMessage request) =>
        request.RequestUri?.OriginalString.Contains("auth/login", StringComparison.OrdinalIgnoreCase) == true;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

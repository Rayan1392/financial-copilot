using System.Net;
using System.Net.Http.Headers;
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
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsAuthRequest(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var token = await EnsureTokenAsync(cancellationToken);
        AddBearerHeader(request, token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized && response.StatusCode != HttpStatusCode.MethodNotAllowed)
        {
            return response;
        }

        response.Dispose();
        var replacement = await RecoverAfterUnauthorizedAsync(token.AccessToken, cancellationToken);
        using var replay = await CloneAsync(request, cancellationToken);
        AddBearerHeader(replay, replacement);
        return await base.SendAsync(replay, cancellationToken);
    }

    private async Task<CyclicalWavesCachedToken> EnsureTokenAsync(CancellationToken cancellationToken)
    {
        var cached = await tokenCache.GetValidTokenAsync(timeProvider.GetUtcNow(), cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await tokenCache.AuthenticationGate.WaitAsync(cancellationToken);
        try
        {
            cached = await tokenCache.GetValidTokenAsync(timeProvider.GetUtcNow(), cancellationToken);
            return cached ?? await LoginAsync(cancellationToken);
        }
        finally
        {
            tokenCache.AuthenticationGate.Release();
        }
    }

    private async Task<CyclicalWavesCachedToken> RecoverAfterUnauthorizedAsync(
        string rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        await tokenCache.AuthenticationGate.WaitAsync(cancellationToken);
        try
        {
            await tokenCache.InvalidateIfMatchesAsync(rejectedAccessToken, cancellationToken);
            var current = await tokenCache.GetValidTokenAsync(timeProvider.GetUtcNow(), cancellationToken);
            return current ?? await LoginAsync(cancellationToken);
        }
        finally
        {
            tokenCache.AuthenticationGate.Release();
        }
    }

    private async Task<CyclicalWavesCachedToken> LoginAsync(CancellationToken cancellationToken)
    {
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
                $"CyclicalWaves login failed with status {(int)loginResponse.StatusCode}.");
        }

        var responseBody = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw InvalidLoginResponse("CyclicalWaves login response was empty.");
        }

        CyclicalWavesAuthResponse? authResponse;
        try
        {
            authResponse = JsonSerializer.Deserialize<CyclicalWavesAuthResponse>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves login response was not valid JSON.",
                exception);
        }

        if (authResponse is null ||
            string.IsNullOrWhiteSpace(authResponse.AccessToken) ||
            authResponse.ExpiresIn <= 0)
        {
            throw InvalidLoginResponse("CyclicalWaves login response did not contain a usable token.");
        }

        var issuedAtUtc = timeProvider.GetUtcNow();
        var token = new CyclicalWavesCachedToken(
            authResponse.AccessToken,
            string.IsNullOrWhiteSpace(authResponse.TokenType) ? "Bearer" : authResponse.TokenType,
            issuedAtUtc,
            issuedAtUtc.AddSeconds(authResponse.ExpiresIn),
            authResponse.RefreshToken);

        await tokenCache.SetTokenAsync(token, cancellationToken);
        return token;
    }

    private static void AddBearerHeader(
        HttpRequestMessage request,
        CyclicalWavesCachedToken token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static bool IsAuthRequest(HttpRequestMessage request) =>
        request.RequestUri?.OriginalString.Contains("auth/login", StringComparison.OrdinalIgnoreCase) == true;

    private static FinancialProviderException InvalidLoginResponse(string message) =>
        new(FinancialProviderErrorCode.InvalidResponse, message);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

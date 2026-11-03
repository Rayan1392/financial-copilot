using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;

/// <summary>
/// Process-local, single-flight access token owner for the CyclicalWaves v2 auth server.
/// The provider rotates refresh tokens: the new one is stored before any caller sees the new access
/// token, because reusing an old refresh token after two minutes invalidates the login.
/// Tokens and credentials are never logged.
/// </summary>
public sealed class CyclicalWavesV2TokenService(
    IHttpClientFactory httpClientFactory,
    IOptions<CyclicalWavesV2Options> options,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesV2TokenService> logger)
{
    public const string AuthHttpClientName = "CyclicalWavesV2.Auth";
    internal static readonly TimeSpan FallbackLifetime = TimeSpan.FromMinutes(4);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var current = _accessToken;
        if (current is not null && IsFresh())
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && IsFresh())
            {
                return _accessToken;
            }

            await RenewAsync(cancellationToken);
            return _accessToken!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Called after a data call was rejected with 401; renews unless another caller already did.</summary>
    public async Task<string> RenewAfterRejectionAsync(string rejectedAccessToken, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessToken != rejectedAccessToken)
            {
                return _accessToken;
            }

            _expiresAt = DateTimeOffset.MinValue;
            await RenewAsync(cancellationToken);
            return _accessToken!;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsFresh() =>
        timeProvider.GetUtcNow() < _expiresAt - TimeSpan.FromSeconds(options.Value.RefreshSkewSeconds);

    private async Task RenewAsync(CancellationToken cancellationToken)
    {
        if (_refreshToken is not null)
        {
            var refreshed = await SendAsync(
                "auth/refresh",
                new RefreshRequest(_refreshToken),
                cancellationToken);
            if (refreshed.Pair is not null)
            {
                Store(refreshed.Pair);
                return;
            }

            logger.LogInformation(
                "CyclicalWaves v2 token refresh was rejected with HTTP {StatusCode}; logging in again.",
                (int)refreshed.Status);
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Identifier) || string.IsNullOrWhiteSpace(settings.Password))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.ConfigurationMissing,
                "CyclicalWaves v2 credentials are not configured.");
        }

        var login = await SendAsync(
            "auth/login",
            new LoginRequest(settings.Identifier, settings.Password),
            cancellationToken);
        if (login.Pair is null)
        {
            _accessToken = null;
            _refreshToken = null;
            throw new FinancialProviderException(
                FinancialProviderErrorCode.Unauthorized,
                $"CyclicalWaves v2 login failed with HTTP {(int)login.Status}.");
        }

        Store(login.Pair);
    }

    private void Store(TokenPair pair)
    {
        // Rotation first: the refresh token must be persisted before the access token is handed out.
        _refreshToken = string.IsNullOrWhiteSpace(pair.RefreshToken) ? null : pair.RefreshToken;
        _expiresAt = ReadExpiry(pair.AccessToken!) ?? timeProvider.GetUtcNow().Add(FallbackLifetime);
        _accessToken = pair.AccessToken;
    }

    private async Task<(HttpStatusCode Status, TokenPair? Pair)> SendAsync<TBody>(
        string path,
        TBody body,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(AuthHttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("x-client-id", options.Value.ClientId);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (response.StatusCode, null);
        }

        TokenPair? pair;
        try
        {
            pair = await response.Content.ReadFromJsonAsync<TokenPair>(cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves v2 auth response was not valid JSON.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(pair?.AccessToken))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves v2 auth response had no access token.");
        }

        return (response.StatusCode, pair);
    }

    /// <summary>Reads the JWT <c>exp</c> claim for timing only; the signature is not validated here.</summary>
    internal static DateTimeOffset? ReadExpiry(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private sealed record LoginRequest(
        [property: JsonPropertyName("identifier")] string Identifier,
        [property: JsonPropertyName("password")] string Password);

    private sealed record RefreshRequest([property: JsonPropertyName("refreshToken")] string RefreshToken);

    private sealed record TokenPair(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);
}

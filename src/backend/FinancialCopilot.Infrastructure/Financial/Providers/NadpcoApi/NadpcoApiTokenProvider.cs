using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

public sealed class NadpcoApiTokenProvider(
    HttpClient httpClient,
    NadpcoApiTokenCache tokenCache,
    IOptions<NadpcoApiProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<NadpcoApiTokenProvider> logger) : INadpcoApiTokenProvider
{
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private readonly NadpcoApiProviderOptions _settings = options.Value;

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && tokenCache.TryGetToken(timeProvider.GetUtcNow(), out var cachedToken))
        {
            return cachedToken;
        }

        await _loginGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && tokenCache.TryGetToken(timeProvider.GetUtcNow(), out cachedToken))
            {
                return cachedToken;
            }

            ValidateCredentials();
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v2/Token");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.UserName}:{_settings.Password}")));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? FinancialProviderErrorCode.Unauthorized
                    : FinancialProviderErrorCode.RemoteUnavailable;
                logger.LogWarning(
                    "NADPCO token request failed with status {StatusCode}.",
                    response.StatusCode);
                throw new FinancialProviderException(code, $"NADPCO token request failed with status {response.StatusCode}.");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<NadpcoApiTokenResponse>(
                JsonOptions,
                cancellationToken) ??
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.InvalidResponse,
                    "NADPCO token response was empty.");

            var token = tokenResponse.GetToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.InvalidResponse,
                    "NADPCO token response did not include an access token.");
            }

            var now = timeProvider.GetUtcNow();
            var expiresAt = tokenResponse.GetExpiresAt(
                now,
                TimeSpan.FromMinutes(Math.Max(1, _settings.DefaultTokenLifetimeMinutes)));
            if (expiresAt <= now)
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.InvalidResponse,
                    "NADPCO token response included an expired token.");
            }

            var cacheExpiresAt = GetTehranDayEndUtc(now);
            tokenCache.SetToken(token, now, cacheExpiresAt);
            return token;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    public void Invalidate() => tokenCache.Invalidate();

    private void ValidateCredentials()
    {
        if (string.IsNullOrWhiteSpace(_settings.UserName) ||
            string.IsNullOrWhiteSpace(_settings.Password))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.ConfigurationMissing,
                "NADPCO API credentials are not configured.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DateTimeOffset GetTehranDayEndUtc(DateTimeOffset now)
    {
        var tehranTimeZone = GetTehranTimeZone();
        var tehranNow = TimeZoneInfo.ConvertTime(now, tehranTimeZone);
        var tehranDayEnd = new DateTimeOffset(
            tehranNow.Date.AddDays(1).AddSeconds(-1),
            tehranNow.Offset);

        return tehranDayEnd.ToUniversalTime();
    }

    private static TimeZoneInfo GetTehranTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
    }
}

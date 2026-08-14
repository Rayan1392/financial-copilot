using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed record CyclicalWavesCachedToken(
    string AccessToken,
    string TokenType,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? RefreshToken);

public sealed class CyclicalWavesTokenCache(
    IDistributedCache distributedCache,
    IOptions<CyclicalWavesProviderOptions> options)
{
    internal const string CacheKey = "cyclicalwaves:auth:token:v1";
    private const string HealthCheckKey = "cyclicalwaves:auth:cache-health:v1";
    internal SemaphoreSlim AuthenticationGate { get; } = new(1, 1);

    public async Task<CyclicalWavesCachedToken?> GetValidTokenAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await distributedCache.GetStringAsync(CacheKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var token = JsonSerializer.Deserialize<CyclicalWavesCachedToken>(json, JsonOptions);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return null;
            }

            var lifetime = token.ExpiresAtUtc - token.IssuedAtUtc;
            if (lifetime <= TimeSpan.Zero)
            {
                return null;
            }

            var configuredMargin = TimeSpan.FromSeconds(options.Value.TokenExpirationSafetyMarginSeconds);
            var effectiveMargin = configuredMargin < lifetime / 2
                ? configuredMargin
                : lifetime / 2;

            return token.ExpiresAtUtc - effectiveMargin > now ? token : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CacheFailure(exception);
        }
    }

    public async Task ValidateAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            await distributedCache.SetStringAsync(
                HealthCheckKey,
                "ok",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                },
                cancellationToken);
            _ = await distributedCache.GetStringAsync(HealthCheckKey, cancellationToken);
            await distributedCache.RemoveAsync(HealthCheckKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CacheFailure(exception);
        }
    }

    public async Task SetTokenAsync(
        CyclicalWavesCachedToken token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token.AccessToken);

        try
        {
            var json = JsonSerializer.Serialize(token, JsonOptions);
            await distributedCache.SetStringAsync(
                CacheKey,
                json,
                new DistributedCacheEntryOptions { AbsoluteExpiration = token.ExpiresAtUtc },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CacheFailure(exception);
        }
    }

    public async Task InvalidateIfMatchesAsync(
        string rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        var current = await GetTokenWithoutExpiryValidationAsync(cancellationToken);
        if (current is null || !FixedTimeEquals(current.AccessToken, rejectedAccessToken))
        {
            return;
        }

        try
        {
            await distributedCache.RemoveAsync(CacheKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CacheFailure(exception);
        }
    }

    private async Task<CyclicalWavesCachedToken?> GetTokenWithoutExpiryValidationAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await distributedCache.GetStringAsync(CacheKey, cancellationToken);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<CyclicalWavesCachedToken>(json, JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CacheFailure(exception);
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static FinancialProviderException CacheFailure(Exception exception) =>
        new(
            FinancialProviderErrorCode.RemoteUnavailable,
            "CyclicalWaves authentication token cache is unavailable.",
            exception);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

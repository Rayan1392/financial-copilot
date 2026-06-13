using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

/// <summary>
/// Persists the NADPCO bearer token in <see cref="IDistributedCache"/> (Redis when configured,
/// in-memory otherwise) so that application restarts do not consume the vendor's daily token
/// quota. The cache key is scoped to the provider so Worker and API share the same token when
/// both point at the same Redis instance.
/// </summary>
public sealed class NadpcoApiTokenCache(IDistributedCache distributedCache)
{
    private const string CacheKey = "nadpco:auth:token";
    private const string ExpiryKey = "nadpco:auth:token:expiry";

    // In-process fallback: used when Redis is unavailable or the distributed cache returns stale
    // expiry metadata. Guards concurrent token-fetch attempts within a single process.
    private readonly object _gate = new();
    private string? _localToken;
    private DateTimeOffset _localExpiresAt;

    public bool TryGetToken(DateTimeOffset now, out string token)
    {
        // Fast path: in-process cache (no network hop).
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_localToken) && _localExpiresAt > now)
            {
                token = _localToken;
                return true;
            }
        }

        // Slow path: distributed cache (survives restarts).
        try
        {
            var cachedToken = distributedCache.GetString(CacheKey);
            var cachedExpiry = distributedCache.GetString(ExpiryKey);
            if (!string.IsNullOrWhiteSpace(cachedToken) &&
                DateTimeOffset.TryParse(cachedExpiry, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt) &&
                expiresAt > now)
            {
                lock (_gate)
                {
                    _localToken = cachedToken;
                    _localExpiresAt = expiresAt;
                }
                token = cachedToken;
                return true;
            }
        }
        catch
        {
            // Distributed cache unavailable — fall through to a fresh token fetch.
        }

        token = string.Empty;
        return false;
    }

    public void SetToken(string token, DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            _localToken = token;
            _localExpiresAt = expiresAt;
        }

        var ttl = expiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        var cacheOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
        try
        {
            distributedCache.SetString(CacheKey, token, cacheOptions);
            distributedCache.SetString(ExpiryKey, expiresAt.ToString("O"), cacheOptions);
        }
        catch
        {
            // Redis unavailable — in-process cache still valid for this instance.
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _localToken = null;
            _localExpiresAt = DateTimeOffset.MinValue;
        }

        try
        {
            distributedCache.Remove(CacheKey);
            distributedCache.Remove(ExpiryKey);
        }
        catch
        {
            // Best-effort Redis eviction.
        }
    }
}

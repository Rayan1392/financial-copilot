using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Scanner;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class ScannerCacheOptions
{
    public const string SectionName = "ScannerCache";

    public bool Enabled { get; set; } = true;

    public bool UseRedis { get; set; }

    public string RedisConfiguration { get; set; } = "localhost:6379";

    public string InstanceName { get; set; } = "financial-copilot:";

    public int PlanTtlSeconds { get; set; } = 300;

    public int ResultTtlSeconds { get; set; } = 60;
}

public sealed class DistributedScannerCache(
    IDistributedCache cache,
    IOptions<ScannerCacheOptions> options) : IScannerCache
{
    private const string VersionKey = "scanner:data-version";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ScannerCacheOptions _options = options.Value;

    public async Task<string> GetDataVersionAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return "disabled";
        }

        return await cache.GetStringAsync(VersionKey, cancellationToken) ?? "initial";
    }

    public Task<ScannerParseResult?> GetPlanAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerParseRequest request,
        CancellationToken cancellationToken) =>
        GetAsync<ScannerParseResult>(PlanKey(scope, dataVersion, request), cancellationToken);

    public Task SetPlanAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerParseRequest request,
        ScannerParseResult result,
        CancellationToken cancellationToken) =>
        SetAsync(
            PlanKey(scope, dataVersion, request),
            result,
            TimeSpan.FromSeconds(_options.PlanTtlSeconds),
            cancellationToken);

    public Task<ScannerTableResult?> GetResultAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerExecutionRequest request,
        CancellationToken cancellationToken) =>
        GetAsync<ScannerTableResult>(ResultKey(scope, dataVersion, request), cancellationToken);

    public Task SetResultAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerExecutionRequest request,
        ScannerTableResult result,
        CancellationToken cancellationToken) =>
        SetAsync(
            ResultKey(scope, dataVersion, request),
            result,
            TimeSpan.FromSeconds(_options.ResultTtlSeconds),
            cancellationToken);

    public Task InvalidateAsync(
        ScannerCacheInvalidation invalidation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidation.Reason);

        return _options.Enabled
            ? cache.SetStringAsync(
                VersionKey,
                $"{invalidation.InvalidatedAt.UtcTicks:x16}-{Guid.NewGuid():N}",
                cancellationToken)
            : Task.CompletedTask;
    }

    private async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return default;
        }

        var payload = await cache.GetStringAsync(key, cancellationToken);
        return payload is null ? default : JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private Task SetAsync<T>(
        string key,
        T value,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || lifetime <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        return cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(value, JsonOptions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            cancellationToken);
    }

    private static string PlanKey(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerParseRequest request) =>
        $"scanner:plan:{ScopeKey(scope)}:{dataVersion}:{Hash(new
        {
            request.UserQuery,
            request.Language,
            request.AsOf
        })}";

    private static string ResultKey(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerExecutionRequest request) =>
        $"scanner:result:{ScopeKey(scope)}:{dataVersion}:{Hash(new
        {
            request.AsOf,
            request.MaxRows,
            request.Plan
        })}";

    private static string ScopeKey(ScannerCacheScope scope) =>
        $"{scope.TenantId:N}:{(scope.ApiClientId ?? scope.ActorId):N}";

    private static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

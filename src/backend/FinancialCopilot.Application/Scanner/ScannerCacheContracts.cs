namespace FinancialCopilot.Application.Scanner;

public sealed record ScannerCacheScope(
    Guid TenantId,
    Guid ActorId,
    Guid? ApiClientId);

public sealed record ScannerCacheInvalidation(
    string Reason,
    DateTimeOffset InvalidatedAt);

public interface IScannerCache
{
    Task<string> GetDataVersionAsync(CancellationToken cancellationToken);

    Task<ScannerParseResult?> GetPlanAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerParseRequest request,
        CancellationToken cancellationToken);

    Task SetPlanAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerParseRequest request,
        ScannerParseResult result,
        CancellationToken cancellationToken);

    Task<ScannerTableResult?> GetResultAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerExecutionRequest request,
        CancellationToken cancellationToken);

    Task SetResultAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerExecutionRequest request,
        ScannerTableResult result,
        CancellationToken cancellationToken);

    Task InvalidateAsync(
        ScannerCacheInvalidation invalidation,
        CancellationToken cancellationToken);
}

public sealed class NoOpScannerCache : IScannerCache
{
    public Task<string> GetDataVersionAsync(CancellationToken cancellationToken) =>
        Task.FromResult("uncached");

    public Task<ScannerParseResult?> GetPlanAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerParseRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult<ScannerParseResult?>(null);

    public Task SetPlanAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerParseRequest request,
        ScannerParseResult result,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<ScannerTableResult?> GetResultAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerExecutionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult<ScannerTableResult?>(null);

    public Task SetResultAsync(
        ScannerCacheScope scope,
        string dataVersion,
        ScannerExecutionRequest request,
        ScannerTableResult result,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task InvalidateAsync(
        ScannerCacheInvalidation invalidation,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

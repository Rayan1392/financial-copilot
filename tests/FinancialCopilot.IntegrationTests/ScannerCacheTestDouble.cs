using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.IntegrationTests;

internal sealed class TrackingScannerCache : IScannerCache
{
    public List<ScannerCacheInvalidation> Invalidations { get; } = [];

    public Task<string> GetDataVersionAsync(CancellationToken cancellationToken) =>
        Task.FromResult("test");

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
        CancellationToken cancellationToken)
    {
        Invalidations.Add(invalidation);
        return Task.CompletedTask;
    }
}

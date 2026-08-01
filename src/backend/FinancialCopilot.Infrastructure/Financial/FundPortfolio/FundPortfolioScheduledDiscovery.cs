using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioScheduledDiscoveryOptions
{
    public const string SectionName = "FundPortfolio:ScheduledDiscovery";
    public bool Enabled { get; set; }
    public string ProviderName { get; set; } = "ConfiguredLocalStorage";
    public int CadenceSeconds { get; set; } = 3600;
    public int LookbackDays { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
    public int Concurrency { get; set; } = 1;
    public int LeaseDurationSeconds { get; set; } = 300;
}

public sealed class FundPortfolioScheduledDiscoveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FundPortfolioScheduledDiscoveryOptions> options,
    ILogger<FundPortfolioScheduledDiscoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Fund portfolio scheduled discovery is disabled.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            await DiscoverOnceAsync(settings, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.CadenceSeconds, 60, 86400)), stoppingToken);
        }
    }

    private async Task DiscoverOnceAsync(FundPortfolioScheduledDiscoveryOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var watermarks = scope.ServiceProvider.GetRequiredService<EfCoreFundPortfolioSourceWatermarkStore>();
        if (!await watermarks.TryAcquireAsync(settings.ProviderName, TimeSpan.FromSeconds(Math.Clamp(settings.LeaseDurationSeconds, 30, 3600)), cancellationToken))
        {
            logger.LogInformation("Fund portfolio scheduled discovery skipped because another worker owns the lease. Provider={Provider}", settings.ProviderName);
            return;
        }
        var source = scope.ServiceProvider.GetRequiredService<IFundPortfolioReportSourceRegistry>().Get(settings.ProviderName);
        if (!source.IsAvailable)
        {
            logger.LogWarning("Fund portfolio scheduled discovery has no verified source adapter. Provider={Provider} Reason={Reason}", settings.ProviderName, source.UnavailableReason);
            await watermarks.AdvanceAsync(settings.ProviderName, null, null, cancellationToken); return;
        }
        var watermark = await watermarks.GetAsync(settings.ProviderName, cancellationToken);
        var lookback = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(settings.LookbackDays, 1, 365));
        var modifiedAfter = watermark?.LastModifiedUtc is null || watermark.Value.LastModifiedUtc < lookback ? lookback : watermark.Value.LastModifiedUtc;
        var batchSize = Math.Clamp(settings.BatchSize, 1, 500);
        var discovered = new List<FundPortfolioReportSourceDescriptor>(batchSize);
        string? continuation = null;
        var continuationTokens = new HashSet<string?>(StringComparer.Ordinal);
        do
        {
            var page = await source.DiscoverAsync(new(settings.ProviderName, modifiedAfter, Math.Min(batchSize - discovered.Count, 500), continuation), cancellationToken);
            discovered.AddRange(page.Items.Take(batchSize - discovered.Count));
            continuation = page.ContinuationToken;
        }
        while (discovered.Count < batchSize && continuation is not null && continuationTokens.Add(continuation));
        var eligible = discovered.Where(item => FundPortfolioSourceEligibilityPolicy.IsNewer(item, watermark?.LastModifiedUtc, watermark?.LastSourceObjectId)).ToArray();
        if (eligible.Length == 0) { await watermarks.AdvanceAsync(settings.ProviderName, null, null, cancellationToken); return; }
        scope.ServiceProvider.GetRequiredService<IFundPortfolioOperationalTelemetry>().RecordDiscovery(eligible.Length);
        var result = await scope.ServiceProvider.GetRequiredService<IStartFundPortfolioImportRunUseCase>().ExecuteAsync(
            new(FundPortfolioImportTriggerType.ScheduledDiscovery, settings.ProviderName, null, eligible), cancellationToken);
        var latest = eligible.OrderByDescending(x => x.LastModifiedUtc).ThenByDescending(x => x.StableSourceObjectId, StringComparer.Ordinal).First();
        await watermarks.AdvanceAsync(settings.ProviderName, latest.LastModifiedUtc, latest.StableSourceObjectId, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IFundPortfolioAuditSink>().WriteAsync(
            new("scheduled-discovery", "system", result.RunId, null, null, result.CorrelationId, $"{result.ItemCount} source objects discovered."), cancellationToken);
        logger.LogInformation("Fund portfolio scheduled discovery queued. RunId={RunId} Provider={Provider} Count={Count}", result.RunId, settings.ProviderName, result.ItemCount);
    }
}

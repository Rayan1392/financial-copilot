using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioImportProcessingOptions
{
    public const string SectionName = "FundPortfolio:ImportProcessing";
    public bool Enabled { get; set; } = true;
    public int Concurrency { get; set; } = 2;
    public int PollSeconds { get; set; } = 5;
    public int MaximumAttempts { get; set; } = 3;
    public int LeaseDurationSeconds { get; set; } = 300;
}

public sealed class FundPortfolioImportProcessingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FundPortfolioImportProcessingOptions> options,
    ILogger<FundPortfolioImportProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) { logger.LogInformation("Fund portfolio import processing is disabled."); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var ids = await GetRunnableItemsAsync(Math.Clamp(settings.Concurrency, 1, 32), stoppingToken);
            if (ids.Count == 0) { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.PollSeconds, 1, 60)), stoppingToken); continue; }
            await Task.WhenAll(ids.Select(item => ProcessOneAsync(item.RunId, item.ItemId, settings.MaximumAttempts, settings.LeaseDurationSeconds, stoppingToken)));
        }
    }

    private async Task<IReadOnlyList<(Guid RunId, Guid ItemId)>> GetRunnableItemsAsync(int maximumItems, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IFundPortfolioImportRunRepository>().ListRunnableItemsAsync(maximumItems, cancellationToken);
    }

    private async Task ProcessOneAsync(Guid runId, Guid itemId, int maximumAttempts, int leaseDurationSeconds, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var status = await scope.ServiceProvider.GetRequiredService<IImportFundPortfolioItemUseCase>().ExecuteAsync(new(runId, itemId, maximumAttempts, leaseDurationSeconds), cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IFinalizeFundPortfolioImportRunUseCase>().ExecuteAsync(runId, cancellationToken);
            logger.LogInformation("Fund portfolio import item processed. RunId={RunId} ItemId={ItemId} Status={Status}", runId, itemId, status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Fund portfolio import item processing failed. RunId={RunId} ItemId={ItemId}", runId, itemId); }
    }
}

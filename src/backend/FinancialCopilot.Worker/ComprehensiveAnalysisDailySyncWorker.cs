using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Worker;

public sealed class ComprehensiveAnalysisDailySyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ComprehensiveAnalysisDailySyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the first scheduled execution window before entering the loop.
        // The worker delays one full cadence on startup so it does not run at every
        // process restart; the admin endpoint covers on-demand triggering.
        var cadence = TimeSpan.FromHours(24);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(cadence, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IComprehensiveAnalysisDailySyncService>();
                var result = await service.ExecuteAsync(stoppingToken);

                logger.LogInformation(
                    "ComprehensiveAnalysis daily sync worker completed: {Pages} pages, {Items} items.",
                    result.PagesTotal,
                    result.ItemsSynced);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ComprehensiveAnalysis daily sync worker tick failed.");
            }
        }
    }
}

using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.API;

/// <summary>Starts due Feature 143 fetches and rechecks incomplete income statements.</summary>
public sealed class TelegramFinancialStatementReconciliationWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    Microsoft.Extensions.Options.IOptions<TelegramChannelFinancialStatementOptions> configured,
    ILogger<TelegramFinancialStatementReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Feature 143 reconciliation scan failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scanScope = scopes.CreateAsyncScope();
        var auth = scanScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var now = clock.GetUtcNow();
        var scheduledCutoff = now.AddMinutes(-configured.Value.InitialDelayMinutes);
        var reconciliationCutoff = now.AddMinutes(-30);
        var checkpoints = await auth.TelegramProcessedUpdates.AsNoTracking()
            .Where(x => x.IdempotencyKey.StartsWith("channel-financial-statement:") &&
                ((x.Status == "Feature143:Scheduled" && x.ProcessedAtUtc <= scheduledCutoff) ||
                 (x.Status == "Feature143:Partial" && x.ProcessedAtUtc <= reconciliationCutoff)))
            .OrderBy(x => x.ProcessedAtUtc).Select(x => new { x.Id, x.Status }).Take(100).ToArrayAsync(cancellationToken);
        foreach (var checkpoint in checkpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<TelegramChannelFinancialStatementHandler>();
                if (checkpoint.Status == "Feature143:Scheduled")
                    await handler.ProcessScheduledAsync(checkpoint.Id, cancellationToken);
                else
                    await handler.ReconcilePendingAsync(checkpoint.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Feature 143 scheduled processing failed for checkpoint {CheckpointId}.", checkpoint.Id);
            }
        }
    }
}

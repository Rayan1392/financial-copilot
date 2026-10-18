using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.API;

/// <summary>Rechecks only incomplete Feature 143 income statements after provider propagation.</summary>
public sealed class TelegramFinancialStatementReconciliationWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<TelegramFinancialStatementReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5), clock);
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
        var cutoff = clock.GetUtcNow().AddMinutes(-30);
        var ids = await auth.TelegramProcessedUpdates.AsNoTracking()
            .Where(x => x.Status == "Feature143:Partial" &&
                x.IdempotencyKey.StartsWith("channel-financial-statement:") && x.ProcessedAtUtc <= cutoff)
            .OrderBy(x => x.ProcessedAtUtc).Select(x => x.Id).Take(100).ToArrayAsync(cancellationToken);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<TelegramChannelFinancialStatementHandler>();
                await handler.ReconcilePendingAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Feature 143 reconciliation failed for checkpoint {CheckpointId}.", id);
            }
        }
    }
}

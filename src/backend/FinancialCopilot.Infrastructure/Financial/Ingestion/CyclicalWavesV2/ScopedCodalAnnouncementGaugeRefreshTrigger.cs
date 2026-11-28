using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// Runs the announcement gauge refresh in its own DI scope. The run store saves and clears its
/// <c>FinancialIngestionDbContext</c>; a separate scope keeps that away from the context of the Telegram
/// handler that reports the announcement. Every failure is logged and swallowed: the gauge refresh must
/// never change how the announcement itself is handled.
/// </summary>
public sealed class ScopedCodalAnnouncementGaugeRefreshTrigger(
    IServiceScopeFactory scopes,
    ILogger<ScopedCodalAnnouncementGaugeRefreshTrigger> logger) : ICodalAnnouncementGaugeRefreshTrigger
{
    public async Task RequestAsync(CodalAnnouncementGaugeRefreshRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<CodalAnnouncementGaugeRefresh>()
                .RequestAsync(request, cancellationToken);
            if (outcome.Status == CodalAnnouncementGaugeRefreshStatus.Disabled)
            {
                return;
            }

            logger.Log(
                outcome.Status == CodalAnnouncementGaugeRefreshStatus.QueueFull ? LogLevel.Warning : LogLevel.Information,
                "CyclicalWaves v2 announcement gauge refresh {Status}. Kind={Kind} Symbol={Symbol} Ticker={Ticker} Immediate={Immediate} Deferred={Deferred} ChatId={ChatId} MessageId={MessageId} CorrelationId={CorrelationId}",
                outcome.Status,
                request.Kind,
                request.Symbol,
                outcome.Ticker,
                outcome.Immediate,
                outcome.Deferred,
                request.TelegramChatId,
                request.TelegramMessageId,
                request.CorrelationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "CyclicalWaves v2 announcement gauge refresh could not be queued. Kind={Kind} Symbol={Symbol} ChatId={ChatId} MessageId={MessageId} CorrelationId={CorrelationId}",
                request.Kind,
                request.Symbol,
                request.TelegramChatId,
                request.TelegramMessageId,
                request.CorrelationId);
        }
    }
}

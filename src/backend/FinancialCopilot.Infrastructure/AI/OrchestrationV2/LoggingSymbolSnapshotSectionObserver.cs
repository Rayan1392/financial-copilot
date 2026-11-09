using FinancialCopilot.Application.AI.Orchestration;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2;

public sealed class LoggingSymbolSnapshotSectionObserver(
    ILogger<LoggingSymbolSnapshotSectionObserver> logger) : ISymbolSnapshotSectionObserver
{
    public void Record(string correlationId, string section, SymbolSnapshotSectionStatus status, TimeSpan elapsed, Exception? failure)
    {
        if (failure is null)
        {
            logger.LogInformation(
                "Symbol snapshot section completed. Section={Section} Status={Status} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                section, status, elapsed.TotalMilliseconds, correlationId);
            return;
        }

        logger.LogWarning(
            failure,
            "Symbol snapshot section failed. Section={Section} Status={Status} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
            section, status, elapsed.TotalMilliseconds, correlationId);
    }
}

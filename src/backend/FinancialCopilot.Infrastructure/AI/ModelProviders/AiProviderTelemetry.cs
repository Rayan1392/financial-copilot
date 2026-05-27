using FinancialCopilot.Application.AI.ModelProviders;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class LoggingAiExecutionTelemetrySink(
    ILogger<LoggingAiExecutionTelemetrySink> logger) : IAiExecutionTelemetrySink
{
    public Task RecordAttemptAsync(AiExecutionUsageFacts facts, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "AI model attempt {AttemptNumber} for correlation {CorrelationId} used {ProviderKey}/{ModelKey} with status {Status} in {DurationMs} ms.",
            facts.AttemptNumber,
            facts.CorrelationId,
            facts.ProviderKey,
            facts.ModelKey,
            facts.Status,
            facts.Duration.TotalMilliseconds);
        return Task.CompletedTask;
    }
}

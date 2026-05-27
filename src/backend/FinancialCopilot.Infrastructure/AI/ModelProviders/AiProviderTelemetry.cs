using FinancialCopilot.Application.AI.ModelProviders;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class LoggingAiExecutionTelemetrySink(
    ILogger<LoggingAiExecutionTelemetrySink> logger) : IAiExecutionTelemetrySink
{
    public Task RecordAttemptAsync(AiExecutionUsageFacts facts, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "AI model attempt {AttemptNumber} for correlation {CorrelationId} used {ProviderKey}/{ModelKey} " +
            "with status {Status} in {DurationMs} ms. " +
            "InputTokens={InputTokens} OutputTokens={OutputTokens} CacheHit={CacheHit} " +
            "UsedTools={UsedTools} ProviderCost={ProviderCost} {Currency} FailureCode={FailureCode}.",
            facts.AttemptNumber,
            facts.CorrelationId,
            facts.ProviderKey,
            facts.ModelKey,
            facts.Status,
            facts.Duration.TotalMilliseconds,
            facts.InputTokens,
            facts.OutputTokens,
            facts.CacheHit,
            facts.UsedTools,
            facts.ProviderReportedCost,
            facts.ProviderReportedCurrency,
            facts.FailureCode);
        return Task.CompletedTask;
    }
}

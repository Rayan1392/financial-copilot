using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

public sealed record MetricAliasLearningSignal(
    string UserExpression,
    string NormalizedExpression,
    string Language,
    MetricResolutionStatus FailureKind,
    string? ActorId,
    string? CorrelationId,
    DateTimeOffset OccurredAt);

public interface IMetricAliasLearningSignalCollector
{
    Task CollectAsync(MetricAliasLearningSignal signal, CancellationToken cancellationToken);
}

using System.Collections.Concurrent;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class InMemoryAiExecutionUsageAccumulator : IAiExecutionUsageAccumulator
{
    private readonly ConcurrentDictionary<string, MutableUsageSummary> _usageByCorrelationId = new(StringComparer.Ordinal);

    public void Record(AiExecutionUsageFacts facts)
    {
        if (facts.Status != AiExecutionStatus.Completed)
        {
            return;
        }

        _usageByCorrelationId.AddOrUpdate(
            facts.CorrelationId,
            _ => MutableUsageSummary.From(facts),
            (_, existing) => existing.Add(facts));
    }

    public AiExecutionUsageSummary? GetSummary(string correlationId) =>
        _usageByCorrelationId.TryGetValue(correlationId, out var summary)
            ? summary.ToImmutable()
            : null;

    private sealed class MutableUsageSummary
    {
        private readonly object _gate = new();

        private string _providerKey;
        private string _modelKey;
        private int? _inputTokens;
        private int? _outputTokens;
        private decimal? _providerReportedCost;
        private string? _providerReportedCurrency;

        private MutableUsageSummary(AiExecutionUsageFacts facts)
        {
            _providerKey = facts.ProviderKey;
            _modelKey = facts.ModelKey;
            _inputTokens = facts.InputTokens;
            _outputTokens = facts.OutputTokens;
            _providerReportedCost = facts.ProviderReportedCost;
            _providerReportedCurrency = facts.ProviderReportedCurrency;
        }

        public static MutableUsageSummary From(AiExecutionUsageFacts facts) => new(facts);

        public MutableUsageSummary Add(AiExecutionUsageFacts facts)
        {
            lock (_gate)
            {
                if (!string.Equals(_providerKey, facts.ProviderKey, StringComparison.OrdinalIgnoreCase))
                {
                    _providerKey = $"{_providerKey},{facts.ProviderKey}";
                }

                if (!string.Equals(_modelKey, facts.ModelKey, StringComparison.OrdinalIgnoreCase))
                {
                    _modelKey = $"{_modelKey},{facts.ModelKey}";
                }

                _inputTokens = Sum(_inputTokens, facts.InputTokens);
                _outputTokens = Sum(_outputTokens, facts.OutputTokens);
                _providerReportedCost = Sum(_providerReportedCost, facts.ProviderReportedCost);
                _providerReportedCurrency ??= facts.ProviderReportedCurrency;
                return this;
            }
        }

        public AiExecutionUsageSummary ToImmutable()
        {
            lock (_gate)
            {
                return new AiExecutionUsageSummary(
                    _providerKey,
                    _modelKey,
                    _inputTokens,
                    _outputTokens,
                    _providerReportedCost,
                    _providerReportedCurrency);
            }
        }

        private static int? Sum(int? left, int? right) =>
            left is null && right is null ? null : (left ?? 0) + (right ?? 0);

        private static decimal? Sum(decimal? left, decimal? right) =>
            left is null && right is null ? null : (left ?? 0m) + (right ?? 0m);
    }
}

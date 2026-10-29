using System.Collections.Concurrent;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

// Request-scoped usage lives in process memory: every model call of one chat turn runs in the
// process that serves the turn. Entries untouched for EntryLifetime are evicted so the
// singleton stays bounded; a turn settles within its five-minute reservation window.
public sealed class InMemoryAiExecutionUsageAccumulator(TimeProvider? timeProvider = null) : IAiExecutionUsageAccumulator
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, UsageEntry> _usageByCorrelationId = new(StringComparer.Ordinal);
    private long _nextSweepTicks;

    public void Record(AiExecutionUsageFacts facts)
    {
        if (facts.Status != AiExecutionStatus.Completed)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        _usageByCorrelationId.GetOrAdd(facts.CorrelationId, _ => new UsageEntry(now)).Add(facts, now);
        SweepExpired(now);
    }

    public AiExecutionUsageSummary? GetSummary(string correlationId) =>
        _usageByCorrelationId.TryGetValue(correlationId, out var entry)
            ? entry.ToSummary()
            : null;

    public IReadOnlyList<AiExecutionUsageFacts> GetUnbilled(string correlationId) =>
        _usageByCorrelationId.TryGetValue(correlationId, out var entry)
            ? entry.GetUnclaimed()
            : [];

    public IReadOnlyList<AiExecutionUsageFacts> ClaimUnbilled(string correlationId, string claimKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimKey);

        return _usageByCorrelationId.TryGetValue(correlationId, out var entry)
            ? entry.Claim(claimKey, _timeProvider.GetUtcNow())
            : [];
    }

    private void SweepExpired(DateTimeOffset now)
    {
        var nextSweep = Interlocked.Read(ref _nextSweepTicks);
        if (now.UtcTicks < nextSweep ||
            Interlocked.CompareExchange(ref _nextSweepTicks, now.Add(SweepInterval).UtcTicks, nextSweep) != nextSweep)
        {
            return;
        }

        foreach (var (correlationId, entry) in _usageByCorrelationId)
        {
            if (entry.IsExpired(now - EntryLifetime))
            {
                _usageByCorrelationId.TryRemove(correlationId, out _);
            }
        }
    }

    private sealed class UsageEntry(DateTimeOffset createdAt)
    {
        private readonly object _gate = new();
        private readonly List<AiExecutionUsageFacts> _calls = [];
        private readonly Dictionary<string, AiExecutionUsageFacts[]> _claims = new(StringComparer.Ordinal);
        private int _claimedCount;
        private DateTimeOffset _touchedAt = createdAt;

        public void Add(AiExecutionUsageFacts facts, DateTimeOffset now)
        {
            lock (_gate)
            {
                _calls.Add(facts);
                _touchedAt = now;
            }
        }

        public IReadOnlyList<AiExecutionUsageFacts> Claim(string claimKey, DateTimeOffset now)
        {
            lock (_gate)
            {
                if (!_claims.TryGetValue(claimKey, out var claimed))
                {
                    claimed = _calls.Skip(_claimedCount).ToArray();
                    _claims[claimKey] = claimed;
                    _claimedCount = _calls.Count;
                }

                _touchedAt = now;
                return claimed;
            }
        }

        public IReadOnlyList<AiExecutionUsageFacts> GetUnclaimed()
        {
            lock (_gate)
            {
                return _calls.Skip(_claimedCount).ToArray();
            }
        }

        public bool IsExpired(DateTimeOffset cutoff)
        {
            lock (_gate)
            {
                return _touchedAt < cutoff;
            }
        }

        public AiExecutionUsageSummary? ToSummary()
        {
            lock (_gate)
            {
                return AiExecutionUsageSummary.From(_calls);
            }
        }
    }
}

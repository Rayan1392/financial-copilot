using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

// ---------------------------------------------------------------------------
// Options
// ---------------------------------------------------------------------------

public sealed class MetricAliasLearningOptions
{
    public const string SectionName = "MetricAliasLearning";

    public bool Enabled { get; init; }
    public int MinFrequency { get; init; } = 5;
    public decimal MinAutoApproveConfidence { get; init; } = 0.90m;
}

// ---------------------------------------------------------------------------
// Signal collectors
// ---------------------------------------------------------------------------

public sealed class NoOpMetricAliasLearningSignalCollector : IMetricAliasLearningSignalCollector
{
    public Task CollectAsync(MetricAliasLearningSignal signal, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class AsyncFireAndForgetMetricAliasLearningSignalCollector(
    IServiceScopeFactory scopeFactory,
    IMetricAliasExpressionNormalizer normalizer,
    ILogger<AsyncFireAndForgetMetricAliasLearningSignalCollector> logger)
    : IMetricAliasLearningSignalCollector
{
    public Task CollectAsync(MetricAliasLearningSignal signal, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var suggested = DeterministicCandidateMapper.Suggest(signal, normalizer);
                if (suggested is null) return;

                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IMetricAliasCandidateRepository>();
                await repo.UpsertAsync(suggested, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Metric alias learning signal collection failed for expression '{Expression}'; swallowed.",
                    signal.UserExpression);
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Deterministic candidate suggestion rules
// ---------------------------------------------------------------------------

internal static class DeterministicCandidateMapper
{
    private static readonly IReadOnlyDictionary<string, string> EnShorthands =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pe"]  = "PE_TTM",
            ["p/e"] = "PE_TTM",
            ["p e"] = "PE_TTM",
            ["ps"]  = "PS_TTM",
            ["p/s"] = "PS_TTM",
            ["p s"] = "PS_TTM",
        };

    private static readonly IReadOnlyDictionary<string, string> FaShorthands =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["پی ای"]    = "PE_TTM",
            ["پی به ای"] = "PE_TTM",
            ["پی اس"]    = "PS_TTM",
            ["پی به اس"] = "PS_TTM",
        };

    public static MetricAliasCandidate? Suggest(
        MetricAliasLearningSignal signal,
        IMetricAliasExpressionNormalizer normalizer)
    {
        var norm = normalizer.Normalize(signal.UserExpression, signal.Language);

        string? code = null;
        if (string.Equals(signal.Language, "en", StringComparison.OrdinalIgnoreCase))
            EnShorthands.TryGetValue(norm, out code);
        else if (string.Equals(signal.Language, "fa", StringComparison.OrdinalIgnoreCase))
            FaShorthands.TryGetValue(norm, out code);

        if (code is null) return null;

        var evidence = JsonSerializer.Serialize(new[]
        {
            new { signal.UserExpression, signal.ActorId, signal.CorrelationId, OccurredAt = signal.OccurredAt }
        });

        return new MetricAliasCandidate(
            Id: Guid.NewGuid(),
            Expression: signal.UserExpression,
            NormalizedExpression: norm,
            Language: signal.Language,
            SuggestedMetricCode: new MetricCode(code),
            SuggestedMetricVersion: null,
            Status: MetricAliasCandidateStatus.Pending,
            ConfidenceScore: 0.95m,
            FrequencyCount: 1,
            DistinctActorCount: 1,
            FirstSeenAt: signal.OccurredAt,
            LastSeenAt: signal.OccurredAt,
            EvidenceExamplesJson: evidence,
            RejectionReason: null,
            PromotedAliasId: null);
    }
}

// ---------------------------------------------------------------------------
// Learning policy (gates for auto-promotion)
// ---------------------------------------------------------------------------

public sealed class MetricAliasLearningPolicy(MetricAliasLearningOptions options)
{
    public bool ShouldAutoPromote(MetricAliasCandidate candidate) =>
        candidate.FrequencyCount >= options.MinFrequency &&
        candidate.ConfidenceScore >= options.MinAutoApproveConfidence;
}

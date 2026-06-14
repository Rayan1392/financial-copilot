using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class MetricAliasLearningWorkerOptions
{
    public const string SectionName = "MetricAliasLearning";

    public int IntervalSeconds { get; init; } = 300;
    public int BatchSize { get; init; } = 50;
}

/// <summary>
/// Drains pending alias candidates and auto-promotes those that meet policy thresholds.
/// Runs every <c>MetricAliasLearning:IntervalSeconds</c> (default 5 min).
/// </summary>
public sealed class MetricAliasLearningWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MetricAliasLearningWorkerOptions> workerOptions,
    IOptions<MetricAliasLearningOptions> learningOptions,
    ILogger<MetricAliasLearningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!learningOptions.Value.Enabled)
        {
            logger.LogInformation("MetricAliasLearningWorker is disabled via configuration.");
            return;
        }

        var settings = workerOptions.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));

        do
        {
            try
            {
                await ProcessBatchAsync(settings.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MetricAliasLearningWorker batch failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(int batchSize, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var candidateRepo = scope.ServiceProvider.GetRequiredService<IMetricAliasCandidateRepository>();
        var aliasRepo = scope.ServiceProvider.GetRequiredService<IDynamicMetricAliasRepository>();
        var policy = scope.ServiceProvider.GetRequiredService<MetricAliasLearningPolicy>();
        var invalidator = scope.ServiceProvider.GetRequiredService<IMetricAliasCacheInvalidator>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var pending = await candidateRepo.GetPendingAsync(batchSize, stoppingToken);
        if (pending.Count == 0) return;

        var promoted = 0;
        foreach (var candidate in pending)
        {
            if (!policy.ShouldAutoPromote(candidate)) continue;

            var aliasId = Guid.NewGuid();
            var alias = new DynamicMetricAlias(
                Id: aliasId,
                Expression: candidate.Expression,
                NormalizedExpression: candidate.NormalizedExpression,
                Language: candidate.Language,
                MetricCode: candidate.SuggestedMetricCode,
                MetricVersion: candidate.SuggestedMetricVersion ?? "v1",
                Source: MetricAliasSource.AutoLearned,
                Status: MetricAliasStatus.Active,
                ConfidenceScore: candidate.ConfidenceScore,
                FrequencyCount: candidate.FrequencyCount,
                CreatedAt: timeProvider.GetUtcNow(),
                CreatedBy: "auto-learning-worker",
                ApprovedAt: timeProvider.GetUtcNow(),
                ApprovedBy: "auto-learning-worker",
                DisabledAt: null,
                DisabledBy: null,
                DisableReason: null);

            await aliasRepo.AddAsync(alias, stoppingToken);
            await candidateRepo.ApproveAsync(candidate.Id, "auto-learning-worker", aliasId, stoppingToken);
            invalidator.InvalidateLanguage(candidate.Language);
            promoted++;
        }

        if (promoted > 0)
        {
            logger.LogInformation(
                "MetricAliasLearningWorker auto-promoted {Count} alias candidates.",
                promoted);
        }
    }
}

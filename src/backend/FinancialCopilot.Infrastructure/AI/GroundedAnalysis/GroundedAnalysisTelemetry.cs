using System.Diagnostics;
using System.Diagnostics.Metrics;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.AI.GroundedAnalysis;

/// <summary>
/// Change «sol-deep-analysis-web-search» (FR-13, PO-7, AC-17): the actual cost of one analysis call, for logs, metrics
/// and the cost alert only. <see cref="Record"/> returns nothing and no billing type depends on this class, so the
/// estimate can never reach a charge. Never logs report values, answer text or the question.
/// </summary>
public sealed class GroundedAnalysisTelemetry(
    IOptions<UsageBasedCreditBillingOptions> creditOptions,
    GroundedAnalysisOptions options,
    UsageBasedCreditCalculator calculator,
    ILogger<GroundedAnalysisTelemetry> logger)
{
    private static readonly Meter Meter = new("FinancialCopilot.GroundedAnalysis");
    private static readonly Counter<long> Runs = Meter.CreateCounter<long>("grounded_analysis.runs");
    private static readonly Counter<long> CostAlerts = Meter.CreateCounter<long>("grounded_analysis.cost_alerts");
    private static readonly Histogram<double> CostUsd = Meter.CreateHistogram<double>("grounded_analysis.cost_usd");
    private static readonly Histogram<double> LatencyMs = Meter.CreateHistogram<double>("grounded_analysis.latency_ms");
    private static readonly Histogram<long> OutputTokens = Meter.CreateHistogram<long>("grounded_analysis.output_tokens");
    private static readonly Histogram<long> ReasoningTokens = Meter.CreateHistogram<long>("grounded_analysis.reasoning_tokens");
    private static readonly Histogram<long> SearchCalls = Meter.CreateHistogram<long>("grounded_analysis.web_search_calls");

    public void Record(
        GroundedAnalysisCallFacts facts,
        string completionStatus,
        string? result,
        TimeSpan elapsed,
        string correlationId)
    {
        try
        {
            var usage = facts.Usage;
            var cost = EstimateUsd(creditOptions.Value.ToPolicy(), calculator, usage, options.Telemetry.WebSearchCallUsd);
            var tags = new TagList
            {
                { "tier", facts.Choice.Tier },
                { "status", completionStatus },
                { "result", result ?? "unknown" },
                { "model", usage?.ModelKey ?? "unknown" }
            };
            Runs.Add(1, tags);
            LatencyMs.Record(elapsed.TotalMilliseconds, tags);
            if (cost is { } usd) CostUsd.Record((double)usd, tags);
            if (usage?.OutputTokens is { } output) OutputTokens.Record(output, tags);
            if (usage?.ReasoningTokens is { } reasoning) ReasoningTokens.Record(reasoning, tags);
            SearchCalls.Record(usage?.WebSearchCalls ?? 0, tags);

            logger.LogInformation(
                "Grounded analysis call measured. Tier={Tier} Status={Status} Result={Result} Model={Model} " +
                "InputTokens={InputTokens} CachedInputTokens={CachedInputTokens} OutputTokens={OutputTokens} " +
                "ReasoningTokens={ReasoningTokens} WebSearchCalls={WebSearchCalls} ConsultedSources={ConsultedSources} " +
                "EstimatedCostUsd={EstimatedCostUsd} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                facts.Choice.Tier, completionStatus, result, usage?.ModelKey, usage?.InputTokens, usage?.CachedInputTokens,
                usage?.OutputTokens, usage?.ReasoningTokens, usage?.WebSearchCalls, facts.ConsultedSources, cost,
                elapsed.TotalMilliseconds, correlationId);

            if (cost > options.Telemetry.CostAlertUsd)
            {
                CostAlerts.Add(1, tags);
                logger.LogWarning(
                    "Grounded analysis cost above the alert threshold. Tier={Tier} EstimatedCostUsd={EstimatedCostUsd} " +
                    "ThresholdUsd={ThresholdUsd} CorrelationId={CorrelationId}",
                    facts.Choice.Tier, cost, options.Telemetry.CostAlertUsd, correlationId);
            }
        }
        catch (Exception exception)
        {
            // Monitoring must never affect the turn.
            logger.LogWarning("Grounded analysis telemetry failed. FailureType={FailureType} CorrelationId={CorrelationId}",
                exception.GetType().Name, correlationId);
        }
    }

    /// <summary>
    /// Token cost from the configured <c>ModelPrices</c> (the same calculation the usage-based path uses) plus the
    /// configured price per search call. Null when the call's usage or the model's price is unknown.
    /// </summary>
    internal static decimal? EstimateUsd(
        UsageBasedCreditPolicy policy,
        UsageBasedCreditCalculator calculator,
        AiExecutionUsageFacts? usage,
        decimal webSearchCallUsd)
    {
        if (usage is null || policy.FindPrice(usage.ProviderKey, usage.ModelKey) is null) return null;
        var charge = calculator.Calculate(policy, [
            new ModelCallUsage(
                usage.ProviderKey,
                usage.ModelKey,
                usage.InputTokens,
                usage.CachedInputTokens,
                usage.OutputTokens,
                usage.ProviderReportedCost,
                usage.ProviderReportedCurrency,
                usage.CacheWriteInputTokens,
                usage.CacheWrite30mInputTokens,
                usage.CacheWrite12hInputTokens)
        ]);
        return charge.CostComplete ? charge.ActualCost + (usage.WebSearchCalls ?? 0) * webSearchCallUsd : null;
    }
}

using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Application.Scanner;

/// <summary>
/// Redacted operational facts for a governed sales-growth scanner execution.
/// User text, provider credentials, and raw provider payloads are intentionally absent.
/// </summary>
public sealed record SalesGrowthScannerTelemetry(
    string CorrelationId,
    Guid TenantId,
    Guid ActorId,
    string AliasFamily,
    SalesGrowthComparisonBaseline? Baseline,
    FilterOrigin? BaselineOrigin,
    SalesGrowthThresholdKind? ThresholdKind,
    ConditionOperator? Operator,
    decimal? ThresholdValue,
    DateOnly? TargetCommonPeriod,
    decimal? CoveragePercent,
    int EligibleSymbolCount,
    int EvaluatedSymbolCount,
    int MatchedSymbolCount,
    IReadOnlyDictionary<string, int> ExcludedByReason,
    TimeSpan Duration,
    bool TimedOut,
    bool FromCache,
    string Outcome,
    string BillingOutcome,
    string? FreshnessStatus,
    string? ParserOutcome,
    DateTimeOffset OccurredAtUtc)
{
    public static SalesGrowthScannerTelemetry Create(
        string correlationId,
        Guid tenantId,
        Guid actorId,
        ScannerQueryPlan? plan,
        ScannerTableResult? table,
        TimeSpan duration,
        string outcome,
        string billingOutcome,
        string? parserOutcome = null,
        bool timedOut = false) =>
        new(
            correlationId,
            tenantId,
            actorId,
            AliasFamily: plan?.SalesGrowth is null ? "none" : "monthly-sales-growth",
            Baseline: plan?.SalesGrowth?.Semantics.Baseline,
            BaselineOrigin: plan?.SalesGrowth?.Semantics.BaselineOrigin,
            ThresholdKind: plan?.SalesGrowth?.Semantics.ThresholdKind,
            Operator: plan?.SalesGrowth?.Semantics.ComparisonOperator,
            ThresholdValue: plan?.SalesGrowth?.Semantics.ThresholdValue,
            TargetCommonPeriod: table?.SalesGrowthMetadata?.TargetCommonPeriod,
            CoveragePercent: table?.SalesGrowthMetadata?.CoveragePercent,
            EligibleSymbolCount: table?.ExecutionFacts.EligibleSymbolCount ?? 0,
            EvaluatedSymbolCount: table?.ExecutionFacts.EvaluatedSymbolCount ?? table?.ExecutionFacts.TotalSymbolsEvaluated ?? 0,
            MatchedSymbolCount: table?.ExecutionFacts.MatchingSymbolCount ?? 0,
            ExcludedByReason: table?.ExecutionFacts.ExcludedByReason ?? new Dictionary<string, int>(),
            Duration: duration,
            TimedOut: timedOut,
            FromCache: table?.ExecutionFacts.FromCache ?? false,
            Outcome: outcome,
            BillingOutcome: billingOutcome,
            FreshnessStatus: table?.SalesGrowthMetadata?.SelectionStatus.ToString(),
            ParserOutcome: parserOutcome,
            OccurredAtUtc: DateTimeOffset.UtcNow);
}

public interface ISalesGrowthScannerTelemetrySink
{
    Task RecordAsync(SalesGrowthScannerTelemetry telemetry, CancellationToken cancellationToken);
}

public sealed class NoOpSalesGrowthScannerTelemetrySink : ISalesGrowthScannerTelemetrySink
{
    public Task RecordAsync(SalesGrowthScannerTelemetry telemetry, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

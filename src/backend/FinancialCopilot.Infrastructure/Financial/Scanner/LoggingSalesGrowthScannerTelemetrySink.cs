using FinancialCopilot.Application.Scanner;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class LoggingSalesGrowthScannerTelemetrySink(
    ILogger<LoggingSalesGrowthScannerTelemetrySink> logger) : ISalesGrowthScannerTelemetrySink
{
    public Task RecordAsync(SalesGrowthScannerTelemetry telemetry, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Sales-growth scanner telemetry correlation {CorrelationId} tenant {TenantId} actor {ActorId}: " +
            "alias={AliasFamily} baseline={Baseline} baselineOrigin={BaselineOrigin} thresholdKind={ThresholdKind} " +
            "operator={Operator} threshold={ThresholdValue} targetPeriod={TargetCommonPeriod} coverage={CoveragePercent} " +
            "eligible={EligibleSymbolCount} evaluated={EvaluatedSymbolCount} matched={MatchedSymbolCount} " +
            "excluded={ExcludedByReason} durationMs={DurationMs} timedOut={TimedOut} cache={FromCache} " +
            "outcome={Outcome} billing={BillingOutcome} freshness={FreshnessStatus} parser={ParserOutcome}.",
            telemetry.CorrelationId,
            telemetry.TenantId,
            telemetry.ActorId,
            telemetry.AliasFamily,
            telemetry.Baseline,
            telemetry.BaselineOrigin,
            telemetry.ThresholdKind,
            telemetry.Operator,
            telemetry.ThresholdValue,
            telemetry.TargetCommonPeriod,
            telemetry.CoveragePercent,
            telemetry.EligibleSymbolCount,
            telemetry.EvaluatedSymbolCount,
            telemetry.MatchedSymbolCount,
            telemetry.ExcludedByReason,
            telemetry.Duration.TotalMilliseconds,
            telemetry.TimedOut,
            telemetry.FromCache,
            telemetry.Outcome,
            telemetry.BillingOutcome,
            telemetry.FreshnessStatus,
            telemetry.ParserOutcome);
        return Task.CompletedTask;
    }
}

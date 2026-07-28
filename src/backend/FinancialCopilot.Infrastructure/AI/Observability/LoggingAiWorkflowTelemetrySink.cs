using System.Diagnostics;
using FinancialCopilot.Application.AI.Observability;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.Observability;

public sealed class LoggingAiWorkflowTelemetrySink(ILogger<LoggingAiWorkflowTelemetrySink> logger)
    : IAiWorkflowTelemetrySink
{
    public Task RecordWorkflowAsync(WorkflowTelemetry telemetry, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        activity?.SetTag(AiObservabilityActivitySource.AttrWorkflowName, telemetry.WorkflowName)
                 .SetTag(AiObservabilityActivitySource.AttrCorrelationId, telemetry.CorrelationId)
                 .SetTag(AiObservabilityActivitySource.AttrTenantId, telemetry.TenantId.ToString())
                 .SetTag(AiObservabilityActivitySource.AttrWorkflowSucceeded, telemetry.Succeeded);

        if (telemetry.DetectedIntent is not null)
        {
            activity?.SetTag(AiObservabilityActivitySource.AttrDetectedIntent, telemetry.DetectedIntent);
        }

        if (telemetry.SelectedTool is not null)
        {
            activity?.SetTag(AiObservabilityActivitySource.AttrSelectedTool, telemetry.SelectedTool);
        }

        if (telemetry.ErrorCategory.HasValue)
        {
            activity?.SetTag(AiObservabilityActivitySource.AttrErrorCategory, telemetry.ErrorCategory.Value.ToString());
        }

        if (telemetry.ErrorCode is not null)
        {
            activity?.SetTag(AiObservabilityActivitySource.AttrErrorCode, telemetry.ErrorCode);
        }

        if (telemetry.Succeeded)
        {
            logger.LogInformation(
                "AI workflow {WorkflowName} completed for correlation {CorrelationId} tenant {TenantId} in {DurationMs} ms. " +
                "Intent={DetectedIntent} Tool={SelectedTool} ProviderAttempts={AttemptCount} ToolExecutions={ToolCount}.",
                telemetry.WorkflowName,
                telemetry.CorrelationId,
                telemetry.TenantId,
                telemetry.TotalDuration.TotalMilliseconds,
                telemetry.DetectedIntent,
                telemetry.SelectedTool,
                telemetry.ProviderAttempts.Count,
                telemetry.ToolExecutions.Count);
        }
        else
        {
            logger.LogWarning(
                "AI workflow {WorkflowName} failed for correlation {CorrelationId} tenant {TenantId} after {DurationMs} ms. " +
                "ErrorCategory={ErrorCategory} ErrorCode={ErrorCode}.",
                telemetry.WorkflowName,
                telemetry.CorrelationId,
                telemetry.TenantId,
                telemetry.TotalDuration.TotalMilliseconds,
                telemetry.ErrorCategory,
                telemetry.ErrorCode);
        }

        if (telemetry.AggregatedTokenUsage is not null)
        {
            var usage = telemetry.AggregatedTokenUsage;
            logger.LogInformation(
                "AI workflow {WorkflowName} token usage for correlation {CorrelationId}: " +
                "provider={ProviderKey} model={ModelKey} input={InputTokens} output={OutputTokens} cached={CacheHit}.",
                telemetry.WorkflowName,
                telemetry.CorrelationId,
                usage.ProviderKey,
                usage.ModelKey,
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheHit);
        }

        if (telemetry.CostOutcome is not null)
        {
            var cost = telemetry.CostOutcome;
            activity?.SetTag(AiObservabilityActivitySource.AttrOperationCode, cost.OperationCode)
                     .SetTag(AiObservabilityActivitySource.AttrBillingPolicyVersion, cost.BillingPolicyVersion)
                     .SetTag(AiObservabilityActivitySource.AttrProviderReportedCost, cost.ProviderReportedCost)
                     .SetTag(AiObservabilityActivitySource.AttrBillingChargedCredits, cost.BillingChargedCredits);

            logger.LogInformation(
                "AI workflow {WorkflowName} cost telemetry for correlation {CorrelationId}: " +
                "operation={OperationCode} providerCost={ProviderCost} {Currency} " +
                "billedCredits={BilledCredits} policy={PolicyVersion} cached={IsCached}.",
                telemetry.WorkflowName,
                telemetry.CorrelationId,
                cost.OperationCode,
                cost.ProviderReportedCost,
                cost.ProviderReportedCurrency,
                cost.BillingChargedCredits,
                cost.BillingPolicyVersion,
                cost.IsCachedResponse);
        }

        return Task.CompletedTask;
    }

    public Task RecordToolExecutionAsync(ToolExecutionTrace trace, CancellationToken cancellationToken)
    {
        if (trace.Succeeded)
        {
            logger.LogInformation(
                "AI tool {ToolName} succeeded for correlation {CorrelationId} in {DurationMs} ms.",
                trace.ToolName,
                trace.CorrelationId,
                trace.Duration.TotalMilliseconds);
        }
        else
        {
            logger.LogWarning(
                "AI tool {ToolName} failed for correlation {CorrelationId} after {DurationMs} ms. " +
                "ErrorCategory={ErrorCategory} ErrorCode={ErrorCode}.",
                trace.ToolName,
                trace.CorrelationId,
                trace.Duration.TotalMilliseconds,
                trace.ErrorCategory,
                trace.ErrorCode);
        }

        return Task.CompletedTask;
    }

    public Task RecordPromptAsync(PromptTrace trace, PromptRedactionPolicy policy, CancellationToken cancellationToken)
    {
        // Content fields must be null when policy disables capture; enforce here as a safety net.
        var effectivePrompt = policy.CapturePromptContent ? trace.PromptContentSummary : null;
        var effectiveResponse = policy.CaptureResponseContent ? trace.ResponseContentSummary : null;

        if (policy.CapturePromptContent || policy.CaptureResponseContent)
        {
            logger.LogDebug(
                "AI prompt trace for correlation {CorrelationId} tenant {TenantId} workload {Workload}. " +
                "Redacted={Redacted} RetentionCategory={RetentionCategory} PolicyVersion={PolicyVersion} " +
                "PromptCaptured={PromptCaptured} ResponseCaptured={ResponseCaptured}.",
                trace.CorrelationId,
                trace.TenantId,
                trace.Workload,
                trace.Redacted,
                policy.RetentionCategory,
                policy.PolicyVersion,
                effectivePrompt is not null,
                effectiveResponse is not null);
        }
        else
        {
            // Policy disallows content capture — log only non-sensitive metadata.
            logger.LogDebug(
                "AI prompt metadata for correlation {CorrelationId} tenant {TenantId} workload {Workload}. " +
                "Content capture disabled by policy {PolicyVersion}.",
                trace.CorrelationId,
                trace.TenantId,
                trace.Workload,
                policy.PolicyVersion);
        }

        return Task.CompletedTask;
    }

    public Task RecordAiExecutionAsync(AiExecutionTrace trace, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "AI execution for correlation {CorrelationId} tenant {TenantId} workload {Workload}. " +
            "FinalStatus={FinalStatus} TotalAttempts={TotalAttempts} DurationMs={DurationMs}.",
            trace.CorrelationId,
            trace.TenantId,
            trace.Workload,
            trace.FinalStatus,
            trace.TotalAttempts,
            trace.TotalDuration.TotalMilliseconds);

        if (trace.FinalTokenUsage is not null)
        {
            var usage = trace.FinalTokenUsage;
            logger.LogInformation(
                "AI execution token usage for correlation {CorrelationId}: " +
                "provider={ProviderKey} model={ModelKey} input={InputTokens} output={OutputTokens} cached={CacheHit}.",
                trace.CorrelationId,
                usage.ProviderKey,
                usage.ModelKey,
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheHit);
        }

        foreach (var attempt in trace.ProviderAttempts)
        {
            logger.LogInformation(
                "AI provider attempt {AttemptNumber} for correlation {CorrelationId}: " +
                "provider={ProviderKey} model={ModelKey} status={Status} durationMs={DurationMs} failureCode={FailureCode}.",
                attempt.AttemptNumber,
                attempt.CorrelationId,
                attempt.ProviderKey,
                attempt.ModelKey,
                attempt.Status,
                attempt.Duration.TotalMilliseconds,
                attempt.FailureCode);
        }

        return Task.CompletedTask;
    }
}

using System.Diagnostics;

namespace FinancialCopilot.Infrastructure.AI.Observability;

// OpenTelemetry-compatible ActivitySource for AI workflow tracing.
// ActivitySource.StartActivity creates child spans of Activity.Current automatically,
// propagating W3C trace context across workflow steps. OTel SDK exporters subscribe
// by SourceName when configured; no OTel SDK package is required to emit activities.
public static class AiObservabilityActivitySource
{
    public const string SourceName = "FinancialCopilot.AI";
    public const string Version = "1.0";

    // Activity/span names — used as operation names in distributed trace explorers.
    public const string WorkflowSpan = "ai.workflow";
    public const string ToolExecutionSpan = "ai.tool_execution";
    public const string ProviderAttemptSpan = "ai.provider_attempt";

    // Attribute names aligned with OpenTelemetry semantic conventions where applicable.
    public const string AttrCorrelationId = "ai.correlation_id";
    public const string AttrTenantId = "ai.tenant_id";
    public const string AttrWorkflowName = "ai.workflow.name";
    public const string AttrWorkflowSucceeded = "ai.workflow.succeeded";
    public const string AttrDetectedIntent = "ai.workflow.detected_intent";
    public const string AttrSelectedTool = "ai.workflow.selected_tool";
    public const string AttrToolName = "ai.tool.name";
    public const string AttrToolSucceeded = "ai.tool.succeeded";
    public const string AttrProviderKey = "ai.provider.key";
    public const string AttrModelKey = "ai.model.key";
    public const string AttrAttemptNumber = "ai.attempt.number";
    public const string AttrExecutionStatus = "ai.execution.status";
    public const string AttrErrorCategory = "ai.error.category";
    public const string AttrErrorCode = "ai.error.code";
    public const string AttrInputTokens = "ai.token.input";
    public const string AttrOutputTokens = "ai.token.output";
    public const string AttrCacheHit = "ai.cache.hit";
    public const string AttrOperationCode = "ai.billing.operation_code";
    public const string AttrBillingPolicyVersion = "ai.billing.policy_version";
    public const string AttrProviderReportedCost = "ai.billing.provider_reported_cost";
    public const string AttrBillingChargedCredits = "ai.billing.charged_credits";

    private static readonly ActivitySource Source = new(SourceName, Version);

    // Exposes the source so the host can register it with an OTel SDK listener.
    public static ActivitySource GetSource() => Source;

    // Starts a workflow-level span. The caller is responsible for stopping the returned
    // activity (e.g. via using or try/finally). Returns null when no listener is active,
    // consistent with ActivitySource contract.
    public static Activity? StartWorkflow(string workflowName, string correlationId, Guid tenantId) =>
        Source.StartActivity(WorkflowSpan, ActivityKind.Internal)?
            .SetTag(AttrWorkflowName, workflowName)
            .SetTag(AttrCorrelationId, correlationId)
            .SetTag(AttrTenantId, tenantId.ToString());

    // Starts a tool execution span as a child of the current ambient activity.
    public static Activity? StartToolExecution(string toolName, string correlationId) =>
        Source.StartActivity(ToolExecutionSpan, ActivityKind.Internal)?
            .SetTag(AttrToolName, toolName)
            .SetTag(AttrCorrelationId, correlationId);

    // Starts a provider attempt span. ActivityKind.Client signals an outbound AI provider call.
    public static Activity? StartProviderAttempt(
        string providerKey,
        string modelKey,
        int attemptNumber,
        string correlationId) =>
        Source.StartActivity(ProviderAttemptSpan, ActivityKind.Client)?
            .SetTag(AttrProviderKey, providerKey)
            .SetTag(AttrModelKey, modelKey)
            .SetTag(AttrAttemptNumber, attemptNumber)
            .SetTag(AttrCorrelationId, correlationId);
}

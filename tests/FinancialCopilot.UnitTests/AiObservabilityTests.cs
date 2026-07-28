using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Observability;

namespace FinancialCopilot.UnitTests;

public sealed class AiObservabilityTests
{
    private static readonly Guid TenantId = Guid.Parse("8c9be50e-01e9-428c-8510-fb88cd739003");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    // --- PromptRedactionPolicy ---

    [Fact]
    public void PromptRedactionPolicy_DefaultSafe_DisablesBothContentCaptures()
    {
        var policy = PromptRedactionPolicy.DefaultSafe;

        Assert.False(policy.CapturePromptContent);
        Assert.False(policy.CaptureResponseContent);
        Assert.True(policy.RedactPii);
        Assert.Null(policy.RetentionCategory);
        Assert.Equal("v1", policy.PolicyVersion);
    }

    [Fact]
    public void PromptRedactionPolicy_ExplicitOptIn_AllowsContentCapture()
    {
        var policy = new PromptRedactionPolicy(
            CapturePromptContent: true,
            CaptureResponseContent: true,
            RedactPii: true,
            RetentionCategory: "debug-7d",
            PolicyVersion: "v1");

        Assert.True(policy.CapturePromptContent);
        Assert.True(policy.CaptureResponseContent);
        Assert.True(policy.RedactPii);
        Assert.Equal("debug-7d", policy.RetentionCategory);
    }

    // --- AiErrorCategory ---

    [Fact]
    public void AiErrorCategory_ContainsAllRequiredDomainClassifications()
    {
        var categories = Enum.GetNames<AiErrorCategory>();

        Assert.Contains("Validation", categories);
        Assert.Contains("Clarification", categories);
        Assert.Contains("ProviderFailure", categories);
        Assert.Contains("Timeout", categories);
        Assert.Contains("ToolFailure", categories);
        Assert.Contains("DataInsufficiency", categories);
        Assert.Contains("BillingRejection", categories);
        Assert.Contains("PersistenceFailure", categories);
    }

    // --- IAiWorkflowTelemetrySink contract ---

    [Fact]
    public async Task WorkflowSink_RecordWorkflow_CapturesSink_Fields()
    {
        var sink = new CapturingWorkflowTelemetrySink();
        var telemetry = BuildWorkflowTelemetry(succeeded: true);

        await sink.RecordWorkflowAsync(telemetry, CancellationToken.None);

        var recorded = Assert.Single(sink.Workflows);
        Assert.Equal(telemetry.CorrelationId, recorded.CorrelationId);
        Assert.Equal(telemetry.TenantId, recorded.TenantId);
        Assert.True(recorded.Succeeded);
    }

    [Fact]
    public async Task WorkflowSink_RecordWorkflow_CapturesFailureCategory()
    {
        var sink = new CapturingWorkflowTelemetrySink();
        var telemetry = BuildWorkflowTelemetry(
            succeeded: false,
            errorCategory: AiErrorCategory.BillingRejection,
            errorCode: "insufficient_credits");

        await sink.RecordWorkflowAsync(telemetry, CancellationToken.None);

        var recorded = Assert.Single(sink.Workflows);
        Assert.False(recorded.Succeeded);
        Assert.Equal(AiErrorCategory.BillingRejection, recorded.ErrorCategory);
        Assert.Equal("insufficient_credits", recorded.ErrorCode);
    }

    [Fact]
    public async Task WorkflowSink_RecordPrompt_DefaultPolicy_ContentFieldsRemainNull()
    {
        var sink = new CapturingWorkflowTelemetrySink();
        // Caller must respect policy: content fields are null when policy disallows capture.
        var trace = new PromptTrace(
            "corr-redact-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            PromptContentSummary: null,
            ResponseContentSummary: null,
            Redacted: true,
            CapturedAt: Now);

        await sink.RecordPromptAsync(trace, PromptRedactionPolicy.DefaultSafe, CancellationToken.None);

        var (recorded, policy) = Assert.Single(sink.Prompts);
        Assert.Null(recorded.PromptContentSummary);
        Assert.Null(recorded.ResponseContentSummary);
        Assert.True(recorded.Redacted);
        Assert.False(policy.CapturePromptContent);
        Assert.False(policy.CaptureResponseContent);
    }

    [Fact]
    public async Task WorkflowSink_RecordToolExecution_CapturesSuccessTrace()
    {
        var sink = new CapturingWorkflowTelemetrySink();
        var trace = new ToolExecutionTrace(
            "corr-tool-1",
            "ScannerTool",
            Succeeded: true,
            Duration: TimeSpan.FromMilliseconds(350),
            ErrorCategory: null,
            ErrorCode: null,
            ExecutedAt: Now);

        await sink.RecordToolExecutionAsync(trace, CancellationToken.None);

        var recorded = Assert.Single(sink.ToolExecutions);
        Assert.True(recorded.Succeeded);
        Assert.Equal("ScannerTool", recorded.ToolName);
        Assert.Null(recorded.ErrorCategory);
    }

    [Fact]
    public async Task WorkflowSink_RecordToolExecution_CapturesFailureCategory()
    {
        var sink = new CapturingWorkflowTelemetrySink();
        var trace = new ToolExecutionTrace(
            "corr-tool-2",
            "ScannerTool",
            Succeeded: false,
            Duration: TimeSpan.FromMilliseconds(120),
            ErrorCategory: AiErrorCategory.DataInsufficiency,
            ErrorCode: "data_not_available",
            ExecutedAt: Now);

        await sink.RecordToolExecutionAsync(trace, CancellationToken.None);

        var recorded = Assert.Single(sink.ToolExecutions);
        Assert.False(recorded.Succeeded);
        Assert.Equal(AiErrorCategory.DataInsufficiency, recorded.ErrorCategory);
        Assert.Equal("data_not_available", recorded.ErrorCode);
    }

    [Fact]
    public async Task WorkflowSink_RecordAiExecution_CapturesProviderAttemptsAndTokenUsage()
    {
        var sink = new CapturingWorkflowTelemetrySink();
        var attempt = new ProviderLatency(
            "corr-exec-1",
            "TestProvider",
            "model-v1",
            AttemptNumber: 1,
            Duration: TimeSpan.FromMilliseconds(800),
            Status: AiExecutionStatus.Completed,
            FailureCode: null,
            AttemptedAt: Now);
        var tokenUsage = new TokenUsage("corr-exec-1", "TestProvider", "model-v1", 150, 80, CacheHit: false);
        var trace = new AiExecutionTrace(
            "corr-exec-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            ProviderAttempts: [attempt],
            FinalStatus: AiExecutionStatus.Completed,
            TotalAttempts: 1,
            TotalDuration: TimeSpan.FromMilliseconds(800),
            FinalTokenUsage: tokenUsage);

        await sink.RecordAiExecutionAsync(trace, CancellationToken.None);

        var recorded = Assert.Single(sink.AiExecutions);
        Assert.Equal(AiExecutionStatus.Completed, recorded.FinalStatus);
        var recordedAttempt = Assert.Single(recorded.ProviderAttempts);
        Assert.Equal("TestProvider", recordedAttempt.ProviderKey);
        Assert.NotNull(recorded.FinalTokenUsage);
        Assert.Equal(150, recorded.FinalTokenUsage.InputTokens);
        Assert.Equal(80, recorded.FinalTokenUsage.OutputTokens);
    }

    [Fact]
    public async Task WorkflowSink_RecordAiExecution_CapturesFallbackAttempts()
    {
        var sink = new CapturingWorkflowTelemetrySink();
        var failedAttempt = new ProviderLatency(
            "corr-fallback-1", "ProviderA", "model-a",
            1, TimeSpan.FromMilliseconds(200),
            AiExecutionStatus.TimedOut, "timeout", Now);
        var successAttempt = new ProviderLatency(
            "corr-fallback-1", "ProviderB", "model-b",
            2, TimeSpan.FromMilliseconds(600),
            AiExecutionStatus.Completed, null, Now.AddMilliseconds(200));
        var trace = new AiExecutionTrace(
            "corr-fallback-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            ProviderAttempts: [failedAttempt, successAttempt],
            FinalStatus: AiExecutionStatus.Completed,
            TotalAttempts: 2,
            TotalDuration: TimeSpan.FromMilliseconds(800),
            FinalTokenUsage: null);

        await sink.RecordAiExecutionAsync(trace, CancellationToken.None);

        var recorded = Assert.Single(sink.AiExecutions);
        Assert.Equal(2, recorded.TotalAttempts);
        Assert.Equal(2, recorded.ProviderAttempts.Count);
        Assert.Contains(recorded.ProviderAttempts, a => a.Status == AiExecutionStatus.TimedOut);
        Assert.Contains(recorded.ProviderAttempts, a => a.Status == AiExecutionStatus.Completed);
    }

    // --- CostTelemetry reconciliation ---

    [Fact]
    public void CostTelemetry_RecordsBillingPolicyVersion_ForReconciliation()
    {
        var cost = new CostTelemetry(
            "corr-cost-1",
            TenantId,
            "AiQuery.Scanner",
            ProviderReportedCost: null,
            ProviderReportedCurrency: null,
            BillingChargedCredits: 1m,
            BillingPolicyVersion: "v1",
            IsCachedResponse: false);

        Assert.Equal("v1", cost.BillingPolicyVersion);
        Assert.Equal("AiQuery.Scanner", cost.OperationCode);
        Assert.Equal(1m, cost.BillingChargedCredits);
    }

    [Fact]
    public void WorkflowTelemetry_CorrelatesAllSubTracesViaCorrelationId()
    {
        const string correlationId = "unified-corr-1";
        var workflowTelemetry = BuildWorkflowTelemetry(succeeded: true, correlationId: correlationId);
        var toolTrace = new ToolExecutionTrace(
            correlationId, "ScannerTool", true, TimeSpan.Zero, null, null, Now);
        var providerLatency = new ProviderLatency(
            correlationId, "P", "M", 1, TimeSpan.Zero,
            AiExecutionStatus.Completed, null, Now);

        Assert.Equal(correlationId, workflowTelemetry.CorrelationId);
        Assert.Equal(correlationId, toolTrace.CorrelationId);
        Assert.Equal(correlationId, providerLatency.CorrelationId);
    }

    [Fact]
    public void WorkflowTelemetry_EmbeddedProviderAttempts_ShareCorrelationId()
    {
        const string correlationId = "embed-corr-1";
        var attempt = new ProviderLatency(
            correlationId, "P", "M", 1, TimeSpan.Zero,
            AiExecutionStatus.Completed, null, Now);
        var telemetry = BuildWorkflowTelemetry(succeeded: true, correlationId: correlationId)
            with { ProviderAttempts = [attempt] };

        Assert.All(telemetry.ProviderAttempts, a => Assert.Equal(correlationId, a.CorrelationId));
    }

    // --- Helpers ---

    private static WorkflowTelemetry BuildWorkflowTelemetry(
        bool succeeded,
        string correlationId = "scanner-workflow-1",
        AiErrorCategory? errorCategory = null,
        string? errorCode = null) =>
        new(
            correlationId,
            TenantId,
            "ScannerWorkflow",
            succeeded,
            TotalDuration: TimeSpan.FromSeconds(1.5),
            DetectedIntent: succeeded ? "scanner" : null,
            SelectedTool: succeeded ? "ScannerTool" : null,
            ErrorCategory: errorCategory,
            ErrorCode: errorCode,
            ProviderAttempts: [],
            ToolExecutions: [],
            AggregatedTokenUsage: null,
            CostOutcome: null,
            StartedAt: Now);

    private sealed class CapturingWorkflowTelemetrySink : IAiWorkflowTelemetrySink
    {
        public List<WorkflowTelemetry> Workflows { get; } = [];
        public List<ToolExecutionTrace> ToolExecutions { get; } = [];
        public List<(PromptTrace Trace, PromptRedactionPolicy Policy)> Prompts { get; } = [];
        public List<AiExecutionTrace> AiExecutions { get; } = [];

        public Task RecordWorkflowAsync(WorkflowTelemetry telemetry, CancellationToken cancellationToken)
        {
            Workflows.Add(telemetry);
            return Task.CompletedTask;
        }

        public Task RecordToolExecutionAsync(ToolExecutionTrace trace, CancellationToken cancellationToken)
        {
            ToolExecutions.Add(trace);
            return Task.CompletedTask;
        }

        public Task RecordPromptAsync(PromptTrace trace, PromptRedactionPolicy policy, CancellationToken cancellationToken)
        {
            Prompts.Add((trace, policy));
            return Task.CompletedTask;
        }

        public Task RecordAiExecutionAsync(AiExecutionTrace trace, CancellationToken cancellationToken)
        {
            AiExecutions.Add(trace);
            return Task.CompletedTask;
        }
    }
}

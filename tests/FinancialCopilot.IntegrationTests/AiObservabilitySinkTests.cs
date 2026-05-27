using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Observability;
using FinancialCopilot.Infrastructure.AI.Observability;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.IntegrationTests;

// Tests the Infrastructure LoggingAiWorkflowTelemetrySink log output behavior directly,
// using a capturing logger to verify structured field emission without a running database.
public sealed class AiObservabilitySinkTests
{
    private static readonly Guid TenantId = Guid.Parse("8c9be50e-01e9-428c-8510-fb88cd739003");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    [Fact]
    public async Task Sink_RecordWorkflow_LogsCompletionAtInformationLevel()
    {
        var logs = new CapturingLogger<LoggingAiWorkflowTelemetrySink>();
        var sink = new LoggingAiWorkflowTelemetrySink(logs);

        await sink.RecordWorkflowAsync(BuildWorkflowTelemetry(succeeded: true), CancellationToken.None);

        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public async Task Sink_RecordWorkflow_LogsWarningOnFailure()
    {
        var logs = new CapturingLogger<LoggingAiWorkflowTelemetrySink>();
        var sink = new LoggingAiWorkflowTelemetrySink(logs);
        var telemetry = BuildWorkflowTelemetry(succeeded: false,
            errorCategory: AiErrorCategory.ProviderFailure, errorCode: "provider_unavailable");

        await sink.RecordWorkflowAsync(telemetry, CancellationToken.None);

        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("failed"));
    }

    [Fact]
    public async Task Sink_RecordWorkflow_LogsCostTelemetry_WhenPresent()
    {
        var logs = new CapturingLogger<LoggingAiWorkflowTelemetrySink>();
        var sink = new LoggingAiWorkflowTelemetrySink(logs);
        var cost = new CostTelemetry(
            "sink-cost-1", TenantId, "AiQuery.Scanner",
            0.001m, "USD", 1m, "v1", false);
        var telemetry = BuildWorkflowTelemetry(succeeded: true) with { CostOutcome = cost };

        await sink.RecordWorkflowAsync(telemetry, CancellationToken.None);

        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("AiQuery.Scanner"));
    }

    [Fact]
    public async Task Sink_RecordWorkflow_LogsTokenUsage_WhenPresent()
    {
        var logs = new CapturingLogger<LoggingAiWorkflowTelemetrySink>();
        var sink = new LoggingAiWorkflowTelemetrySink(logs);
        var tokenUsage = new TokenUsage("sink-token-1", "Provider", "model", 200, 100, false);
        var telemetry = BuildWorkflowTelemetry(succeeded: true) with { AggregatedTokenUsage = tokenUsage };

        await sink.RecordWorkflowAsync(telemetry, CancellationToken.None);

        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("token usage"));
    }

    [Fact]
    public async Task Sink_RecordPrompt_DoesNotEmitSensitiveContent_WhenDefaultPolicyApplied()
    {
        var logs = new CapturingLogger<LoggingAiWorkflowTelemetrySink>();
        var sink = new LoggingAiWorkflowTelemetrySink(logs);
        var trace = new PromptTrace(
            "sink-redact-1", TenantId, AiWorkloadKind.ScannerParsing,
            PromptContentSummary: "find low PE stocks — CONFIDENTIAL",
            ResponseContentSummary: "{\"intent\":\"scanner\"} — CONFIDENTIAL",
            Redacted: false,
            CapturedAt: Now);

        await sink.RecordPromptAsync(trace, PromptRedactionPolicy.DefaultSafe, CancellationToken.None);

        // Sensitive keywords must never reach any log entry.
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("CONFIDENTIAL"));
        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Debug && e.Message.Contains("Content capture disabled"));
    }

    [Fact]
    public async Task Sink_RecordToolExecution_LogsSuccessAtInformationLevel()
    {
        var logs = new CapturingLogger<LoggingAiWorkflowTelemetrySink>();
        var sink = new LoggingAiWorkflowTelemetrySink(logs);
        var trace = new ToolExecutionTrace(
            "sink-tool-1", "ScannerTool", true,
            TimeSpan.FromMilliseconds(300), null, null, Now);

        await sink.RecordToolExecutionAsync(trace, CancellationToken.None);

        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("ScannerTool") &&
            e.Message.Contains("succeeded"));
    }

    [Fact]
    public async Task Sink_RecordToolExecution_LogsWarning_WhenFailed()
    {
        var logs = new CapturingLogger<LoggingAiWorkflowTelemetrySink>();
        var sink = new LoggingAiWorkflowTelemetrySink(logs);
        var trace = new ToolExecutionTrace(
            "sink-tool-fail-1", "ScannerTool", false,
            TimeSpan.FromMilliseconds(50),
            AiErrorCategory.DataInsufficiency, "data_not_available", Now);

        await sink.RecordToolExecutionAsync(trace, CancellationToken.None);

        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("ScannerTool") &&
            e.Message.Contains("failed"));
    }

    [Fact]
    public async Task Sink_RecordAiExecution_LogsProviderAttemptDetails()
    {
        var logs = new CapturingLogger<LoggingAiWorkflowTelemetrySink>();
        var sink = new LoggingAiWorkflowTelemetrySink(logs);
        var failedAttempt = new ProviderLatency(
            "sink-fallback-1", "ProviderA", "model-a",
            1, TimeSpan.FromMilliseconds(200),
            AiExecutionStatus.TimedOut, "timeout", Now);
        var successAttempt = new ProviderLatency(
            "sink-fallback-1", "ProviderB", "model-b",
            2, TimeSpan.FromMilliseconds(600),
            AiExecutionStatus.Completed, null, Now.AddMilliseconds(200));
        var trace = new AiExecutionTrace(
            "sink-fallback-1", TenantId, AiWorkloadKind.ScannerParsing,
            [failedAttempt, successAttempt],
            AiExecutionStatus.Completed, 2,
            TimeSpan.FromMilliseconds(800), null);

        await sink.RecordAiExecutionAsync(trace, CancellationToken.None);

        var attemptEntries = logs.Entries
            .Where(e => e.Message.Contains("provider attempt"))
            .ToList();
        Assert.Equal(2, attemptEntries.Count);
        Assert.Contains(attemptEntries, e => e.Message.Contains("TimedOut"));
        Assert.Contains(attemptEntries, e => e.Message.Contains("Completed"));
    }

    // --- Helpers ---

    private static WorkflowTelemetry BuildWorkflowTelemetry(
        bool succeeded,
        AiErrorCategory? errorCategory = null,
        string? errorCode = null) =>
        new(
            "sink-workflow-1",
            TenantId,
            "ScannerWorkflow",
            succeeded,
            TotalDuration: TimeSpan.FromSeconds(1),
            DetectedIntent: succeeded ? "scanner" : null,
            SelectedTool: succeeded ? "ScannerTool" : null,
            ErrorCategory: errorCategory,
            ErrorCode: errorCode,
            ProviderAttempts: [],
            ToolExecutions: [],
            AggregatedTokenUsage: null,
            CostOutcome: null,
            StartedAt: Now);

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

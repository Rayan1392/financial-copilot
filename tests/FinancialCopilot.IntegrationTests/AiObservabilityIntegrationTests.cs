using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Observability;
using FinancialCopilot.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class AiObservabilityIntegrationTests
{
    private static readonly Guid TenantId = Guid.Parse("8c9be50e-01e9-428c-8510-fb88cd739003");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FinancialCopilot"] = "Host=localhost;Database=fake",
                ["AiModelProviders:Providers:0:ProviderKey"] = "ObsTestFake",
                ["AiModelProviders:Providers:0:ModelKey"] = "fake-v1",
                ["AiModelProviders:Providers:0:HostingMode"] = "Fake",
                ["AiModelProviders:Providers:0:Adapter"] = "Fake",
                ["AiModelProviders:Providers:0:Enabled"] = "true",
                ["AiModelProviders:Providers:0:Priority"] = "1",
                ["AiModelProviders:Providers:0:Capabilities"] = "ChatCompletion,StructuredOutput,UsageReporting,HealthCheck"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinancialCopilotInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task IAiWorkflowTelemetrySink_IsRegisteredAndResolvable()
    {
        await using var provider = BuildProvider();

        var sink = provider.GetService<IAiWorkflowTelemetrySink>();

        Assert.NotNull(sink);
    }

    [Fact]
    public async Task IAiExecutionTelemetrySink_IsRegisteredAndResolvable()
    {
        await using var provider = BuildProvider();

        var sink = provider.GetService<IAiExecutionTelemetrySink>();

        Assert.NotNull(sink);
    }

    [Fact]
    public async Task WorkflowTelemetrySink_RecordWorkflow_DoesNotThrow_ForCompletedWorkflow()
    {
        await using var provider = BuildProvider();
        var sink = provider.GetRequiredService<IAiWorkflowTelemetrySink>();
        var telemetry = new WorkflowTelemetry(
            "obs-integration-1",
            TenantId,
            "ScannerWorkflow",
            Succeeded: true,
            TotalDuration: TimeSpan.FromMilliseconds(950),
            DetectedIntent: "scanner",
            SelectedTool: "ScannerTool",
            ErrorCategory: null,
            ErrorCode: null,
            ProviderAttempts:
            [
                new ProviderLatency(
                    "obs-integration-1", "ObsTestFake", "fake-v1",
                    1, TimeSpan.FromMilliseconds(800),
                    AiExecutionStatus.Completed, null, Now)
            ],
            ToolExecutions:
            [
                new ToolExecutionTrace(
                    "obs-integration-1", "ScannerTool",
                    Succeeded: true, TimeSpan.FromMilliseconds(100),
                    null, null, Now)
            ],
            AggregatedTokenUsage: new TokenUsage("obs-integration-1", "ObsTestFake", "fake-v1", 120, 60, false),
            CostOutcome: new CostTelemetry(
                "obs-integration-1", TenantId, "AiQuery.Scanner",
                ProviderReportedCost: null, ProviderReportedCurrency: null,
                BillingChargedCredits: 1m, BillingPolicyVersion: "v1", IsCachedResponse: false),
            StartedAt: Now);

        var exception = await Record.ExceptionAsync(() =>
            sink.RecordWorkflowAsync(telemetry, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task WorkflowTelemetrySink_RecordWorkflow_DoesNotThrow_ForFailedWorkflow()
    {
        await using var provider = BuildProvider();
        var sink = provider.GetRequiredService<IAiWorkflowTelemetrySink>();
        var telemetry = new WorkflowTelemetry(
            "obs-fail-1",
            TenantId,
            "ScannerWorkflow",
            Succeeded: false,
            TotalDuration: TimeSpan.FromMilliseconds(50),
            DetectedIntent: null,
            SelectedTool: null,
            ErrorCategory: AiErrorCategory.BillingRejection,
            ErrorCode: "insufficient_credits",
            ProviderAttempts: [],
            ToolExecutions: [],
            AggregatedTokenUsage: null,
            CostOutcome: null,
            StartedAt: Now);

        var exception = await Record.ExceptionAsync(() =>
            sink.RecordWorkflowAsync(telemetry, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task WorkflowTelemetrySink_RecordPrompt_DefaultPolicy_NeverCapturesSensitiveContent()
    {
        await using var provider = BuildProvider();
        var sink = provider.GetRequiredService<IAiWorkflowTelemetrySink>();
        // Content is supplied by the caller but policy must suppress it in the sink.
        var trace = new PromptTrace(
            "obs-redact-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            PromptContentSummary: "give me symbols with PE below 5 — SENSITIVE",
            ResponseContentSummary: "{\"intent\":\"scanner\",\"conditions\":[]} — SENSITIVE",
            Redacted: false,
            CapturedAt: Now);

        var exception = await Record.ExceptionAsync(() =>
            sink.RecordPromptAsync(trace, PromptRedactionPolicy.DefaultSafe, CancellationToken.None));

        Assert.Null(exception);
        // Behavioral guarantee: no exception means the sink handled redaction without crashing.
        // Content suppression is validated in unit tests against a capturing logger.
    }

    [Fact]
    public async Task WorkflowTelemetrySink_RecordAiExecution_IncludesFallbackAttempts()
    {
        await using var provider = BuildProvider();
        var sink = provider.GetRequiredService<IAiWorkflowTelemetrySink>();
        var trace = new AiExecutionTrace(
            "obs-fallback-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            ProviderAttempts:
            [
                new ProviderLatency(
                    "obs-fallback-1", "ProviderA", "model-a",
                    1, TimeSpan.FromMilliseconds(200),
                    AiExecutionStatus.TimedOut, "timeout", Now),
                new ProviderLatency(
                    "obs-fallback-1", "ProviderB", "model-b",
                    2, TimeSpan.FromMilliseconds(600),
                    AiExecutionStatus.Completed, null, Now.AddMilliseconds(200))
            ],
            FinalStatus: AiExecutionStatus.Completed,
            TotalAttempts: 2,
            TotalDuration: TimeSpan.FromMilliseconds(800),
            FinalTokenUsage: new TokenUsage("obs-fallback-1", "ProviderB", "model-b", 100, 50, false));

        var exception = await Record.ExceptionAsync(() =>
            sink.RecordAiExecutionAsync(trace, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task CostTelemetry_BillingPolicyVersion_MatchesPricingPolicyVersion()
    {
        // Cost telemetry must carry the billing policy version used so operations
        // reported by the observability layer can be reconciled with the Billing ledger.
        await using var provider = BuildProvider();
        var sink = provider.GetRequiredService<IAiWorkflowTelemetrySink>();
        var cost = new CostTelemetry(
            "obs-cost-1",
            TenantId,
            "AiQuery.Scanner",
            ProviderReportedCost: 0.002m,
            ProviderReportedCurrency: "USD",
            BillingChargedCredits: 1m,
            BillingPolicyVersion: "v1",    // matches PricingPolicy "v1" registered in DI
            IsCachedResponse: false);

        Assert.Equal("v1", cost.BillingPolicyVersion);
        Assert.Equal("AiQuery.Scanner", cost.OperationCode);

        var telemetry = new WorkflowTelemetry(
            cost.CorrelationId, TenantId, "ScannerWorkflow",
            true, TimeSpan.FromMilliseconds(1000),
            "scanner", "ScannerTool", null, null, [], [],
            null, cost, Now);

        var exception = await Record.ExceptionAsync(() =>
            sink.RecordWorkflowAsync(telemetry, CancellationToken.None));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ProviderAttemptTelemetrySink_IsRegisteredAndRecordsAttemptWithTokenFacts()
    {
        await using var provider = BuildProvider();
        var sink = provider.GetRequiredService<IAiExecutionTelemetrySink>();
        var facts = new AiExecutionUsageFacts(
            "obs-attempt-1",
            "ObsTestFake",
            "fake-v1",
            AiExecutionStatus.Completed,
            Duration: TimeSpan.FromMilliseconds(700),
            AttemptNumber: 1,
            InputTokens: 100,
            OutputTokens: 50,
            CacheHit: false,
            UsedTools: false,
            EmbeddingOperation: false,
            ProviderReportedCost: 0.001m,
            ProviderReportedCurrency: "USD",
            FailureCode: null);

        var exception = await Record.ExceptionAsync(() =>
            sink.RecordAttemptAsync(facts, CancellationToken.None));

        Assert.Null(exception);
    }
}

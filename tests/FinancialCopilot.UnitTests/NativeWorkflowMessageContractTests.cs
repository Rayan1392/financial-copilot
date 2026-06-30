using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 056 / Order 60 — verifies that the typed workflow message contracts used by
/// <see cref="FinancialCopilotWorkflowDefinition"/> form a valid, non-lossy chain from
/// the initial request through to the persistence-completed stage.
/// </summary>
public sealed class NativeWorkflowMessageContractTests
{
    private static readonly Guid SampleTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SampleActorId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly AiQueryRequest SampleRequest = new(
        Message: "سهام با P/E زیر ۱۰",
        TenantId: SampleTenantId,
        ActorId: SampleActorId,
        CorrelationId: "corr-001");

    private static readonly DateTimeOffset SampleNow = new(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);

    private static readonly AuthorizedMemoryContext EmptyMemory =
        new(Items: [], Disclosures: [], OptionalMemoryEnabled: false);

    [Fact]
    public void WorkflowStartMessage_CarriesAllInitialFields()
    {
        var conversationId = Guid.NewGuid();
        var msg = new WorkflowStartMessage(SampleRequest, conversationId, true, SampleNow);

        Assert.Equal(SampleRequest, msg.Request);
        Assert.Equal(conversationId, msg.ConversationId);
        Assert.True(msg.CreateConversation);
        Assert.Equal(SampleNow, msg.Now);
    }

    [Fact]
    public void MemoryRetrievedMessage_AddsMemoryContextAndEnrichedMessage()
    {
        var conversationId = Guid.NewGuid();
        var msg = new MemoryRetrievedMessage(
            SampleRequest, conversationId, true, SampleNow,
            EmptyMemory, "enriched text");

        Assert.Equal(EmptyMemory, msg.MemoryContext);
        Assert.Equal("enriched text", msg.EnrichedMessage);
    }

    [Fact]
    public void BillingReservedMessage_PreservesEnrichedMessageAndNullReservation()
    {
        var conversationId = Guid.NewGuid();
        var msg = new BillingReservedMessage(
            SampleRequest, conversationId, true, SampleNow,
            EmptyMemory, "enriched", Reservation: null);

        Assert.Equal("enriched", msg.EnrichedMessage);
        Assert.Null(msg.Reservation);
    }

    [Fact]
    public void AgentExecutedMessage_CarriesNullToolResultsForUnknownIntent()
    {
        var conversationId = Guid.NewGuid();
        var modelClient = new StubAiModelClient();

        var msg = new AgentExecutedMessage(
            SampleRequest, conversationId, true, SampleNow,
            EmptyMemory, Reservation: null,
            AgentResponseText: "some text",
            ScannerResult: null,
            LookupResult: null,
            ComprehensiveAnalysisResult: null,
            FinancialStatementAnalysisResult: null,
            ProductRevenueMixResult: null,
            MonthlyActivityTrendResult: null,
            MonthlySalesQualityRankingResult: null,
            CompletionStatus: "Completed",
            FromCache: false,
            ModelClient: modelClient,
            Usage: null);

        Assert.Null(msg.ScannerResult);
        Assert.Null(msg.LookupResult);
        Assert.Equal("some text", msg.AgentResponseText);
        Assert.Equal("Completed", msg.CompletionStatus);
        Assert.False(msg.FromCache);
    }

    [Fact]
    public void PersistenceCompletedMessage_HasDistinctUserAndAssistantMessageIds()
    {
        var conversationId = Guid.NewGuid();
        var userMsgId = Guid.NewGuid();
        var assistantMsgId = Guid.NewGuid();
        var modelClient = new StubAiModelClient();

        var msg = new PersistenceCompletedMessage(
            SampleRequest, conversationId,
            userMsgId, assistantMsgId,
            DetectedIntent.Unknown,
            ClarificationRequired: false,
            ClarificationMessage: null,
            ScannerResult: null,
            LookupResult: null,
            ComprehensiveAnalysisResult: null,
            FinancialStatementAnalysisResult: null,
            ProductRevenueMixResult: null,
            MonthlyActivityTrendResult: null,
            MonthlySalesQualityRankingResult: null,
            ExplainableAnswer: null,
            ConfidenceScore: null,
            TextAnswer: "answered",
            Usage: null,
            Disclosures: null,
            ModelClient: modelClient,
            WorkflowCorrelationId: "corr-001");

        Assert.NotEqual(userMsgId, assistantMsgId);
        Assert.Equal(userMsgId, msg.UserMessageId);
        Assert.Equal(assistantMsgId, msg.AssistantMessageId);
        Assert.Equal("corr-001", msg.WorkflowCorrelationId);
    }

    [Fact]
    public void MessageChain_IsNonLossy_RequestIdentityPreservedThroughAllSteps()
    {
        // Verify that every field set in WorkflowStartMessage still exists in PersistenceCompletedMessage
        // via the intermediate records — a compile-time + runtime contract check.
        var conversationId = Guid.NewGuid();
        var modelClient = new StubAiModelClient();

        var start = new WorkflowStartMessage(SampleRequest, conversationId, true, SampleNow);

        var memory = new MemoryRetrievedMessage(
            start.Request, start.ConversationId, start.CreateConversation,
            start.Now, EmptyMemory, "msg");

        var billing = new BillingReservedMessage(
            memory.Request, memory.ConversationId, memory.CreateConversation,
            memory.Now, memory.MemoryContext, memory.EnrichedMessage, null);

        var agent = new AgentExecutedMessage(
            billing.Request, billing.ConversationId, billing.CreateConversation,
            billing.Now, billing.MemoryContext, billing.Reservation,
            "answer", null, null, null, null, null, null, null, "Completed", false, modelClient, null);

        var results = new ResultsComputedMessage(
            agent.Request, agent.ConversationId, agent.CreateConversation, agent.Now,
            agent.MemoryContext, agent.Reservation,
            agent.AgentResponseText, agent.ScannerResult, agent.LookupResult,
            agent.ComprehensiveAnalysisResult, agent.FinancialStatementAnalysisResult, agent.ProductRevenueMixResult, agent.MonthlyActivityTrendResult,
            agent.MonthlySalesQualityRankingResult,
            agent.CompletionStatus, agent.FromCache, agent.ModelClient,
            DetectedIntent.Unknown, false, null, null, null, "answer", null);

        var persistence = new PersistenceCompletedMessage(
            results.Request, results.ConversationId,
            Guid.NewGuid(), Guid.NewGuid(),
            results.DetectedIntent, results.ClarificationRequired, results.ClarificationMessage,
            results.ScannerResult, results.LookupResult, results.ComprehensiveAnalysisResult, results.FinancialStatementAnalysisResult, results.ProductRevenueMixResult, results.MonthlyActivityTrendResult,
            results.MonthlySalesQualityRankingResult,
            results.ExplainableAnswer, results.ConfidenceScore,
            "answer", null, null, results.ModelClient,
            results.Request.CorrelationId);

        Assert.Equal(SampleRequest, persistence.Request);
        Assert.Equal(conversationId, persistence.ConversationId);
        Assert.Equal("corr-001", persistence.WorkflowCorrelationId);
        Assert.Equal(DetectedIntent.Unknown, persistence.DetectedIntent);
    }

    // ── Stub ─────────────────────────────────────────────────────────────────

    private sealed class StubAiModelClient : IAiModelClient
    {
        public AiModelProviderDescriptor Descriptor { get; } = new(
            ProviderKey: "stub-provider",
            ModelKey: "stub-model",
            HostingMode: AiProviderHostingMode.Hosted,
            Capabilities: AiModelCapability.ChatCompletion,
            Enabled: true,
            Priority: 0);

        public Task<AiModelResult> CompleteAsync(
            AiModelRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
            AiModelRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}

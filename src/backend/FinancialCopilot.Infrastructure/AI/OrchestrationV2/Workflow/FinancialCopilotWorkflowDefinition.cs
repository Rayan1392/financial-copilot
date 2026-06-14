using System.Diagnostics;
using System.Text;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Bridge;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.InProc;
using Microsoft.Agents.AI.Workflows.Observability;
using Microsoft.Extensions.AI;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

/// <summary>
/// Builds and executes the native Microsoft Agent Framework Workflow graph for a single AI
/// query (spec 056 / order 60). Replaces the imperative C# chain in
/// <see cref="FinancialCopilotAgentWorkflowRunner"/> with an explicit step-executor graph,
/// enabling durable execution, step-level observability, and future multi-agent expansion.
/// </summary>
internal sealed class FinancialCopilotWorkflowDefinition(
    IConversationRepository conversationRepository,
    IAiModelProviderResolver providerResolver,
    IAiExecutionUsageAccumulator usageAccumulator,
    ScannerToolAdapter scannerAdapter,
    SymbolLookupToolAdapter lookupAdapter,
    ComprehensiveAnalysisToolAdapter comprehensiveAnalysisAdapter,
    ExplainableAnswerAdapter explainableAnswerAdapter,
    MemoryContextAdapter memoryAdapter,
    BillingFunctions billingFunctions,
    MessagePersistenceFunction persistenceFunction,
    MissingAnswerFeedbackFunction feedbackFunction,
    IAnswerConsistencyValidator consistencyValidator,
    IConfidenceScoringService confidenceScoringService,
    FinancialCopilotAgentFactory agentFactory,
    TimeProvider timeProvider)
{
    internal static readonly ActivitySource ActivitySource =
        new("FinancialCopilot.AI.OrchestrationV2.Workflow", "2.0");

    internal async Task<AiQueryResponse> RunAsync(AiQueryRequest request, CancellationToken ct)
    {
        var startMessage = await PrepareStartMessageAsync(request, ct);
        var workflow = Build(request, ct);

        using var workflowActivity = ActivitySource.StartActivity(
            "FinancialCopilotWorkflow",
            ActivityKind.Internal,
            parentContext: default,
            tags: [
                new("workflow.correlation_id", request.CorrelationId),
                new("workflow.version", "2"),
                new("workflow.mode", "MicrosoftAgentFrameworkV2"),
            ]);

        var run = await InProcessExecution.Default.RunStreamingAsync(
            workflow, startMessage, sessionId: request.CorrelationId, cancellationToken: ct);

        AiQueryResponse? result = null;

        await foreach (var evt in run.WatchStreamAsync(ct))
        {
            if (evt is WorkflowOutputEvent outputEvent && outputEvent.Is<AiQueryResponse>(out var response))
            {
                result = response;
            }
            else if (evt is WorkflowErrorEvent errorEvent)
            {
                workflowActivity?.SetStatus(ActivityStatusCode.Error,
                    errorEvent.Exception?.Message ?? "Workflow execution failed");
                throw new InvalidOperationException(
                    $"Workflow execution failed: {errorEvent.Exception?.Message}",
                    errorEvent.Exception);
            }
        }

        return result
            ?? throw new InvalidOperationException(
                "Workflow completed without producing a final AiQueryResponse.");
    }

    // ── Workflow graph construction ──────────────────────────────────────────────────────

    private Microsoft.Agents.AI.Workflows.Workflow Build(AiQueryRequest request, CancellationToken ct)
    {
        // Step 1: Conversation validation + memory retrieval
        ExecutorBinding step1 = ((Func<WorkflowStartMessage, IWorkflowContext, CancellationToken, ValueTask<MemoryRetrievedMessage>>)
            ((msg, _, token) => ExecuteConversationAndMemoryStepAsync(msg, token)))
            .BindAsExecutor("step1-conversation-memory");

        // Step 2: Billing reservation
        ExecutorBinding step2 = ((Func<MemoryRetrievedMessage, IWorkflowContext, CancellationToken, ValueTask<BillingReservedMessage>>)
            ((msg, _, token) => ExecuteBillingReservationStepAsync(msg, token)))
            .BindAsExecutor("step2-billing-reservation");

        // Step 3: Agent execution (tool calling loop)
        ExecutorBinding step3 = ((Func<BillingReservedMessage, IWorkflowContext, CancellationToken, ValueTask<AgentExecutedMessage>>)
            ((msg, _, token) => ExecuteAgentStepAsync(msg, request, token)))
            .BindAsExecutor("step3-agent-execution");

        // Step 4: Result computation (intent, explainability, consistency, confidence)
        ExecutorBinding step4 = ((Func<AgentExecutedMessage, IWorkflowContext, CancellationToken, ValueTask<ResultsComputedMessage>>)
            ((msg, _, token) => ExecuteResultComputationStepAsync(msg, token)))
            .BindAsExecutor("step4-result-computation");

        // Step 5: Side effects — feedback collection + memory audit (fire-and-forget-safe)
        ExecutorBinding step5 = ((Func<ResultsComputedMessage, IWorkflowContext, CancellationToken, ValueTask<ResultsComputedMessage>>)
            ((msg, _, token) => ExecuteSideEffectsStepAsync(msg, token)))
            .BindAsExecutor("step5-side-effects");

        // Step 6: Persistence
        ExecutorBinding step6 = ((Func<ResultsComputedMessage, IWorkflowContext, CancellationToken, ValueTask<PersistenceCompletedMessage>>)
            ((msg, _, token) => ExecutePersistenceStepAsync(msg, token)))
            .BindAsExecutor("step6-persistence");

        // Step 7: Final response — returns AiQueryResponse.
        // Non-void return + WithOutputFrom(step7) causes the SDK to auto-yield the result as a
        // WorkflowOutputEvent (AutoYieldOutputHandlerResultObject defaults to true per the SDK docs).
        // No ExecutorOptions override needed; passing null uses SDK defaults.
        ExecutorBinding step7 = ((Func<PersistenceCompletedMessage, IWorkflowContext, CancellationToken, ValueTask<AiQueryResponse>>)
            ((msg, _, token) => ValueTask.FromResult(BuildFinalResponse(msg))))
            .BindAsExecutor("step7-final-response");

        var builder = new WorkflowBuilder(step1)
            .WithName("FinancialCopilotQueryWorkflow")
            .WithDescription("Processes a financial AI query through conversation, memory, billing, agent, and persistence steps.")
            .WithOpenTelemetry(_ => { }, ActivitySource);

        builder.AddEdge(step1, step2);
        builder.AddEdge(step2, step3);
        builder.AddEdge(step3, step4);
        builder.AddEdge(step4, step5);
        builder.AddEdge(step5, step6);
        builder.AddEdge(step6, step7);
        builder.WithOutputFrom(step7);

        return builder.Build();
    }

    // ── Step implementations ─────────────────────────────────────────────────────────────

    private async Task<WorkflowStartMessage> PrepareStartMessageAsync(
        AiQueryRequest request, CancellationToken ct)
    {
        var createConversation = request.ConversationId is null;
        var conversationId = request.ConversationId ?? Guid.NewGuid();

        if (!createConversation &&
            await conversationRepository.FindAsync(
                conversationId, request.TenantId, request.ActorId, ct) is null)
        {
            throw new ConversationNotFoundException(conversationId);
        }

        return new WorkflowStartMessage(request, conversationId, createConversation, timeProvider.GetUtcNow());
    }

    private async ValueTask<MemoryRetrievedMessage> ExecuteConversationAndMemoryStepAsync(
        WorkflowStartMessage msg, CancellationToken ct)
    {
        using var stepActivity = ActivitySource.StartActivity("Step1.ConversationMemory");

        var memoryContext = await memoryAdapter.GetContextAsync(
            msg.Request.TenantId, msg.Request.ActorId, msg.Request.UserId,
            msg.ConversationId, msg.Request.CorrelationId, ct);

        var enrichedMessage = BuildEnrichedMessage(msg.Request.Message, memoryContext);

        return new MemoryRetrievedMessage(
            msg.Request, msg.ConversationId, msg.CreateConversation,
            msg.Now, memoryContext, enrichedMessage);
    }

    private async ValueTask<BillingReservedMessage> ExecuteBillingReservationStepAsync(
        MemoryRetrievedMessage msg, CancellationToken ct)
    {
        using var stepActivity = ActivitySource.StartActivity("Step2.BillingReservation");

        var reservation = await billingFunctions.TryReserveAsync(msg.Request, ct);

        return new BillingReservedMessage(
            msg.Request, msg.ConversationId, msg.CreateConversation,
            msg.Now, msg.MemoryContext, msg.EnrichedMessage, reservation);
    }

    private async ValueTask<AgentExecutedMessage> ExecuteAgentStepAsync(
        BillingReservedMessage msg, AiQueryRequest request, CancellationToken ct)
    {
        using var stepActivity = ActivitySource.StartActivity("Step3.AgentExecution");

        var modelClient = ResolveModelClient(request);
        var chatClientAdapter = new FinancialCopilotChatClientAdapter(
            modelClient, usageAccumulator,
            request.CorrelationId, request.TenantId,
            AiWorkloadKind.ResearchTool);

        ScannerToolResult? scannerResult = null;
        SymbolLookupToolResult? lookupResult = null;
        ComprehensiveAnalysisToolResult? comprehensiveAnalysisResult = null;

        var scannerTool = AIFunctionFactory.Create(
            async (string query) =>
            {
                var result = await scannerAdapter.SearchAsync(
                    query, request.CorrelationId, request.TenantId, request.ActorId,
                    request.ApiClientId, request.ScannerPage, request.ScannerPageSize, ct);
                scannerResult = result;
                return result.AgentSummary;
            },
            name: "screen_stocks",
            description: "Screen and filter stocks by financial metric conditions. " +
                         "Use when the user wants to find stocks matching criteria such as P/E ratios, ROE, revenue growth, etc.");

        var lookupTool = AIFunctionFactory.Create(
            async (string query) =>
            {
                var result = await lookupAdapter.LookupAsync(
                    query, request.CorrelationId, request.TenantId, request.ActorId, ct);
                lookupResult = result;
                return result.AgentSummary;
            },
            name: "lookup_symbol_metrics",
            description: "Look up specific financial metric values for named stock symbols. " +
                         "Use when the user asks for a metric of a specific stock by name or ticker (e.g., P/E of فولاد).");

        var comprehensiveAnalysisTool = AIFunctionFactory.Create(
            async (string[]? symbolNames, string[]? topicTags, string? fromDateIso, int limit) =>
            {
                var result = await comprehensiveAnalysisAdapter.QueryAsync(
                    symbolNames, topicTags, fromDateIso, limit <= 0 ? 3 : limit, ct);
                comprehensiveAnalysisResult = result;
                return result.AgentSummary;
            },
            name: "query_comprehensive_analysis",
            description: "Retrieve comprehensive stock analysis posts (تحلیل جامع) from CyclicalWaves. " +
                         "Use when the user asks about fundamental analysis, technical analysis, P/E valuation, " +
                         "equilibrium price (قیمت تعادلی), suspicious volumes (حجم مشکوک), dollar-indexed index, " +
                         "or investment suitability for a specific stock symbol. " +
                         "symbol_names: Persian stock tickers (e.g. شغدیر, کرازی, غگلپا). " +
                         "topic_tags: allowed slugs: تحلیل_تکنیکال, قیمت_تعادلی, رصد_معاملات_عمده, گزارش_فصلی, گزارش_ماهانه, نمودار_P_S, نمودار_P_E. " +
                         "from_date_iso: ISO 8601 date to filter analyses published after this date (optional). " +
                         "limit: max results 1-5 (default 3).");

        var agent = agentFactory.Create(chatClientAdapter, BuildSystemInstructions(), [scannerTool, lookupTool, comprehensiveAnalysisTool]);

        string agentResponseText;
        var completionStatus = "Completed";
        var fromCache = false;
        UsageAccountingResult? usage = null;

        try
        {
            var session = await agent.CreateSessionAsync(request.CorrelationId, ct);
            var agentResponse = await agent.RunAsync(msg.EnrichedMessage, session, options: null, ct);
            agentResponseText = agentResponse.Text;

            fromCache = scannerResult?.FromCache ?? false;
            completionStatus = scannerResult?.CompletionStatus ?? lookupResult?.CompletionStatus ?? "Completed";

            if (msg.Reservation is not null)
                usage = await billingFunctions.FinalizeAsync(
                    msg.Reservation, completionStatus, fromCache, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            if (msg.Reservation is not null)
                await billingFunctions.FinalizeAsync(
                    msg.Reservation, "CancelledBeforeExecution", false, CancellationToken.None);
            throw;
        }
        catch
        {
            if (msg.Reservation is not null)
                await billingFunctions.FinalizeAsync(
                    msg.Reservation, "ProviderFailed", false, CancellationToken.None);
            throw;
        }

        stepActivity?.SetTag("workflow.intent",
            scannerResult is not null ? "Scanner"
            : lookupResult is not null ? "SymbolLookup"
            : comprehensiveAnalysisResult is not null ? "ComprehensiveAnalysis"
            : "Unknown");
        stepActivity?.SetTag("workflow.from_cache", fromCache);

        return new AgentExecutedMessage(
            msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
            msg.MemoryContext, msg.Reservation,
            agentResponseText, scannerResult, lookupResult, comprehensiveAnalysisResult,
            completionStatus, fromCache, modelClient, usage);
    }

    private async ValueTask<ResultsComputedMessage> ExecuteResultComputationStepAsync(
        AgentExecutedMessage msg, CancellationToken ct)
    {
        using var stepActivity = ActivitySource.StartActivity("Step4.ResultComputation");

        var detectedIntent = DetermineIntent(msg.ScannerResult, msg.LookupResult, msg.ComprehensiveAnalysisResult);
        var clarificationRequired =
            msg.ScannerResult?.ClarificationRequired ?? msg.LookupResult?.ClarificationRequired ?? false;
        var clarificationMessage =
            msg.ScannerResult?.ClarificationMessage ?? msg.LookupResult?.ClarificationMessage;

        ExplainableAnswer? explainableAnswer = null;
        if (msg.ScannerResult?.Table is not null && msg.ScannerResult.Plan is not null)
        {
            explainableAnswer = await explainableAnswerAdapter.BuildAsync(
                msg.ScannerResult.Plan, msg.ScannerResult.Table,
                msg.Request.TenantId, msg.Request.CorrelationId,
                CancellationToken.None);
        }

        var consistencyContext = new AnswerConsistencyContext(
            msg.Request.CorrelationId, msg.ConversationId, "MicrosoftAgentFrameworkV2", WorkflowVersion: 2);

        var groundedAnswer = GroundAgentProse(
            detectedIntent, msg.ScannerResult, msg.LookupResult, msg.AgentResponseText, consistencyContext);

        var confidenceScore = CalculateConfidenceScore(
            msg.Request.CorrelationId, groundedAnswer, msg.LookupResult?.Table, explainableAnswer);

        stepActivity?.SetTag("workflow.detected_intent", detectedIntent.ToString());
        stepActivity?.SetTag("workflow.clarification_required", clarificationRequired);

        return new ResultsComputedMessage(
            msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
            msg.MemoryContext, msg.Reservation,
            msg.AgentResponseText, msg.ScannerResult, msg.LookupResult, msg.ComprehensiveAnalysisResult,
            msg.CompletionStatus, msg.FromCache, msg.ModelClient,
            detectedIntent, clarificationRequired, clarificationMessage,
            explainableAnswer, confidenceScore, groundedAnswer, msg.Usage);
    }

    private async ValueTask<ResultsComputedMessage> ExecuteSideEffectsStepAsync(
        ResultsComputedMessage msg, CancellationToken ct)
    {
        using var stepActivity = ActivitySource.StartActivity("Step5.SideEffects");

        if (msg.LookupResult?.Table is not null)
            await feedbackFunction.TryCollectAsync(
                msg.Request, msg.LookupResult.Table, msg.Now, CancellationToken.None);

        await memoryAdapter.RecordAuditAsync(
            msg.MemoryContext, msg.Request.TenantId, msg.Request.ActorId,
            msg.Request.CorrelationId, msg.Now, CancellationToken.None);

        return msg;
    }

    private async ValueTask<PersistenceCompletedMessage> ExecutePersistenceStepAsync(
        ResultsComputedMessage msg, CancellationToken ct)
    {
        using var stepActivity = ActivitySource.StartActivity("Step6.Persistence");

        var textAnswer = msg.DetectedIntent == DetectedIntent.Unknown ? msg.AgentResponseText : null;

        var persistedExchange = await persistenceFunction.PersistAsync(
            msg.ConversationId, msg.Request,
            msg.DetectedIntent, msg.ClarificationRequired, msg.ClarificationMessage,
            textAnswer,
            msg.ScannerResult?.Plan, msg.ScannerResult?.Table,
            msg.LookupResult?.Table,
            msg.ExplainableAnswer, msg.ConfidenceScore, msg.Usage,
            msg.MemoryContext, msg.GroundedAnswer,
            msg.CreateConversation, ct,
            comprehensiveAnalysisResult: msg.ComprehensiveAnalysisResult?.QueryResponse);

        var disclosures = msg.MemoryContext.Disclosures.Count > 0 ? msg.MemoryContext.Disclosures : null;

        return new PersistenceCompletedMessage(
            msg.Request, msg.ConversationId,
            persistedExchange.UserMessageId, persistedExchange.AssistantMessageId,
            msg.DetectedIntent, msg.ClarificationRequired, msg.ClarificationMessage,
            msg.ScannerResult, msg.LookupResult, msg.ComprehensiveAnalysisResult,
            msg.ExplainableAnswer, msg.ConfidenceScore,
            textAnswer, msg.Usage, disclosures, msg.ModelClient,
            msg.Request.CorrelationId);
    }

    private static AiQueryResponse BuildFinalResponse(PersistenceCompletedMessage msg)
    {
        var providerSelection =
            $"{msg.ModelClient.Descriptor.ProviderKey}/{msg.ModelClient.Descriptor.ModelKey}";

        return new AiQueryResponse(
            msg.ConversationId,
            msg.UserMessageId,
            msg.AssistantMessageId,
            msg.DetectedIntent,
            msg.ScannerResult?.Plan,
            msg.ScannerResult?.Table,
            msg.LookupResult?.Table,
            msg.ExplainableAnswer,
            msg.ConfidenceScore,
            msg.TextAnswer,
            msg.ClarificationRequired,
            msg.ClarificationMessage,
            msg.Usage,
            msg.Disclosures,
            AiOrchestrationMode: "MicrosoftAgentFrameworkV2",
            WorkflowVersion: "2",
            ProviderSelection: providerSelection,
            ProviderFallbackOccurred: false,
            WorkflowCorrelationId: msg.WorkflowCorrelationId,
            ComprehensiveAnalysisResult: msg.ComprehensiveAnalysisResult?.QueryResponse);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    private static DetectedIntent DetermineIntent(
        ScannerToolResult? scannerResult,
        SymbolLookupToolResult? lookupResult,
        ComprehensiveAnalysisToolResult? comprehensiveAnalysisResult)
    {
        if (scannerResult is not null) return DetectedIntent.Scanner;
        if (lookupResult is not null) return DetectedIntent.SymbolLookup;
        if (comprehensiveAnalysisResult is not null) return DetectedIntent.ComprehensiveAnalysis;
        return DetectedIntent.Unknown;
    }

    private string? GroundAgentProse(
        DetectedIntent intent,
        ScannerToolResult? scannerResult,
        SymbolLookupToolResult? lookupResult,
        string? candidateProse,
        AnswerConsistencyContext context)
    {
        if (intent == DetectedIntent.SymbolLookup && lookupResult?.Table is not null)
            return consistencyValidator
                .ValidateSymbolLookup(lookupResult.Table, candidateProse, context)
                .Answer;

        if (intent == DetectedIntent.Scanner
            && scannerResult?.Table is not null
            && scannerResult.Plan is not null)
            return consistencyValidator
                .ValidateScanner(scannerResult.Table, scannerResult.Plan, candidateProse, context)
                .Answer;

        return candidateProse;
    }

    private ConfidenceScoreResult? CalculateConfidenceScore(
        string correlationId, string? answerText,
        SymbolLookupTableResult? symbolLookupTable,
        ExplainableAnswer? explainableAnswer)
    {
        if (explainableAnswer is not null)
            return explainableAnswer.Confidence;

        if (symbolLookupTable is null)
            return null;

        return confidenceScoringService.Calculate(new ConfidenceScoringRequest(
            answerText, null, symbolLookupTable,
            DetermineLookupSourceType(symbolLookupTable),
            correlationId));
    }

    private static ConfidenceSourceType DetermineLookupSourceType(SymbolLookupTableResult table)
    {
        var financialColumns = table.Columns
            .Where(c => c.ColumnType is ScannerColumnType.Metric
                or ScannerColumnType.LatestPrice
                or ScannerColumnType.DailyChangePercent
                or ScannerColumnType.MarketCap)
            .ToList();

        var hasSupportedValue = table.Rows.Any(row =>
            financialColumns.Any(column =>
                row.Cells.TryGetValue(column.Identifier, out var cell) &&
                cell.Value is not null &&
                cell.FreshnessStatus != CellFreshnessStatus.Missing));

        if (!hasSupportedValue)
            return ConfidenceSourceType.MissingDataFallback;

        return financialColumns.All(c =>
            string.Equals(c.MetricCode ?? c.Identifier, "PE_TTM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.MetricCode ?? c.Identifier, "PS_TTM", StringComparison.OrdinalIgnoreCase))
            ? ConfidenceSourceType.PreCalculatedMetric
            : ConfidenceSourceType.DerivedMetric;
    }

    private IAiModelClient ResolveModelClient(AiQueryRequest request)
    {
        var selectionRequest = new AiModelSelectionRequest(
            request.TenantId,
            AiWorkloadKind.ResearchTool,
            AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.ResearchTool),
            request.CorrelationId);

        return providerResolver.ResolveCandidates(selectionRequest).FirstOrDefault()
            ?? throw new AiModelProviderException(
                AiExecutionStatus.CapabilityUnavailable,
                "compatible_provider_not_configured",
                "No AI model provider is configured for V2 ResearchTool workload (ChatCompletion + ToolCalling required).");
    }

    private static string BuildSystemInstructions() =>
        """
        You are a financial data assistant for the Iranian stock market (Tehran Stock Exchange).
        You have three tools available:
        - screen_stocks: Use when the user wants to screen, filter, or find stocks based on financial metrics or conditions (e.g., P/E below 10, high ROE, low debt).
        - lookup_symbol_metrics: Use when the user wants specific metric values for named stock symbols (e.g., "P/E of فولاد", "revenue of شپدیس").
        - query_comprehensive_analysis: Use when the user asks about comprehensive analysis posts — fundamental analysis (تحلیل بنیادی), technical analysis (تحلیل تکنیکال), equilibrium price (قیمت تعادلی), suspicious volumes (حجم مشکوک), dollar-indexed index, or investment suitability for a specific stock (e.g., "آخرین تحلیل بنیادی سهم کرازی", "تحلیل دلاری شاخص کل").
        Always respond in the same language as the user's message (Persian/Farsi or English).
        If the request does not fit any tool, briefly explain what you can help with.
        """;

    private static string BuildEnrichedMessage(string originalMessage, AuthorizedMemoryContext memoryContext)
    {
        var promptItems = memoryContext.Items
            .Where(i => i.Type != MemoryType.ShortTermConversationMemory)
            .ToList();

        var conversationItems = memoryContext.Items
            .Where(i => i.Type == MemoryType.ShortTermConversationMemory)
            .ToList();

        if (promptItems.Count == 0 && conversationItems.Count == 0)
            return originalMessage;

        var sb = new StringBuilder();

        if (conversationItems.Count > 0)
        {
            sb.AppendLine("[Recent conversation]");
            foreach (var item in conversationItems)
                sb.AppendLine(item.Summary);
            sb.AppendLine("---");
        }

        if (promptItems.Count > 0)
        {
            sb.AppendLine("[Stored context]");
            foreach (var item in promptItems)
                sb.AppendLine($"- {item.Type}: {item.Summary}");
            sb.AppendLine("---");
        }

        sb.Append(originalMessage);
        return sb.ToString();
    }
}

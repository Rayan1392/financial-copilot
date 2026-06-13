using System.Text;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Bridge;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2;

internal sealed class FinancialCopilotAgentWorkflowRunner(
    IConversationRepository conversationRepository,
    IAiModelProviderResolver providerResolver,
    IAiExecutionUsageAccumulator usageAccumulator,
    ScannerToolAdapter scannerAdapter,
    SymbolLookupToolAdapter lookupAdapter,
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
    // Mutable state captured by tool closures; one instance per RunAsync call.
    private sealed class OrchestrationState
    {
        public Adapters.ScannerToolResult? ScannerResult { get; set; }
        public Adapters.SymbolLookupToolResult? LookupResult { get; set; }
    }

    internal async Task<AiQueryResponse> RunAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Step 1: Conversation validation
        var createConversation = request.ConversationId is null;
        var conversationId = request.ConversationId ?? Guid.NewGuid();
        if (!createConversation &&
            await conversationRepository.FindAsync(
                conversationId, request.TenantId, request.ActorId, cancellationToken) is null)
        {
            throw new ConversationNotFoundException(conversationId);
        }

        // Step 2: Memory retrieval
        var memoryContext = await memoryAdapter.GetContextAsync(
            request.TenantId, request.ActorId, request.UserId,
            conversationId, request.CorrelationId, cancellationToken);

        var enrichedMessage = BuildEnrichedMessage(request.Message, memoryContext);

        // Step 3: Billing reservation — before agent executes
        var reservation = await billingFunctions.TryReserveAsync(request, cancellationToken);

        // Step 4: Build tools with request-scoped closures
        var state = new OrchestrationState();
        var scannerTool = CreateScannerTool(state, request, cancellationToken);
        var lookupTool = CreateLookupTool(state, request, cancellationToken);

        var modelClient = ResolveModelClient(request);
        var chatClientAdapter = new FinancialCopilotChatClientAdapter(
            modelClient,
            usageAccumulator,
            request.CorrelationId,
            request.TenantId,
            AiWorkloadKind.ResearchTool);

        var agent = agentFactory.Create(
            chatClientAdapter,
            BuildSystemInstructions(),
            [scannerTool, lookupTool]);

        AgentResponse agentResponse;
        var completionStatus = "Completed";
        var fromCache = false;
        UsageAccountingResult? usage = null;

        // Step 5: Run the agent — billing finalization in all paths
        try
        {
            var session = await agent.CreateSessionAsync(request.CorrelationId, cancellationToken);
            agentResponse = await agent.RunAsync(enrichedMessage, session, null, cancellationToken);

            fromCache = state.ScannerResult?.FromCache ?? false;
            completionStatus = state.ScannerResult?.CompletionStatus
                ?? state.LookupResult?.CompletionStatus
                ?? "Completed";

            if (reservation is not null)
                usage = await billingFunctions.FinalizeAsync(
                    reservation, completionStatus, fromCache, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (reservation is not null)
                await billingFunctions.FinalizeAsync(
                    reservation, "CancelledBeforeExecution", false, CancellationToken.None);
            throw;
        }
        catch
        {
            if (reservation is not null)
                await billingFunctions.FinalizeAsync(
                    reservation, "ProviderFailed", false, CancellationToken.None);
            throw;
        }

        // Step 6: Determine intent and derive structured results
        var detectedIntent = DetermineIntent(state);
        var clarificationRequired =
            state.ScannerResult?.ClarificationRequired ?? state.LookupResult?.ClarificationRequired ?? false;
        var clarificationMessage =
            state.ScannerResult?.ClarificationMessage ?? state.LookupResult?.ClarificationMessage;

        ExplainableAnswer? explainableAnswer = null;
        if (state.ScannerResult?.Table is not null && state.ScannerResult.Plan is not null)
        {
            explainableAnswer = await explainableAnswerAdapter.BuildAsync(
                state.ScannerResult.Plan,
                state.ScannerResult.Table,
                request.TenantId,
                request.CorrelationId,
                CancellationToken.None);
        }

        // Step 7: Collect symbol lookup feedback (fire-and-forget-safe)
        if (state.LookupResult?.Table is not null)
        {
            await feedbackFunction.TryCollectAsync(
                request, state.LookupResult.Table, now, CancellationToken.None);
        }

        // Step 8: Memory audit
        await memoryAdapter.RecordAuditAsync(
            memoryContext, request.TenantId, request.ActorId,
            request.CorrelationId, now, CancellationToken.None);

        // textAnswer only for non-tool responses (Unknown intent)
        var textAnswer = detectedIntent == DetectedIntent.Unknown ? agentResponse.Text : null;

        // Step 8b: Consistency guardrail. The LLM-authored prose (agentResponse.Text) becomes the
        // persisted assistant content for tool intents, but the LLM never sees the deterministic
        // table values — so it may invent or use a stale number. Ground the prose against the
        // structured result before persistence so prose and table can never disagree.
        var consistencyContext = new AnswerConsistencyContext(
            request.CorrelationId, conversationId, "MicrosoftAgentFrameworkV2", WorkflowVersion: 2);
        var groundedAnswer = GroundAgentProse(
            detectedIntent, state, agentResponse.Text, consistencyContext);
        var confidenceScore = CalculateConfidenceScore(
            request.CorrelationId,
            groundedAnswer,
            state.LookupResult?.Table,
            explainableAnswer);

        var disclosures = memoryContext.Disclosures.Count > 0 ? memoryContext.Disclosures : null;

        // Step 9: Persist conversation exchange
        var persistedExchange = await persistenceFunction.PersistAsync(
            conversationId, request,
            detectedIntent, clarificationRequired, clarificationMessage,
            textAnswer,
            state.ScannerResult?.Plan, state.ScannerResult?.Table,
            state.LookupResult?.Table,
            explainableAnswer, confidenceScore, usage,
            memoryContext, groundedAnswer,
            createConversation, cancellationToken);

        var providerSelection = $"{modelClient.Descriptor.ProviderKey}/{modelClient.Descriptor.ModelKey}";

        return new AiQueryResponse(
            conversationId,
            persistedExchange.UserMessageId,
            persistedExchange.AssistantMessageId,
            detectedIntent,
            state.ScannerResult?.Plan,
            state.ScannerResult?.Table,
            state.LookupResult?.Table,
            explainableAnswer,
            confidenceScore,
            textAnswer,
            clarificationRequired,
            clarificationMessage,
            usage,
            disclosures,
            AiOrchestrationMode: "MicrosoftAgentFrameworkV2",
            WorkflowVersion: "2",
            ProviderSelection: providerSelection,
            ProviderFallbackOccurred: false,
            WorkflowCorrelationId: request.CorrelationId);
    }

    private ConfidenceScoreResult? CalculateConfidenceScore(
        string correlationId,
        string? answerText,
        SymbolLookupTableResult? symbolLookupTable,
        ExplainableAnswer? explainableAnswer)
    {
        if (explainableAnswer is not null)
            return explainableAnswer.Confidence;

        if (symbolLookupTable is null)
            return null;

        return confidenceScoringService.Calculate(new ConfidenceScoringRequest(
            answerText,
            null,
            symbolLookupTable,
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

    private AIFunction CreateScannerTool(
        OrchestrationState state, AiQueryRequest request, CancellationToken ct) =>
        AIFunctionFactory.Create(
            async (string query) =>
            {
                var result = await scannerAdapter.SearchAsync(
                    query,
                    request.CorrelationId,
                    request.TenantId,
                    request.ActorId,
                    request.ApiClientId,
                    request.ScannerPage,
                    request.ScannerPageSize,
                    ct);
                state.ScannerResult = result;
                return result.AgentSummary;
            },
            name: "screen_stocks",
            description: "Screen and filter stocks by financial metric conditions. " +
                         "Use when the user wants to find stocks matching criteria such as P/E ratios, ROE, revenue growth, etc.");

    private AIFunction CreateLookupTool(
        OrchestrationState state, AiQueryRequest request, CancellationToken ct) =>
        AIFunctionFactory.Create(
            async (string query) =>
            {
                var result = await lookupAdapter.LookupAsync(
                    query,
                    request.CorrelationId,
                    request.TenantId,
                    request.ActorId,
                    ct);
                state.LookupResult = result;
                return result.AgentSummary;
            },
            name: "lookup_symbol_metrics",
            description: "Look up specific financial metric values for named stock symbols. " +
                         "Use when the user asks for a metric of a specific stock by name or ticker (e.g., P/E of فولاد).");

    // Replaces LLM-authored prose with deterministic, table-grounded prose when the prose states a
    // numeric metric value that conflicts with (or is unsupported by) the deterministic structured
    // result. Wording for clarifications and Unknown-intent answers is left untouched.
    private string? GroundAgentProse(
        DetectedIntent intent,
        OrchestrationState state,
        string? candidateProse,
        AnswerConsistencyContext context)
    {
        if (intent == DetectedIntent.SymbolLookup && state.LookupResult?.Table is not null)
            return consistencyValidator
                .ValidateSymbolLookup(state.LookupResult.Table, candidateProse, context)
                .Answer;

        if (intent == DetectedIntent.Scanner
            && state.ScannerResult?.Table is not null
            && state.ScannerResult.Plan is not null)
            return consistencyValidator
                .ValidateScanner(state.ScannerResult.Table, state.ScannerResult.Plan, candidateProse, context)
                .Answer;

        return candidateProse;
    }

    private static DetectedIntent DetermineIntent(OrchestrationState state)
    {
        if (state.ScannerResult is not null) return DetectedIntent.Scanner;
        if (state.LookupResult is not null) return DetectedIntent.SymbolLookup;
        return DetectedIntent.Unknown;
    }

    private static string BuildSystemInstructions() =>
        """
        You are a financial data assistant for the Iranian stock market (Tehran Stock Exchange).
        You have two tools available:
        - screen_stocks: Use when the user wants to screen, filter, or find stocks based on financial metrics or conditions (e.g., P/E below 10, high ROE, low debt).
        - lookup_symbol_metrics: Use when the user wants specific metric values for named stock symbols (e.g., "P/E of فولاد", "revenue of شپدیس").
        Always respond in the same language as the user's message (Persian/Farsi or English).
        If the request does not fit either tool, briefly explain what you can help with.
        """;

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

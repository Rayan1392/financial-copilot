using System.Diagnostics;
using System.Text;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Bridge;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2;

internal sealed class FinancialCopilotAgentWorkflowRunner(
    IConversationRepository conversationRepository,
    IConversationDialogueGate dialogueGate,
    ISemanticExecutionCoordinator semanticExecutionCoordinator,
    ISemanticRoutingRolloutCoordinator semanticRolloutCoordinator,
    ISemanticDialogueOutcomeTelemetry outcomeTelemetry,
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
    IExplainInsightUseCase explainInsightUseCase,
    FinancialCopilotAgentFactory agentFactory,
    TimeProvider timeProvider,
    ISalesGrowthScannerTelemetrySink? salesGrowthTelemetry = null)
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
        request = (await dialogueGate.PrepareAsync(request, conversationId, cancellationToken)).Request;

        // Step 2: Memory retrieval
        var memoryContext = await memoryAdapter.GetContextAsync(
            request.TenantId, request.ActorId, request.UserId,
            conversationId, request.CorrelationId, cancellationToken);

        var enrichedMessage = BuildEnrichedMessage(request.Message, memoryContext);

        // Step 3: Billing reservation — before agent executes
        var reservation = request.SemanticFrame is null
            ? await billingFunctions.TryReserveAsync(request, cancellationToken)
            : null;

        // Step 4: Build tools with request-scoped closures
        var state = new OrchestrationState();
        var scannerTool = CreateScannerTool(state, request, cancellationToken);
        var lookupTool = CreateLookupTool(state, request, cancellationToken);

        UsageAccountingResult? usage = null;

        if (request.SemanticFrame is { } semanticFrame)
        {
            var semantic = await semanticExecutionCoordinator.ExecuteAsync(
                semanticFrame,
                new QueryExecutionContext(request.TenantId, request.ActorId, conversationId,
                    request.CorrelationId, semanticFrame.Interpretation.ReplyLanguage, now,
                    request.ScannerPage, request.ScannerPageSize,
                    request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
                    request.ActorType, request.AuthenticationMode, request.UserId, request.ApiClientId),
                request,
                cancellationToken);
            var scannerPayload = semantic.Execution.Payload as SemanticScannerPayload;
            var lookupTable = semantic.Execution.Payload as SymbolLookupTableResult;
            var comprehensivePayload = semantic.Execution.Payload as SemanticComprehensiveAnalysisPayload;
            if (lookupTable is null && comprehensivePayload?.Lookup.Rows.Count > 0)
                lookupTable = comprehensivePayload.Lookup;
            var semanticText = semantic.Execution.Payload switch
            {
                string text => text,
                ComprehensiveAnalysisQueryResponse analysis => ComprehensiveAnalysisToolResult.Success(analysis).AgentSummary,
                SemanticComprehensiveAnalysisPayload combined when combined.Lookup.Rows.Count > 0 =>
                    $"{SymbolLookupToolResult.Success(combined.Lookup).AgentSummary}\n\n{ComprehensiveAnalysisToolResult.Success(combined.Analysis).AgentSummary}",
                SemanticComprehensiveAnalysisPayload combined => ComprehensiveAnalysisToolResult.Success(combined.Analysis).AgentSummary,
                _ => null
            };
            state.ScannerResult = scannerPayload is null ? null : ScannerToolResult.Success(scannerPayload.Plan, scannerPayload.Table, scannerPayload.Table.ExecutionFacts.FromCache);
            state.LookupResult = lookupTable is null ? null : SymbolLookupToolResult.Success(lookupTable);
            var clarification = semantic.Execution.Status is CapabilityExecutionStatus.ClarificationRequired or CapabilityExecutionStatus.DisambiguationRequired;
            var semanticOutcome = semantic.Execution.Status switch
            {
                CapabilityExecutionStatus.Executed => DialogueOutcome.Answered,
                CapabilityExecutionStatus.Partial => DialogueOutcome.PartialAnswer,
                CapabilityExecutionStatus.NoData => DialogueOutcome.NoData,
                CapabilityExecutionStatus.ClarificationRequired => DialogueOutcome.ClarificationNeeded,
                CapabilityExecutionStatus.DisambiguationRequired => DialogueOutcome.DisambiguationNeeded,
                CapabilityExecutionStatus.TemporarilyUnavailable => DialogueOutcome.TemporarilyUnavailable,
                CapabilityExecutionStatus.Failed => DialogueOutcome.Failed,
                _ => DialogueOutcome.Unsupported
            };
            var semanticClarificationMessage = clarification
                ? AiDialogueOutcomePolicy.ComposeSystemMessage(new DialogueOutcomeResult(semanticOutcome, semantic.Execution.ReasonCode, semanticFrame.Interpretation.ReplyLanguage, null, false))
                : null;
            var semanticDialogueOutcome = AiDialogueOutcomePolicy.ApplyLanguageGuard(
                new DialogueOutcomeResult(semanticOutcome, semantic.Execution.ReasonCode,
                    semanticFrame.Interpretation.ReplyLanguage, null, false), semanticText);
            if (semanticOutcome is not DialogueOutcome.Answered and not DialogueOutcome.PartialAnswer)
                semanticText = AiDialogueOutcomePolicy.ComposeSystemMessage(semanticDialogueOutcome);
            outcomeTelemetry.Record(request, semanticDialogueOutcome,
                request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai", now);
            var persisted = await persistenceFunction.PersistAsync(
                conversationId, request, SemanticIntent(semanticFrame.CapabilityCode), clarification,
                semanticClarificationMessage, semanticText, scannerPayload?.Plan, scannerPayload?.Table,
                lookupTable, null, null, semantic.Usage, memoryContext, null,
                createConversation, cancellationToken, semanticOutcome, semantic.Execution.ReasonCode,
                semanticFrame.Interpretation.ReplyLanguage,
                languageGuardApplied: semanticDialogueOutcome.LanguageGuardApplied,
                comprehensiveAnalysisResult: comprehensivePayload?.Analysis ?? semantic.Execution.Payload as ComprehensiveAnalysisQueryResponse,
                financialStatementAnalysisResult: semantic.Execution.Payload as FinancialStatementAnalysisResponse,
                financialStatementTableResult: semantic.Execution.Payload as FinancialStatementTableResult,
                productRevenueMixResult: semantic.Execution.Payload as ProductRevenueMixResponse,
                monthlyActivityTrendResult: semantic.Execution.Payload as MonthlyActivityTrendResponse,
                monthlySalesQualityRankingResult: semantic.Execution.Payload as MonthlySalesQualityRankingResponse,
                disclosureListingResult: semantic.Execution.Payload as DisclosureListingResult,
                psVisualizationResult: semantic.Execution.Payload as PsVisualizationResult);
            await dialogueGate.RecordOutcomeAsync(request, conversationId, clarification, semantic.Execution.ReasonCode, cancellationToken);
            return new AiQueryResponse(
                conversationId, persisted.UserMessageId, persisted.AssistantMessageId,
                SemanticIntent(semanticFrame.CapabilityCode), scannerPayload?.Plan, scannerPayload?.Table,
                lookupTable, null, null, semanticText, clarification, semanticClarificationMessage, semantic.Usage,
                AiOrchestrationMode: "MicrosoftAgentFrameworkV2", WorkflowVersion: "2-fallback",
                WorkflowCorrelationId: request.CorrelationId,
                ComprehensiveAnalysisResult: comprehensivePayload?.Analysis ?? semantic.Execution.Payload as ComprehensiveAnalysisQueryResponse,
                FinancialStatementAnalysisResult: semantic.Execution.Payload as FinancialStatementAnalysisResponse,
                FinancialStatementTableResult: semantic.Execution.Payload as FinancialStatementTableResult,
                ProductRevenueMixResult: semantic.Execution.Payload as ProductRevenueMixResponse,
                MonthlyActivityTrendResult: semantic.Execution.Payload as MonthlyActivityTrendResponse,
                MonthlySalesQualityRankingResult: semantic.Execution.Payload as MonthlySalesQualityRankingResponse,
                DisclosureListingResult: semantic.Execution.Payload as DisclosureListingResult,
                PsVisualizationResult: semantic.Execution.Payload as PsVisualizationResult,
                Outcome: semanticOutcome, OutcomeReasonCode: semantic.Execution.ReasonCode,
                ReplyLanguage: semanticFrame.Interpretation.ReplyLanguage,
                LanguageGuardApplied: semanticDialogueOutcome.LanguageGuardApplied,
                SuggestedActions: persisted.SuggestedActions,
                SemanticCapabilityCode: semanticFrame.CapabilityCode,
                SemanticRegistryVersion: semanticFrame.RegistryVersion);
        }

        var modelClient = ResolveModelClient(request);
        var chatClientAdapter = new FinancialCopilotChatClientAdapter(
            modelClient,
            usageAccumulator,
            request.CorrelationId,
            request.TenantId,
            AiWorkloadKind.ResearchTool);

        if (request.Context?.InsightEventId is Guid insightEventId)
        {
            var explanation = await explainInsightUseCase.ExecuteAsync(
                new ExplainInsightQuery(
                    new CurrentActor(
                        request.ActorType,
                        request.ActorId,
                        request.TenantId,
                        request.AuthenticationMode,
                        request.UserId,
                        request.ApiClientId),
                    insightEventId),
                cancellationToken);

            if (reservation is not null)
                usage = await billingFunctions.FinalizeAsync(
                    reservation, "Completed", false, cancellationToken);

            var insightOutcome = AiDialogueOutcomePolicy.Determine(
                request.Message,
                DetectedIntent.PersonalizedInsightExplanation,
                false,
                null,
                hasStructuredResult: true,
                hasData: true);

            await memoryAdapter.RecordAuditAsync(
                memoryContext, request.TenantId, request.ActorId,
                request.CorrelationId, now, CancellationToken.None);

            var persistedInsightExchange = await persistenceFunction.PersistAsync(
                conversationId, request,
                DetectedIntent.PersonalizedInsightExplanation,
                false, null,
                explanation,
                null, null, null,
                null, null, usage,
                memoryContext, explanation,
                createConversation, cancellationToken,
                outcome: insightOutcome.Outcome,
                outcomeReasonCode: insightOutcome.ReasonCode,
                replyLanguage: insightOutcome.ReplyLanguage,
                languageGuardApplied: insightOutcome.LanguageGuardApplied);

            var provider = $"{modelClient.Descriptor.ProviderKey}/{modelClient.Descriptor.ModelKey}";
            return new AiQueryResponse(
                conversationId,
                persistedInsightExchange.UserMessageId,
                persistedInsightExchange.AssistantMessageId,
                DetectedIntent.PersonalizedInsightExplanation,
                null,
                null,
                null,
                null,
                null,
                explanation,
                false,
                null,
                usage,
                memoryContext.Disclosures.Count > 0 ? memoryContext.Disclosures : null,
                AiOrchestrationMode: "MicrosoftAgentFrameworkV2",
                WorkflowVersion: "2",
                ProviderSelection: provider,
                ProviderFallbackOccurred: false,
                WorkflowCorrelationId: request.CorrelationId,
                Outcome: insightOutcome.Outcome,
                OutcomeReasonCode: insightOutcome.ReasonCode,
                ReplyLanguage: insightOutcome.ReplyLanguage,
                LanguageGuardApplied: insightOutcome.LanguageGuardApplied);
        }

        var agent = agentFactory.Create(
            chatClientAdapter,
            BuildSystemInstructions(),
            [scannerTool, lookupTool]);

        AgentResponse agentResponse;
        var completionStatus = "Completed";
        var fromCache = false;

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
            if (state.ScannerResult?.Plan?.SalesGrowth is not null)
            {
                await (salesGrowthTelemetry ?? new NoOpSalesGrowthScannerTelemetrySink()).RecordAsync(
                    SalesGrowthScannerTelemetry.Create(
                        request.CorrelationId, request.TenantId, request.ActorId,
                        state.ScannerResult.Plan, state.ScannerResult.Table,
                        timeProvider.GetUtcNow() - now, "CancelledBeforeExecution",
                        reservation is null ? "not-reserved" : "cancelled",
                        parserOutcome: state.ScannerResult.ClarificationRequired ? "clarification" : "parsed",
                        timedOut: true),
                    CancellationToken.None);
            }
            if (reservation is not null)
                await billingFunctions.FinalizeAsync(
                    reservation, "CancelledBeforeExecution", false, CancellationToken.None);
            throw;
        }
        catch
        {
            if (state.ScannerResult?.Plan?.SalesGrowth is not null)
            {
                await (salesGrowthTelemetry ?? new NoOpSalesGrowthScannerTelemetrySink()).RecordAsync(
                    SalesGrowthScannerTelemetry.Create(
                        request.CorrelationId, request.TenantId, request.ActorId,
                        state.ScannerResult.Plan, state.ScannerResult.Table,
                        timeProvider.GetUtcNow() - now, "ProviderFailed",
                        reservation is null ? "not-reserved" : "provider-failed",
                        parserOutcome: state.ScannerResult.ClarificationRequired ? "clarification" : "parsed"),
                    CancellationToken.None);
            }
            if (reservation is not null)
                await billingFunctions.FinalizeAsync(
                    reservation, "ProviderFailed", false, CancellationToken.None);
            throw;
        }

        if (state.ScannerResult?.Plan?.SalesGrowth is not null)
        {
            await (salesGrowthTelemetry ?? new NoOpSalesGrowthScannerTelemetrySink()).RecordAsync(
                SalesGrowthScannerTelemetry.Create(
                    request.CorrelationId,
                    request.TenantId,
                    request.ActorId,
                    state.ScannerResult.Plan,
                    state.ScannerResult.Table,
                    timeProvider.GetUtcNow() - now,
                    completionStatus,
                    reservation is null ? "not-reserved" : usage?.CompletionStatus ?? completionStatus,
                    parserOutcome: state.ScannerResult.ClarificationRequired ? "clarification" : "parsed"),
                CancellationToken.None);
        }

        // Step 6: Determine intent and derive structured results
        var detectedIntent = DetermineIntent(state);
        if (request.SemanticShadowFrame is { } shadowFrame)
            semanticRolloutCoordinator.RecordShadowComparison(
                shadowFrame.CapabilityCode,
                SemanticRouteMapping.FromIntent(detectedIntent),
                shadowFrame.CapabilityCode,
                request.CorrelationId);
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
        var outcome = AiDialogueOutcomePolicy.Determine(
            request.Message,
            detectedIntent,
            clarificationRequired,
            clarificationMessage,
            state.ScannerResult is not null || state.LookupResult is not null || detectedIntent != DetectedIntent.Unknown,
            state.ScannerResult?.Table is not null || state.LookupResult?.Table?.Rows.Count > 0);
        outcome = AiDialogueOutcomePolicy.ApplyLanguageGuard(
            outcome,
            outcome.SafeDetail ?? (detectedIntent == DetectedIntent.Unknown ? null : groundedAnswer));
        outcomeTelemetry.Record(request, outcome,
            request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai", now);
        Activity.Current?.SetTag("workflow.outcome", outcome.Outcome.ToString());
        Activity.Current?.SetTag("workflow.outcome_reason", outcome.ReasonCode);
        Activity.Current?.SetTag("workflow.reply_language", outcome.ReplyLanguage);
        Activity.Current?.SetTag("workflow.language_guard_applied", outcome.LanguageGuardApplied);

        if (outcome.Outcome is DialogueOutcome.ClarificationNeeded or DialogueOutcome.DisambiguationNeeded)
        {
            clarificationRequired = true;
            clarificationMessage = AiDialogueOutcomePolicy.ComposeSystemMessage(outcome, outcome.SafeDetail);
            outcome = outcome with { SafeDetail = clarificationMessage };
        }

        if (outcome.Outcome != DialogueOutcome.Answered && outcome.Outcome != DialogueOutcome.PartialAnswer)
            groundedAnswer = AiDialogueOutcomePolicy.ComposeSystemMessage(
                outcome,
                detectedIntent == DetectedIntent.Unknown ? null : groundedAnswer);

        var confidenceScore = CalculateConfidenceScore(
            request.CorrelationId,
            groundedAnswer,
            state.LookupResult?.Table,
            explainableAnswer);
        var responseTextAnswer = detectedIntent == DetectedIntent.SymbolLookup
            ? groundedAnswer
            : textAnswer;

        if (outcome.Outcome != DialogueOutcome.Answered && outcome.Outcome != DialogueOutcome.PartialAnswer)
            responseTextAnswer = groundedAnswer;

        var disclosures = memoryContext.Disclosures.Count > 0 ? memoryContext.Disclosures : null;

        // Step 9: Persist conversation exchange
        await dialogueGate.RecordOutcomeAsync(request, conversationId, clarificationRequired, outcome.ReasonCode, cancellationToken);
        var persistedExchange = await persistenceFunction.PersistAsync(
            conversationId, request,
            detectedIntent, clarificationRequired, clarificationMessage,
            responseTextAnswer,
            state.ScannerResult?.Plan, state.ScannerResult?.Table,
            state.LookupResult?.Table,
            explainableAnswer, confidenceScore, usage,
            memoryContext, groundedAnswer,
            createConversation, cancellationToken,
            outcome: outcome.Outcome,
            outcomeReasonCode: outcome.ReasonCode,
            replyLanguage: outcome.ReplyLanguage,
            languageGuardApplied: outcome.LanguageGuardApplied);

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
            responseTextAnswer,
            clarificationRequired,
            clarificationMessage,
            usage,
            disclosures,
            AiOrchestrationMode: "MicrosoftAgentFrameworkV2",
            WorkflowVersion: "2",
            ProviderSelection: providerSelection,
            ProviderFallbackOccurred: false,
            WorkflowCorrelationId: request.CorrelationId,
            Outcome: outcome.Outcome,
            OutcomeReasonCode: outcome.ReasonCode,
            ReplyLanguage: outcome.ReplyLanguage,
            LanguageGuardApplied: outcome.LanguageGuardApplied,
            SuggestedActions: persistedExchange.SuggestedActions,
            SemanticCapabilityCode: request.SemanticFrame?.CapabilityCode ?? request.SemanticShadowFrame?.CapabilityCode,
            SemanticRegistryVersion: request.SemanticFrame?.RegistryVersion ?? request.SemanticShadowFrame?.RegistryVersion);
    }

    private static DetectedIntent SemanticIntent(string capabilityCode) => capabilityCode switch
    {
        "stock_screening" => DetectedIntent.Scanner,
        "symbol_metric_lookup" => DetectedIntent.SymbolLookup,
        "comprehensive_analysis" => DetectedIntent.ComprehensiveAnalysis,
        "monthly_activity_trend" => DetectedIntent.MonthlyActivityTrend,
        "product_revenue_mix" => DetectedIntent.ProductRevenueMix,
        "financial_statement_table" => DetectedIntent.FinancialStatementTableLookup,
        "financial_statement_period_analysis" => DetectedIntent.FinancialStatementPeriodAnalysis,
        "disclosure_listing" => DetectedIntent.DisclosureListing,
        "monthly_sales_quality_ranking" => DetectedIntent.MonthlySalesQualityRanking,
        "ps_gauge_visualization" => DetectedIntent.PsGaugeVisualization,
        "personalized_insight_explanation" => DetectedIntent.PersonalizedInsightExplanation,
        _ => DetectedIntent.Unknown
    };

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

        static bool IsPreCalculated(string id) =>
            string.Equals(id, "PE_TTM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "PS_TTM", StringComparison.OrdinalIgnoreCase);

        var hasNonMissingPreCalc = table.Rows.Any(row =>
            financialColumns
                .Where(c => IsPreCalculated(c.MetricCode ?? c.Identifier))
                .Any(c => row.Cells.TryGetValue(c.Identifier, out var cell) &&
                          cell.Value is not null &&
                          cell.FreshnessStatus != CellFreshnessStatus.Missing));

        if (hasNonMissingPreCalc)
            return ConfidenceSourceType.PreCalculatedMetric;

        return ConfidenceSourceType.DerivedMetric;
    }

    private AIFunction CreateScannerTool(
        OrchestrationState state, AiQueryRequest request, CancellationToken ct) =>
        AIFunctionFactory.Create(
            async (string query) =>
            {
                var result = await scannerAdapter.SearchAsync(
                    request.Message,
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
                // The LLM may rewrite "آخرین فروش ..." as "REVENUE ..."; parsing must use the
                // original user wording so deterministic sales-routing rules are preserved.
                var result = await lookupAdapter.LookupAsync(
                    request.Message,
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
            // Null candidate forces ValidateScanner to return the deterministic count sentence.
            // Scanner prose must never enumerate symbols — the table carries the full results.
            return consistencyValidator
                .ValidateScanner(state.ScannerResult.Table, state.ScannerResult.Plan, null, context)
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

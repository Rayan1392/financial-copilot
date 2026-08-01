using System.Diagnostics;
using System.Text;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Bridge;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.InProc;
using Microsoft.Agents.AI.Workflows.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

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
    IDirectMetricRoutingRegistry directMetricRoutingRegistry,
    IFinancialStatementAnalysisUseCase financialStatementAnalysisUseCase,
    IFinancialStatementTableQueryUseCase financialStatementTableQueryUseCase,
    IProductRevenueMixQueryUseCase productRevenueMixUseCase,
    IMonthlyActivityTrendQueryUseCase monthlyActivityTrendUseCase,
    IDisclosureListingUseCase disclosureListingUseCase,
    IMonthlySalesQualityRankingQueryUseCase monthlySalesQualityRankingUseCase,
    IPsVisualizationExperienceUseCase psVisualizationExperienceUseCase,
    IOptions<CyclicalWavesPsVisualizationOptions> psVisualizationOptions,
    IExplainInsightUseCase explainInsightUseCase,
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
        FinancialStatementAnalysisResponse? financialStatementAnalysisResult = null;
        FinancialStatementTableResult? financialStatementTableResult = null;
        ProductRevenueMixResponse? productRevenueMixResult = null;
        MonthlyActivityTrendResponse? monthlyActivityTrendResult = null;
        MonthlySalesQualityRankingResponse? monthlySalesQualityRankingResult = null;

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
                ct);

            UsageAccountingResult? insightUsage = null;
            if (msg.Reservation is not null)
            {
                insightUsage = await billingFunctions.FinalizeAsync(
                    msg.Reservation, "Completed", false, CancellationToken.None);
            }

            stepActivity?.SetTag("workflow.intent", "PersonalizedInsightExplanation");

            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                explanation,
                scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult,
                productRevenueMixResult, monthlyActivityTrendResult, monthlySalesQualityRankingResult,
                "Completed", false, modelClient, insightUsage);
        }

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
                // Use the original message, not an LLM-rewritten metric phrase, so sales aliases
                // like "آخرین فروش" cannot be converted into generic REVENUE lookup.
                var result = await lookupAdapter.LookupAsync(
                    request.Message, request.CorrelationId, request.TenantId, request.ActorId, ct);
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

        UsageAccountingResult? usage = null;

        var isMonthlySalesQualityRanking = MonthlySalesQualityRankingIntentRules.LooksLikeMonthlySalesQualityRankingQuery(request.Message);
        if (isMonthlySalesQualityRanking)
        {
            var rankingQuery = MonthlySalesQualityRankingIntentRules.BuildQuery(request.Message);
            monthlySalesQualityRankingResult = await monthlySalesQualityRankingUseCase.ExecuteAsync(rankingQuery, ct);

            UsageAccountingResult? rankingUsage = null;
            if (msg.Reservation is not null)
            {
                rankingUsage = await billingFunctions.FinalizeAsync(
                    msg.Reservation, "Completed", false, CancellationToken.None);
            }

            stepActivity?.SetTag("workflow.intent", "MonthlySalesQualityRanking");

            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                BuildMonthlySalesQualityRankingContent(monthlySalesQualityRankingResult),
                scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                monthlySalesQualityRankingResult,
                "Completed", false, modelClient, rankingUsage);
        }

        var isDisclosureListing = DisclosureListingIntentRules.LooksLikeDisclosureListingQuery(request.Message);
        if (isDisclosureListing)
        {
            var disclosureListing = await disclosureListingUseCase.ExecuteAsync(
                DisclosureListingIntentRules.BuildQuery(request.Message, msg.Now, request.DisclosurePage, request.DisclosurePageSize) with { Channel = request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai" }, ct);
            UsageAccountingResult? disclosureUsage = null;
            if (msg.Reservation is not null)
                disclosureUsage = await billingFunctions.FinalizeAsync(msg.Reservation, "Completed", false, CancellationToken.None);

            stepActivity?.SetTag("workflow.intent", "DisclosureListing");
            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                BuildDisclosureListingContent(disclosureListing),
                scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                monthlySalesQualityRankingResult,
                "Completed", false, modelClient, disclosureUsage,
                DisclosureListingResult: disclosureListing);
        }

        var isMonthlyActivityTrend = MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(request.Message);
        if (isMonthlyActivityTrend)
        {
            var symbol = MonthlyActivityTrendIntentRules.ExtractCompanySymbol(request.Message);
            if (symbol is null)
            {
                stepActivity?.SetTag("workflow.intent", "MonthlyActivityTrend");
                return new AgentExecutedMessage(
                    msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                    msg.MemoryContext, msg.Reservation,
                    "لطفاً نام نماد یا شرکت موردنظر را در پرسش خود مشخص کنید.",
                    scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                    monthlySalesQualityRankingResult,
                    "ClarificationRequired", false, modelClient, usage);
            }

            monthlyActivityTrendResult = await monthlyActivityTrendUseCase.ExecuteAsync(
                new MonthlyActivityTrendQuery(request.Message, symbol),
                ct);

            // The monthly-sales chart can optionally be enriched with the persisted
            // P/S gauge.  The flag was previously configured but never consumed,
            // leaving the frontend with no PsVisualizationResult to render.
            PsVisualizationResult? monthlyTrendGauge = null;
            if (monthlyActivityTrendResult is not null &&
                psVisualizationOptions.Value.IncludeGaugeInMonthlySalesTrendChart)
            {
                monthlyTrendGauge = await psVisualizationExperienceUseCase.ExecuteAsync(
                    new PsVisualizationQuery(symbol, IncludeHistory: false), ct);
            }

            UsageAccountingResult? trendUsage = null;
            if (msg.Reservation is not null)
            {
                trendUsage = await billingFunctions.FinalizeAsync(
                    msg.Reservation, "Completed", false, CancellationToken.None);
            }

            stepActivity?.SetTag("workflow.intent", "MonthlyActivityTrend");

            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                monthlyActivityTrendResult is null
                    ? $"اطلاعات روند فروش ماهانه برای نماد «{symbol}» در پایگاه داده یافت نشد."
                    : BuildMonthlyActivityTrendContent(monthlyActivityTrendResult),
                scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                monthlySalesQualityRankingResult,
                monthlyActivityTrendResult is null ? "Completed" : "Completed", false, modelClient, trendUsage,
                PsVisualizationResult: monthlyTrendGauge);
        }

        var isProductRevenueMix = ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery(request.Message);
        if (isProductRevenueMix)
        {
            var symbol = ProductRevenueMixIntentRules.ExtractCompanySymbol(request.Message);
            if (symbol is null)
            {
                stepActivity?.SetTag("workflow.intent", "ProductRevenueMix");
                return new AgentExecutedMessage(
                    msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                    msg.MemoryContext, msg.Reservation,
                    "لطفاً نام نماد یا شرکت موردنظر را در پرسش خود مشخص کنید.",
                    scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                    monthlySalesQualityRankingResult,
                    "ClarificationRequired", false, modelClient, usage);
            }

            productRevenueMixResult = await productRevenueMixUseCase.ExecuteAsync(
                new ProductRevenueMixQuery(symbol),
                ct);

            if (productRevenueMixResult is null)
            {
                UsageAccountingResult? productRevenueMixUsage = null;
                if (msg.Reservation is not null)
                {
                    productRevenueMixUsage = await billingFunctions.FinalizeAsync(
                        msg.Reservation, "Completed", false, CancellationToken.None);
                }

                stepActivity?.SetTag("workflow.intent", "ProductRevenueMix");
                return new AgentExecutedMessage(
                    msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                    msg.MemoryContext, msg.Reservation,
                    $"اطلاعات ترکیب درآمد محصولات برای نماد «{symbol}» در پایگاه داده یافت نشد.",
                    scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                    monthlySalesQualityRankingResult,
                    "Completed", false, modelClient, productRevenueMixUsage);
            }

            var productRevenueMixText = BuildProductRevenueMixContent(productRevenueMixResult);
            UsageAccountingResult? productRevenueMixUsageSuccess = null;
            if (msg.Reservation is not null)
            {
                productRevenueMixUsageSuccess = await billingFunctions.FinalizeAsync(
                    msg.Reservation, "Completed", false, CancellationToken.None);
            }

            stepActivity?.SetTag("workflow.intent", "ProductRevenueMix");

            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                productRevenueMixText, scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                monthlySalesQualityRankingResult,
                "Completed", false, modelClient, productRevenueMixUsageSuccess);
        }

        var isFinancialStatementTable = FinancialStatementTableIntentRules.LooksLikeFinancialStatementTableQuery(request.Message);
        if (isFinancialStatementTable)
        {
            var tableQuery = FinancialStatementTableIntentRules.BuildQuery(request.Message);
            financialStatementTableResult = await financialStatementTableQueryUseCase.ExecuteAsync(tableQuery, ct);

            UsageAccountingResult? statementTableUsage = null;
            if (msg.Reservation is not null)
            {
                statementTableUsage = await billingFunctions.FinalizeAsync(
                    msg.Reservation, "Completed", false, CancellationToken.None);
            }

            stepActivity?.SetTag("workflow.intent", "FinancialStatementTableLookup");

            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                financialStatementTableResult?.RenderedAnswer ??
                "اطلاعات صورت مالی برای نماد یا شرکت درخواستی با فیلترهای اعمال شده در پایگاه داده یافت نشد.",
                scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult,
                financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                monthlySalesQualityRankingResult,
                "Completed", false, modelClient, statementTableUsage);
        }

        var isFinancialStatementAnalysis = FinancialStatementAnalysisIntentRules.LooksLikeFinancialStatementAnalysisQuery(request.Message);
        if (isFinancialStatementAnalysis)
        {
            var analysisQuery = FinancialStatementAnalysisIntentRules.BuildQuery(request.Message);
            financialStatementAnalysisResult = await financialStatementAnalysisUseCase.ExecuteAsync(analysisQuery, ct);

            UsageAccountingResult? statementUsage = null;
            if (msg.Reservation is not null)
            {
                statementUsage = await billingFunctions.FinalizeAsync(
                    msg.Reservation, "Completed", false, CancellationToken.None);
            }

            stepActivity?.SetTag("workflow.intent", "FinancialStatementPeriodAnalysis");

            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                financialStatementAnalysisResult?.RenderedAnswer ??
                "اطلاعات صورت‌های مالی برای نماد یا شرکت درخواستی در پایگاه داده یافت نشد.",
                scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult,
                financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                monthlySalesQualityRankingResult,
                "Completed", false, modelClient, statementUsage);
        }

        var isDirectMetricLookup = IsDirectMetricLookupRequest(request.Message);
        var psGaugeSymbol = PsGaugeIntentRules.ExtractCompanySymbol(request.Message);
        if (psGaugeSymbol is not null)
        {
            var psGaugeResult = await psVisualizationExperienceUseCase.ExecuteAsync(
                new PsVisualizationQuery(psGaugeSymbol, IncludeHistory: true), ct);
            UsageAccountingResult? gaugeUsage = null;
            if (msg.Reservation is not null)
                gaugeUsage = await billingFunctions.FinalizeAsync(msg.Reservation, "Completed", false, CancellationToken.None);

            stepActivity?.SetTag("workflow.intent", "PsGaugeVisualization");
            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                BuildPsGaugeContent(psGaugeSymbol, psGaugeResult), scannerResult, lookupResult,
                comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult,
                productRevenueMixResult, monthlyActivityTrendResult, monthlySalesQualityRankingResult,
                "Completed", false, modelClient, gaugeUsage,
                PsVisualizationResult: psGaugeResult);
        }

        var isDirectMetricFollowup = !isDirectMetricLookup &&
            IsDirectMetricLookupFollowup(request.Message, msg.EnrichedMessage);
        if (isDirectMetricLookup || isDirectMetricFollowup)
        {
            var queryTextForLookup = isDirectMetricLookup ? request.Message : msg.EnrichedMessage;
            lookupResult = await lookupAdapter.LookupAsync(
                request.Message,
                request.CorrelationId,
                request.TenantId,
                request.ActorId,
                ct,
                queryTextForLookup: queryTextForLookup,
                parserContextMessage: msg.EnrichedMessage);

            var directCompletionStatus = lookupResult.CompletionStatus;
            UsageAccountingResult? directUsage = null;
            if (msg.Reservation is not null)
            {
                directUsage = await billingFunctions.FinalizeAsync(
                    msg.Reservation, directCompletionStatus, false, CancellationToken.None);
            }

            stepActivity?.SetTag("workflow.intent", "SymbolLookup");
            stepActivity?.SetTag("workflow.direct_metric_lookup", true);
            stepActivity?.SetTag("workflow.direct_metric_followup", isDirectMetricFollowup);

            return new AgentExecutedMessage(
                msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
                msg.MemoryContext, msg.Reservation,
                lookupResult.AgentSummary, scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                monthlySalesQualityRankingResult,
                directCompletionStatus, false, modelClient, directUsage);
        }

        var agent = agentFactory.Create(chatClientAdapter, BuildSystemInstructions(), [scannerTool, lookupTool, comprehensiveAnalysisTool]);

        string agentResponseText;
        var completionStatus = "Completed";
        var fromCache = false;

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

        var intentLabel =
            scannerResult is not null ? "Scanner"
            : lookupResult is not null && comprehensiveAnalysisResult is not null ? "SymbolLookup+ComprehensiveAnalysis"
            : lookupResult is not null ? "SymbolLookup"
            : comprehensiveAnalysisResult is not null ? "ComprehensiveAnalysis"
            : "Unknown";
        stepActivity?.SetTag("workflow.intent", intentLabel);
        stepActivity?.SetTag("workflow.from_cache", fromCache);

        return new AgentExecutedMessage(
            msg.Request, msg.ConversationId, msg.CreateConversation, msg.Now,
            msg.MemoryContext, msg.Reservation,
            agentResponseText, scannerResult, lookupResult, comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
            monthlySalesQualityRankingResult,
            completionStatus, fromCache, modelClient, usage);
    }

    private async ValueTask<ResultsComputedMessage> ExecuteResultComputationStepAsync(
        AgentExecutedMessage msg, CancellationToken ct)
    {
        using var stepActivity = ActivitySource.StartActivity("Step4.ResultComputation");

        var detectedIntent =
            msg.Request.Context?.InsightEventId is not null
                ? DetectedIntent.PersonalizedInsightExplanation
                : msg.MonthlySalesQualityRankingResult is not null ||
            MonthlySalesQualityRankingIntentRules.LooksLikeMonthlySalesQualityRankingQuery(msg.Request.Message)
                ? DetectedIntent.MonthlySalesQualityRanking
                : DisclosureListingIntentRules.LooksLikeDisclosureListingQuery(msg.Request.Message)
                ? DetectedIntent.DisclosureListing
                : msg.MonthlyActivityTrendResult is not null ||
            MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(msg.Request.Message)
                ? DetectedIntent.MonthlyActivityTrend
                : msg.FinancialStatementTableResult is not null ||
            FinancialStatementTableIntentRules.LooksLikeFinancialStatementTableQuery(msg.Request.Message)
                ? DetectedIntent.FinancialStatementTableLookup
            : msg.FinancialStatementAnalysisResult is not null ||
            FinancialStatementAnalysisIntentRules.LooksLikeFinancialStatementAnalysisQuery(msg.Request.Message)
                ? DetectedIntent.FinancialStatementPeriodAnalysis
                : msg.PsVisualizationResult is not null || PsGaugeIntentRules.LooksLikeQuery(msg.Request.Message)
                ? DetectedIntent.PsGaugeVisualization
                : DetermineIntent(
                    msg.ScannerResult,
                    msg.LookupResult,
                    msg.ComprehensiveAnalysisResult,
                    msg.FinancialStatementAnalysisResult,
                    msg.FinancialStatementTableResult,
                    msg.ProductRevenueMixResult,
                    msg.MonthlyActivityTrendResult,
                    msg.MonthlySalesQualityRankingResult);
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
            msg.AgentResponseText, msg.ScannerResult, msg.LookupResult, msg.ComprehensiveAnalysisResult, msg.FinancialStatementAnalysisResult, msg.FinancialStatementTableResult, msg.ProductRevenueMixResult, msg.MonthlyActivityTrendResult,
            msg.MonthlySalesQualityRankingResult,
            msg.CompletionStatus, msg.FromCache, msg.ModelClient,
            detectedIntent, clarificationRequired, clarificationMessage,
            explainableAnswer, confidenceScore, groundedAnswer, msg.Usage,
            DisclosureListingResult: msg.DisclosureListingResult,
            PsVisualizationResult: msg.PsVisualizationResult);
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

        var textAnswer = msg.DetectedIntent is DetectedIntent.Unknown or DetectedIntent.ProductRevenueMix or DetectedIntent.MonthlyActivityTrend or DetectedIntent.MonthlySalesQualityRanking or DetectedIntent.DisclosureListing or DetectedIntent.FinancialStatementPeriodAnalysis or DetectedIntent.FinancialStatementTableLookup or DetectedIntent.PersonalizedInsightExplanation or DetectedIntent.PsGaugeVisualization
            ? msg.AgentResponseText
            : null;
        var responseTextAnswer = msg.DetectedIntent is DetectedIntent.SymbolLookup or DetectedIntent.ProductRevenueMix or DetectedIntent.MonthlyActivityTrend or DetectedIntent.MonthlySalesQualityRanking or DetectedIntent.DisclosureListing or DetectedIntent.FinancialStatementPeriodAnalysis or DetectedIntent.FinancialStatementTableLookup or DetectedIntent.PersonalizedInsightExplanation or DetectedIntent.PsGaugeVisualization
            ? msg.GroundedAnswer
            : textAnswer;

        var persistedExchange = await persistenceFunction.PersistAsync(
            msg.ConversationId, msg.Request,
            msg.DetectedIntent, msg.ClarificationRequired, msg.ClarificationMessage,
            responseTextAnswer,
            msg.ScannerResult?.Plan, msg.ScannerResult?.Table,
            msg.LookupResult?.Table,
            msg.ExplainableAnswer, msg.ConfidenceScore, msg.Usage,
            msg.MemoryContext, msg.GroundedAnswer,
            msg.CreateConversation, ct,
            comprehensiveAnalysisResult: msg.ComprehensiveAnalysisResult?.QueryResponse,
            financialStatementAnalysisResult: msg.FinancialStatementAnalysisResult,
            financialStatementTableResult: msg.FinancialStatementTableResult,
            productRevenueMixResult: msg.ProductRevenueMixResult,
            monthlyActivityTrendResult: msg.MonthlyActivityTrendResult,
            monthlySalesQualityRankingResult: msg.MonthlySalesQualityRankingResult,
             disclosureListingResult: msg.DisclosureListingResult,
             psVisualizationResult: msg.PsVisualizationResult);

        var disclosures = msg.MemoryContext.Disclosures.Count > 0 ? msg.MemoryContext.Disclosures : null;

        return new PersistenceCompletedMessage(
            msg.Request, msg.ConversationId,
            persistedExchange.UserMessageId, persistedExchange.AssistantMessageId,
            msg.DetectedIntent, msg.ClarificationRequired, msg.ClarificationMessage,
            msg.ScannerResult, msg.LookupResult, msg.ComprehensiveAnalysisResult, msg.FinancialStatementAnalysisResult, msg.FinancialStatementTableResult, msg.ProductRevenueMixResult, msg.MonthlyActivityTrendResult,
            msg.MonthlySalesQualityRankingResult,
            msg.ExplainableAnswer, msg.ConfidenceScore,
            responseTextAnswer, msg.Usage, disclosures, msg.ModelClient,
            msg.Request.CorrelationId,
             DisclosureListingResult: msg.DisclosureListingResult,
             PsVisualizationResult: msg.PsVisualizationResult);
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
            ComprehensiveAnalysisResult: msg.ComprehensiveAnalysisResult?.QueryResponse,
            FinancialStatementAnalysisResult: msg.FinancialStatementAnalysisResult,
            FinancialStatementTableResult: msg.FinancialStatementTableResult,
            ProductRevenueMixResult: msg.ProductRevenueMixResult,
            MonthlyActivityTrendResult: msg.MonthlyActivityTrendResult,
            MonthlySalesQualityRankingResult: msg.MonthlySalesQualityRankingResult,
            DisclosureListingResult: msg.DisclosureListingResult,
            PsVisualizationResult: msg.PsVisualizationResult);
    }

    private static string BuildPsGaugeContent(string symbol, PsVisualizationResult? result)
    {
        if (result is null)
            return $"داده‌ای برای نمایش گیج P/S نماد {symbol} در پایگاه داده موجود نیست.";
        if (result.TtmPs.Value is decimal value)
            return $"گیج P/S نماد {result.CompanySymbol}: مقدار TTM برابر {value:0.00} است. وضعیت داده: {result.Status}.";
        return $"داده‌ای برای نمایش گیج P/S نماد {result.CompanySymbol} موجود نیست (وضعیت: {result.Status}).";
    }

    private static string BuildDisclosureListingContent(DisclosureListingResult result)
    {
        if (result.Items.Count == 0)
            return "اطلاعیه‌ای با فیلترهای درخواستی یافت نشد.";

        var lines = result.Items.Select((item, index) =>
            $"{index + 1}. {item.Symbol ?? item.CompanyName ?? "—"} | {item.Title} | دریافت: {item.ReceivedAt:yyyy-MM-dd HH:mm}");
        var suffix = result.HasNextPage ? $"\nصفحه {result.Page} از {result.TotalPages} — نتایج بیشتری وجود دارد." : string.Empty;
        return $"فهرست اطلاعیه‌های منتشرشده:\n{string.Join("\n", lines)}{suffix}\nاین فهرست صرفاً اطلاع‌رسانی است و توصیهٔ خرید یا فروش نیست.";
    }

    private static string BuildProductRevenueMixContent(ProductRevenueMixResponse result)
    {
        var sb = new StringBuilder();
        var companyLabel = result.CompanyName is not null
            ? $"{result.CompanyName} ({result.CompanySymbol})"
            : result.CompanySymbol;

        sb.AppendLine($"### ترکیب درآمد محصولات — {companyLabel}");
        sb.AppendLine($"دوره: {result.ReportYear}/{result.ReportMonth:D2} | کل فروش: {result.TotalSalesAmount:N0} ریال");
        sb.AppendLine();
        sb.AppendLine("| ردیف | محصول | فروش (ریال) | سهم (٪) | غالب |");
        sb.AppendLine("|------|-------|------------|---------|------|");

        foreach (var product in result.Products)
        {
            var dominant = product.IsDominantProduct ? "✓" : "";
            sb.AppendLine($"| {product.Rank} | {product.ProductName} | {product.SalesAmount:N0} | {product.RevenueSharePercentage:F1}٪ | {dominant} |");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildMonthlyActivityTrendContent(MonthlyActivityTrendResponse result)
    {
        var sb = new StringBuilder();
        var companyLabel = result.CompanyName is not null
            ? $"{result.CompanyName} ({result.CompanySymbol})"
            : result.CompanySymbol;

        sb.AppendLine($"### روند فروش ماهانه - {companyLabel}");
        sb.AppendLine($"آخرین دوره گزارش: {result.LatestReportYear}/{result.LatestReportMonth:D2} | واحد: {result.UnitLabelFa}");
        sb.AppendLine();

        if (result.LatestMonthlySalesAmount.HasValue)
            sb.AppendLine($"**خلاصه آخرین ماه:** فروش {FormatTrendAmount(result.LatestMonthlySalesAmount.Value)} {result.UnitLabelFa}");

        if (result.SameMonthPreviousYearSalesAmount.HasValue)
        {
            if (result.SalesAmountYoYGrowthPercent.HasValue)
            {
                var sign = result.SalesAmountYoYGrowthPercent.Value >= 0 ? "+" : "";
                sb.AppendLine($"**مقایسه با ماه مشابه سال قبل:** {FormatTrendAmount(result.SameMonthPreviousYearSalesAmount.Value)} {result.UnitLabelFa} ({sign}{result.SalesAmountYoYGrowthPercent.Value:F1}٪)");
            }
            else
            {
                sb.AppendLine($"**مقایسه با ماه مشابه سال قبل:** {FormatTrendAmount(result.SameMonthPreviousYearSalesAmount.Value)} {result.UnitLabelFa}");
            }
        }

        if (result.Average12MonthSalesAmount.HasValue)
        {
            var vsAvgText = result.SalesVsAverage12MonthPercent.HasValue
                ? $" ({(result.SalesVsAverage12MonthPercent.Value >= 0 ? "+" : "")}{result.SalesVsAverage12MonthPercent.Value:F1}٪ نسبت به میانگین)"
                : "";
            sb.AppendLine($"**میانگین ۱۲ ماهه:** {FormatTrendAmount(result.Average12MonthSalesAmount.Value)} {result.UnitLabelFa}{vsAvgText}");
        }

        if (result.Insights.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**نکات تحلیلی:**");
            foreach (var insight in result.Insights)
                sb.AppendLine($"- {insight.TextFa}");
        }

        if (result.ChartPoints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**داده نمودار ماهانه:**");
            sb.AppendLine();

            var firstPoint = result.ChartPoints.First(p => p.PreviousFiscalYear.HasValue || p.CurrentFiscalYear.HasValue);
            var prevYearLabel = firstPoint.PreviousFiscalYear.HasValue ? $"فروش {firstPoint.PreviousFiscalYear}" : "فروش سال قبل";
            var currYearLabel = firstPoint.CurrentFiscalYear.HasValue ? $"فروش {firstPoint.CurrentFiscalYear}" : "فروش سال جاری";

            sb.AppendLine($"| ماه | {prevYearLabel} | {currYearLabel} | میانگین ۱۲ ماهه |");
            sb.AppendLine("|-----|------------:|-------------:|----------------:|");

            foreach (var pt in result.ChartPoints)
            {
                var prevVal = pt.PreviousFiscalYearSalesAmount.HasValue
                    ? FormatTrendAmount(pt.PreviousFiscalYearSalesAmount.Value)
                    : "—";
                var currVal = pt.IsCurrentYearReported && pt.CurrentFiscalYearSalesAmount.HasValue
                    ? FormatTrendAmount(pt.CurrentFiscalYearSalesAmount.Value)
                    : "—";
                var avgVal = pt.Average12MonthSalesAmount.HasValue
                    ? FormatTrendAmount(pt.Average12MonthSalesAmount.Value)
                    : "—";
                sb.AppendLine($"| {pt.FiscalMonthNameFa} | {prevVal} | {currVal} | {avgVal} |");
            }
        }

        if (result.MissingDataPoints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"*داده‌های ناقص: {result.MissingDataPoints.Count} دوره موجود نیست.*");
        }

        sb.AppendLine();
        sb.AppendLine($"*منبع: {ProviderSources.GetDisplayName(result.SourceProviderName)} | محاسبه: {ShamsiMonthCalculator.FormatJalaliDate(result.CalculatedAtUtc)}*");

        return sb.ToString().TrimEnd();
    }

    private static string FormatTrendAmount(decimal value) =>
        value.ToString("#,##0.###");

    private static string BuildMonthlySalesQualityRankingContent(MonthlySalesQualityRankingResponse result)
    {
        var sb = new StringBuilder();
        var title = result.Direction == MonthlySalesQualityDirection.Bottom
            ? "ضعیف‌ترین گزارش‌ها از نظر کیفیت تولید و فروش"
            : "برترین گزارش‌ها از نظر کیفیت تولید و فروش";

        sb.AppendLine($"### {title}");
        sb.AppendLine($"دوره: {result.ReportYear}/{result.ReportMonth:D2}");
        sb.AppendLine("این رتبه‌بندی توصیه خرید/فروش نیست و فقط کیفیت داده‌های تولید و فروش را ارزیابی می‌کند.");
        sb.AppendLine();
        sb.AppendLine("| رتبه | نماد | شرکت | صنعت | امتیاز کیفیت | برچسب | دلیل اصلی | اطمینان |");
        sb.AppendLine("|---:|---|---|---|---:|---|---|---:|");

        foreach (var item in result.Items)
        {
            var mainDriver = item.PositiveDrivers.FirstOrDefault()
                ?? item.NegativeDrivers.FirstOrDefault()
                ?? "—";
            sb.AppendLine($"| {item.Rank} | {item.Symbol} | {item.CompanyName ?? "—"} | {item.IndustryTitle ?? "—"} | {item.QualityScore:F1} | {item.QualityLabel} | {mainDriver} | {item.ConfidenceScore:F0} |");
        }

        return sb.ToString().TrimEnd();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    private static DetectedIntent DetermineIntent(
        ScannerToolResult? scannerResult,
        SymbolLookupToolResult? lookupResult,
        ComprehensiveAnalysisToolResult? comprehensiveAnalysisResult,
        FinancialStatementAnalysisResponse? financialStatementAnalysisResult,
        FinancialStatementTableResult? financialStatementTableResult,
        ProductRevenueMixResponse? productRevenueMixResult,
        MonthlyActivityTrendResponse? monthlyActivityTrendResult,
        MonthlySalesQualityRankingResponse? monthlySalesQualityRankingResult)
    {
        if (scannerResult is not null) return DetectedIntent.Scanner;
        if (monthlySalesQualityRankingResult is not null) return DetectedIntent.MonthlySalesQualityRanking;
        if (monthlyActivityTrendResult is not null) return DetectedIntent.MonthlyActivityTrend;
        if (financialStatementTableResult is not null) return DetectedIntent.FinancialStatementTableLookup;
        if (financialStatementAnalysisResult is not null) return DetectedIntent.FinancialStatementPeriodAnalysis;
        if (productRevenueMixResult is not null) return DetectedIntent.ProductRevenueMix;
        if (lookupResult?.Table is not null && comprehensiveAnalysisResult?.Succeeded != true)
            return DetectedIntent.SymbolLookup;
        if (comprehensiveAnalysisResult?.Succeeded == true) return DetectedIntent.ComprehensiveAnalysis;
        if (lookupResult is not null) return DetectedIntent.SymbolLookup;
        return DetectedIntent.Unknown;
    }

    private bool IsDirectMetricLookupRequest(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var normalized = NormalizeForIntent(message);
        if (ContainsAny(normalized,
                "تحلیل", "بررسی", "ارزیابی", "چطوره", "ارزنده",
                "analysis", "analyze", "review", "opinion"))
        {
            return false;
        }

        if (ContainsAny(normalized,
                " زیر ", " بالای ", " بیشتر از ", " کمتر از ", "کمترين", "بیشترین",
                "فیلتر", "غربال", "screen", "filter", "below", "above", "greater than", "less than"))
        {
            return false;
        }

        return directMetricRoutingRegistry.ContainsDirectMetricTerm(
            normalized,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime));
    }

    private bool IsDirectMetricLookupFollowup(string message, string enrichedMessage)
    {
        if (string.IsNullOrWhiteSpace(message) ||
            string.Equals(message, enrichedMessage, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedMessage = NormalizeForIntent(message);
        if (ContainsAny(normalizedMessage,
                "تحلیل", "بررسی", "ارزیابی", "چطوره", "ارزنده",
                "analysis", "analyze", "review", "opinion",
                " زیر ", " بالای ", " بیشتر از ", " کمتر از ", "فیلتر", "غربال",
                "screen", "filter", "below", "above", "greater than", "less than"))
        {
            return false;
        }

        if (!LooksLikeShortSymbolOrCompanyReply(message))
            return false;

        return directMetricRoutingRegistry.ContainsDirectMetricTerm(
            NormalizeForIntent(enrichedMessage),
            DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime));
    }

    private static bool LooksLikeShortSymbolOrCompanyReply(string message)
    {
        var trimmed = message.Trim();
        if (trimmed.Length is < 2 or > 48)
            return false;

        var words = trimmed
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        return words.Length <= 4;
    }

    private static bool ContainsDirectMetricTerm(string normalized) =>
        ContainsAny(normalized,
            "فروش ماهانه", "آخرین فروش", "مبلغ فروش", "فروش آخرین ماه", "فروش ماه",
            "فروش ytd", "متوسط فروش 12 ماهه", "متوسط فروش ۱۲ ماهه",
            "میانگین فروش 12 ماهه", "میانگین فروش ۱۲ ماهه",
            "تولید ماهانه", "مقدار تولید", "p/e", "pe ", " p e",
            "نسبت پی به ای", "پی به ای", "نسبت قیمت به سود", "قیمت به سود",
            "p/s", "ps ", "eps",
            "roe", "roa", "نسبت جاری", "حاشیه سود", "ارزش بازار")
        || ContainsDirectQuoteMetricTerm(normalized);

    private static bool ContainsDirectQuoteMetricTerm(string normalized)
    {
        if (ContainsAny(normalized,
                "نسبت قیمت به سود", "قیمت به سود", "price to earnings", "price-to-earnings", "p/e", "pe "))
        {
            return false;
        }

        return ContainsAny(normalized,
            "آخرین قیمت",
            "قیمت امروز",
            "قیمت پایانی",
            "تغییر قیمت",
            "درصد تغییر قیمت",
            "درصد تغییر روزانه",
            "تغییر روزانه درصدی",
            "تغییر روزانه",
            " latest price ",
            " last price ",
            " closing price ",
            " daily change ",
            " daily change percent ",
            " daily change pct ",
            " change percent ")
            || HasStandalonePriceToken(normalized);
    }

    private static bool HasStandalonePriceToken(string normalized)
    {
        if (!normalized.Contains("قیمت", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains(" price ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ContainsAny(normalized,
            "قیمت به سود",
            "نسبت قیمت به سود",
            "price to earnings",
            "price-to-earnings");
    }

    private static string NormalizeForIntent(string value) =>
        $" {value.Replace('ي', 'ی').Replace('ك', 'ک').Trim().ToLowerInvariant()} ";

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

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
            // Null candidate forces ValidateScanner to return the deterministic count sentence.
            // Scanner prose must never enumerate symbols — the table carries the full results.
            return consistencyValidator
                .ValidateScanner(scannerResult.Table, scannerResult.Plan, null, context)
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
        You have four tools / routes available:

        - screen_stocks: Use when the user wants to screen or filter stocks by financial metric conditions (e.g., P/E below 10, high ROE, low debt).
        - lookup_symbol_metrics: Use when the user requests a specific financial metric value for a named stock.
          For valuation/financial-ratio lookups, fetch LATEST_PRICE and DAILY_CHANGE_PCT alongside the requested metric when useful.
          For monthly sales, monthly production, sales quantity, sales rate, and related monthly production/sales metrics, do NOT fetch or show LATEST_PRICE or DAILY_CHANGE_PCT.
          For CyclicalWaves general latest/monthly sales, return the monthly sales snapshot: latest monthly sales, 12-month average monthly sales, fiscal-year-to-date sales, and fiscal-year-to-previous-month sales.
          For Noavaran monthly activity latest/monthly sales, keep the spec 069 snapshot: latest monthly sales, prior fiscal-year same-month sales, fiscal-year-to-date sales, and fiscal-year-to-previous-month sales.
          For CyclicalWaves, include prior fiscal-year same-month sales instead of the 12-month average only when the user explicitly asks for same-month previous-period/year sales.
        - query_comprehensive_analysis: Searches the ComprehensiveAnalyses database for expert narrative analysis posts.
          Use ONLY for analysis/opinion/review/outlook requests — NOT for financial metric requests.
        - ProductRevenueMix: handled by the workflow before tool execution; asks which product contributes the most revenue,
          product mix/composition, dominant product, most important product, or similar monthly product-sales questions.
          Do NOT route these to lookup_symbol_metrics.
        - Monthly sales trend: handled by the workflow before tool execution. The equivalent Persian
          requests `روند فروش ماهانه`, `چارت فروش ماهانه`, `روند فروش`, `روند تولید و فروش`,
          `نمودار تولید و فروش ماهانه`, `نمودار فروش`, and `نمودار فروش ماهانه` with a symbol
          return the canonical monthly sales-trend chart result. Do NOT call a tool, generate a
          chart yourself, or infer a separate production series for these requests.

        ── INTENT CLASSIFICATION (decide before calling any tool) ──

        ANALYSIS INTENT → call query_comprehensive_analysis (+ lookup_symbol_metrics in parallel):
          Triggers: تحلیل, بررسی کن, بررسی, ارزنده است؟, نظرت چیه, نظر, وضعیت, ارزیابی, چطوره,
                    آخرین تحلیل, outlook, review, analyze, opinion, investment decision
          Examples: "شغدیر را بررسی کن", "تحلیل شغدیر", "شغدیر ارزنده است؟", "نظرت درباره شغدیر"
          Action: call BOTH query_comprehensive_analysis AND lookup_symbol_metrics in parallel.
          Do NOT ask clarifying questions. The symbol itself is sufficient.

        FINANCIAL METRIC INTENT → call lookup_symbol_metrics ONLY (never query_comprehensive_analysis):
          Triggers: any specific metric name alongside a symbol — P/E, P/S, EPS, فروش, درآمد, سود خالص,
                    حاشیه سود, ارزش بازار, تولید ماهانه, نسبت جاری, ROE, ROA, MONTHLY_SALES,
                    آخرین قیمت, قیمت امروز, قیمت پایانی, تغییر قیمت, درصد تغییر قیمت
          Examples: "P/E شغدیر", "EPS فملی", "فروش ماهانه شغدیر", "تولید ماهانه کگل", "ROE کگل",
                    "آخرین قیمت کگل", "قیمت امروز کچاد", "درصد تغییر قیمت کگل"
          Action: call lookup_symbol_metrics ONLY. Do NOT call query_comprehensive_analysis.
          Return the metric value directly. Do NOT summarize analyst reports.

        PRODUCT REVENUE MIX INTENT → handled by the workflow before tool execution:
          Triggers: پرفروش‌ترین/مهم‌ترین/بیشترین درآمد/ترکیب فروش محصولات/محصول اصلی/dominant product/most important product
          Action: do NOT call lookup_symbol_metrics. Return the product revenue mix answer from Noavaran monthly sales data.

        SCREENING INTENT → call screen_stocks ONLY:
          Triggers: condition + threshold across many stocks ("P/E زیر ۵", "سهام با رشد بالا")

        ── TOOL CALL PRIORITY FOR ANALYSIS INTENT ──
          1. query_comprehensive_analysis result → present verbatim (see Faithfulness Rule below)
          2. lookup_symbol_metrics result → present as live metrics block
          3. Only fall back to AI reasoning if query_comprehensive_analysis returns ZERO results

        ── FAITHFULNESS RULE (CRITICAL — applies to analysis intent only) ──
        When query_comprehensive_analysis returns analysis text, you MUST:
        - Copy the author's statements, numbers, and conclusions EXACTLY as written in PlainTextSummary.
        - Do NOT paraphrase, generalize, or soften any numeric fact (ارزش ذاتی, P/E, P/S, قیمت تعادلی, سود, تقسیم سود, EPS).
        - Do NOT rewrite conclusions. If the source says "سوپر مفت" or "ارزنده", use those exact words.
        - Do NOT add your own technical analysis, support/resistance levels, or valuation estimates.
        - Do NOT expand with AI-generated commentary.
        - You MAY only: add section headers, improve readability, translate section titles.
        - Always cite: analysis title, PersianCreatedAt date, and AuthorName.

        Output format when analysis is found:
        آخرین تحلیل یافت‌شده برای {symbol}:
        تاریخ: {PersianCreatedAt}
        [sections from PlainTextSummary verbatim]
        منبع: ComprehensiveAnalyses | نویسنده: {AuthorName}

        If NO analysis found in the database:
        "تحلیل جدیدی از نماد {symbol} در ۳۰ روز گذشته یافت نشد."
        Do NOT generate your own stock analysis. Do NOT speculate.

        ── DATA PERIOD TRANSPARENCY (quarterly and monthly metrics) ──
        When returning quarterly metrics (سود خالص, فروش فصلی, حاشیه سود, EPS, P/E, P/S, رشد فصلی,
        میانگین فروش, etc.) or monthly sales metrics, ALWAYS state the reporting period.
        Use the SourceTimestamp field (period end date) from the tool response to indicate which quarter
        or month the data belongs to. For example:
          - "فروش فصلی فملی در فصل منتهی به شهریور ۱۴۰۳: ۱۲۵ میلیارد تومان"
          - "حاشیه سود خالص فصل اخیر (تا تیر ۱۴۰۳): ۱۸٪"
        If the period end date is not available in the response, say "آخرین دوره موجود" instead of
        asserting a specific date. Do NOT present quarterly or margin data without a period reference.

        ── ANTI-HALLUCINATION RULES (CRITICAL) ──
        1. DISPLAY NAMES: Always use the Persian column header from the tool response as the metric label.
           NEVER show raw English metric codes (NET_PROFIT_MARGIN, OPERATING_PROFIT_MARGIN, PE_TTM, etc.)
           to the user. The column header in the tool response is already localized.
        2. SYMBOL DISPLAY: Always use the Persian ticker symbol (نماد) from the SYMBOL cell of the tool
           response. NEVER show English tickers (PGDR, FMELI, etc.), instrument IDs, or database keys.
           Show it as: نماد: {Persian ticker} | شرکت: {Persian company name}
        3. DATA FAITHFULNESS (most critical): If lookup_symbol_metrics returns a row with a non-null
           value in any metric cell, you MUST present that exact value. NEVER say "data not found",
           "عدد دقیق در خروجی برنگشت", "پیدا نشد", or "در دسترس نیست" for a metric that has a
           returned value. Only declare data unavailable when the cell's FreshnessStatus is Missing
           AND there is no numeric value in the cell.
        4. PRICE FRESHNESS: Show the price only when the tool response includes price columns. If SourceKind is Live or
           Intraday, label it as "آخرین قیمت (لحظه‌ای)". If PreviousTradingDay, label it as
           "آخرین قیمت (پایان جلسه قبل)". Monthly production/sales lookup responses intentionally omit price and daily-change columns.

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

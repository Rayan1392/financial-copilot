using System.Text;
using System.Text.Json;
using System.Diagnostics;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;

namespace FinancialCopilot.Application.AI.Orchestration;

public sealed class AiQueryOrchestrationService(
    IConversationRepository conversationRepository,
    IAiIntentDetector intentDetector,
    IScannerQueryParser scannerParser,
    IScannerExecutionService scannerExecutionService,
    IExplainableAnswerBuilder explainableAnswerBuilder,
    IScannerCache scannerCache,
    IBillingFacadeHook billingHook,
    IMemoryContextProvider memoryContextProvider,
    IMemoryAuditService memoryAuditService,
    ISymbolLookupParser symbolLookupParser,
    ISymbolMetricLookupService symbolMetricLookupService,
    IMissingAnswerFeedbackCollector feedbackCollector,
    IAnswerConsistencyValidator consistencyValidator,
    ISymbolLookupProseBuilder symbolLookupProseBuilder,
    IConfidenceScoringService confidenceScoringService,
    IComprehensiveAnalysisQueryParser comprehensiveAnalysisParser,
    IComprehensiveAnalysisQueryUseCase comprehensiveAnalysisUseCase,
    IFinancialStatementAnalysisUseCase financialStatementAnalysisUseCase,
    IFinancialStatementTableQueryUseCase financialStatementTableQueryUseCase,
    IProductRevenueMixQueryUseCase productRevenueMixUseCase,
    IMonthlyActivityTrendQueryUseCase monthlyActivityTrendUseCase,
    IDisclosureListingUseCase disclosureListingUseCase,
    IExplainInsightUseCase explainInsightUseCase,
    IConversationDialogueGate dialogueGate,
    ISemanticExecutionCoordinator semanticExecutionCoordinator,
    ISemanticRoutingRolloutCoordinator semanticRolloutCoordinator,
    ICapabilityGuidanceService guidanceService,
    ISemanticDialogueOutcomeTelemetry outcomeTelemetry,
    TimeProvider timeProvider,
    ISalesGrowthScannerTelemetrySink? salesGrowthTelemetry = null) : IAiQueryOrchestrationService
{
    public async Task<AiQueryResponse> ExecuteAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var createConversation = request.ConversationId is null;
        var conversationId = request.ConversationId ?? Guid.NewGuid();
        if (!createConversation &&
            await conversationRepository.FindAsync(
                conversationId,
                request.TenantId,
                request.ActorId,
                cancellationToken) is null)
        {
            throw new ConversationNotFoundException(conversationId);
        }
        request = (await dialogueGate.PrepareAsync(request, conversationId, cancellationToken)).Request;

        // Retrieve authorized memory context before AI execution.
        var subjectId = request.UserId ?? request.ActorId;
        var memoryContext = await memoryContextProvider.GetAuthorizedContextAsync(
            new MemoryContextRequest(
                new MemorySubject(request.TenantId, subjectId),
                conversationId,
                MemoryPurpose.CurrentConversationContinuity,
                request.CorrelationId,
                PermitProviderPromptContext: true),
            cancellationToken);

        var enrichedMessage = BuildEnrichedMessage(request.Message, memoryContext);

        BillingReservationHandle? billingReservation = request.SemanticFrame is null
            ? await billingHook.TryReserveAsync(
            new BillingReservationRequest(
                request.CorrelationId,
                request.TenantId,
                request.ActorId,
                "AiQuery.Scanner",
                request.UserId,
                request.ApiClientId,
                request.ExternalUserId),
            cancellationToken)
            : null;

        ScannerQueryPlan? scannerPlan = null;
        ScannerTableResult? scannerTable = null;
        SymbolLookupTableResult? symbolLookupTable = null;
        ComprehensiveAnalysisQueryResponse? comprehensiveAnalysisResult = null;
        FinancialStatementAnalysisResponse? financialStatementAnalysisResult = null;
        FinancialStatementTableResult? financialStatementTableResult = null;
        ProductRevenueMixResponse? productRevenueMixResult = null;
        MonthlyActivityTrendResponse? monthlyActivityTrendResult = null;
        MonthlySalesQualityRankingResponse? monthlySalesQualityRankingResult = null;
        DisclosureListingResult? disclosureListingResult = null;
        PsVisualizationResult? psVisualizationResult = null;
        ExplainableAnswer? explainableAnswer = null;
        ConfidenceScoreResult? confidenceScore = null;
        string? textAnswer = null;
        bool clarificationRequired;
        string? clarificationMessage;
        var detectedIntent = DetectedIntent.Unknown;
        UsageAccountingResult? usage = null;
        CapabilityExecutionResult? semanticExecution = null;
        var completionStatus = "Completed";
        var fromCache = false;

        try
        {
            if (request.Context?.InsightEventId is Guid insightEventId && request.SemanticFrame is null)
            {
                detectedIntent = DetectedIntent.PersonalizedInsightExplanation;
                textAnswer = await explainInsightUseCase.ExecuteAsync(
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
                clarificationRequired = false;
                clarificationMessage = null;
            }
            else if (request.SemanticFrame is { } semanticFrame)
            {
                var semantic = await semanticExecutionCoordinator.ExecuteAsync(
                    semanticFrame,
                    new QueryExecutionContext(
                        request.TenantId,
                        request.ActorId,
                        conversationId,
                        request.CorrelationId,
                        semanticFrame.Interpretation.ReplyLanguage,
                        now,
                        request.ScannerPage,
                        request.ScannerPageSize,
                        request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
                        request.ActorType,
                        request.AuthenticationMode,
                        request.UserId,
                        request.ApiClientId),
                    request,
                    cancellationToken);
                usage = semantic.Usage;
                semanticExecution = semantic.Execution;
                detectedIntent = SemanticIntent(semanticFrame.CapabilityCode);
                clarificationRequired = semantic.Execution.Status is CapabilityExecutionStatus.ClarificationRequired or CapabilityExecutionStatus.DisambiguationRequired;
                clarificationMessage = clarificationRequired
                    ? AiDialogueOutcomePolicy.ComposeSystemMessage(new DialogueOutcomeResult(
                        semantic.Execution.Status == CapabilityExecutionStatus.DisambiguationRequired ? DialogueOutcome.DisambiguationNeeded : DialogueOutcome.ClarificationNeeded,
                        semantic.Execution.ReasonCode,
                        semanticFrame.Interpretation.ReplyLanguage,
                        null,
                        false))
                    : null;
                completionStatus = semantic.Execution.Status.ToString();
                switch (semantic.Execution.Payload)
                {
                    case SemanticScannerPayload scanner:
                        scannerPlan = scanner.Plan;
                        scannerTable = scanner.Table;
                        explainableAnswer = await explainableAnswerBuilder.BuildAsync(new ExplainableAnswerRequest(scanner.Plan, scanner.Table, request.TenantId, request.CorrelationId), cancellationToken);
                        break;
                    case SymbolLookupTableResult lookup: symbolLookupTable = lookup; break;
                    case ComprehensiveAnalysisQueryResponse analysis: comprehensiveAnalysisResult = analysis; break;
                    case SemanticComprehensiveAnalysisPayload combined:
                        comprehensiveAnalysisResult = combined.Analysis;
                        symbolLookupTable = combined.Lookup;
                        break;
                    case FinancialStatementAnalysisResponse analysis: financialStatementAnalysisResult = analysis; break;
                    case FinancialStatementTableResult table: financialStatementTableResult = table; break;
                    case ProductRevenueMixResponse product: productRevenueMixResult = product; break;
                    case MonthlyActivityTrendResponse trend: monthlyActivityTrendResult = trend; break;
                    case MonthlySalesQualityRankingResponse ranking: monthlySalesQualityRankingResult = ranking; break;
                    case DisclosureListingResult disclosures: disclosureListingResult = disclosures; break;
                    case PsVisualizationResult gauge: psVisualizationResult = gauge; break;
                    case string explanation: textAnswer = explanation; break;
                }
            }
            else
            {
            var intentResult = await intentDetector.DetectAsync(
                new IntentDetectionInput(
                    request.Message,
                    "en",
                    request.CorrelationId,
                    request.TenantId),
                cancellationToken);

            detectedIntent = intentResult.Intent;
            if (request.SemanticShadowFrame is { } shadowFrame)
                semanticRolloutCoordinator.RecordShadowComparison(
                    shadowFrame.CapabilityCode,
                    SemanticRouteMapping.FromIntent(detectedIntent),
                    shadowFrame.CapabilityCode,
                    request.CorrelationId);

            if (intentResult.Intent == DetectedIntent.Scanner)
            {
                var cacheScope = new ScannerCacheScope(
                    request.TenantId,
                    request.ActorId,
                    request.ApiClientId);
                var dataVersion = await scannerCache.GetDataVersionAsync(cancellationToken);
                var parseRequest = new ScannerParseRequest(
                    enrichedMessage,
                    "en",
                    request.CorrelationId,
                    request.TenantId,
                    DateOnly.FromDateTime(now.DateTime));
                var parseResult = await scannerCache.GetPlanAsync(
                    cacheScope,
                    dataVersion,
                    parseRequest,
                    cancellationToken) ??
                    await scannerParser.ParseAsync(parseRequest, cancellationToken);

                if (parseResult.Succeeded && !parseResult.Plan.ClarificationRequired)
                {
                    await scannerCache.SetPlanAsync(
                        cacheScope,
                        dataVersion,
                        parseRequest,
                        parseResult,
                        cancellationToken);
                }

                scannerPlan = parseResult.Plan;
                clarificationRequired = parseResult.Plan.ClarificationRequired;
                clarificationMessage = parseResult.Plan.ClarificationMessage;

                if (!parseResult.Succeeded)
                {
                    clarificationRequired = true;
                    clarificationMessage = parseResult.FailureReason;
                    completionStatus = "ValidationFailed";
                }
                else if (!clarificationRequired)
                {
                    var executionRequest = new ScannerExecutionRequest(
                        parseResult.Plan,
                        DateOnly.FromDateTime(now.DateTime),
                        Page: request.ScannerPage,
                        PageSize: request.ScannerPageSize,
                        ActorId: request.ActorId.ToString(),
                        QueryText: request.Message);
                    var cachedTable = await scannerCache.GetResultAsync(
                        cacheScope,
                        dataVersion,
                        executionRequest,
                        cancellationToken);

                    if (cachedTable is not null)
                    {
                        scannerTable = cachedTable with
                        {
                            ExecutionFacts = cachedTable.ExecutionFacts with { FromCache = true }
                        };
                    }
                    else
                    {
                        scannerTable = await scannerExecutionService.ExecuteAsync(
                            executionRequest,
                            cancellationToken);
                        await scannerCache.SetResultAsync(
                            cacheScope,
                            dataVersion,
                            executionRequest,
                            scannerTable,
                            cancellationToken);
                    }

                    fromCache = scannerTable.ExecutionFacts.FromCache;

                    explainableAnswer = await explainableAnswerBuilder.BuildAsync(
                        new ExplainableAnswerRequest(
                            parseResult.Plan,
                            scannerTable,
                            request.TenantId,
                            request.CorrelationId),
                        cancellationToken);
                }
                else
                {
                    completionStatus = "ClarificationRequired";
                }
            }
            else if (intentResult.Intent == DetectedIntent.SymbolLookup)
            {
                var parseRequest = new SymbolLookupParseRequest(
                    enrichedMessage,
                    "fa",
                    request.CorrelationId,
                    request.TenantId,
                    DateOnly.FromDateTime(now.DateTime));

                var parseResult = await symbolLookupParser.ParseAsync(parseRequest, cancellationToken);

                if (parseResult.Status == LookupParseStatus.ClarificationRequired)
                {
                    clarificationRequired = true;
                    clarificationMessage = parseResult.ClarificationMessage ??
                        (ContainsPersianText(request.Message)
                            ? "لطفاً نام نماد و معیار مالی موردنظر را مشخص کنید."
                            : "Please specify the symbol name and the metric you want to look up.");
                    completionStatus = "ClarificationRequired";
                }
                else
                {
                    // Build request from resolved pairs (symbol name + metric code).
                    var lookupPairs = parseResult.Pairs
                        .Where(p => p.ResolvedMetricCode is not null)
                        .Select(p => new SymbolLookupRequestPair(
                            p.RawSymbolName,
                            p.ResolvedMetricCode!,
                            p.PeriodSelector))
                        .ToList();

                    var lookupRequest = new SymbolLookupRequest(
                        lookupPairs,
                        DateOnly.FromDateTime(now.DateTime),
                        ActorId: request.ActorId.ToString(),
                        QueryText: request.Message);

                    symbolLookupTable = await symbolMetricLookupService.LookupAsync(
                        lookupRequest,
                        cancellationToken);

                    // Collect feedback for unresolved symbols and missing data.
                    await TryCollectLookupFeedbackAsync(
                        request,
                        symbolLookupTable,
                        now,
                        cancellationToken);

                    clarificationRequired = false;
                    clarificationMessage = null;
                }
            }
            else if (intentResult.Intent == DetectedIntent.ComprehensiveAnalysis)
            {
                var parseResult = await comprehensiveAnalysisParser.ParseAsync(
                    enrichedMessage, cancellationToken);

                if (parseResult.Status == ComprehensiveAnalysisParseStatus.ClarificationRequired)
                {
                    clarificationRequired = true;
                    clarificationMessage = parseResult.ClarificationPrompt ??
                        "لطفاً نماد سهم، نوع تحلیل، یا بازه زمانی مورد نظر را مشخص کنید.";
                    completionStatus = "ClarificationRequired";

                    try
                    {
                        await feedbackCollector.CollectAsync(
                            new MissingAnswerFeedbackRequest(
                                ActorId: request.ActorId.ToString(),
                                QueryText: request.Message,
                                Classification: MissingAnswerFeedbackClassification.ParserLimitation,
                                RequestedMetricCode: null,
                                AffectedDataCodeOrName: null,
                                SymbolCountTotal: 0,
                                SymbolCountMatched: 0,
                                SubmittedAt: now,
                                Context: "ComprehensiveAnalysis: parser returned ClarificationRequired"),
                            cancellationToken);
                    }
                    catch { /* fire-and-forget */ }
                }
                else
                {
                    var queryRequest = new ComprehensiveAnalysisQueryRequest(
                        parseResult.SymbolNames,
                        parseResult.TopicTags,
                        parseResult.FromDate,
                        parseResult.Limit);

                    comprehensiveAnalysisResult = await comprehensiveAnalysisUseCase.ExecuteAsync(
                        queryRequest, cancellationToken);

                    clarificationRequired = false;
                    clarificationMessage = null;

                    if (!comprehensiveAnalysisResult.HasResults && parseResult.SymbolNames.Count > 0)
                    {
                        try
                        {
                            await feedbackCollector.CollectAsync(
                                new MissingAnswerFeedbackRequest(
                                    ActorId: request.ActorId.ToString(),
                                    QueryText: request.Message,
                                    Classification: MissingAnswerFeedbackClassification.DataCoverageGap,
                                    RequestedMetricCode: null,
                                    AffectedDataCodeOrName: string.Join(",", parseResult.SymbolNames),
                                    SymbolCountTotal: parseResult.SymbolNames.Count,
                                    SymbolCountMatched: 0,
                                    SubmittedAt: now,
                                    Context: $"ComprehensiveAnalysis: no results for symbols [{string.Join(",", parseResult.SymbolNames)}]"),
                                cancellationToken);
                        }
                        catch { /* fire-and-forget */ }
                    }
                }
            }
            else if (intentResult.Intent == DetectedIntent.FinancialStatementPeriodAnalysis)
            {
                var analysisQuery = FinancialStatementAnalysisIntentRules.BuildQuery(request.Message);
                financialStatementAnalysisResult = await financialStatementAnalysisUseCase.ExecuteAsync(
                    analysisQuery,
                    cancellationToken);

                clarificationRequired = false;
                clarificationMessage = null;

                if (financialStatementAnalysisResult is null)
                {
                    textAnswer = "اطلاعات صورت‌های مالی برای نماد یا شرکت درخواستی در پایگاه داده یافت نشد.";
                }
            }
            else if (intentResult.Intent == DetectedIntent.DisclosureListing)
            {
                var disclosureQuery = DisclosureListingIntentRules.BuildQuery(request.Message, now, request.DisclosurePage, request.DisclosurePageSize) with { Channel = request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai" };
                disclosureListingResult = await disclosureListingUseCase.ExecuteAsync(disclosureQuery, cancellationToken);
                clarificationRequired = false;
                clarificationMessage = null;
                textAnswer = BuildDisclosureListingContent(disclosureListingResult);
            }
            else if (intentResult.Intent == DetectedIntent.FinancialStatementTableLookup)
            {
                var tableQuery = FinancialStatementTableIntentRules.BuildQuery(request.Message);
                financialStatementTableResult = await financialStatementTableQueryUseCase.ExecuteAsync(
                    tableQuery,
                    cancellationToken);

                clarificationRequired = false;
                clarificationMessage = null;

                if (financialStatementTableResult is null)
                {
                    textAnswer = "اطلاعات صورت مالی برای نماد یا شرکت درخواستی با فیلترهای اعمال شده در پایگاه داده یافت نشد.";
                }
            }
            else if (intentResult.Intent == DetectedIntent.ProductRevenueMix)
            {
                // Extract company symbol from query using a simple heuristic:
                // take the last Persian word or any 2-5-char uppercase token as the symbol.
                var symbol = ExtractProductRevenueMixSymbol(request.Message);
                if (symbol is null)
                {
                    clarificationRequired = true;
                    clarificationMessage = "لطفاً نام نماد یا شرکت موردنظر را در پرسش خود مشخص کنید.";
                    completionStatus = "ClarificationRequired";
                }
                else
                {
                    productRevenueMixResult = await productRevenueMixUseCase.ExecuteAsync(
                        new ProductRevenueMixQuery(symbol),
                        cancellationToken);

                    clarificationRequired = false;
                    clarificationMessage = null;

                    if (productRevenueMixResult is null)
                    {
                        textAnswer = $"اطلاعات ترکیب درآمد محصولات برای نماد «{symbol}» در پایگاه داده یافت نشد.";

                        try
                        {
                            await feedbackCollector.CollectAsync(
                                new MissingAnswerFeedbackRequest(
                                    ActorId: request.ActorId.ToString(),
                                    QueryText: request.Message,
                                    Classification: MissingAnswerFeedbackClassification.DataCoverageGap,
                                    RequestedMetricCode: "PRODUCT_REVENUE_MIX",
                                    AffectedDataCodeOrName: symbol,
                                    SymbolCountTotal: 1,
                                    SymbolCountMatched: 0,
                                    SubmittedAt: now,
                                    Context: $"ProductRevenueMix: no data for symbol [{symbol}]"),
                                cancellationToken);
                        }
                        catch { /* fire-and-forget */ }
                    }
                }
            }
            else if (intentResult.Intent == DetectedIntent.MonthlyActivityTrend)
            {
                var symbol = MonthlyActivityTrendIntentRules.ExtractCompanySymbol(request.Message);
                if (symbol is null)
                {
                    clarificationRequired = true;
                    clarificationMessage = "لطفاً نام نماد یا شرکت موردنظر را در پرسش خود مشخص کنید.";
                    completionStatus = "ClarificationRequired";
                }
                else
                {
                    monthlyActivityTrendResult = await monthlyActivityTrendUseCase.ExecuteAsync(
                        new MonthlyActivityTrendQuery(request.Message, symbol),
                        cancellationToken);

                    clarificationRequired = false;
                    clarificationMessage = null;

                    if (monthlyActivityTrendResult is null)
                    {
                        textAnswer = $"اطلاعات روند فروش ماهانه برای نماد «{symbol}» در پایگاه داده یافت نشد.";

                        try
                        {
                            await feedbackCollector.CollectAsync(
                                new MissingAnswerFeedbackRequest(
                                    ActorId: request.ActorId.ToString(),
                                    QueryText: request.Message,
                                    Classification: MissingAnswerFeedbackClassification.DataCoverageGap,
                                    RequestedMetricCode: "MONTHLY_ACTIVITY_TREND",
                                    AffectedDataCodeOrName: symbol,
                                    SymbolCountTotal: 1,
                                    SymbolCountMatched: 0,
                                    SubmittedAt: now,
                                    Context: $"MonthlyActivityTrend: no snapshot data for symbol [{symbol}]"),
                                cancellationToken);
                        }
                        catch { /* fire-and-forget */ }
                    }
                }
            }
            else if (intentResult.Intent == DetectedIntent.Clarification)
            {
                clarificationRequired = true;
                clarificationMessage = ContainsPersianText(request.Message)
                    ? "برای بررسی نمادها، لطفاً پرسش خود را با جزئیات بیشتری بیان کنید."
                    : "Your request needs clarification before I can screen stocks.";
                completionStatus = "ClarificationRequired";
            }
            else
            {
                clarificationRequired = false;
                clarificationMessage = null;
                textAnswer = ContainsPersianText(request.Message)
                    ? "می‌توانم نمادها را بر اساس معیارهای مالی بررسی و فیلتر کنم. لطفاً معیارهای موردنظر خود را توضیح دهید."
                    : "I can help you screen and filter stocks by financial metrics. Please describe your screening criteria.";
            }
            }

            if (billingReservation is not null)
            {
                usage = await billingHook.FinalizeAsync(
                    billingReservation,
                    new BillingFinalizationRequest(completionStatus, fromCache),
                    cancellationToken);
            }

            if (scannerPlan?.SalesGrowth is not null)
            {
                await (salesGrowthTelemetry ?? new NoOpSalesGrowthScannerTelemetrySink()).RecordAsync(
                    SalesGrowthScannerTelemetry.Create(
                        request.CorrelationId, request.TenantId, request.ActorId, scannerPlan, scannerTable,
                        timeProvider.GetUtcNow() - now, completionStatus,
                        billingReservation is null ? "not-reserved" : usage?.CompletionStatus ?? completionStatus,
                        parserOutcome: scannerPlan.ClarificationRequired ? "clarification" : "parsed"),
                    CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            if (scannerPlan?.SalesGrowth is not null)
            {
                await (salesGrowthTelemetry ?? new NoOpSalesGrowthScannerTelemetrySink()).RecordAsync(
                    SalesGrowthScannerTelemetry.Create(
                        request.CorrelationId, request.TenantId, request.ActorId, scannerPlan, scannerTable,
                        timeProvider.GetUtcNow() - now, "CancelledBeforeExecution",
                        billingReservation is null ? "not-reserved" : "cancelled",
                        parserOutcome: scannerPlan.ClarificationRequired ? "clarification" : "parsed",
                        timedOut: true),
                    CancellationToken.None);
            }
            if (billingReservation is not null)
            {
                await billingHook.FinalizeAsync(
                    billingReservation,
                    new BillingFinalizationRequest("CancelledBeforeExecution"),
                    CancellationToken.None);
            }

            throw;
        }
        catch
        {
            if (scannerPlan?.SalesGrowth is not null)
            {
                await (salesGrowthTelemetry ?? new NoOpSalesGrowthScannerTelemetrySink()).RecordAsync(
                    SalesGrowthScannerTelemetry.Create(
                        request.CorrelationId, request.TenantId, request.ActorId, scannerPlan, scannerTable,
                        timeProvider.GetUtcNow() - now, "ProviderFailed",
                        billingReservation is null ? "not-reserved" : "provider-failed",
                        parserOutcome: scannerPlan.ClarificationRequired ? "clarification" : "parsed"),
                    CancellationToken.None);
            }
            if (billingReservation is not null)
            {
                await billingHook.FinalizeAsync(
                    billingReservation,
                    new BillingFinalizationRequest("ProviderFailed"),
                    CancellationToken.None);
            }
            throw;
        }

        // Record audit events for each memory item used in this execution.
        foreach (var item in memoryContext.Items)
        {
            await memoryAuditService.RecordAsync(new MemoryAuditEvent(
                Guid.NewGuid(),
                item.Owner,
                item.MemoryId,
                MemoryAuditAction.UsedInAnswer,
                item.Purpose,
                request.CorrelationId,
                timeProvider.GetUtcNow()),
                CancellationToken.None);
        }

        var planJson = scannerPlan is not null
            ? JsonSerializer.Serialize(scannerPlan)
            : null;

        var consistencyContext = new AnswerConsistencyContext(
            request.CorrelationId, conversationId, "V1", WorkflowVersion: 1);
        var assistantContent = BuildAssistantContent(
            detectedIntent, scannerPlan, scannerTable, symbolLookupTable,
            explainableAnswer, textAnswer, clarificationRequired, clarificationMessage,
            consistencyContext, comprehensiveAnalysisResult, financialStatementAnalysisResult, productRevenueMixResult,
            monthlyActivityTrendResult,
            financialStatementTableResult);

        var hasStructuredResult =
            scannerTable is not null ||
            symbolLookupTable is not null ||
            comprehensiveAnalysisResult is not null ||
            financialStatementAnalysisResult is not null ||
            financialStatementTableResult is not null ||
            productRevenueMixResult is not null ||
            monthlyActivityTrendResult is not null ||
            monthlySalesQualityRankingResult is not null ||
            disclosureListingResult is not null ||
            psVisualizationResult is not null ||
            (detectedIntent is not DetectedIntent.Unknown and not DetectedIntent.PersonalizedInsightExplanation);

        var hasData =
            scannerTable is not null ||
            symbolLookupTable?.Rows.Count > 0 ||
            comprehensiveAnalysisResult?.HasResults == true ||
            financialStatementAnalysisResult is not null ||
            financialStatementTableResult is not null ||
            productRevenueMixResult is not null ||
            monthlyActivityTrendResult is not null ||
            disclosureListingResult?.Items.Count > 0 ||
            detectedIntent == DetectedIntent.PersonalizedInsightExplanation;

        var hasUnresolvedEntity =
            symbolLookupTable?.UnresolvedSymbols.Count > 0 ||
            comprehensiveAnalysisResult?.UnresolvedSymbols.Count > 0;

        var outcome = semanticExecution is null
            ? AiDialogueOutcomePolicy.Determine(
                request.Message,
                detectedIntent,
                clarificationRequired,
                clarificationMessage,
                hasStructuredResult,
                hasData,
                hasUnresolvedEntity)
            : new DialogueOutcomeResult(
                SemanticDialogueOutcome(semanticExecution.Status),
                semanticExecution.ReasonCode,
                request.SemanticFrame!.Interpretation.ReplyLanguage,
                null,
                false);
        outcome = AiDialogueOutcomePolicy.ApplyLanguageGuard(
            outcome,
            outcome.SafeDetail ?? (detectedIntent == DetectedIntent.Unknown ? null : assistantContent));
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
        {
            assistantContent = AiDialogueOutcomePolicy.ComposeSystemMessage(
                outcome,
                detectedIntent == DetectedIntent.Unknown ? null : assistantContent);
        }

        confidenceScore = CalculateConfidenceScore(
            request.CorrelationId,
            assistantContent,
            scannerTable,
            symbolLookupTable,
            explainableAnswer);
        var responseTextAnswer = detectedIntent is DetectedIntent.SymbolLookup
            or DetectedIntent.FinancialStatementPeriodAnalysis
            or DetectedIntent.FinancialStatementTableLookup
            or DetectedIntent.ProductRevenueMix
            or DetectedIntent.MonthlyActivityTrend
            or DetectedIntent.DisclosureListing
            or DetectedIntent.PersonalizedInsightExplanation
            ? assistantContent
            : textAnswer;
        if (outcome.Outcome != DialogueOutcome.Answered && outcome.Outcome != DialogueOutcome.PartialAnswer)
            responseTextAnswer = assistantContent;

        await dialogueGate.RecordOutcomeAsync(
            request, conversationId, clarificationRequired, outcome.ReasonCode, cancellationToken);
        var suggestedActions = guidanceService.Suggest(new CapabilityGuidanceRequest(
            request.OriginalUserMessage ?? request.Message,
            outcome.ReplyLanguage,
            outcome.Outcome,
            outcome.ReasonCode,
            request.SemanticFrame?.Interpretation,
            CorrelationId: request.CorrelationId,
            Channel: request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai"));

        var persistedExchange = await conversationRepository.PersistExchangeAsync(
            new ConversationExchange(
                conversationId,
                request.TenantId,
                request.ActorId,
                timeProvider.GetUtcNow(),
                BuildConversationTitle(request.OriginalUserMessage ?? request.Message),
                request.OriginalUserMessage ?? request.Message,
                assistantContent,
                planJson,
                new AssistantMessagePayload(
                    Version: 1,
                    detectedIntent,
                    clarificationRequired,
                    clarificationMessage,
                    responseTextAnswer,
                    scannerPlan,
                    scannerTable,
                    symbolLookupTable,
                    explainableAnswer,
                    confidenceScore,
                    usage,
                    memoryContext.Disclosures.Count > 0 ? memoryContext.Disclosures : null,
                    comprehensiveAnalysisResult,
                    financialStatementAnalysisResult,
                    financialStatementTableResult,
                    productRevenueMixResult,
                    monthlyActivityTrendResult,
                    MonthlySalesQualityRankingResult: monthlySalesQualityRankingResult,
                    DisclosureListingResult: disclosureListingResult,
                    PsVisualizationResult: psVisualizationResult,
                    Outcome: outcome.Outcome,
                    OutcomeReasonCode: outcome.ReasonCode,
                    ReplyLanguage: outcome.ReplyLanguage,
                    LanguageGuardApplied: outcome.LanguageGuardApplied,
                    SuggestedActions: suggestedActions,
                    SemanticCapabilityCode: request.SemanticFrame?.CapabilityCode ?? request.SemanticShadowFrame?.CapabilityCode,
                    SemanticRegistryVersion: request.SemanticFrame?.RegistryVersion ?? request.SemanticShadowFrame?.RegistryVersion)),
            createConversation,
            cancellationToken);

        return new AiQueryResponse(
            conversationId,
            persistedExchange.UserMessageId,
            persistedExchange.AssistantMessageId,
            detectedIntent,
            scannerPlan,
            scannerTable,
            symbolLookupTable,
            explainableAnswer,
            confidenceScore,
            responseTextAnswer,
            clarificationRequired,
            clarificationMessage,
            usage,
            memoryContext.Disclosures.Count > 0 ? memoryContext.Disclosures : null,
            AiOrchestrationMode: "V1",
            WorkflowVersion: "1",
            WorkflowCorrelationId: request.CorrelationId,
            ComprehensiveAnalysisResult: comprehensiveAnalysisResult,
            FinancialStatementAnalysisResult: financialStatementAnalysisResult,
            FinancialStatementTableResult: financialStatementTableResult,
            ProductRevenueMixResult: productRevenueMixResult,
            MonthlyActivityTrendResult: monthlyActivityTrendResult,
            MonthlySalesQualityRankingResult: monthlySalesQualityRankingResult,
            DisclosureListingResult: disclosureListingResult,
            PsVisualizationResult: psVisualizationResult,
            Outcome: outcome.Outcome,
            OutcomeReasonCode: outcome.ReasonCode,
            ReplyLanguage: outcome.ReplyLanguage,
            LanguageGuardApplied: outcome.LanguageGuardApplied,
            SuggestedActions: suggestedActions,
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

    private static DialogueOutcome SemanticDialogueOutcome(CapabilityExecutionStatus status) => status switch
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

    private ConfidenceScoreResult? CalculateConfidenceScore(
        string correlationId,
        string? answerText,
        ScannerTableResult? scannerTable,
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

        // PreCalculatedMetric when PE_TTM or PS_TTM is present and non-missing in every row —
        // these are pre-persisted ratios and do not require live inference.
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

    private async Task TryCollectLookupFeedbackAsync(
        AiQueryRequest request,
        SymbolLookupTableResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            if (result.UnresolvedSymbols.Count == 0 &&
                !result.Rows.Any(r => r.Cells.Values.Any(c => c.FreshnessStatus == CellFreshnessStatus.Missing)))
            {
                return;
            }

            foreach (var unresolved in result.UnresolvedSymbols)
            {
                await feedbackCollector.CollectAsync(
                    new MissingAnswerFeedbackRequest(
                        ActorId: request.ActorId.ToString(),
                        QueryText: request.Message,
                        Classification: MissingAnswerFeedbackClassification.DataCoverageGap,
                        RequestedMetricCode: null,
                        AffectedDataCodeOrName: unresolved,
                        SymbolCountTotal: result.ExecutionFacts.TotalSymbolsEvaluated,
                        SymbolCountMatched: result.ExecutionFacts.MatchingSymbolCount,
                        SubmittedAt: now,
                        Context: $"SymbolLookup: symbol '{unresolved}' could not be resolved"),
                    cancellationToken);
            }
        }
        catch
        {
            // Collection must never disturb the lookup response.
        }
    }

    private static string BuildConversationTitle(string message)
    {
        const int maxLength = 80;
        var normalized = string.Join(' ', message.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
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

    private string BuildAssistantContent(
        DetectedIntent intent,
        ScannerQueryPlan? plan,
        ScannerTableResult? table,
        SymbolLookupTableResult? lookupTable,
        ExplainableAnswer? explainableAnswer,
        string? textAnswer,
        bool clarificationRequired,
        string? clarificationMessage,
        AnswerConsistencyContext consistencyContext,
        ComprehensiveAnalysisQueryResponse? comprehensiveAnalysisResult = null,
        FinancialStatementAnalysisResponse? financialStatementAnalysisResult = null,
        ProductRevenueMixResponse? productRevenueMixResult = null,
        MonthlyActivityTrendResponse? monthlyActivityTrendResult = null,
        FinancialStatementTableResult? financialStatementTableResult = null)
    {
        if (clarificationRequired && clarificationMessage is not null)
            return clarificationMessage;

        if (monthlyActivityTrendResult is not null)
            return BuildMonthlyActivityTrendContent(monthlyActivityTrendResult);

        if (financialStatementAnalysisResult?.RenderedAnswer is { Length: > 0 } rendered)
            return rendered;

        if (financialStatementTableResult?.RenderedAnswer is { Length: > 0 } renderedTable)
            return renderedTable;

        if (productRevenueMixResult is not null)
            return BuildProductRevenueMixContent(productRevenueMixResult);

        if (comprehensiveAnalysisResult is not null)
        {
            if (!comprehensiveAnalysisResult.HasResults)
                return "هیچ تحلیلی برای معیارهای درخواست‌شده در پایگاه داده یافت نشد.";

            var sb = new StringBuilder();
            foreach (var item in comprehensiveAnalysisResult.Items)
            {
                sb.AppendLine($"### {item.Title}");
                sb.AppendLine($"تاریخ: {item.PersianCreatedAt} | نویسنده: {item.AuthorName}");
                sb.AppendLine(item.PlainTextSummary);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        // Symbol-lookup prose is built deterministically from the structured table cell — never from
        // LLM free text — so the prose value always equals the table value.
        if (lookupTable is not null)
            return symbolLookupProseBuilder.Build(lookupTable);

        if (table is not null)
        {
            // Scanner prose must be a short deterministic count summary — never LLM symbol enumeration.
            // The structured table carries the full results; the prose is one sentence only.
            // Passing null to ValidateScanner causes it to return ScannerSafeSentence directly.
            return consistencyValidator
                .ValidateScanner(table, plan!, null, consistencyContext)
                .Answer;
        }

        if (plan is not null)
            return IsPersianLanguage(plan.Language)
                ? $"برنامه اسکن با {plan.Conditions.Count} شرط ایجاد شد."
                : $"Scanner plan created with {plan.Conditions.Count} condition(s).";

        return textAnswer ?? "I can help you screen stocks. Please describe your criteria.";
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
        foreach (var p in result.Products)
        {
            var dominant = p.IsDominantProduct ? "✓" : "";
            sb.AppendLine($"| {p.Rank} | {p.ProductName} | {p.SalesAmount:N0} | {p.RevenueSharePercentage:F1}٪ | {dominant} |");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildMonthlyActivityTrendContent(MonthlyActivityTrendResponse result)
    {
        var sb = new StringBuilder();
        var companyLabel = result.CompanyName is not null
            ? $"{result.CompanyName} ({result.CompanySymbol})"
            : result.CompanySymbol;

        sb.AppendLine($"### روند فروش ماهانه — {companyLabel}");
        sb.AppendLine($"آخرین دوره گزارش: {result.LatestReportYear}/{result.LatestReportMonth:D2} | واحد: {result.UnitLabelFa}");
        sb.AppendLine();

        // Latest month summary
        if (result.LatestMonthlySalesAmount.HasValue)
            sb.AppendLine($"**خلاصه آخرین ماه:** فروش {result.LatestMonthlySalesAmount.Value:N0} {result.UnitLabelFa}");

        // YoY comparison
        if (result.SameMonthPreviousYearSalesAmount.HasValue)
        {
            if (result.SalesAmountYoYGrowthPercent.HasValue)
            {
                var sign = result.SalesAmountYoYGrowthPercent.Value >= 0 ? "+" : "";
                sb.AppendLine($"**مقایسه با ماه مشابه سال قبل:** {result.SameMonthPreviousYearSalesAmount.Value:N0} {result.UnitLabelFa} ({sign}{result.SalesAmountYoYGrowthPercent.Value:F1}٪)");
            }
            else
            {
                sb.AppendLine($"**مقایسه با ماه مشابه سال قبل:** {result.SameMonthPreviousYearSalesAmount.Value:N0} {result.UnitLabelFa}");
            }
        }

        // 12-month average comparison
        if (result.Average12MonthSalesAmount.HasValue)
        {
            var vsAvgText = result.SalesVsAverage12MonthPercent.HasValue
                ? $" ({(result.SalesVsAverage12MonthPercent.Value >= 0 ? "+" : "")}{result.SalesVsAverage12MonthPercent.Value:F1}٪ نسبت به میانگین)"
                : "";
            sb.AppendLine($"**میانگین ۱۲ ماهه:** {result.Average12MonthSalesAmount.Value:N0} {result.UnitLabelFa}{vsAvgText}");
        }

        // Insights
        if (result.Insights.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**نکات تحلیلی:**");
            foreach (var insight in result.Insights)
                sb.AppendLine($"- {insight.TextFa}");
        }

        // Chart table
        if (result.ChartPoints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**داده نمودار ماهانه:**");
            sb.AppendLine();

            // Determine column headers from first point that has year values
            var firstPoint = result.ChartPoints.First(p => p.PreviousFiscalYear.HasValue || p.CurrentFiscalYear.HasValue);
            var prevYearLabel = firstPoint.PreviousFiscalYear.HasValue ? $"فروش {firstPoint.PreviousFiscalYear}" : "فروش سال قبل";
            var currYearLabel = firstPoint.CurrentFiscalYear.HasValue ? $"فروش {firstPoint.CurrentFiscalYear}" : "فروش سال جاری";

            sb.AppendLine($"| ماه | {prevYearLabel} | {currYearLabel} | میانگین ۱۲ ماهه |");
            sb.AppendLine("|-----|------------:|-------------:|----------------:|");

            foreach (var pt in result.ChartPoints)
            {
                var prevVal = pt.PreviousFiscalYearSalesAmount.HasValue
                    ? pt.PreviousFiscalYearSalesAmount.Value.ToString("N0")
                    : "—";
                var currVal = pt.IsCurrentYearReported && pt.CurrentFiscalYearSalesAmount.HasValue
                    ? pt.CurrentFiscalYearSalesAmount.Value.ToString("N0")
                    : "—";
                var avgVal = pt.Average12MonthSalesAmount.HasValue
                    ? pt.Average12MonthSalesAmount.Value.ToString("N0")
                    : "—";
                sb.AppendLine($"| {pt.FiscalMonthNameFa} | {prevVal} | {currVal} | {avgVal} |");
            }
        }

        // Missing data note
        if (result.MissingDataPoints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"*داده‌های ناقص: {result.MissingDataPoints.Count} دوره موجود نیست.*");
        }

        // Source note
        sb.AppendLine();
        sb.AppendLine($"*منبع: {ProviderSources.GetDisplayName(result.SourceProviderName)} | محاسبه: {ShamsiMonthCalculator.FormatJalaliDate(result.CalculatedAtUtc)}*");

        return sb.ToString().TrimEnd();
    }

    // Common Persian stop/question/verb words that are short but are never ticker symbols.
    private static readonly HashSet<string> PersianNonTickerWords =
    [
        "از", "به", "در", "با", "که", "را", "تا", "یا", "هم", "هر", "این", "آن", "اگر",
        "چه", "کی", "کو", "هم", "اما", "ولی", "پس", "نه", "بله", "خیر",
        "چیست", "چیه", "هست", "است", "بود", "شد", "کرد", "داد", "برای",
        "دارد", "دارم", "دارن", "دارند", "ندارد",
        "بده", "بگو", "بگیر", "بزن", "نشان", "نده",
        "می", "نمی", "هم", "فقط", "اول", "آخر", "کجا", "کدام",
        "مهم", "اصلی", "ترین", "بیشتر", "کمتر", "بالا", "پایین",
        "محصول", "فروش", "درآمد", "سهم", "ترکیب",
    ];

    // Extracts a company symbol from a product-revenue-mix query.
    // Tries Persian tickers (2-5 Persian letters, not a stop word) then uppercase ASCII tokens.
    private static string? ExtractProductRevenueMixSymbol(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        // Normalize Arabic chars so ك/ي variants match Persian tickers.
        var normalized = message.Replace('ك', 'ک').Replace('ي', 'ی').Replace('‌', ' ').Trim();

        // Collect all 2–5-char Persian-letter runs that are not common stop/question words.
        var candidateTokens = new List<string>();
        var start = -1;
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            var isPersian = c is >= '؀' and <= 'ۿ';
            if (isPersian)
            {
                if (start < 0) start = i;
            }
            else
            {
                if (start >= 0)
                {
                    var len = i - start;
                    var token = normalized.Substring(start, len);
                    if (len is >= 2 and <= 5 && !PersianNonTickerWords.Contains(token))
                        candidateTokens.Add(token);
                    start = -1;
                }
            }
        }
        if (start >= 0)
        {
            var len = normalized.Length - start;
            var token = normalized.Substring(start, len);
            if (len is >= 2 and <= 5 && !PersianNonTickerWords.Contains(token))
                candidateTokens.Add(token);
        }

        // Prefer the first candidate — tickers appear before question/verb words in Persian queries.
        if (candidateTokens.Count > 0)
            return candidateTokens[0];

        // Fall back to uppercase ASCII tokens (e.g. "MSFT").
        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length is >= 2 and <= 5 && token.All(char.IsUpper))
                return token;
        }

        return null;
    }

    private static bool ContainsPersianText(string text) =>
        text.Any(character =>
            character is >= '؀' and <= 'ۿ' or
            >= 'ݐ' and <= 'ݿ');

    private static bool IsPersianLanguage(string? language) =>
        language?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class NoOpBillingFacadeHook : IBillingFacadeHook
{
    public Task<BillingReservationHandle?> TryReserveAsync(
        BillingReservationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult<BillingReservationHandle?>(null);

    public Task<UsageAccountingResult?> FinalizeAsync(
        BillingReservationHandle handle,
        BillingFinalizationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult<UsageAccountingResult?>(null);

    public Task ReleaseAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

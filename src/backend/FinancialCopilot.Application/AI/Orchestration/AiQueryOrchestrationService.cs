using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Application.AI.Orchestration;

public sealed class AiQueryOrchestrationService(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IAiIntentDetector intentDetector,
    IScannerQueryParser scannerParser,
    IScannerExecutionService scannerExecutionService,
    IExplainableAnswerBuilder explainableAnswerBuilder,
    IScannerCache scannerCache,
    IBillingFacadeHook billingHook,
    IMemoryContextProvider memoryContextProvider,
    IMemoryAuditService memoryAuditService,
    TimeProvider timeProvider) : IAiQueryOrchestrationService
{
    public async Task<AiQueryResponse> ExecuteAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var conversationId = request.ConversationId ?? await conversationRepository.CreateAsync(
            request.TenantId,
            request.ActorId,
            now,
            cancellationToken);

        var userMessageId = await messageRepository.AppendAsync(
            conversationId,
            MessageRole.User,
            request.Message,
            scannerQueryPlanJson: null,
            now,
            cancellationToken);

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

        var billingReservation = await billingHook.TryReserveAsync(
            new BillingReservationRequest(
                request.CorrelationId,
                request.TenantId,
                request.ActorId,
                "AiQuery.Scanner",
                request.UserId,
                request.ApiClientId,
                request.ExternalUserId),
            cancellationToken);

        ScannerQueryPlan? scannerPlan = null;
        ScannerTableResult? scannerTable = null;
        ExplainableAnswer? explainableAnswer = null;
        string? textAnswer = null;
        bool clarificationRequired;
        string? clarificationMessage;
        var detectedIntent = DetectedIntent.Unknown;
        UsageAccountingResult? usage = null;
        var completionStatus = "Completed";
        var fromCache = false;

        try
        {
            var intentResult = await intentDetector.DetectAsync(
                new IntentDetectionInput(
                    enrichedMessage,
                    "en",
                    request.CorrelationId,
                    request.TenantId),
                cancellationToken);

            detectedIntent = intentResult.Intent;

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
                        DateOnly.FromDateTime(now.DateTime));
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
            else if (intentResult.Intent == DetectedIntent.Clarification)
            {
                clarificationRequired = true;
                clarificationMessage = "Your request needs clarification before I can screen stocks.";
                completionStatus = "ClarificationRequired";
            }
            else
            {
                clarificationRequired = false;
                clarificationMessage = null;
                textAnswer = "I can help you screen and filter stocks by financial metrics. Please describe your screening criteria.";
            }

            if (billingReservation is not null)
            {
                usage = await billingHook.FinalizeAsync(
                    billingReservation,
                    new BillingFinalizationRequest(completionStatus, fromCache),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
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

        var assistantContent = BuildAssistantContent(
            detectedIntent, scannerPlan, scannerTable, explainableAnswer, textAnswer, clarificationRequired, clarificationMessage);

        var assistantMessageId = await messageRepository.AppendAsync(
            conversationId,
            MessageRole.Assistant,
            assistantContent,
            planJson,
            timeProvider.GetUtcNow(),
            cancellationToken);

        await conversationRepository.TouchAsync(conversationId, timeProvider.GetUtcNow(), cancellationToken);

        return new AiQueryResponse(
            conversationId,
            userMessageId,
            assistantMessageId,
            detectedIntent,
            scannerPlan,
            scannerTable,
            explainableAnswer,
            textAnswer,
            clarificationRequired,
            clarificationMessage,
            usage,
            memoryContext.Disclosures.Count > 0 ? memoryContext.Disclosures : null);
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

    private static string BuildAssistantContent(
        DetectedIntent intent,
        ScannerQueryPlan? plan,
        ScannerTableResult? table,
        ExplainableAnswer? explainableAnswer,
        string? textAnswer,
        bool clarificationRequired,
        string? clarificationMessage)
    {
        if (clarificationRequired && clarificationMessage is not null)
            return clarificationMessage;

        if (explainableAnswer?.ExplanationText is not null)
            return explainableAnswer.ExplanationText;

        if (table is not null)
            return $"Scanner found {table.Rows.Count} matching symbol(s) for {plan!.Conditions.Count} condition(s).";

        if (plan is not null)
            return $"Scanner plan created with {plan.Conditions.Count} condition(s).";

        return textAnswer ?? "I can help you screen stocks. Please describe your criteria.";
    }
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

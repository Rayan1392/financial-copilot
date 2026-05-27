using System.Text.Json;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Application.AI.Orchestration;

public sealed class AiQueryOrchestrationService(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IAiIntentDetector intentDetector,
    IScannerQueryParser scannerParser,
    IScannerExecutionService scannerExecutionService,
    IExplainableAnswerBuilder explainableAnswerBuilder,
    IBillingFacadeHook billingHook,
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

        var billingReservation = await billingHook.TryReserveAsync(
            new BillingReservationRequest(
                request.CorrelationId,
                request.TenantId,
                request.ActorId,
                "AiQuery.Scanner"),
            cancellationToken);

        ScannerQueryPlan? scannerPlan = null;
        ScannerTableResult? scannerTable = null;
        ExplainableAnswer? explainableAnswer = null;
        string? textAnswer = null;
        bool clarificationRequired;
        string? clarificationMessage;
        var detectedIntent = DetectedIntent.Unknown;

        try
        {
            var intentResult = await intentDetector.DetectAsync(
                new IntentDetectionInput(
                    request.Message,
                    "en",
                    request.CorrelationId,
                    request.TenantId),
                cancellationToken);

            detectedIntent = intentResult.Intent;

            if (intentResult.Intent == DetectedIntent.Scanner)
            {
                var parseResult = await scannerParser.ParseAsync(
                    new ScannerParseRequest(
                        request.Message,
                        "en",
                        request.CorrelationId,
                        request.TenantId,
                        DateOnly.FromDateTime(now.DateTime)),
                    cancellationToken);

                scannerPlan = parseResult.Plan;
                clarificationRequired = parseResult.Plan.ClarificationRequired;
                clarificationMessage = parseResult.Plan.ClarificationMessage;

                if (!parseResult.Succeeded)
                {
                    clarificationRequired = true;
                    clarificationMessage = parseResult.FailureReason;
                }
                else if (!clarificationRequired)
                {
                    scannerTable = await scannerExecutionService.ExecuteAsync(
                        new ScannerExecutionRequest(
                            parseResult.Plan,
                            DateOnly.FromDateTime(now.DateTime)),
                        cancellationToken);

                    explainableAnswer = await explainableAnswerBuilder.BuildAsync(
                        new ExplainableAnswerRequest(
                            parseResult.Plan,
                            scannerTable,
                            request.TenantId,
                            request.CorrelationId),
                        cancellationToken);
                }
            }
            else if (intentResult.Intent == DetectedIntent.Clarification)
            {
                clarificationRequired = true;
                clarificationMessage = "Your request needs clarification before I can screen stocks.";
            }
            else
            {
                clarificationRequired = false;
                clarificationMessage = null;
                textAnswer = "I can help you screen and filter stocks by financial metrics. Please describe your screening criteria.";
            }

            if (billingReservation is not null)
            {
                await billingHook.FinalizeAsync(
                    billingReservation,
                    new BillingFinalizationRequest(Succeeded: true),
                    cancellationToken);
            }
        }
        catch
        {
            if (billingReservation is not null)
            {
                await billingHook.ReleaseAsync(billingReservation, cancellationToken);
            }
            throw;
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
            clarificationMessage);
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

    public Task FinalizeAsync(
        BillingReservationHandle handle,
        BillingFinalizationRequest request,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReleaseAsync(
        BillingReservationHandle handle,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

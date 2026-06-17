using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;

internal sealed class MessagePersistenceFunction(
    IConversationRepository repository,
    TimeProvider timeProvider,
    ISymbolLookupProseBuilder symbolLookupProseBuilder)
{
    internal async Task<PersistedConversationExchange> PersistAsync(
        Guid conversationId,
        AiQueryRequest request,
        DetectedIntent intent,
        bool clarificationRequired,
        string? clarificationMessage,
        string? textAnswer,
        ScannerQueryPlan? scannerPlan,
        ScannerTableResult? scannerTable,
        SymbolLookupTableResult? symbolLookupTable,
        ExplainableAnswer? explainableAnswer,
        ConfidenceScoreResult? confidenceScore,
        UsageAccountingResult? usage,
        AuthorizedMemoryContext memoryContext,
        string? agentResponseText,
        bool createConversation,
        CancellationToken cancellationToken,
        ComprehensiveAnalysisQueryResponse? comprehensiveAnalysisResult = null)
    {
        var planJson = scannerPlan is not null ? JsonSerializer.Serialize(scannerPlan) : null;
        var assistantContent = agentResponseText is { Length: > 0 }
            ? agentResponseText
            : BuildAssistantContent(
                intent, scannerPlan, scannerTable, symbolLookupTable,
                explainableAnswer, textAnswer, clarificationRequired, clarificationMessage,
                comprehensiveAnalysisResult);

        var disclosures = memoryContext.Disclosures.Count > 0 ? memoryContext.Disclosures : null;

        return await repository.PersistExchangeAsync(
            new ConversationExchange(
                conversationId,
                request.TenantId,
                request.ActorId,
                timeProvider.GetUtcNow(),
                BuildConversationTitle(request.Message),
                request.Message,
                assistantContent,
                planJson,
                new AssistantMessagePayload(
                    Version: 2,
                    intent,
                    clarificationRequired,
                    clarificationMessage,
                    textAnswer,
                    scannerPlan,
                    scannerTable,
                    symbolLookupTable,
                    explainableAnswer,
                    confidenceScore,
                    usage,
                    disclosures,
                    ComprehensiveAnalysisResult: comprehensiveAnalysisResult)),
            createConversation,
            cancellationToken);
    }

    private static string BuildConversationTitle(string message)
    {
        const int maxLength = 80;
        var normalized = string.Join(' ', message.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
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
        ComprehensiveAnalysisQueryResponse? comprehensiveAnalysisResult = null)
    {
        if (clarificationRequired && clarificationMessage is not null)
            return clarificationMessage;

        if (lookupTable is not null)
            return symbolLookupProseBuilder.Build(lookupTable);

        if (explainableAnswer?.ExplanationText is not null)
            return explainableAnswer.ExplanationText;

        if (table is not null)
            return plan?.Language?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) == true
                ? $"اسکنر برای {plan!.Conditions.Count} شرط، {table.Rows.Count} نماد منطبق پیدا کرد."
                : $"Scanner found {table.Rows.Count} matching symbol(s) for {plan!.Conditions.Count} condition(s).";

        if (plan is not null)
            return plan.Language?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) == true
                ? $"برنامه اسکن با {plan.Conditions.Count} شرط ایجاد شد."
                : $"Scanner plan created with {plan.Conditions.Count} condition(s).";

        if (comprehensiveAnalysisResult is not null)
            return comprehensiveAnalysisResult.HasResults
                ? $"{comprehensiveAnalysisResult.Items.Count} تحلیل جامع یافت شد."
                : "تحلیل جامعی برای معیارهای درخواستی یافت نشد.";

        return textAnswer ?? "I can help you screen stocks. Please describe your criteria.";
    }
}

using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public enum SchemaNavigationLlmCallType
{
    RequirementsExtraction,
    InitialNavigation,
    HierarchyExpansion,
    StructuredOutputRetry
}

public sealed record SchemaNavigationLlmCallStarted(
    string CallId,
    string CorrelationId,
    SchemaNavigationLlmCallType CallType,
    string SchemaName,
    int MaximumOutputTokens,
    DateTimeOffset StartedAtUtc);

public interface ISchemaNavigationExecutionObserver
{
    void CallStarted(SchemaNavigationLlmCallStarted call);

    void CallCompleted(string callId, AiExecutionUsageFacts usage, string? providerResponseId);

    void CallFailed(string callId, string errorCode, string? providerCompletionReason);
}

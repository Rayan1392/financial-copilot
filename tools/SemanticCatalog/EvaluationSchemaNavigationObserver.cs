using System.Collections.Concurrent;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

internal sealed record EvaluationSchemaNavigationCall(
    string CallId,
    string CorrelationId,
    string CallType,
    string SchemaName,
    int MaximumOutputTokens,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Status,
    string? Provider,
    string? Model,
    int? InputTokens,
    int? OutputTokens,
    long? LatencyMs,
    string? ProviderResponseId,
    string? ErrorCode,
    string? ProviderCompletionReason,
    string ProviderCompletionReasonAvailability);

internal sealed class EvaluationSchemaNavigationObserver : ISchemaNavigationExecutionObserver
{
    private readonly ConcurrentDictionary<string, EvaluationSchemaNavigationCall> calls = new(StringComparer.Ordinal);

    public void CallStarted(SchemaNavigationLlmCallStarted call) => calls[call.CallId] = new(
        call.CallId,
        call.CorrelationId,
        call.CallType.ToString(),
        call.SchemaName,
        call.MaximumOutputTokens,
        call.StartedAtUtc,
        null,
        "Started",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        "provider_response_not_received");

    public void CallCompleted(string callId, AiExecutionUsageFacts usage, string? providerResponseId) =>
        calls.AddOrUpdate(callId,
            _ => throw new InvalidOperationException($"Unknown schema-navigation diagnostic call {callId}."),
            (_, current) => current with
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Status = "Completed",
                Provider = usage.ProviderKey,
                Model = usage.ModelKey,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                LatencyMs = (long)usage.Duration.TotalMilliseconds,
                ProviderResponseId = providerResponseId,
                ErrorCode = usage.FailureCode,
                ProviderCompletionReasonAvailability = "not_exposed_on_success"
            });

    public void CallFailed(string callId, string errorCode, string? providerCompletionReason) =>
        calls.AddOrUpdate(callId,
            _ => throw new InvalidOperationException($"Unknown schema-navigation diagnostic call {callId}."),
            (_, current) => current with
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Status = "Failed",
                ErrorCode = errorCode,
                ProviderCompletionReason = providerCompletionReason,
                ProviderCompletionReasonAvailability = providerCompletionReason is null
                    ? "provider_did_not_return_finish_reason"
                    : "provider_reported"
            });

    public IReadOnlyList<EvaluationSchemaNavigationCall> GetSnapshot(string correlationId) => calls.Values
        .Where(call => string.Equals(call.CorrelationId, correlationId, StringComparison.Ordinal))
        .OrderBy(call => call.StartedAtUtc)
        .ThenBy(call => call.CallId, StringComparer.Ordinal)
        .ToArray();
}

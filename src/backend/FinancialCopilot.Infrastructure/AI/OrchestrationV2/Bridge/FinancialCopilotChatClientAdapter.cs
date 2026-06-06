using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using Microsoft.Extensions.AI;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Bridge;

// Bridges IAiModelClient (provider-neutral) to IChatClient (MAF/Extensions.AI contract).
// Each instance is scoped to a single request: correlation ID, tenant, and workload are
// fixed at construction time so the LLM context is never leaked across requests.
//
// Responses API continuation model:
//   Turn 1 — full history in input, no previous_response_id.
//   Turn N — only new tool outputs in input, previous_response_id = last response.id.
// This avoids stateless function_call history replay, which the Responses API does not
// support reliably without the exact item IDs from the prior response.
internal sealed class FinancialCopilotChatClientAdapter(
    IAiModelClient modelClient,
    IAiExecutionUsageAccumulator usageAccumulator,
    string correlationId,
    Guid tenantId,
    AiWorkloadKind workload) : IChatClient
{
    // Mutable per-instance state — safe because one adapter is created per RunAsync call.
    private string? _lastResponseId;
    private IReadOnlySet<string>? _pendingToolCallIds;

    public ChatClientMetadata Metadata { get; } =
        new(modelClient.Descriptor.ProviderKey, providerUri: null, modelClient.Descriptor.ModelKey);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();

        var tools = options?.Tools?
            .OfType<AIFunction>()
            .Select(f => new AiToolDefinition(f.Name, f.Description ?? string.Empty, GetSchemaJson(f)))
            .ToList();

        // Detect continuation: tool-result messages are present from a prior model turn.
        var hasToolResults = messageList.Any(m => m.Role == ChatRole.Tool);

        if (hasToolResults)
        {
            GuardToolOutputsHaveMatchingCalls(messageList);

            // Continuation turn: only send the new tool outputs.
            // The Responses API already has the conversation context via previous_response_id.
            var newMessages = ExtractNewToolOutputMessages(messageList);

            var continuationRequest = new AiModelRequest(
                correlationId, tenantId, workload,
                newMessages.Select(MapMessage).ToList(),
                StructuredOutput: null,
                Tools: tools is { Count: > 0 } ? tools : null,
                PreviousResponseId: _lastResponseId);

            var continuationResult = await modelClient.CompleteAsync(continuationRequest, cancellationToken);
            usageAccumulator.Record(continuationResult.Usage);
            _lastResponseId = continuationResult.ResponseId;
            _pendingToolCallIds = continuationResult.ToolCalls.Count > 0
                ? continuationResult.ToolCalls.Select(tc => tc.Id).ToHashSet()
                : null;
            return BuildChatResponse(continuationResult, correlationId);
        }

        // First turn (or non-tool response): send full message history.
        _lastResponseId = null;
        _pendingToolCallIds = null;

        var request = new AiModelRequest(
            correlationId, tenantId, workload,
            messageList.Select(MapMessage).ToList(),
            StructuredOutput: null,
            Tools: tools is { Count: > 0 } ? tools : null);

        var result = await modelClient.CompleteAsync(request, cancellationToken);
        usageAccumulator.Record(result.Usage);
        _lastResponseId = result.ResponseId;
        _pendingToolCallIds = result.ToolCalls.Count > 0
            ? result.ToolCalls.Select(tc => tc.Id).ToHashSet()
            : null;
        return BuildChatResponse(result, correlationId);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is not used in V2 orchestration.");

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }

    // Throws before we would send an orphan function_call_output to the provider.
    private void GuardToolOutputsHaveMatchingCalls(List<ChatMessage> messages)
    {
        if (_lastResponseId is null)
        {
            var orphanCallIds = messages
                .Where(m => m.Role == ChatRole.Tool)
                .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
                .Select(r => r.CallId)
                .ToList();

            throw new InvalidOperationException(
                "Cannot submit function_call_output without a previous_response_id. " +
                "The Responses API requires every tool output to be linked to a prior model response. " +
                $"Orphaned call_id(s): {string.Join(", ", orphanCallIds)}");
        }

        if (_pendingToolCallIds is null) return;

        var submittedCallIds = messages
            .Where(m => m.Role == ChatRole.Tool)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Select(r => r.CallId)
            .ToHashSet();

        var unmatched = submittedCallIds.Except(_pendingToolCallIds).ToList();
        if (unmatched.Count > 0)
        {
            throw new InvalidOperationException(
                $"function_call_output call_id(s) have no matching pending function_call: " +
                $"{string.Join(", ", unmatched)}. " +
                $"Pending call_id(s) from last response: {string.Join(", ", _pendingToolCallIds)}");
        }
    }

    // Returns only messages after the last assistant message that contained function calls.
    // These are the tool-result messages for the most recent model response.
    private static List<ChatMessage> ExtractNewToolOutputMessages(List<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.Assistant &&
                messages[i].Contents.OfType<FunctionCallContent>().Any())
            {
                return messages.Skip(i + 1).ToList();
            }
        }
        return messages.Where(m => m.Role == ChatRole.Tool).ToList();
    }

    private static ChatResponse BuildChatResponse(AiModelResult result, string conversationId)
    {
        ChatResponse response;
        if (result.ToolCalls.Count > 0)
        {
            var contents = result.ToolCalls
                .Select(tc =>
                {
                    var fcc = new FunctionCallContent(tc.Id, tc.Name, DeserializeArgs(tc.ArgumentsJson));
                    if (tc.ItemId is not null)
                    {
                        fcc.AdditionalProperties = new AdditionalPropertiesDictionary { ["item_id"] = tc.ItemId };
                    }
                    return (AIContent)fcc;
                })
                .ToList();
            response = new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)]);
        }
        else
        {
            response = new ChatResponse([new ChatMessage(ChatRole.Assistant, result.Text ?? string.Empty)]);
        }
        // MAF requires a non-null ConversationId when using service-managed session history.
        // We manage history client-side, so we echo back the correlation ID as the stable conversation key.
        response.ConversationId = conversationId;
        return response;
    }

    // Maps an Extensions.AI ChatMessage to our provider-neutral AiConversationMessage.
    private static AiConversationMessage MapMessage(ChatMessage m)
    {
        var role = MapRole(m.Role);

        if (role == AiMessageRole.Tool)
        {
            var toolResult = m.Contents.OfType<FunctionResultContent>().FirstOrDefault();
            if (toolResult is not null)
            {
                var content = toolResult.Result switch
                {
                    null => string.Empty,
                    string s => s,
                    _ => JsonSerializer.Serialize(toolResult.Result)
                };
                return new AiConversationMessage(AiMessageRole.Tool, content, toolResult.CallId);
            }
        }

        var functionCalls = m.Contents.OfType<FunctionCallContent>().ToList();
        if (functionCalls.Count > 0)
        {
            var serialized = JsonSerializer.Serialize(functionCalls.Select(fc => new
            {
                id = fc.CallId,
                item_id = fc.AdditionalProperties?.TryGetValue("item_id", out var iid) == true ? iid as string : null,
                function = new { name = fc.Name, arguments = SerializeArgs(fc.Arguments) }
            }));
            return new AiConversationMessage(role, serialized);
        }

        return new AiConversationMessage(role, m.Text ?? string.Empty);
    }

    private static AiMessageRole MapRole(ChatRole role)
    {
        if (role == ChatRole.System) return AiMessageRole.System;
        if (role == ChatRole.Assistant) return AiMessageRole.Assistant;
        if (role == ChatRole.Tool) return AiMessageRole.Tool;
        return AiMessageRole.User;
    }

    private static string GetSchemaJson(AIFunction f)
    {
        try
        {
            var schema = f.JsonSchema;
            return schema.ValueKind != System.Text.Json.JsonValueKind.Undefined
                ? schema.GetRawText()
                : "{}";
        }
        catch
        {
            return "{}";
        }
    }

    private static IDictionary<string, object?> DeserializeArgs(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private static string SerializeArgs(IDictionary<string, object?>? args)
    {
        try { return args is null ? "{}" : JsonSerializer.Serialize(args); }
        catch { return "{}"; }
    }
}

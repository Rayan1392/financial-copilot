using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using Microsoft.Extensions.AI;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Bridge;

// Bridges IAiModelClient (provider-neutral) to IChatClient (MAF/Extensions.AI contract).
// Each instance is scoped to a single request: correlation ID, tenant, and workload are
// fixed at construction time so the LLM context is never leaked across requests.
internal sealed class FinancialCopilotChatClientAdapter(
    IAiModelClient modelClient,
    string correlationId,
    Guid tenantId,
    AiWorkloadKind workload) : IChatClient
{
    public ChatClientMetadata Metadata { get; } =
        new(modelClient.Descriptor.ProviderKey, providerUri: null, modelClient.Descriptor.ModelKey);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var aiMessages = messages.Select(MapMessage).ToList();

        var tools = options?.Tools?
            .OfType<AIFunction>()
            .Select(f => new AiToolDefinition(f.Name, f.Description ?? string.Empty, GetSchemaJson(f)))
            .ToList();

        var request = new AiModelRequest(
            correlationId,
            tenantId,
            workload,
            aiMessages,
            StructuredOutput: null,
            Tools: tools is { Count: > 0 } ? tools : null);

        var result = await modelClient.CompleteAsync(request, cancellationToken);

        return BuildChatResponse(result);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is not used in V2 orchestration.");

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }

    private static ChatResponse BuildChatResponse(AiModelResult result)
    {
        if (result.ToolCalls.Count > 0)
        {
            var contents = result.ToolCalls
                .Select(tc => (AIContent)new FunctionCallContent(tc.Id, tc.Name, DeserializeArgs(tc.ArgumentsJson)))
                .ToList();
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)]);
        }

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, result.Text ?? string.Empty)]);
    }

    // Maps an Extensions.AI ChatMessage to our provider-neutral AiConversationMessage.
    // Tool result messages carry their call ID so the model can correlate results back to
    // the original tool-call request. Assistant messages with pending tool calls are
    // serialized to JSON — this is sufficient for the DeterministicFakeAiModelClient used
    // in tests and will need provider-specific handling in production transports.
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

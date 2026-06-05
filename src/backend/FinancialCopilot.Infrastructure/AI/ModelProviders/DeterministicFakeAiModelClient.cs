using System.Runtime.CompilerServices;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class DeterministicFakeAiModelClient(
    AiModelProviderDescriptor descriptor,
    TimeProvider timeProvider,
    Func<AiModelRequest, AiModelResult>? resultFactory = null) : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = descriptor;

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        if ((Descriptor.Capabilities & AiModelCapability.ChatCompletion) == 0)
        {
            throw NotSupported(AiModelCapability.ChatCompletion);
        }

        if (resultFactory is not null)
            return Task.FromResult(resultFactory.Invoke(request));

        // V2 tool-call simulation: if tools are present and no tool result is in history yet,
        // return a tool call to the first tool so FunctionInvokingChatClient can execute it.
        // On the second turn (Tool-role message in history), return final text.
        var hasTools = request.Tools is { Count: > 0 };
        var hasToolResult = request.Messages.Any(m => m.Role == AiMessageRole.Tool);

        if (hasTools && !hasToolResult)
        {
            var firstTool = request.Tools!.First();
            var lastUserContent = request.Messages
                .LastOrDefault(m => m.Role == AiMessageRole.User)?.Content ?? "query";
            return Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: null,
                ToolCalls: [new AiToolCall(
                    "fake-call-id-1",
                    firstTool.Name,
                    $"{{\"query\":\"{EscapeJsonString(lastUserContent)}\"}}") ],
                Usage: MakeUsage(request, usedTools: true)));
        }

        var responseText = hasToolResult ? "Fake V2 agent response." : "Deterministic fake AI response.";
        return Task.FromResult(new AiModelResult(
            Text: responseText,
            StructuredJson: request.StructuredOutput is null
                ? null
                : CreateStructuredOutput(request.StructuredOutput),
            ToolCalls: [],
            Usage: MakeUsage(request, usedTools: hasTools)));
    }

    private AiExecutionUsageFacts MakeUsage(AiModelRequest request, bool usedTools = false) =>
        new(request.CorrelationId,
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            AiExecutionStatus.Completed,
            TimeSpan.Zero,
            AttemptNumber: 0,
            InputTokens: request.Messages.Sum(m => m.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length),
            OutputTokens: 4,
            UsedTools: usedTools);

    private static string EscapeJsonString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    public async IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if ((Descriptor.Capabilities & AiModelCapability.Streaming) == 0)
        {
            throw NotSupported(AiModelCapability.Streaming);
        }

        await Task.Yield();
        yield return new AiStreamingChunk(
            "Deterministic fake AI response.",
            null,
            IsComplete: true,
            new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                OutputTokens: 4));
    }

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        if ((Descriptor.Capabilities & AiModelCapability.Embeddings) == 0)
        {
            throw NotSupported(AiModelCapability.Embeddings);
        }

        var vectors = request.Inputs
            .Select(input => (IReadOnlyList<float>)[input.Length, input.Length % 7, 1])
            .ToArray();

        return Task.FromResult(new AiEmbeddingResult(
            vectors,
            new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                InputTokens: request.Inputs.Sum(input => input.Length),
                EmbeddingOperation: true)));
    }

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            Available: true,
            timeProvider.GetUtcNow(),
            "Deterministic fake model provider is available."));

    private static string CreateStructuredOutput(AiStructuredOutputContract contract) =>
        "{" + string.Join(",", contract.RequiredRootProperties.Select(property => $"\"{property}\":null")) + "}";

    private AiModelProviderException NotSupported(AiModelCapability capability) =>
        new(
            AiExecutionStatus.CapabilityUnavailable,
            "capability_unavailable",
            $"Fake provider '{Descriptor.ProviderKey}' does not support '{capability}'.");
}

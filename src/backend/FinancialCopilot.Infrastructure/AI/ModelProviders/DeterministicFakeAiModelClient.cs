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

        var result = resultFactory?.Invoke(request) ?? new AiModelResult(
            Text: "Deterministic fake AI response.",
            StructuredJson: request.StructuredOutput is null
                ? null
                : CreateStructuredOutput(request.StructuredOutput),
            ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                InputTokens: request.Messages.Sum(message => message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length),
                OutputTokens: 4,
                UsedTools: request.Tools?.Count > 0));

        return Task.FromResult(result);
    }

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

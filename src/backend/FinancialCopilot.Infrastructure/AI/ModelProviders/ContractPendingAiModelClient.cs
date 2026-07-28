using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class ContractPendingAiModelClient(AiModelProviderDescriptor descriptor) : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = descriptor;

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken) =>
        Task.FromException<AiModelResult>(NotImplemented());

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw NotImplemented();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<AiEmbeddingResult>(NotImplemented());

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            Available: false,
            DateTimeOffset.UtcNow,
            "Provider contract is pending official API and authentication documentation."));

    private static AiModelProviderException NotImplemented() =>
        new(
            AiExecutionStatus.RuntimeUnavailable,
            "provider_contract_pending",
            "Provider execution is unavailable until its official integration contract is implemented.");
}

using System.Runtime.CompilerServices;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public interface IHostedAiModelTransport
{
    Task<HostedAiCompletionResponse> CompleteAsync(
        string modelKey,
        AiModelRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        string modelKey,
        AiModelRequest request,
        CancellationToken cancellationToken);

    Task<HostedAiEmbeddingResponse> CreateEmbeddingsAsync(
        string modelKey,
        AiEmbeddingRequest request,
        CancellationToken cancellationToken);

    Task<bool> CheckAvailabilityAsync(string modelKey, CancellationToken cancellationToken);
}

public sealed record HostedAiCompletionResponse(
    string? Text,
    string? StructuredJson,
    IReadOnlyCollection<AiToolCall> ToolCalls,
    int? InputTokens,
    int? OutputTokens,
    bool CacheHit = false,
    decimal? ProviderReportedCost = null,
    string? ProviderReportedCurrency = null,
    string? ResponseId = null);

public sealed record HostedAiEmbeddingResponse(
    IReadOnlyCollection<IReadOnlyList<float>> Vectors,
    int? InputTokens,
    decimal? ProviderReportedCost = null,
    string? ProviderReportedCurrency = null);

public sealed class ConfiguredHostedAiModelClient(
    AiModelProviderDescriptor descriptor,
    IHostedAiModelTransport transport,
    TimeProvider timeProvider) : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = descriptor;

    public async Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        Require(AiModelCapability.ChatCompletion);
        var startedAt = timeProvider.GetUtcNow();

        try
        {
            var response = await transport.CompleteAsync(Descriptor.ModelKey, request, cancellationToken);
            return new AiModelResult(
                response.Text,
                response.StructuredJson,
                response.ToolCalls,
                new AiExecutionUsageFacts(
                    request.CorrelationId,
                    Descriptor.ProviderKey,
                    Descriptor.ModelKey,
                    AiExecutionStatus.Completed,
                    timeProvider.GetUtcNow() - startedAt,
                    AttemptNumber: 0,
                    response.InputTokens,
                    response.OutputTokens,
                    response.CacheHit,
                    response.ToolCalls.Count > 0,
                    ProviderReportedCost: response.ProviderReportedCost,
                    ProviderReportedCurrency: response.ProviderReportedCurrency),
                ResponseId: response.ResponseId);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.TimedOut,
                "hosted_provider_timeout",
                "Hosted AI provider request timed out.",
                exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiModelProviderException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.Failed,
                "hosted_provider_failed",
                "Hosted AI provider request failed.",
                exception);
        }
    }

    public async IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Require(AiModelCapability.Streaming);

        await foreach (var chunk in transport.StreamAsync(Descriptor.ModelKey, request, cancellationToken))
        {
            yield return chunk;
        }
    }

    public async Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        Require(AiModelCapability.Embeddings);
        var startedAt = timeProvider.GetUtcNow();
        var response = await transport.CreateEmbeddingsAsync(Descriptor.ModelKey, request, cancellationToken);
        return new AiEmbeddingResult(
            response.Vectors,
            new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                timeProvider.GetUtcNow() - startedAt,
                AttemptNumber: 0,
                response.InputTokens,
                EmbeddingOperation: true,
                ProviderReportedCost: response.ProviderReportedCost,
                ProviderReportedCurrency: response.ProviderReportedCurrency));
    }

    public async Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        new(
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            await transport.CheckAvailabilityAsync(Descriptor.ModelKey, cancellationToken),
            timeProvider.GetUtcNow());

    private void Require(AiModelCapability capability)
    {
        if ((Descriptor.Capabilities & capability) != capability)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.CapabilityUnavailable,
                "capability_unavailable",
                $"Hosted provider '{Descriptor.ProviderKey}' does not expose '{capability}'.");
        }
    }
}

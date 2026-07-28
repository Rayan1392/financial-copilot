using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class OllamaAiModelClient(
    HttpClient httpClient,
    AiModelProviderDescriptor descriptor,
    TimeProvider timeProvider) : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = descriptor;

    public async Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        Require(AiModelCapability.ChatCompletion);
        var startedAt = timeProvider.GetUtcNow();
        var payload = new
        {
            model = Descriptor.ModelKey,
            messages = request.Messages.Select(message => new
            {
                role = message.Role.ToString().ToLowerInvariant(),
                content = message.Content
            }),
            tools = request.Tools?.Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = JsonDocument.Parse(tool.ParametersJsonSchema).RootElement
                }
            }),
            format = request.StructuredOutput is null ? null : "json",
            stream = false
        };

        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsJsonAsync("api/chat", payload, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw RuntimeFailure("Ollama chat runtime is unavailable.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw RuntimeFailure($"Ollama chat returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken) ??
                throw RuntimeFailure("Ollama chat returned an empty response.");
            var text = body.Message?.Content;

            return new AiModelResult(
                text,
                request.StructuredOutput is null ? null : text,
                MapToolCalls(body.Message?.ToolCalls),
                new AiExecutionUsageFacts(
                    request.CorrelationId,
                    Descriptor.ProviderKey,
                    Descriptor.ModelKey,
                    AiExecutionStatus.Completed,
                    timeProvider.GetUtcNow() - startedAt,
                    AttemptNumber: 0,
                    InputTokens: body.PromptEvalCount,
                    OutputTokens: body.EvalCount,
                    UsedTools: body.Message?.ToolCalls?.Length > 0));
        }
    }

    public async IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Require(AiModelCapability.Streaming);
        var result = await CompleteAsync(request with { Stream = false }, cancellationToken);
        yield return new AiStreamingChunk(result.Text, null, IsComplete: true, result.Usage);
    }

    public async Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        Require(AiModelCapability.Embeddings);
        var startedAt = timeProvider.GetUtcNow();
        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsJsonAsync(
                "api/embed",
                new { model = Descriptor.ModelKey, input = request.Inputs },
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw RuntimeFailure("Ollama embedding runtime is unavailable.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw RuntimeFailure($"Ollama embedding returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
                cancellationToken: cancellationToken) ??
                throw RuntimeFailure("Ollama embedding returned an empty response.");

            return new AiEmbeddingResult(
                body.Embeddings ?? [],
                new AiExecutionUsageFacts(
                    request.CorrelationId,
                    Descriptor.ProviderKey,
                    Descriptor.ModelKey,
                    AiExecutionStatus.Completed,
                    timeProvider.GetUtcNow() - startedAt,
                    AttemptNumber: 0,
                    InputTokens: body.PromptEvalCount,
                    EmbeddingOperation: true));
        }
    }

    public async Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/tags", cancellationToken);
            var body = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken: cancellationToken)
                : null;
            var modelAvailable = body?.Models?.Any(model =>
                string.Equals(model.Model, Descriptor.ModelKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.Name, Descriptor.ModelKey, StringComparison.OrdinalIgnoreCase)) == true;
            return new AiProviderHealthResult(
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                response.IsSuccessStatusCode && modelAvailable,
                timeProvider.GetUtcNow(),
                !response.IsSuccessStatusCode
                    ? $"HTTP {(int)response.StatusCode}"
                    : modelAvailable ? null : "Configured model is not available in the local runtime.");
        }
        catch (HttpRequestException exception)
        {
            return new AiProviderHealthResult(
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                Available: false,
                timeProvider.GetUtcNow(),
                exception.Message);
        }
    }

    private void Require(AiModelCapability capability)
    {
        if ((Descriptor.Capabilities & capability) != capability)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.CapabilityUnavailable,
                "capability_unavailable",
                $"Ollama provider '{Descriptor.ProviderKey}' does not expose '{capability}'.");
        }
    }

    private static IReadOnlyCollection<AiToolCall> MapToolCalls(OllamaToolCall[]? toolCalls) =>
        toolCalls?.Select((toolCall, index) => new AiToolCall(
            $"ollama-tool-{index + 1}",
            toolCall.Function.Name,
            toolCall.Function.Arguments.GetRawText())).ToArray() ?? [];

    private static AiModelProviderException RuntimeFailure(string message, Exception? exception = null) =>
        new(AiExecutionStatus.RuntimeUnavailable, "local_runtime_unavailable", message, exception);

    private sealed record OllamaChatResponse(
        OllamaMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);

    private sealed record OllamaMessage(
        string? Content,
        [property: JsonPropertyName("tool_calls")] OllamaToolCall[]? ToolCalls);

    private sealed record OllamaToolCall(OllamaFunction Function);

    private sealed record OllamaFunction(string Name, JsonElement Arguments);

    private sealed record OllamaEmbeddingResponse(
        IReadOnlyList<float>[]? Embeddings,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount);

    private sealed record OllamaTagsResponse(OllamaTagModel[]? Models);

    private sealed record OllamaTagModel(string? Name, string? Model);
}

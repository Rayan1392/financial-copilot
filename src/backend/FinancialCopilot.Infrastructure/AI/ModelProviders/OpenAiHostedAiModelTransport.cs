using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class OpenAiHostedAiModelTransport(HttpClient httpClient) : IHostedAiModelTransport
{
    private const int MaxRateLimitAttempts = 3;

    public async Task<HostedAiCompletionResponse> CompleteAsync(
        string modelKey,
        AiModelRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCredential();
        var payload = new
        {
            model = modelKey,
            input = request.Messages.Select(message => new
            {
                role = message.Role.ToString().ToLowerInvariant(),
                content = message.Content,
            }),
            tools = request.Tools?.Select(tool => new
            {
                type = "function",
                name = tool.Name,
                description = tool.Description,
                parameters = JsonDocument.Parse(tool.ParametersJsonSchema).RootElement.Clone()
            }),
            text = request.StructuredOutput is null
                ? null
                : new
                {
                    format = new
                    {
                        type = "json_object"
                    }
                }
        };

        for (var attempt = 1; attempt <= MaxRateLimitAttempts; attempt++)
        {
            using var response = await httpClient.PostAsJsonAsync("responses", payload, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<OpenAiResponse>(
                    cancellationToken: cancellationToken) ??
                    throw Failure("OpenAI response API returned an empty response.");

                return new HostedAiCompletionResponse(
                    GetOutputText(body.Output),
                    request.StructuredOutput is null ? null : GetOutputText(body.Output),
                    MapToolCalls(body.Output),
                    body.Usage?.InputTokens,
                    body.Usage?.OutputTokens);
            }

            var failure = await ReadFailureAsync(response, cancellationToken);
            if (failure.Code != "hosted_provider_rate_limited" || attempt == MaxRateLimitAttempts)
            {
                throw failure;
            }

            await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
        }

        throw Failure("OpenAI response API request failed after bounded retries.");
    }

    public async IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        string modelKey,
        AiModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await CompleteAsync(modelKey, request with { Stream = false }, cancellationToken);
        yield return new AiStreamingChunk(
            response.Text,
            null,
            IsComplete: true,
            new AiExecutionUsageFacts(
                request.CorrelationId,
                "OpenAI",
                modelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                response.InputTokens,
                response.OutputTokens,
                UsedTools: response.ToolCalls.Count > 0));
    }

    public Task<HostedAiEmbeddingResponse> CreateEmbeddingsAsync(
        string modelKey,
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new AiModelProviderException(
            AiExecutionStatus.CapabilityUnavailable,
            "capability_unavailable",
            "The configured OpenAI response model does not expose embeddings.");

    public async Task<bool> CheckAvailabilityAsync(string modelKey, CancellationToken cancellationToken)
    {
        if (httpClient.DefaultRequestHeaders.Authorization is null)
        {
            return false;
        }

        try
        {
            using var response = await httpClient.GetAsync(
            $"models/{Uri.EscapeDataString(modelKey)}",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private void EnsureCredential()
    {
        if (httpClient.DefaultRequestHeaders.Authorization is null)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.RuntimeUnavailable,
                "hosted_provider_credentials_missing",
                "The OpenAI API credential secret is not configured.");
        }
    }

    private static string? GetOutputText(OpenAiOutputItem[]? output) =>
        output?
            .Where(item => string.Equals(item.Type, "message", StringComparison.Ordinal))
            .SelectMany(item => item.Content ?? [])
            .FirstOrDefault(item => string.Equals(item.Type, "output_text", StringComparison.Ordinal))
            ?.Text;

    private static IReadOnlyCollection<AiToolCall> MapToolCalls(OpenAiOutputItem[]? output) =>
        output?
            .Where(item => string.Equals(item.Type, "function_call", StringComparison.Ordinal))
            .Select(item => new AiToolCall(
                item.CallId ?? item.Id,
                item.Name ?? string.Empty,
                item.Arguments ?? "{}"))
            .ToArray() ?? [];

    private static AiModelProviderException Failure(string message) =>
        new(AiExecutionStatus.RuntimeUnavailable, "hosted_provider_unavailable", message);

    private static async Task<AiModelProviderException> ReadFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        OpenAiErrorResponse? body = null;

        try
        {
            body = await response.Content.ReadFromJsonAsync<OpenAiErrorResponse>(
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            // Preserve an actionable HTTP status when the upstream response is not JSON.
        }

        var upstreamCode = body?.Error?.Code ?? body?.Error?.Type;
        var code = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests when string.Equals(
                upstreamCode,
                "insufficient_quota",
                StringComparison.OrdinalIgnoreCase) => "hosted_provider_quota_exceeded",
            HttpStatusCode.TooManyRequests => "hosted_provider_rate_limited",
            HttpStatusCode.Unauthorized => "hosted_provider_authentication_failed",
            _ => "hosted_provider_unavailable"
        };
        var detail = string.IsNullOrWhiteSpace(body?.Error?.Message)
            ? null
            : $" {body.Error.Message}";
        return new AiModelProviderException(
            AiExecutionStatus.RuntimeUnavailable,
            code,
            $"OpenAI response API returned HTTP {(int)response.StatusCode}.{detail}");
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is not null)
        {
            return retryAfter.Delta.Value;
        }

        if (retryAfter?.Date is not null)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
    }

    private sealed record OpenAiResponse(
        OpenAiOutputItem[]? Output,
        OpenAiUsage? Usage);

    private sealed record OpenAiOutputItem(
        string Id,
        string Type,
        OpenAiContentItem[]? Content,
        [property: JsonPropertyName("call_id")] string? CallId,
        string? Name,
        string? Arguments);

    private sealed record OpenAiContentItem(string Type, string? Text);

    private sealed record OpenAiUsage(
        [property: JsonPropertyName("input_tokens")] int? InputTokens,
        [property: JsonPropertyName("output_tokens")] int? OutputTokens);

    private sealed record OpenAiErrorResponse(OpenAiError? Error);

    private sealed record OpenAiError(string? Message, string? Type, string? Code);
}

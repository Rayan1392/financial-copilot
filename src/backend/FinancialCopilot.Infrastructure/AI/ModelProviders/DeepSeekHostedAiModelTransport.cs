using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.AI.ModelProviders;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class DeepSeekHostedAiModelTransport(
    HttpClient httpClient,
    IOptions<AiProviderOptions> providerOptions) : IHostedAiModelTransport
{
    private const int MaxRateLimitAttempts = 3;

    public async Task<HostedAiCompletionResponse> CompleteAsync(
        string modelKey,
        AiModelRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCredential();

        var payload = new JsonObject
        {
            ["model"] = modelKey,
            ["messages"] = new JsonArray(request.Messages.Select(MapMessage).ToArray()),
            ["stream"] = false
        };

        if (request.StructuredOutput is not null)
        {
            payload["response_format"] = new JsonObject { ["type"] = "json_object" };
        }

        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = new JsonArray(request.Tools.Select(MapTool).ToArray());
            payload["tool_choice"] = "auto";
        }

        var deepSeekOptions = providerOptions.Value.DeepSeek;
        if (deepSeekOptions.ThinkingEnabled)
        {
            var thinking = new JsonObject { ["type"] = "enabled" };
            if (!string.IsNullOrWhiteSpace(deepSeekOptions.ReasoningEffort))
            {
                thinking["reasoning_effort"] = deepSeekOptions.ReasoningEffort;
            }
            payload["thinking"] = thinking;
        }

        for (var attempt = 1; attempt <= MaxRateLimitAttempts; attempt++)
        {
            using var response = await httpClient.PostAsJsonAsync("chat/completions", payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<DeepSeekChatCompletionResponse>(
                    cancellationToken: cancellationToken) ??
                    throw Failure("DeepSeek chat completion API returned an empty response.");
                var message = body.Choices?.OrderBy(choice => choice.Index).FirstOrDefault()?.Message;
                var text = message?.Content;

                return new HostedAiCompletionResponse(
                    text,
                    request.StructuredOutput is null ? null : text,
                    MapToolCalls(message?.ToolCalls),
                    body.Usage?.PromptTokens,
                    body.Usage?.CompletionTokens,
                    ResponseId: body.Id);
            }

            var failure = await ReadFailureAsync(response, cancellationToken);
            if (failure.Code != "hosted_provider_rate_limited" || attempt == MaxRateLimitAttempts)
            {
                throw failure;
            }

            await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
        }

        throw Failure("DeepSeek chat completion request failed after bounded retries.");
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
                "DeepSeek",
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
            "The configured DeepSeek chat model does not expose embeddings.");

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
                "The DeepSeek API credential secret is not configured.");
        }
    }

    private static JsonObject MapMessage(AiConversationMessage message)
    {
        var role = message.Role switch
        {
            AiMessageRole.System => "system",
            AiMessageRole.Assistant => "assistant",
            AiMessageRole.Tool => "tool",
            _ => "user"
        };

        var item = new JsonObject
        {
            ["role"] = role,
            ["content"] = message.Content
        };
        if (message.Role == AiMessageRole.Tool)
        {
            item["tool_call_id"] = message.ToolCallId ?? string.Empty;
        }

        return item;
    }

    private static JsonObject MapTool(AiToolDefinition tool) =>
        new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema) ?? new JsonObject()
            }
        };

    private static IReadOnlyCollection<AiToolCall> MapToolCalls(DeepSeekToolCall[]? toolCalls) =>
        toolCalls?
            .Where(toolCall => string.Equals(toolCall.Type, "function", StringComparison.OrdinalIgnoreCase))
            .Select(toolCall => new AiToolCall(
                toolCall.Id,
                toolCall.Function?.Name ?? string.Empty,
                toolCall.Function?.Arguments ?? "{}"))
            .ToArray() ?? [];

    private static AiModelProviderException Failure(string message) =>
        new(AiExecutionStatus.RuntimeUnavailable, "hosted_provider_unavailable", message);

    private static async Task<AiModelProviderException> ReadFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        DeepSeekErrorResponse? body = null;

        try
        {
            body = await response.Content.ReadFromJsonAsync<DeepSeekErrorResponse>(
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            // Preserve status-only failures for non-JSON upstream responses.
        }

        var code = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "hosted_provider_rate_limited",
            HttpStatusCode.Unauthorized => "hosted_provider_authentication_failed",
            HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden => "hosted_provider_quota_exceeded",
            _ => "hosted_provider_unavailable"
        };
        var detail = string.IsNullOrWhiteSpace(body?.Error?.Message)
            ? null
            : $" {body.Error.Message}";

        return new AiModelProviderException(
            AiExecutionStatus.RuntimeUnavailable,
            code,
            $"DeepSeek chat completion API returned HTTP {(int)response.StatusCode}.{detail}");
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

    private sealed record DeepSeekChatCompletionResponse(
        string? Id,
        DeepSeekChoice[]? Choices,
        DeepSeekUsage? Usage);

    private sealed record DeepSeekChoice(int Index, DeepSeekMessage? Message);

    private sealed record DeepSeekMessage(
        string? Content,
        [property: JsonPropertyName("tool_calls")] DeepSeekToolCall[]? ToolCalls);

    private sealed record DeepSeekToolCall(
        string Id,
        string Type,
        DeepSeekFunctionCall? Function);

    private sealed record DeepSeekFunctionCall(string? Name, string? Arguments);

    private sealed record DeepSeekUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);

    private sealed record DeepSeekErrorResponse(DeepSeekError? Error);

    private sealed record DeepSeekError(string? Message, string? Type, string? Code);
}

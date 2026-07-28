using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        // Responses API supports stateful continuation via previous_response_id.
        // When set, the server already has the full conversation context — only send
        // the new input items (tool outputs) and skip replaying prior history.
        var toolsPayload = request.Tools?.Select(tool => new
        {
            type = "function",
            name = tool.Name,
            description = tool.Description,
            parameters = JsonDocument.Parse(tool.ParametersJsonSchema).RootElement.Clone()
        });

        object payload = request.PreviousResponseId is not null
            ? new
            {
                model = modelKey,
                previous_response_id = request.PreviousResponseId,
                input = request.Messages.SelectMany(MapToResponsesApiInputItems).ToArray(),
                tools = toolsPayload,
                text = request.StructuredOutput is null ? null : new { format = new { type = "json_object" } }
            }
            : (object)new
            {
                model = modelKey,
                input = request.Messages.SelectMany(MapToResponsesApiInputItems).ToArray(),
                tools = toolsPayload,
                text = request.StructuredOutput is null ? null : new { format = new { type = "json_object" } }
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
                    body.Usage?.OutputTokens,
                    ResponseId: body.Id);
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

    private static IEnumerable<JsonNode> MapToResponsesApiInputItems(AiConversationMessage message)
    {
        // Tool results → function_call_output (Responses API format)
        if (message.Role == AiMessageRole.Tool)
        {
            yield return new JsonObject
            {
                ["type"] = "function_call_output",
                ["call_id"] = message.ToolCallId ?? string.Empty,
                ["output"] = message.Content
            };
            yield break;
        }

        // Assistant messages that serialized function calls → function_call items
        if (message.Role == AiMessageRole.Assistant)
        {
            var functionCallItems = TryParseFunctionCallItems(message.Content);
            if (functionCallItems is { Count: > 0 })
            {
                foreach (var item in functionCallItems) yield return item;
                yield break;
            }
        }

        var role = message.Role switch
        {
            AiMessageRole.System => "system",
            AiMessageRole.Assistant => "assistant",
            _ => "user"
        };
        yield return new JsonObject { ["role"] = role, ["content"] = message.Content };
    }

    private static List<JsonObject>? TryParseFunctionCallItems(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var result = new List<JsonObject>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("id", out var idEl) ||
                    !element.TryGetProperty("function", out var fnEl))
                    return null;

                var callId = idEl.GetString() ?? string.Empty;
                var itemId = element.TryGetProperty("item_id", out var itemIdEl) ? itemIdEl.GetString() : null;
                var name = fnEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                var args = fnEl.TryGetProperty("arguments", out var argsEl) ? argsEl.GetString() ?? "{}" : "{}";

                result.Add(new JsonObject
                {
                    ["type"] = "function_call",
                    ["id"] = itemId ?? callId,
                    ["call_id"] = callId,
                    ["name"] = name,
                    ["arguments"] = args
                });
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
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
                item.Arguments ?? "{}",
                ItemId: item.Id))
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
        string? Id,
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

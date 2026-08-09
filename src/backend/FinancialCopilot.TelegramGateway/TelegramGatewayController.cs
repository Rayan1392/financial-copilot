using System.Text.Json;
using FinancialCopilot.Application.Telegram;
using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.TelegramGateway;

[ApiController]
[Route("v1/gateway/telegram")]
public sealed class TelegramGatewayController(
    GatewayRequestAuthenticator authenticator,
    GatewayIdempotencyStore idempotency,
    TelegramApiClient telegram) : ControllerBase
{
    [HttpPost("send-message")]
    public Task<TelegramGatewayOperationResult> SendMessage(SendMessageRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.IdempotencyKey, () => telegram.SendMessageAsync(request.ChatId, request.Text, request.ParseMode, request.Actions, cancellationToken), cancellationToken);

    [HttpPost("send-photo")]
    public Task<TelegramGatewayOperationResult> SendPhoto(SendPhotoRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.IdempotencyKey, () => telegram.SendPhotoAsync(request.ChatId, request.Caption, request.ParseMode, request.Actions, request.Media, cancellationToken), cancellationToken);

    [HttpPost("send-chat-action")]
    public Task<TelegramGatewayOperationResult> SendChatAction(SendChatActionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.IdempotencyKey, () => telegram.SendChatActionAsync(request.ChatId, request.Action, cancellationToken), cancellationToken);

    [HttpPost("answer-callback-query")]
    public Task<TelegramGatewayOperationResult> AnswerCallback(AnswerCallbackRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request.IdempotencyKey, () => telegram.AnswerCallbackQueryAsync(request.CallbackQueryId, cancellationToken), cancellationToken);

    [HttpPost("get-chat-member")]
    public async Task<TelegramGatewayMembershipResult> GetChatMember(GetChatMemberRequest request, CancellationToken cancellationToken)
    {
        if (!await IsAuthenticatedAsync(cancellationToken)) return new(false, ErrorCode: "Unauthorized");
        try { return await telegram.GetChatMemberAsync(request.TelegramUserId, request.ChannelId, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, ErrorCode: "Timeout", RedactedError: "Telegram Gateway timed out."); }
        catch { return new(false, ErrorCode: "GatewayUnavailable", RedactedError: "Telegram Gateway could not verify membership."); }
    }

    private async Task<TelegramGatewayOperationResult> ExecuteAsync(string key, Func<Task<TelegramGatewayOperationResult>> action, CancellationToken cancellationToken)
    {
        if (!await IsAuthenticatedAsync(cancellationToken)) return new(false, ErrorCode: "Unauthorized");
        if (string.IsNullOrWhiteSpace(key)) return new(false, ErrorCode: "InvalidIdempotencyKey");
        if (idempotency.TryGet(key, out var existing)) return existing;
        try
        {
            var result = await action();
            if (result.Succeeded || result.ErrorCode is not ("RateLimited" or "Timeout" or "GatewayUnavailable"))
                await idempotency.SetAsync(key, result, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, ErrorCode: "Timeout", RedactedError: "Telegram Gateway timed out."); }
        catch { return new(false, ErrorCode: "GatewayUnavailable", RedactedError: "Telegram Gateway operation failed."); }
    }

    private async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;
        return authenticator.IsValid(Request, body);
    }
}

public sealed record SendMessageRequest(long ChatId, string Text, string? ParseMode, string IdempotencyKey, IReadOnlyList<TelegramAssistantAction>? Actions);
public sealed record SendPhotoRequest(long ChatId, string Caption, string? ParseMode, string IdempotencyKey, IReadOnlyList<TelegramAssistantAction>? Actions, TelegramAssistantMediaAttachment Media);
public sealed record SendChatActionRequest(long ChatId, string Action, string IdempotencyKey);
public sealed record AnswerCallbackRequest(string CallbackQueryId, string IdempotencyKey);
public sealed record GetChatMemberRequest(long TelegramUserId, string ChannelId, string CorrelationId);

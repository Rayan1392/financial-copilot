using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/telegram/assistant")]
[Authorize(Policy = AuthorizationPolicies.ApiClientOnly)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class TelegramAssistantController(
    ITelegramAiAssistantAdapter assistantAdapter,
    ITelegramChannelMonthlyReportHandler channelHandler,
    FinancialCopilot.Application.Authentication.ICurrentActorContext actorContext) : ControllerBase
{
    [HttpPost("updates")]
    public async Task<ActionResult<TelegramAssistantResult>> HandleUpdate(
        TelegramAssistantUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind == TelegramAssistantUpdateKind.ChannelPost &&
            (request.TelegramUserId != 0 || !string.Equals(request.ChatType, "channel", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new TelegramAssistantResult(
                TelegramAssistantResultStatus.ValidationError, null, null, null, [],
                request.CorrelationId ?? HttpContext.TraceIdentifier));
        }

        var update = new TelegramAssistantUpdate(
            request.TelegramUpdateId,
            request.Kind,
            request.TelegramUserId,
            request.TelegramChatId,
            request.MessageThreadId,
            request.TelegramMessageId,
            request.CallbackQueryId,
            request.CallbackData,
            request.Text,
            string.IsNullOrWhiteSpace(request.Locale) ? "fa-IR" : request.Locale.Trim(),
            request.ReceivedAtUtc ?? DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(request.CorrelationId)
                ? HttpContext.TraceIdentifier
                : request.CorrelationId.Trim(),
            request.ChatType);

        var result = request.Kind == TelegramAssistantUpdateKind.ChannelPost
            ? await channelHandler.HandleAsync(update, actorContext.Actor, cancellationToken)
            : await assistantAdapter.HandleAsync(update, cancellationToken);
        return result.Status == TelegramAssistantResultStatus.ValidationError
            ? BadRequest(result)
            : Ok(result);
    }
}

public sealed record TelegramAssistantUpdateRequest(
    long TelegramUpdateId,
    TelegramAssistantUpdateKind Kind,
    long TelegramUserId,
    long TelegramChatId,
    int? MessageThreadId = null,
    long? TelegramMessageId = null,
    string? CallbackQueryId = null,
    string? CallbackData = null,
    string? Text = null,
    string? Locale = null,
    DateTimeOffset? ReceivedAtUtc = null,
    string? CorrelationId = null,
    string? ChatType = null);

using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/telegram")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class TelegramLinkController(
    ITelegramLinkService linkService,
    ICurrentActorContext currentActor) : ControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.TelegramLinkManageSelf)]
    [HttpPost("link-token")]
    public async Task<ActionResult<TelegramLinkChallenge>> CreateLinkToken(CancellationToken cancellationToken) =>
        Ok(await linkService.CreateWebChallengeAsync(
            currentActor.Actor,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.ApiClientOnly)]
    [HttpPost("link/telegram-start")]
    public async Task<ActionResult<TelegramWebConfirmationChallenge>> StartFromTelegram(
        TelegramStartLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsPrivateChat(request.TelegramUserId, request.TelegramChatId))
        {
            return ValidationProblem("Telegram account linking is supported only in a private chat.");
        }

        try
        {
            return Ok(await linkService.CreateTelegramChallengeAsync(
                currentActor.Actor,
                request.ToIdentity(),
                request.TelegramUpdateId,
                HttpContext.TraceIdentifier,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    [Authorize(Policy = AuthorizationPolicies.ApiClientOnly)]
    [HttpPost("link/confirm")]
    public async Task<ActionResult<TelegramLinkResult>> ConfirmFromTelegram(
        ConfirmTelegramLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseStartToken(request.StartParameter, out var token) ||
            !IsPrivateChat(request.TelegramUserId, request.TelegramChatId))
        {
            return BadRequest(CreateProblem("invalid-link-token", "The Telegram linking token is invalid or the chat is not private."));
        }

        TelegramLinkResult result;
        try
        {
            result = await linkService.ConfirmFromTelegramAsync(
                currentActor.Actor,
                token,
                request.ToIdentity(),
                request.TelegramUpdateId,
                HttpContext.TraceIdentifier,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
        return MapResult(result);
    }

    [Authorize(Policy = AuthorizationPolicies.TelegramLinkManageSelf)]
    [HttpPost("link/web-preview")]
    public async Task<ActionResult<TelegramLinkPreview>> PreviewFromWeb(
        ConfirmWebTelegramLinkRequest request,
        CancellationToken cancellationToken)
    {
        var preview = await linkService.PreviewFromWebAsync(currentActor.Actor, request.Token, cancellationToken);
        return preview is null ? NotFound() : Ok(preview);
    }

    [Authorize(Policy = AuthorizationPolicies.TelegramLinkManageSelf)]
    [HttpPost("link/web-confirm")]
    public async Task<ActionResult<TelegramLinkResult>> ConfirmFromWeb(
        ConfirmWebTelegramLinkRequest request,
        CancellationToken cancellationToken) =>
        MapResult(await linkService.ConfirmFromWebAsync(
            currentActor.Actor,
            request.Token,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.TelegramLinkManageSelf)]
    [HttpGet("link/me")]
    public async Task<ActionResult<TelegramLinkView>> GetMyLink(CancellationToken cancellationToken)
    {
        var link = await linkService.GetCurrentAsync(currentActor.Actor, cancellationToken);
        return link is null ? NotFound() : Ok(link);
    }

    [Authorize(Policy = AuthorizationPolicies.TelegramLinkManageSelf)]
    [HttpDelete("link/me")]
    public async Task<IActionResult> Unlink(CancellationToken cancellationToken)
    {
        await linkService.UnlinkAsync(currentActor.Actor, HttpContext.TraceIdentifier, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = AuthorizationPolicies.ApiClientOnly)]
    [HttpPost("link/unlink-from-telegram")]
    public async Task<IActionResult> UnlinkFromTelegram(
        TelegramUnlinkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await linkService.UnlinkFromTelegramAsync(
                currentActor.Actor,
                request.TelegramUserId,
                request.TelegramUpdateId,
                HttpContext.TraceIdentifier,
                cancellationToken);
            return NoContent();
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    internal static bool TryParseStartToken(string? startParameter, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(startParameter) ||
            !startParameter.StartsWith("link_", StringComparison.Ordinal) ||
            startParameter.Length != 53)
        {
            return false;
        }

        var candidate = startParameter[5..];
        if (!candidate.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            return false;
        }
        token = candidate;
        return true;
    }

    private ActionResult<TelegramLinkResult> MapResult(TelegramLinkResult result) => result.Outcome switch
    {
        TelegramLinkOutcome.Linked or TelegramLinkOutcome.AlreadyLinked => Ok(result),
        TelegramLinkOutcome.Conflict => Conflict(CreateProblem(
            "telegram-link-conflict",
            "This account or Telegram identity is already linked. Unlink it before linking a different identity.")),
        _ => BadRequest(CreateProblem(
            "invalid-link-token",
            "The Telegram linking token is invalid, expired, revoked, or already consumed."))
    };

    private ProblemDetails CreateProblem(string code, string title) => new()
    {
        Type = $"https://financialcopilot/errors/{code}",
        Title = title,
        Status = code == "telegram-link-conflict" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
        Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
    };

    private static bool IsPrivateChat(long telegramUserId, long telegramChatId) =>
        telegramUserId > 0 && telegramChatId == telegramUserId;
}

public sealed record TelegramStartLinkRequest(
    long TelegramUserId,
    long TelegramChatId,
    string? Username,
    long TelegramUpdateId)
{
    public TelegramIdentity ToIdentity() => new(TelegramUserId, TelegramChatId, Username);
}

public sealed record ConfirmTelegramLinkRequest(
    string StartParameter,
    long TelegramUserId,
    long TelegramChatId,
    string? Username,
    long TelegramUpdateId)
{
    public TelegramIdentity ToIdentity() => new(TelegramUserId, TelegramChatId, Username);
}

public sealed record ConfirmWebTelegramLinkRequest(string Token);

public sealed record TelegramUnlinkRequest(long TelegramUserId, long TelegramUpdateId);

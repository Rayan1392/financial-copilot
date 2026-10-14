using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/telegram/financial-statements")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminTelegramFinancialStatementRecoveryController(
    ITelegramChannelFinancialStatementHandler handler) : ControllerBase
{
    [HttpPost("recover-skipped")]
    public async Task<ActionResult<TelegramFinancialStatementRecoveryResult>> RecoverSkipped(
        [FromBody] RecoverTelegramFinancialStatementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.RecoverSkippedAsync(
            request.TelegramChatId, request.TelegramMessageId, cancellationToken);
        if (result.Code == "Recovered") return Ok(result);
        if (result.Code == "CheckpointNotFound") return NotFound(result);
        if (result.Code == "InvalidTarget") return BadRequest(result);
        return Conflict(result);
    }
}

public sealed record RecoverTelegramFinancialStatementRequest(long TelegramChatId, long TelegramMessageId);

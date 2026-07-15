using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/notifications")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminNotificationsController(
    ICurrentActorContext actorContext,
    INotificationOperations operations) : ControllerBase
{
    [HttpGet("dead-letters")]
    public Task<IReadOnlyCollection<NotificationDeadLetterDto>> GetDeadLetters(
        [FromQuery] int maximumCount = 50,
        CancellationToken cancellationToken = default) =>
        operations.GetDeadLettersAsync(maximumCount, cancellationToken);

    [HttpPost("dead-letters/{notificationIntentId:guid}/retry")]
    public async Task<IActionResult> Retry(
        Guid notificationIntentId,
        [FromBody] RetryNotificationRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            await operations.RetryDeadLetterAsync(notificationIntentId, actorContext.Actor.ActorId,
                actorContext.Actor.TenantId,
                string.IsNullOrWhiteSpace(request?.CorrelationId)
                    ? HttpContext.TraceIdentifier : request.CorrelationId.Trim(), cancellationToken);
            return Accepted();
        }
        catch (NotificationValidationException exception)
        {
            ModelState.AddModelError(nameof(notificationIntentId), exception.Message);
            return ValidationProblem(ModelState);
        }
    }
}

public sealed record RetryNotificationRequest(string? CorrelationId);

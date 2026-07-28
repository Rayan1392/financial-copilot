using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/alerts/me")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AlertHistoryController(
    ICurrentActorContext actorContext,
    IAlertHistoryUseCases useCases) : ControllerBase
{
    [HttpGet("history")]
    [Authorize(Policy = AuthorizationPolicies.NotificationReadSelf)]
    public async Task<ActionResult<AlertHistoryResponse>> History(
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? symbol = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? dismissed = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var page = await useCases.GetHistoryAsync(new AlertHistoryQuery(
            actorContext.Actor, cursor, pageSize, symbol, eventType, category, status, dismissed,
            fromUtc, toUtc), cancellationToken);
        return Ok(new AlertHistoryResponse(page.Items.Select(Map).ToArray(), page.NextCursor,
            page.PageSize, page.HasMore, page.RetentionPolicy));
    }

    [HttpGet("{alertId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.NotificationReadSelf)]
    public async Task<ActionResult<UserAlertDetailResponse>> Detail(
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var detail = await useCases.GetDetailAsync(actorContext.Actor, alertId, cancellationToken);
        return detail is null ? NotFound() : Ok(Map(detail));
    }

    [HttpGet("{alertId:guid}/why")]
    [Authorize(Policy = AuthorizationPolicies.NotificationReadSelf)]
    public async Task<ActionResult<AlertWhyResponse>> Why(
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var why = await useCases.GetWhyAsync(actorContext.Actor, alertId, cancellationToken);
        return why is null ? NotFound() : Ok(new AlertWhyResponse(
            why.AlertId, why.WhyText, why.EvidenceHash, why.EvidenceSnapshotJson, why.Methodology));
    }

    [HttpPost("{alertId:guid}/dismiss")]
    [Authorize(Policy = AuthorizationPolicies.NotificationManageSelf)]
    public async Task<ActionResult<UserAlertDetailResponse>> Dismiss(
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var result = await useCases.DismissAsync(new DismissAlertCommand(
            actorContext.Actor, alertId, HttpContext.TraceIdentifier), cancellationToken);
        return result is null ? NotFound() : Ok(Map(result));
    }

    [HttpPost("{alertId:guid}/restore")]
    [Authorize(Policy = AuthorizationPolicies.NotificationManageSelf)]
    public async Task<ActionResult<UserAlertDetailResponse>> Restore(
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var result = await useCases.RestoreAsync(new RestoreAlertCommand(
            actorContext.Actor, alertId, HttpContext.TraceIdentifier), cancellationToken);
        return result is null ? NotFound() : Ok(Map(result));
    }

    [HttpPost("{alertId:guid}/feedback")]
    [Authorize(Policy = AuthorizationPolicies.NotificationManageSelf)]
    public async Task<ActionResult<UserAlertDetailResponse>> Feedback(
        Guid alertId,
        [FromBody] AlertFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Feedback))
        {
            ModelState.AddModelError(nameof(request.Feedback), "Feedback is required.");
            return ValidationProblem(ModelState);
        }

        var result = await useCases.RecordFeedbackAsync(new FeedbackAlertCommand(
            actorContext.Actor, alertId, request.Feedback, HttpContext.TraceIdentifier), cancellationToken);
        return result is null ? NotFound() : Ok(Map(result));
    }

    [HttpPost("{alertId:guid}/mute")]
    [Authorize(Policy = AuthorizationPolicies.NotificationManageSelf)]
    public async Task<ActionResult<UserAlertDetailResponse>> Mute(
        Guid alertId,
        [FromBody] AlertMuteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCases.MuteAsync(new MuteAlertCommand(
                actorContext.Actor, alertId, request.Scope, request.Confirmed, HttpContext.TraceIdentifier),
                cancellationToken);
            return result is null ? NotFound() : Ok(Map(result));
        }
        catch (NotificationValidationException exception)
        {
            ModelState.AddModelError(nameof(request.Scope), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPost("{alertId:guid}/reaction-refresh")]
    [Authorize(Policy = AuthorizationPolicies.NotificationManageSelf)]
    public async Task<ActionResult<IReadOnlyCollection<AlertReactionResponse>>> RefreshReaction(
        Guid alertId,
        [FromBody] AlertReactionRefreshRequest? request,
        CancellationToken cancellationToken)
    {
        var reactions = await useCases.RefreshReactionAsync(new RefreshAlertReactionCommand(
            actorContext.Actor, alertId, request?.HorizonCode, HttpContext.TraceIdentifier), cancellationToken);
        return reactions.Count == 0 ? NotFound() : Ok(reactions.Select(Map).ToArray());
    }

    [HttpGet("{alertId:guid}/similar")]
    [Authorize(Policy = AuthorizationPolicies.NotificationReadSelf)]
    public async Task<ActionResult<IReadOnlyCollection<AlertSimilarEventResponse>>> Similar(
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var detail = await useCases.GetDetailAsync(actorContext.Actor, alertId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail.SimilarEvents.Select(Map).ToArray());
    }

    private static UserAlertRecordResponse Map(UserAlertRecordDto value) => new(
        value.Id, value.SymbolKey, value.EventType, value.Category, value.Severity,
        value.DeliveryStatus, value.DeliveryReason, value.CreatedAtUtc, value.DeliveredAtUtc,
        value.DismissedAtUtc, value.MutedAtUtc, value.WhyText, value.EvidenceHash, value.CorrelationId);

    private static UserAlertDetailResponse Map(UserAlertDetailDto value) => new(
        Map(value.Record), value.SourceEventId, value.AlertRuleId, value.NotificationIntentId,
        value.EvidenceReference, value.EvidenceSnapshotJson, value.DetectorVersion, value.RuleVersion,
        value.PreferenceVersion, value.PolicyVersion, value.DeliveryTimeline.Select(Map).ToArray(),
        value.Reactions.Select(Map).ToArray(), value.SimilarEvents.Select(Map).ToArray(),
        value.RetentionPolicy);

    private static AlertDeliveryTimelineResponse Map(AlertDeliveryTimelineDto value) => new(
        value.OccurredAtUtc, value.Status, value.Reason, value.AttemptNumber,
        value.ProviderMessageId, value.ErrorCode);

    private static AlertReactionResponse Map(AlertReactionDto value) => new(
        value.HorizonCode, value.Status, value.CalculationVersion, value.AnchorPrice,
        value.AnchorAtUtc, value.ReactionPercent, value.Reason, value.CalculatedAtUtc);

    private static AlertSimilarEventResponse Map(AlertSimilarEventDto value) => new(
        value.AlertId, value.SymbolKey, value.EventType, value.Category, value.CreatedAtUtc,
        value.Methodology);
}

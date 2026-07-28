using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/notifications/me")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class NotificationsController(
    ICurrentActorContext actorContext,
    INotificationUseCases useCases) : ControllerBase
{
    [HttpGet("preferences")]
    [Authorize(Policy = AuthorizationPolicies.NotificationReadSelf)]
    public async Task<ActionResult<NotificationPreferencesResponse>> GetPreferences(
        CancellationToken cancellationToken) =>
        Ok(Map(await useCases.GetPreferencesAsync(actorContext.Actor, cancellationToken)));

    [HttpPut("preferences")]
    [Authorize(Policy = AuthorizationPolicies.NotificationManageSelf)]
    public async Task<ActionResult<NotificationPreferencesResponse>> UpdatePreferences(
        UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryInput(request, out var input)) return ValidationProblem(ModelState);
        try
        {
            var result = await useCases.UpdatePreferencesAsync(new UpdateNotificationPreferenceCommand(
                actorContext.Actor, request.ExpectedVersion, input!, "Api",
                string.IsNullOrWhiteSpace(request.CorrelationId)
                    ? HttpContext.TraceIdentifier : request.CorrelationId.Trim()), cancellationToken);
            return Ok(Map(result));
        }
        catch (NotificationValidationException exception)
        {
            ModelState.AddModelError(nameof(request.ExpectedVersion), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpGet("history")]
    [Authorize(Policy = AuthorizationPolicies.NotificationReadSelf)]
    public async Task<ActionResult<NotificationHistoryResponse>> GetHistory(
        [FromQuery] int offset = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var page = await useCases.GetHistoryAsync(actorContext.Actor, offset, pageSize, cancellationToken);
        return Ok(new NotificationHistoryResponse(page.Items.Select(item => new NotificationHistoryItemResponse(
            item.Id, item.EventType, item.EntityKey, item.Severity.ToString(), item.Status.ToString(),
            item.SuppressionReason.ToString(), item.EvidenceReference, item.CreatedAtUtc,
            item.DeliveredAtUtc, item.LastErrorCode, item.AttemptCount, item.CorrelationId)).ToArray(),
            page.Offset, page.PageSize, page.HasMore));
    }

    private bool TryInput(UpdateNotificationPreferencesRequest request, out NotificationPreferenceInput? input)
    {
        input = null;
        var valid = TryEnum(request.DeliveryMode, nameof(request.DeliveryMode), out NotificationDeliveryMode deliveryMode) &
                    TryEnum(request.MinimumSeverity, nameof(request.MinimumSeverity), out InsightSeverity minimumSeverity);
        var categories = new List<NotificationCategoryPreferenceInput>();
        foreach (var item in request.Categories ?? [])
        {
            InsightSeverity? severity = null;
            if (item.MinimumSeverity is not null &&
                !TryEnum(item.MinimumSeverity, $"categories.{item.EventType}.minimumSeverity", out severity)) valid = false;
            categories.Add(new NotificationCategoryPreferenceInput(item.EventType, item.Enabled, severity, item.CooldownMinutes));
        }
        if (!valid) return false;
        input = new NotificationPreferenceInput(request.TimeZoneId, deliveryMode,
            request.QuietHoursStart, request.QuietHoursEnd, minimumSeverity, request.DailyCap,
            request.DigestTime, request.CooldownMinutes, categories,
            (request.Symbols ?? []).Select(item =>
                new NotificationSymbolPreferenceInput(item.ExternalCompanyId, item.Muted)).ToArray());
        return true;
    }

    private bool TryEnum<T>(string? value, string field, out T result) where T : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out result)) return true;
        ModelState.AddModelError(field, $"Valid values: {string.Join(", ", Enum.GetNames<T>())}.");
        result = default;
        return false;
    }

    private bool TryEnum<T>(string? value, string field, out T? result) where T : struct, Enum
    {
        if (TryEnum(value, field, out T parsed)) { result = parsed; return true; }
        result = null;
        return false;
    }

    private static NotificationPreferencesResponse Map(NotificationPreferenceDto item) => new(
        item.Id, item.TimeZoneId, item.DeliveryMode.ToString(), item.QuietHoursStart,
        item.QuietHoursEnd, item.MinimumSeverity.ToString(), item.DailyCap, item.DigestTime,
        item.CooldownMinutes, item.Version,
        item.Categories.Select(value => new NotificationCategoryPreferenceResponse(
            value.EventType, value.Enabled, value.MinimumSeverity?.ToString(), value.CooldownMinutes)).ToArray(),
        item.Symbols.Select(value => new NotificationSymbolPreferenceResponse(
            value.ExternalCompanyId, value.Muted)).ToArray(), item.PolicyVersion,
        item.EffectivePolicyExplanation, item.UpdatedAtUtc);
}

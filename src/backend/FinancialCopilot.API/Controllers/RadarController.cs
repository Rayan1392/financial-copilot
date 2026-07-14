using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Radar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/radar")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class RadarController(
    ICurrentActorContext actorContext,
    IRadarUseCases useCases) : ControllerBase
{
    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.RadarReadSelf)]
    public async Task<ActionResult<RadarProfileResponse>> GetMine(CancellationToken cancellationToken) =>
        Ok(Map(await useCases.GetAsync(new GetMyRadarQuery(actorContext.Actor), cancellationToken)));

    [HttpPut("me/preferences")]
    [Authorize(Policy = AuthorizationPolicies.RadarWriteSelf)]
    public async Task<ActionResult<RadarProfileResponse>> UpdatePreferences(
        UpdateRadarPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryProfileInput(request, out var input)) return ValidationProblem(ModelState);
        try
        {
            var result = await useCases.UpdateAsync(
                new UpdateMyRadarCommand(actorContext.Actor, request.ExpectedVersion, input!, "Api"),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(request.ExpectedVersion), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPost("me/enable")]
    [HttpPost("me/pause")]
    [Authorize(Policy = AuthorizationPolicies.RadarWriteSelf)]
    public async Task<ActionResult<RadarProfileResponse>> SetState(
        RadarStateChangeRequest request,
        CancellationToken cancellationToken)
    {
        var requestedState = Request.Path.Value?.EndsWith("/pause", StringComparison.OrdinalIgnoreCase) == true
            ? RadarState.Paused : RadarState.Active;
        try
        {
            var current = await useCases.GetAsync(new GetMyRadarQuery(actorContext.Actor), cancellationToken);
            var input = new RadarProfileInput(current.EventTypes, current.MinimumSeverity, current.MinimumImportance,
                current.Sensitivity, current.DeliveryMode, requestedState);
            return Ok(Map(await useCases.UpdateAsync(
                new UpdateMyRadarCommand(actorContext.Actor, request.ExpectedVersion, input, "Api"),
                cancellationToken)));
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(request.ExpectedVersion), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpDelete("me")]
    [Authorize(Policy = AuthorizationPolicies.RadarWriteSelf)]
    public async Task<IActionResult> Remove([FromQuery] int expectedVersion, CancellationToken cancellationToken)
    {
        try
        {
            await useCases.RemoveAsync(new RemoveMyRadarCommand(actorContext.Actor, expectedVersion, "Api"), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(expectedVersion), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPut("me/symbols/{externalCompanyId}")]
    [Authorize(Policy = AuthorizationPolicies.RadarWriteSelf)]
    public async Task<ActionResult<RadarProfileResponse>> UpsertOverride(
        string externalCompanyId,
        UpdateRadarSymbolOverrideRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryOverrideInput(request, out var input)) return ValidationProblem(ModelState);
        try
        {
            return Ok(Map(await useCases.UpsertOverrideAsync(
                new UpsertRadarSymbolOverrideCommand(
                    actorContext.Actor, externalCompanyId, request.ExpectedVersion, input!, "Api"),
                cancellationToken)));
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(externalCompanyId), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpDelete("me/symbols/{externalCompanyId}")]
    [Authorize(Policy = AuthorizationPolicies.RadarWriteSelf)]
    public async Task<ActionResult<RadarProfileResponse>> RemoveOverride(
        string externalCompanyId,
        [FromQuery] int expectedVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(Map(await useCases.RemoveOverrideAsync(
                new RemoveRadarSymbolOverrideCommand(
                    actorContext.Actor, externalCompanyId, expectedVersion, "Api"), cancellationToken)));
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(externalCompanyId), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPost("me/test-notification")]
    [Authorize(Policy = AuthorizationPolicies.RadarWriteSelf)]
    public async Task<ActionResult<RadarTestNotificationResponse>> TestNotification(
        RadarTestNotificationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = await useCases.SendTestNotificationAsync(new SendRadarTestNotificationCommand(
                actorContext.Actor,
                request.IdempotencyKey ?? string.Empty,
                request.CorrelationId ?? HttpContext.TraceIdentifier), cancellationToken);
            return Ok(new RadarTestNotificationResponse(id, Informational: true, Billable: false));
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(request.IdempotencyKey), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    private bool TryProfileInput(UpdateRadarPreferencesRequest request, out RadarProfileInput? input)
    {
        input = null;
        var eventTypes = ParseTypes(request.EventTypes, required: true);
        var valid = eventTypes is not null &
                    TryEnum(request.MinimumSeverity, nameof(request.MinimumSeverity), out InsightSeverity severity) &
                    TryEnum(request.Sensitivity, nameof(request.Sensitivity), out RadarSensitivity sensitivity) &
                    TryEnum(request.DeliveryMode, nameof(request.DeliveryMode), out RadarDeliveryMode delivery) &
                    TryEnum(request.State, nameof(request.State), out RadarState state);
        if (!valid) return false;
        input = new RadarProfileInput(eventTypes!, severity, request.MinimumImportance, sensitivity, delivery, state);
        return true;
    }

    private bool TryOverrideInput(UpdateRadarSymbolOverrideRequest request, out RadarSymbolOverrideInput? input)
    {
        input = null;
        var valid = TryEnum(request.State, nameof(request.State), out RadarState state);
        var eventTypes = ParseTypes(request.EventTypes, required: false);
        if (request.EventTypes is not null && eventTypes is null) valid = false;
        InsightSeverity? severity = null;
        RadarSensitivity? sensitivity = null;
        if (request.MinimumSeverity is not null &&
            !TryEnum(request.MinimumSeverity, nameof(request.MinimumSeverity), out severity)) valid = false;
        if (request.Sensitivity is not null &&
            !TryEnum(request.Sensitivity, nameof(request.Sensitivity), out sensitivity)) valid = false;
        if (!valid) return false;
        input = new RadarSymbolOverrideInput(state, eventTypes, severity, request.MinimumImportance, sensitivity);
        return true;
    }

    private IReadOnlyCollection<InsightType>? ParseTypes(IReadOnlyCollection<string>? values, bool required)
    {
        if (values is null || values.Count == 0)
        {
            if (required) ModelState.AddModelError("eventTypes", "At least one event type is required.");
            return required ? null : values is null ? null : [];
        }
        var parsed = new List<InsightType>();
        foreach (var value in values)
        {
            if (Enum.TryParse<InsightType>(value, true, out var item)) parsed.Add(item);
            else ModelState.AddModelError("eventTypes", $"Unknown insight event type '{value}'.");
        }
        return parsed.Count == values.Count ? parsed : null;
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
        if (TryEnum(value, field, out T parsed))
        {
            result = parsed;
            return true;
        }
        result = null;
        return false;
    }

    private static RadarProfileResponse Map(RadarProfileDto item) => new(
        item.Id, item.State.ToString(), item.EventTypes.Select(value => value.ToString()).ToArray(),
        item.MinimumSeverity.ToString(), item.MinimumImportance, item.Sensitivity.ToString(),
        item.DeliveryMode.ToString(), item.Version,
        item.SymbolOverrides.Select(value => new RadarSymbolOverrideResponse(
            value.Id, value.ExternalCompanyId, value.Symbol, value.State.ToString(),
            value.EventTypes?.Select(type => type.ToString()).ToArray(), value.MinimumSeverity?.ToString(),
            value.MinimumImportance, value.Sensitivity?.ToString(), value.Version, value.UpdatedAtUtc)).ToArray(),
        item.EvaluationCadenceSeconds, item.LastEvaluatedAtUtc, item.LastSourceFreshnessUtc,
        item.FreshnessDisclosure, item.CreatedAtUtc, item.UpdatedAtUtc);
}

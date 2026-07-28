using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/trackers")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class ConditionalTrackersController(
    ICurrentActorContext actorContext,
    IConditionalTrackerUseCases useCases) : ControllerBase
{
    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.TrackerReadSelf)]
    public async Task<ActionResult<AlertRulesResponse>> GetMine(
        [FromQuery] bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var result = await useCases.GetAsync(
            new GetMyAlertRulesQuery(actorContext.Actor, includeRemoved), cancellationToken);
        return Ok(new AlertRulesResponse(result.Select(Map).ToArray()));
    }

    [HttpGet("me/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TrackerReadSelf)]
    public async Task<ActionResult<AlertRuleResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await useCases.GetAsync(actorContext.Actor, id, cancellationToken);
        return result is null ? NotFound() : Ok(Map(result));
    }

    [HttpPost("me")]
    [Authorize(Policy = AuthorizationPolicies.TrackerWriteSelf)]
    public async Task<ActionResult<AlertRuleResponse>> Create(
        CreateAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            AlertRuleDto result;
            if (!string.IsNullOrWhiteSpace(request.NaturalLanguageText))
            {
                result = await useCases.ParseAsync(
                    new ParseNaturalLanguageAlertRuleCommand(
                        actorContext.Actor,
                        request.ExternalCompanyId,
                        request.NaturalLanguageText,
                        request.IdempotencyKey),
                    cancellationToken);
            }
            else
            {
                if (!TryBuildInput(request, out var input)) return ValidationProblem(ModelState);
                result = await useCases.CreateAsync(
                    new CreateAlertRuleCommand(
                        actorContext.Actor,
                        request.ExternalCompanyId,
                        input!,
                        request.IdempotencyKey,
                        request.ConfirmImmediately),
                    cancellationToken);
            }

            return CreatedAtAction(nameof(Get), new { id = result.Id }, Map(result));
        }
        catch (Exception exception) when (exception is AlertRuleValidationException or ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(nameof(request.ExternalCompanyId), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPost("me/{id:guid}/confirm")]
    [Authorize(Policy = AuthorizationPolicies.TrackerWriteSelf)]
    public async Task<ActionResult<AlertRuleResponse>> Confirm(
        Guid id,
        ConfirmAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCases.ConfirmAsync(
                new ConfirmAlertRuleCommand(
                    actorContext.Actor, id, request.ExpectedVersion, request.ConfirmationToken),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (Exception exception) when (exception is AlertRuleValidationException or InvalidOperationException)
        {
            ModelState.AddModelError(nameof(id), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPatch("me/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TrackerWriteSelf)]
    public async Task<ActionResult<AlertRuleResponse>> Update(
        Guid id,
        UpdateAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            AlertRuleInput? input = null;
            AlertRuleState? state = null;
            if (!string.IsNullOrWhiteSpace(request.RuleType))
            {
                if (!TryBuildInput(request, out input)) return ValidationProblem(ModelState);
            }
            else if (!TryParseEnum(request.State, nameof(request.State), out state))
            {
                return ValidationProblem(ModelState);
            }

            var result = await useCases.UpdateAsync(
                new UpdateAlertRuleCommand(actorContext.Actor, id, request.ExpectedVersion, input, state),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (Exception exception) when (exception is AlertRuleValidationException or ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(nameof(id), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpDelete("me/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TrackerWriteSelf)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await useCases.RemoveAsync(new RemoveAlertRuleCommand(actorContext.Actor, id), cancellationToken);
            return NoContent();
        }
        catch (AlertRuleValidationException)
        {
            return NotFound();
        }
    }

    private bool TryBuildInput(CreateAlertRuleRequest request, out AlertRuleInput? input) =>
        TryBuildInput(
            request.RuleType, request.MetricOrEventCode, request.Operator, request.Threshold,
            request.Unit, request.BaselineWindow, request.Recurrence, request.CooldownMinutes,
            request.ResetPolicy, request.SessionPolicy, request.Hysteresis, out input);

    private bool TryBuildInput(UpdateAlertRuleRequest request, out AlertRuleInput? input) =>
        TryBuildInput(
            request.RuleType, request.MetricOrEventCode, request.Operator, request.Threshold,
            request.Unit, request.BaselineWindow, request.Recurrence, request.CooldownMinutes,
            request.ResetPolicy, request.SessionPolicy, request.Hysteresis, out input);

    private bool TryBuildInput(
        string? ruleType,
        string? metricOrEventCode,
        string? @operator,
        decimal? threshold,
        string? unit,
        int? baselineWindow,
        string? recurrence,
        int? cooldownMinutes,
        string? resetPolicy,
        string? sessionPolicy,
        decimal? hysteresis,
        out AlertRuleInput? input)
    {
        input = null;
        var valid = TryParseEnum(ruleType, nameof(ruleType), out AlertRuleType? parsedType) &
                    TryParseEnum(@operator, nameof(@operator), out AlertRuleOperator? parsedOperator) &
                    TryParseEnum(unit, nameof(unit), out AlertRuleUnit? parsedUnit) &
                    TryParseEnum(recurrence, nameof(recurrence), out AlertRuleRecurrence? parsedRecurrence) &
                    TryParseEnum(resetPolicy, nameof(resetPolicy), out AlertRuleResetPolicy? parsedReset) &
                    TryParseEnum(sessionPolicy, nameof(sessionPolicy), out AlertRuleSessionPolicy? parsedSession);
        if (string.IsNullOrWhiteSpace(metricOrEventCode))
        {
            ModelState.AddModelError(nameof(metricOrEventCode), "MetricOrEventCode is required.");
            valid = false;
        }
        if (!threshold.HasValue)
        {
            ModelState.AddModelError(nameof(threshold), "Threshold is required.");
            valid = false;
        }
        if (!valid) return false;
        input = new AlertRuleInput(
            parsedType!.Value, metricOrEventCode!, parsedOperator!.Value, threshold!.Value,
            parsedUnit!.Value, baselineWindow, parsedRecurrence!.Value, cooldownMinutes ?? 30,
            parsedReset!.Value, parsedSession!.Value, hysteresis);
        return true;
    }

    private bool TryParseEnum<T>(string? value, string field, out T? parsed) where T : struct, Enum
    {
        parsed = null;
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<T>(value, true, out var result))
        {
            parsed = result;
            return true;
        }
        ModelState.AddModelError(field, $"Valid values: {string.Join(", ", Enum.GetNames<T>())}.");
        return false;
    }

    private static AlertRuleResponse Map(AlertRuleDto item) =>
        new(item.Id, item.ExternalCompanyId, item.Symbol, item.CompanyName, item.RuleType.ToString(),
            item.MetricOrEventCode, item.Operator.ToString(), item.Threshold, item.Unit.ToString(),
            item.BaselineWindow, item.Recurrence.ToString(), item.CooldownMinutes,
            item.ResetPolicy.ToString(), item.SessionPolicy.ToString(), item.Hysteresis,
            item.State.ToString(), item.Version, item.ConfirmationToken, item.ConfirmationExpiresAtUtc, item.ConfirmationText,
            item.OriginalText, item.ParserVersion, item.LastObservedValue, item.LastObservedAtUtc,
            item.LastTriggeredAtUtc, item.NextEligibleAtUtc, item.TriggerSequence,
            item.CreatedAtUtc, item.UpdatedAtUtc);
}

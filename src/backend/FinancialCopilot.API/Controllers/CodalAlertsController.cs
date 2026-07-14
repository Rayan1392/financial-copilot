using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.CodalAlerts;
using FinancialCopilot.Domain.Financial.CodalAlerts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/codal-alerts")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class CodalAlertsController(
    ICurrentActorContext actorContext,
    IGetMyCodalAlertSubscriptionsUseCase getSubscriptions,
    ICreateCodalAlertSubscriptionUseCase createSubscription,
    IUpdateCodalAlertSubscriptionUseCase updateSubscription,
    IDeleteCodalAlertSubscriptionUseCase deleteSubscription,
    IGenerateCodalAlertSummaryUseCase generateSummary) : ControllerBase
{
    [HttpGet("me/subscriptions")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistReadSelf)]
    public async Task<ActionResult<CodalAlertSubscriptionsResponse>> GetMine(CancellationToken cancellationToken)
    {
        var result = await getSubscriptions.ExecuteAsync(
            new GetMyCodalAlertSubscriptionsQuery(actorContext.Actor),
            cancellationToken);
        return Ok(new CodalAlertSubscriptionsResponse(result.Select(Map).ToArray()));
    }

    [HttpPost("me/subscriptions")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistWriteSelf)]
    public async Task<ActionResult<CodalAlertSubscriptionResponse>> Create(
        CodalAlertSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseTypes(request.AnnouncementTypes, out var types)) return ValidationProblem(ModelState);
        if (!TryParseEnum<CodalAnnouncementImportance>(request.MinimumImportance, nameof(request.MinimumImportance), out var importance)) return ValidationProblem(ModelState);

        try
        {
            var result = await createSubscription.ExecuteAsync(
                new CreateCodalAlertSubscriptionCommand(
                    actorContext.Actor,
                    request.ExternalCompanyId,
                    types,
                    importance!.Value,
                    request.RawAlertEnabled,
                    request.AiSummaryEnabled),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (CodalAlertSubscriptionValidationException exception)
        {
            ModelState.AddModelError(nameof(request.ExternalCompanyId), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPut("me/subscriptions/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistWriteSelf)]
    public async Task<ActionResult<CodalAlertSubscriptionResponse>> Update(
        Guid id,
        UpdateCodalAlertSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseTypes(request.AnnouncementTypes, out var types)) return ValidationProblem(ModelState);
        if (!TryParseEnum<CodalAnnouncementImportance>(request.MinimumImportance, nameof(request.MinimumImportance), out var importance)) return ValidationProblem(ModelState);
        if (!TryParseEnum<CodalAlertSubscriptionState>(request.State, nameof(request.State), out var state)) return ValidationProblem(ModelState);

        try
        {
            var result = await updateSubscription.ExecuteAsync(
                new UpdateCodalAlertSubscriptionCommand(
                    actorContext.Actor,
                    id,
                    types,
                    importance!.Value,
                    request.RawAlertEnabled,
                    request.AiSummaryEnabled,
                    state!.Value),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (CodalAlertSubscriptionValidationException exception)
        {
            ModelState.AddModelError(nameof(id), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpDelete("me/subscriptions/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistWriteSelf)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteSubscription.ExecuteAsync(
            new DeleteCodalAlertSubscriptionCommand(actorContext.Actor, id),
            cancellationToken);
        return NoContent();
    }

    [HttpPost("me/insights/{insightEventId:guid}/summary")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistWriteSelf)]
    public async Task<ActionResult<CodalAlertSummaryResponse>> GenerateSummary(
        Guid insightEventId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await generateSummary.ExecuteAsync(
                new GenerateCodalAlertSummaryCommand(
                    actorContext.Actor,
                    insightEventId,
                    HttpContext.TraceIdentifier),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (CodalAlertSubscriptionValidationException exception)
        {
            ModelState.AddModelError(nameof(insightEventId), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    private bool TryParseTypes(
        IReadOnlyCollection<string>? values,
        out IReadOnlyCollection<CodalAnnouncementType> parsed)
    {
        var requested = values is { Count: > 0 }
            ? values
            : Enum.GetNames<CodalAnnouncementType>();
        var result = new List<CodalAnnouncementType>();
        foreach (var value in requested)
        {
            if (Enum.TryParse<CodalAnnouncementType>(value, ignoreCase: true, out var type))
            {
                result.Add(type);
                continue;
            }

            ModelState.AddModelError(nameof(CodalAlertSubscriptionRequest.AnnouncementTypes), $"Unknown announcement type '{value}'. Valid: {string.Join(", ", Enum.GetNames<CodalAnnouncementType>())}.");
            parsed = [];
            return false;
        }

        parsed = result.Distinct().ToArray();
        return true;
    }

    private bool TryParseEnum<T>(string? value, string parameterName, out T? parsed)
        where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            ModelState.AddModelError(parameterName, $"{parameterName} is required.");
            return false;
        }

        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
        {
            parsed = result;
            return true;
        }

        ModelState.AddModelError(parameterName, $"Unknown {parameterName} '{value}'. Valid: {string.Join(", ", Enum.GetNames<T>())}.");
        return false;
    }

    private static CodalAlertSubscriptionResponse Map(CodalAlertSubscriptionDto subscription) =>
        new(
            subscription.Id,
            subscription.ExternalCompanyId,
            subscription.Symbol,
            subscription.CompanyName,
            subscription.AnnouncementTypes.Select(item => item.ToString()).ToArray(),
            subscription.MinimumImportance.ToString(),
            subscription.RawAlertEnabled,
            subscription.AiSummaryEnabled,
            subscription.State.ToString(),
            subscription.CreatedAtUtc,
            subscription.UpdatedAtUtc);

    private static CodalAlertSummaryResponse Map(CodalAlertSummaryDto summary) =>
        new(
            summary.Id,
            summary.InsightEventId,
            summary.Status,
            summary.SummaryText,
            summary.EvidenceHash,
            summary.PromptPolicyVersion,
            summary.ProviderName,
            summary.ModelName,
            summary.FailureReason,
            summary.UpdatedAtUtc);
}

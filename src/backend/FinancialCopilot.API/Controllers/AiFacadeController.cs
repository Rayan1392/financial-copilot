using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Middleware;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Billing;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Application.Scanner;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static FinancialCopilot.API.Controllers.AiFacadeHttpMapper;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/ai/v1")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AiFacadeController(
    ICurrentActorContext actorContext,
    IAiQueryOrchestrationService orchestrationService,
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IAlertHistoryUseCases alertHistory,
    ICapabilityGuidanceService guidanceService,
    // Change «sol-deep-analysis-web-search», OQ-113 (a): registered with the V2 workflow only.
    GroundedAnalysisHintFactory? groundedAnalysisHints = null,
    TimeProvider? timeProvider = null,
    // Feature 159 change «standalone-valuation-gauge-cards»: what the symbol menu may offer.
    IChatCapabilityAvailability? capabilityAvailability = null,
    Feature154RoutingScope? routingScope = null) : ControllerBase
{
    [HttpGet("capabilities/guidance")]
    public ActionResult<IReadOnlyCollection<CapabilityStarterPromptHttpResponse>> GetCapabilityGuidance(
        [FromQuery] string language = "fa")
    {
        var safeLanguage = language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "fa";
        return Ok(guidanceService.StarterPrompts(safeLanguage)
            .Select(item => new CapabilityStarterPromptHttpResponse(
                item.CapabilityCode, item.LocalizedLabel, item.Example, item.RegistryVersion))
            .ToArray());
    }

    /// <summary>
    /// Feature 159 change «standalone-valuation-gauge-cards»: optional chat capabilities evaluated on the server, so the
    /// client shows a shortcut only when the same request would be answered (V2, flag on, CyclicalWaves source).
    /// </summary>
    [HttpGet("capabilities/availability")]
    public ActionResult<ChatCapabilityAvailabilityHttpResponse> GetCapabilityAvailability() =>
        Ok(new ChatCapabilityAvailabilityHttpResponse(
            ValuationGaugeCards: routingScope?.IsActive == true &&
                capabilityAvailability?.EvaluateForV2(CapabilityExecutionRoutes.ValuationGauges, ValuationGaugeCapability.Code).IsAvailable == true,
            OptionComparison: routingScope?.IsActive == true &&
                capabilityAvailability?.EvaluateForV2(CapabilityExecutionRoutes.OptionComparison, OptionComparisonCapability.Code).IsAvailable == true));

    [HttpPost("query")]
    public async Task<ActionResult<AiQueryHttpResponse>> Query(
        [FromBody] AiQueryHttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(httpRequest.Message))
        {
            ModelState.AddModelError(nameof(httpRequest.Message), "Message is required.");
            return ValidationProblem(ModelState);
        }

        if (httpRequest.IdempotencyKey == Guid.Empty)
        {
            ModelState.AddModelError(nameof(httpRequest.IdempotencyKey), "IdempotencyKey must be a non-empty GUID.");
            return ValidationProblem(ModelState);
        }

        if (!TryMapAnalysisOptions(httpRequest.AnalysisOptions, out var analysisOptions))
        {
            ModelState.AddModelError(nameof(httpRequest.AnalysisOptions), "AnalysisOptions.Depth must be 'simple' or 'deep'.");
            return ValidationProblem(ModelState);
        }

        var actor = actorContext.Actor;
        var correlationId = HttpContext.TraceIdentifier;
        // Keep the legacy optional field usable while returning the generated key so clients can
        // persist it for future retries. First-party chat clients send their stable key directly.
        var billingOperationId = httpRequest.IdempotencyKey ?? Guid.NewGuid();
        Response.Headers["Idempotency-Key"] = billingOperationId.ToString("D");

        var message = httpRequest.Message;
        if (httpRequest.Context?.AlertId is Guid alertId)
        {
            var alertContext = await alertHistory.BuildAiContextAsync(actor, alertId, cancellationToken);
            if (alertContext is null) return NotFound();
            message = $"{alertContext}\n\nUser question:\n{httpRequest.Message}";
        }

        AiQueryResponse result;
        try
        {
            result = await orchestrationService.ExecuteAsync(
                new AiQueryRequest(
                    message,
                    actor.TenantId,
                    actor.ActorId,
                    correlationId,
                    httpRequest.ConversationId,
                    actor.UserId,
                    actor.ApiClientId,
                    ScannerPage: Math.Max(1, httpRequest.ScannerPage),
                    ScannerPageSize: Math.Clamp(httpRequest.ScannerPageSize, 1, 100),
                    DisclosurePage: Math.Max(1, httpRequest.DisclosurePage),
                    ActorType: actor.ActorType,
                    AuthenticationMode: actor.AuthenticationMode,
                    Context: httpRequest.Context is null
                        ? null
                        : new AiQueryContext(httpRequest.Context.InsightEventId, httpRequest.Context.AlertId),
                    OriginalUserMessage: httpRequest.Message,
                    SuggestedActionId: httpRequest.SuggestedActionId,
                    BillingOperationId: billingOperationId,
                    AnalysisOptions: analysisOptions),
                cancellationToken);
        }
        catch (BillingIdempotencyConflictException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Idempotency key conflict",
                Detail = "This idempotency key has already been used with a different request payload."
            });
        }
        catch (ConversationNotFoundException)
        {
            return NotFound();
        }

        return Ok(MapQueryResponse(result));
    }

    [HttpPost("conversations")]
    public async Task<ActionResult<ConversationSummaryResponse>> CreateConversation(
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Actor;
        var conversationId = await conversationRepository.CreateEmptyAsync(
            actor.TenantId,
            actor.ActorId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        var summary = await conversationRepository.FindAsync(
            conversationId,
            actor.TenantId,
            actor.ActorId,
            cancellationToken);

        return CreatedAtAction(
            nameof(Conversation),
            new { conversationId },
            MapConversationSummary(summary!));
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyCollection<ConversationSummaryResponse>>> Conversations(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        var actor = actorContext.Actor;
        var conversations = await conversationRepository.ListByActorAsync(
            actor.TenantId,
            actor.ActorId,
            limit,
            cancellationToken);

        return Ok(conversations.Select(MapConversationSummary).ToList());
    }

    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<ActionResult<ConversationSummaryResponse>> Conversation(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Actor;
        var summary = await conversationRepository.FindAsync(
            conversationId,
            actor.TenantId,
            actor.ActorId,
            cancellationToken);

        if (summary is null)
        {
            return NotFound();
        }

        return Ok(MapConversationSummary(summary));
    }

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<ConversationDetailResponse>> Messages(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Actor;
        var summary = await conversationRepository.FindAsync(
            conversationId,
            actor.TenantId,
            actor.ActorId,
            cancellationToken);

        if (summary is null)
        {
            return NotFound();
        }

        var messages = await messageRepository.ListByConversationAsync(
            conversationId,
            cancellationToken);

        var records = messages.ToList();
        var mapped = records.Select(MapMessage).ToList();
        await AttachLatestAnalysisHintAsync(actor, conversationId, records, mapped, cancellationToken);

        return Ok(new ConversationDetailResponse(
            summary.ConversationId,
            summary.StartedAt,
            summary.UpdatedAt,
            mapped));
    }

    /// <summary>
    /// OQ-113 (a): a reloaded conversation shows the same analysis settings and 163 offer as the live answer. Only the
    /// latest assistant message carries the hint; the policy (stored availability, offer expiry, current prices and
    /// balance) is <see cref="GroundedAnalysisHintFactory.ForSavedAnswerAsync"/>.
    /// </summary>
    private async Task AttachLatestAnalysisHintAsync(
        CurrentActor actor,
        Guid conversationId,
        IReadOnlyList<MessageRecord> messages,
        List<MessageResponse> mapped,
        CancellationToken cancellationToken)
    {
        if (groundedAnalysisHints is not { Enabled: true }) return;
        var index = messages.Count - 1;
        while (index >= 0 && messages[index].Role != MessageRole.Assistant) index--;
        if (index < 0 || messages[index].AssistantPayload is not { } payload || mapped[index].AssistantContent is not { } content)
            return;

        var caller = new AiQueryRequest(
            string.Empty,
            actor.TenantId,
            actor.ActorId,
            HttpContext.TraceIdentifier,
            conversationId,
            actor.UserId,
            actor.ApiClientId,
            ActorType: actor.ActorType,
            AuthenticationMode: actor.AuthenticationMode);
        var hint = await groundedAnalysisHints.ForSavedAnswerAsync(
            caller, payload, (timeProvider ?? TimeProvider.System).GetUtcNow(), cancellationToken);
        if (hint is not null)
            mapped[index] = mapped[index] with { AssistantContent = content with { GroundedAnalysis = MapGroundedAnalysisHint(hint) } };
    }

    [HttpDelete("conversations/{conversationId:guid}")]
    public async Task<IActionResult> DeleteConversation(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Actor;
        var deleted = await conversationRepository.DeleteAsync(
            conversationId,
            actor.TenantId,
            actor.ActorId,
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    // Change «sol-deep-analysis-web-search» (FR-4): only the depth and web choice cross the boundary; never an amount.
    private static bool TryMapAnalysisOptions(AnalysisOptionsHttpRequest? options, out AnalysisOptionsRequest? mapped)
    {
        mapped = null;
        if (options is null) return true;
        AnalysisDepth? depth = null;
        if (!string.IsNullOrWhiteSpace(options.Depth))
        {
            if (string.Equals(options.Depth, "simple", StringComparison.OrdinalIgnoreCase)) depth = AnalysisDepth.Simple;
            else if (string.Equals(options.Depth, "deep", StringComparison.OrdinalIgnoreCase)) depth = AnalysisDepth.Deep;
            else return false;
        }

        mapped = new AnalysisOptionsRequest(
            depth,
            options.WebSearch,
            string.IsNullOrWhiteSpace(options.PricingVersion) ? null : options.PricingVersion.Trim());
        return true;
    }
}

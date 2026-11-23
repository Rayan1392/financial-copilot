using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

/// <summary>Feature 164: read-only, cross-user AI conversation logs for administrators.</summary>
[ApiController]
[Route("api/v1/admin/ai-conversation-logs")]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminAiConversationLogsController(
    ICurrentActorContext actorContext,
    IAdminConversationLogQueries queries) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersRead)]
    public async Task<ActionResult<AdminConversationLogPageHttpResponse>> GetPage(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminConversationLogQueryRules.DefaultPageSize,
        [FromQuery] string? q = null,
        [FromQuery] string? user = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        AdminConversationLogStatus? parsedStatus = null;
        if (status is not null)
        {
            parsedStatus = AdminConversationLogQueryRules.ParseStatus(status);
            if (parsedStatus is null) ModelState.AddModelError("status", "Status is not a known value.");
        }

        var query = new AdminConversationLogQuery(page, pageSize, q, user, fromUtc, toUtc, parsedStatus);
        foreach (var error in AdminConversationLogQueryRules.Validate(query))
            ModelState.AddModelError(error.Key, error.Value);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        try
        {
            var result = await queries.GetPageAsync(
                actorContext.Actor.TenantId,
                query with { Text = q?.Trim(), User = user?.Trim() },
                cancellationToken);
            return Ok(new AdminConversationLogPageHttpResponse(
                result.Items.Select(MapItem).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount,
                result.TotalPages));
        }
        catch (AdminConversationLogUserSearchTooBroadException)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = AdminConversationLogUserSearchTooBroadException.Code,
                Detail = $"The user filter matches more than {AdminConversationLogQueryRules.MaximumMatchedUsers} users. Narrow the search.",
                Type = $"urn:financial-copilot:problem:{AdminConversationLogUserSearchTooBroadException.Code}"
            };
            problem.Extensions["code"] = AdminConversationLogUserSearchTooBroadException.Code;
            problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
            return BadRequest(problem);
        }
    }

    [HttpGet("{turnId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersRead)]
    public async Task<ActionResult<AdminConversationLogTurnHttpResponse>> GetTurn(
        Guid turnId,
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Actor;
        var turn = await queries.GetTurnAsync(
            actor.TenantId,
            actor.ActorId,
            actor.ActorType.ToString(),
            HttpContext.TraceIdentifier,
            turnId,
            cancellationToken);
        if (turn is null) return NotFound();

        return Ok(new AdminConversationLogTurnHttpResponse(
            turn.TurnId,
            turn.ConversationId,
            turn.ConversationTitle,
            turn.Channel.ToString(),
            MapUser(turn.User),
            turn.Question,
            turn.QuestionCreatedAtUtc,
            turn.Status.ToString(),
            turn.Answers.Select(MapAnswer).ToArray()));
    }

    private static AdminConversationLogItemHttpResponse MapItem(AdminConversationLogItem item) =>
        new(
            item.TurnId,
            item.ConversationId,
            item.ConversationTitle,
            MapUser(item.User),
            item.QuestionPreview,
            item.AnswerPreview,
            item.QuestionCreatedAtUtc,
            item.AnswerCreatedAtUtc,
            item.Status.ToString(),
            item.Channel.ToString());

    private static AdminConversationLogUserHttpResponse MapUser(AdminConversationLogUser user) =>
        new(user.UserId, user.DisplayName, user.Email, user.MaskedMobile, user.HasUserAccount);

    private static AdminConversationLogAnswerHttpResponse MapAnswer(AdminConversationLogAnswer answer) =>
        new(
            answer.MessageId,
            answer.Content,
            answer.CreatedAtUtc,
            answer.Status.ToString(),
            answer.OutcomeReasonCode,
            answer.Usage is { } usage
                ? new AdminConversationLogUsageHttpResponse(
                    usage.ProviderName,
                    usage.ModelName,
                    usage.PromptTokens,
                    usage.CompletionTokens,
                    usage.TotalTokens,
                    usage.CreditsCharged,
                    usage.CompletionStatus,
                    usage.ExecutionRoute)
                : null,
            AiFacadeHttpMapper.MapAssistantContent(answer.Payload) is { } content
                ? content with { ScannerPlan = null, Usage = null, MemoryDisclosures = null }
                : null);
}

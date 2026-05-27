using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Middleware;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Scanner;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/ai/v1")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AiFacadeController(
    ICurrentActorContext actorContext,
    IAiQueryOrchestrationService orchestrationService,
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository) : ControllerBase
{
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

        var actor = actorContext.Actor;
        var correlationId = HttpContext.TraceIdentifier;

        var result = await orchestrationService.ExecuteAsync(
            new AiQueryRequest(
                httpRequest.Message,
                actor.TenantId,
                actor.ActorId,
                correlationId,
                httpRequest.ConversationId),
            cancellationToken);

        return Ok(MapQueryResponse(result));
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
            cancellationToken);

        if (summary is null)
        {
            return NotFound();
        }

        var messages = await messageRepository.ListByConversationAsync(
            conversationId,
            cancellationToken);

        return Ok(new ConversationDetailResponse(
            summary.ConversationId,
            summary.StartedAt,
            summary.UpdatedAt,
            messages.Select(MapMessage).ToList()));
    }

    private static AiQueryHttpResponse MapQueryResponse(AiQueryResponse result) =>
        new(
            result.ConversationId,
            result.MessageId,
            result.AssistantMessageId,
            result.Intent.ToString(),
            result.ClarificationRequired,
            result.ClarificationMessage,
            result.TextAnswer,
            result.ScannerPlan is null ? null : new ScannerPlanResponse(
                result.ScannerPlan.PlanId,
                result.ScannerPlan.Conditions.Count,
                result.ScannerPlan.ClarificationRequired,
                result.ScannerPlan.ClarificationMessage,
                result.ScannerPlan.ColumnOverflowWarnings),
            MapScannerTable(result.ScannerTable));

    private static ScannerTableResponse? MapScannerTable(ScannerTableResult? table)
    {
        if (table is null) return null;

        return new ScannerTableResponse(
            table.PlanId,
            table.Columns.Select(c => new ScannerTableColumnResponse(
                c.Identifier, c.DisplayName, c.ColumnType.ToString(), c.MetricCode)).ToList(),
            table.Rows.Select(r => new ScannerTableRowResponse(
                r.SymbolCode,
                r.CompanyName,
                r.Cells.ToDictionary(
                    kv => kv.Key,
                    kv => new ScannerTableCellResponse(
                        kv.Value.Value,
                        kv.Value.FormattedValue,
                        kv.Value.FreshnessStatus.ToString(),
                        kv.Value.SourceTimestamp)),
                r.Score,
                r.MatchedConditionMetrics)).ToList(),
            new ScannerExecutionFactsResponse(
                table.ExecutionFacts.ExecutedAt,
                table.ExecutionFacts.Duration,
                table.ExecutionFacts.TotalSymbolsEvaluated,
                table.ExecutionFacts.MatchingSymbolCount,
                table.ExecutionFacts.FromCache),
            table.MissingDataWarnings);
    }

    private static ConversationSummaryResponse MapConversationSummary(ConversationSummary summary) =>
        new(summary.ConversationId, summary.StartedAt, summary.UpdatedAt, summary.MessageCount);

    private static MessageResponse MapMessage(MessageRecord message) =>
        new(
            message.MessageId,
            message.Role.ToString(),
            message.Content,
            message.ScannerQueryPlanJson is not null,
            message.CreatedAt);
}

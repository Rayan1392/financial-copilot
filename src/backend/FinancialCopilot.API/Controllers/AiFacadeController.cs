using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Middleware;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Memory;
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

        AiQueryResponse result;
        try
        {
            result = await orchestrationService.ExecuteAsync(
                new AiQueryRequest(
                    httpRequest.Message,
                    actor.TenantId,
                    actor.ActorId,
                    correlationId,
                    httpRequest.ConversationId,
                    actor.UserId,
                    actor.ApiClientId,
                    ScannerPage: Math.Max(1, httpRequest.ScannerPage),
                    ScannerPageSize: Math.Clamp(httpRequest.ScannerPageSize, 1, 100)),
                cancellationToken);
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

        return Ok(new ConversationDetailResponse(
            summary.ConversationId,
            summary.StartedAt,
            summary.UpdatedAt,
            messages.Select(MapMessage).ToList()));
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
            MapScannerTable(result.ScannerTable),
            MapSymbolLookupTable(result.SymbolLookupTable),
            MapExplainableAnswer(result.ExplainableAnswer),
            MapConfidenceScore(ShouldSuppressSymbolLookupConfidence(result.SymbolLookupTable)
                ? null
                : result.ConfidenceScore),
            result.Usage is null ? null : new UsageAccountingResponse(
                result.Usage.OperationCode,
                result.Usage.CompletionStatus,
                result.Usage.CreditsCharged,
                result.Usage.RemainingSpendingCapacity,
                result.Usage.PricingPolicyVersion,
                result.Usage.Cached,
                result.Usage.ProviderName,
                result.Usage.ModelName,
                result.Usage.PromptTokens,
                result.Usage.CompletionTokens,
                result.Usage.TotalTokens,
                result.Usage.EstimatedCost),
            result.MemoryDisclosures?.Select(d => new MemoryDisclosureResponse(
                d.Type.ToString(), d.Purpose.ToString(), d.Explanation)).ToList(),
            result.AiOrchestrationMode,
            result.WorkflowVersion,
            result.ProviderSelection,
            result.ProviderFallbackOccurred,
            result.WorkflowCorrelationId,
            MapComprehensiveAnalysisResult(result.ComprehensiveAnalysisResult));

    private static ScannerTableResponse? MapSymbolLookupTable(SymbolLookupTableResult? table)
    {
        if (table is null) return null;
        if (table.Rows.Count == 0) return null;

        return new ScannerTableResponse(
            table.LookupId,
            table.Columns.Select(c => new ScannerTableColumnResponse(
                c.Identifier, c.DisplayName, c.ColumnType.ToString(), c.MetricCode)).ToList(),
            table.Rows.Select(r => new ScannerTableRowResponse(
                r.SymbolCode,
                r.CompanyName,
                MapCells(table.Columns, r),
                r.Score,
                r.MatchedConditionMetrics)).ToList(),
            new ScannerExecutionFactsResponse(
                table.ExecutionFacts.ExecutedAt,
                table.ExecutionFacts.Duration,
                table.ExecutionFacts.TotalSymbolsEvaluated,
                table.ExecutionFacts.MatchingSymbolCount,
                table.ExecutionFacts.FromCache,
                table.ExecutionFacts.Page,
                table.ExecutionFacts.PageSize,
                table.ExecutionFacts.TotalPages),
            table.MissingDataWarnings.Concat(
                table.UnresolvedSymbols.Select(s => $"Symbol '{s}' could not be resolved.")).ToList());
    }

    private static bool ShouldSuppressSymbolLookupConfidence(SymbolLookupTableResult? table) =>
        table is not null && table.Rows.Count == 0;

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
                MapCells(table.Columns, r),
                r.Score,
                r.MatchedConditionMetrics)).ToList(),
            new ScannerExecutionFactsResponse(
                table.ExecutionFacts.ExecutedAt,
                table.ExecutionFacts.Duration,
                table.ExecutionFacts.TotalSymbolsEvaluated,
                table.ExecutionFacts.MatchingSymbolCount,
                table.ExecutionFacts.FromCache,
                table.ExecutionFacts.Page,
                table.ExecutionFacts.PageSize,
                table.ExecutionFacts.TotalPages),
            table.MissingDataWarnings);
    }

    private static IReadOnlyDictionary<string, ScannerTableCellResponse> MapCells(
        IReadOnlyCollection<ScannerTableColumn> columns,
        ScannerTableRow row) =>
        columns.ToDictionary(
            column => column.Identifier,
            column =>
            {
                row.Cells.TryGetValue(column.Identifier, out var cell);
                cell = NormalizeDisplayCell(column, row, cell);
                return new ScannerTableCellResponse(
                    cell.Value,
                    cell.FormattedValue,
                    cell.FreshnessStatus.ToString(),
                    cell.SourceTimestamp,
                    cell.TradingDate,
                    cell.TradingDatePersian,
                    cell.SourceLabel);
            });

    private static ScannerTableCell NormalizeDisplayCell(
        ScannerTableColumn column,
        ScannerTableRow row,
        ScannerTableCell? cell)
    {
        var existing = cell ?? new ScannerTableCell(null, null, CellFreshnessStatus.Persisted, null);
        return column.ColumnType switch
        {
            ScannerColumnType.Symbol when string.IsNullOrWhiteSpace(existing.FormattedValue) =>
                existing with { FormattedValue = row.SymbolCode },

            ScannerColumnType.CompanyName when string.IsNullOrWhiteSpace(existing.FormattedValue) =>
                existing with { FormattedValue = row.CompanyName },

            _ => existing
        };
    }

    private static ExplainableAnswerResponse? MapExplainableAnswer(ExplainableAnswer? answer)
    {
        if (answer is null) return null;

        return new ExplainableAnswerResponse(
            answer.FilterChips.Select(chip => new ConditionFilterChipResponse(
                chip.MetricCode,
                chip.MetricDisplayName,
                chip.OperatorSymbol,
                chip.OperatorLabel,
                chip.Threshold,
                chip.ThresholdFormatted,
                chip.FilterOrigin,
                chip.IsInferred,
                chip.InferredReason)).ToList(),
            answer.MetricEvidence.Select(ev => new MetricEvidenceSummaryResponse(
                ev.MetricCode,
                ev.MetricVersion,
                ev.CalculationPolicyVersion,
                ev.MetricDisplayName,
                ev.Unit,
                ev.ActualValue,
                ev.FormattedValue,
                ev.PeriodType,
                ev.ObservedAt)).ToList(),
            answer.DataCitations.Select(c => new DataCitationResponse(
                c.SymbolCode,
                c.MetricCode,
                c.ObservedAt,
                c.FreshnessStatus,
                c.SourceProvider)).ToList(),
            new ConfidenceScoreResponse(
                answer.Confidence.Score,
                new ConfidenceFactorsResponse(
                    answer.Confidence.Factors.InterpretationCertainty,
                    answer.Confidence.Factors.EvidenceCompleteness,
                    answer.Confidence.Factors.SourceFreshness,
                    answer.Confidence.Factors.WarningPenalty),
                answer.Confidence.PolicyVersion),
            answer.SuggestedFollowUpQuestions,
            answer.ExplanationText);
    }

    private static ConversationSummaryResponse MapConversationSummary(ConversationSummary summary) =>
        new(summary.ConversationId, summary.StartedAt, summary.UpdatedAt, summary.MessageCount, summary.Title);

    private static MessageResponse MapMessage(MessageRecord message) =>
        new(
            message.MessageId,
            message.Role.ToString(),
            message.Content,
            message.ScannerQueryPlanJson is not null,
            message.CreatedAt,
            MapAssistantContent(message.AssistantPayload));

    private static AssistantMessageContentResponse? MapAssistantContent(AssistantMessagePayload? payload) =>
        payload is null
            ? null
            : new AssistantMessageContentResponse(
                payload.Version,
                payload.Intent.ToString(),
                payload.ClarificationRequired,
                payload.ClarificationMessage,
                payload.TextAnswer,
                payload.ScannerPlan is null ? null : new ScannerPlanResponse(
                    payload.ScannerPlan.PlanId,
                    payload.ScannerPlan.Conditions.Count,
                    payload.ScannerPlan.ClarificationRequired,
                    payload.ScannerPlan.ClarificationMessage,
                    payload.ScannerPlan.ColumnOverflowWarnings),
                MapScannerTable(payload.ScannerTable),
                MapSymbolLookupTable(payload.SymbolLookupTable),
                MapExplainableAnswer(payload.ExplainableAnswer),
                MapConfidenceScore(ShouldSuppressSymbolLookupConfidence(payload.SymbolLookupTable)
                    ? null
                    : payload.ConfidenceScore),
                payload.Usage is null ? null : new UsageAccountingResponse(
                    payload.Usage.OperationCode,
                    payload.Usage.CompletionStatus,
                    payload.Usage.CreditsCharged,
                    payload.Usage.RemainingSpendingCapacity,
                    payload.Usage.PricingPolicyVersion,
                    payload.Usage.Cached),
                payload.MemoryDisclosures?.Select(d => new MemoryDisclosureResponse(
                    d.Type.ToString(), d.Purpose.ToString(), d.Explanation)).ToList(),
                MapComprehensiveAnalysisResult(payload.ComprehensiveAnalysisResult));

    private static ComprehensiveAnalysisResultResponse? MapComprehensiveAnalysisResult(
        ComprehensiveAnalysisQueryResponse? result)
    {
        if (result is null) return null;

        return new ComprehensiveAnalysisResultResponse(
            result.Items.Select(i => new ComprehensiveAnalysisItemResponse(
                i.AnalysisId,
                i.Title,
                i.PersianCreatedAt,
                i.AuthorName,
                i.PlainTextSummary,
                i.TagNames,
                i.SyncedAt)).ToList(),
            result.UnresolvedSymbols,
            result.HasResults);
    }

    private static ConfidenceScoreResponse? MapConfidenceScore(ConfidenceScoreResult? confidence)
    {
        if (confidence is null) return null;

        return new ConfidenceScoreResponse(
            confidence.Score,
            new ConfidenceFactorsResponse(
                confidence.Factors.InterpretationCertainty,
                confidence.Factors.EvidenceCompleteness,
                confidence.Factors.SourceFreshness,
                confidence.Factors.WarningPenalty),
            confidence.PolicyVersion);
    }
}

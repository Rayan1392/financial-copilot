using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;

namespace FinancialCopilot.Application.AI.Evaluation;

public interface ISemanticOutcomeFeedbackCollector
{
    Task TryCollectAsync(
        AiQueryRequest request,
        ValidatedQueryFrame frame,
        CapabilityExecutionResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>
/// Central missing-answer side effect for every semantic executor. The persistence
/// implementation coalesces actor/query/classification/day replays, and failures are
/// deliberately isolated from the user response.
/// </summary>
public sealed class SemanticOutcomeFeedbackCollector(
    IMissingAnswerFeedbackCollector collector) : ISemanticOutcomeFeedbackCollector
{
    public async Task TryCollectAsync(
        AiQueryRequest request,
        ValidatedQueryFrame frame,
        CapabilityExecutionResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (result.Status == CapabilityExecutionStatus.Executed)
            return;

        var classification = result.Status switch
        {
            CapabilityExecutionStatus.NoData => MissingAnswerFeedbackClassification.DataCoverageGap,
            CapabilityExecutionStatus.ClarificationRequired or CapabilityExecutionStatus.DisambiguationRequired => MissingAnswerFeedbackClassification.ParserLimitation,
            CapabilityExecutionStatus.Unsupported => MissingAnswerFeedbackClassification.MetricGap,
            _ => MissingAnswerFeedbackClassification.UnknownGap
        };
        var metric = frame.Slots.FirstOrDefault(slot => slot.Type == QuerySlotType.Metric)?.Value;
        var symbol = frame.Slots.FirstOrDefault(slot => slot.Type == QuerySlotType.CompanyOrSymbol)?.Value;

        try
        {
            await collector.CollectAsync(new MissingAnswerFeedbackRequest(
                request.ActorId.ToString(),
                request.OriginalUserMessage ?? request.Message,
                classification,
                metric,
                symbol ?? frame.CapabilityCode,
                1,
                result.Status == CapabilityExecutionStatus.Executed ? 1 : 0,
                now,
                $"semantic:{frame.CapabilityCode}:{result.Status}:{result.ReasonCode}"), cancellationToken);
        }
        catch
        {
            // Feedback is observational and must never alter execution or latency policy.
        }
    }
}

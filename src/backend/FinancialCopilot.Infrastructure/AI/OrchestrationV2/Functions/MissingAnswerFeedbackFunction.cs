using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;

internal sealed class MissingAnswerFeedbackFunction(IMissingAnswerFeedbackCollector collector)
{
    internal async Task TryCollectAsync(
        AiQueryRequest request,
        SymbolLookupTableResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            var hasUnresolved = result.UnresolvedSymbols.Count > 0;
            var hasMissingCells = result.Rows.Any(
                r => r.Cells.Values.Any(c => c.FreshnessStatus == CellFreshnessStatus.Missing));

            if (!hasUnresolved && !hasMissingCells)
                return;

            foreach (var unresolved in result.UnresolvedSymbols)
            {
                await collector.CollectAsync(
                    new MissingAnswerFeedbackRequest(
                        ActorId: request.ActorId.ToString(),
                        QueryText: request.Message,
                        Classification: MissingAnswerFeedbackClassification.DataCoverageGap,
                        RequestedMetricCode: null,
                        AffectedDataCodeOrName: unresolved,
                        SymbolCountTotal: result.ExecutionFacts.TotalSymbolsEvaluated,
                        SymbolCountMatched: result.ExecutionFacts.MatchingSymbolCount,
                        SubmittedAt: now,
                        Context: $"SymbolLookup: symbol '{unresolved}' could not be resolved"),
                    cancellationToken);
            }
        }
        catch
        {
            // Feedback collection must never disturb the response.
        }
    }
}

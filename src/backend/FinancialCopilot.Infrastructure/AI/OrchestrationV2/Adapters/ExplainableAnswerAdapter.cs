using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

internal sealed class ExplainableAnswerAdapter(IExplainableAnswerBuilder builder)
{
    internal Task<ExplainableAnswer> BuildAsync(
        ScannerQueryPlan plan,
        ScannerTableResult table,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken) =>
        builder.BuildAsync(
            new ExplainableAnswerRequest(plan, table, tenantId, correlationId),
            cancellationToken);
}

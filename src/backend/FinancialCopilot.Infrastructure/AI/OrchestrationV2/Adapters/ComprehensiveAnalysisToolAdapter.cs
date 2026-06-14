using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

internal sealed class ComprehensiveAnalysisToolAdapter(
    IQueryComprehensiveAnalysisUseCase useCase)
{
    internal async Task<ComprehensiveAnalysisToolResult> QueryAsync(
        string? symbolName,
        string[]? topicTags,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = new ComprehensiveAnalysisQuery(symbolName, topicTags?.ToList(), limit);
        var result = await useCase.ExecuteAsync(query, cancellationToken);

        return result.Items.Count == 0
            ? ComprehensiveAnalysisToolResult.NotFound()
            : ComprehensiveAnalysisToolResult.Success(result.Items);
    }
}

internal sealed record ComprehensiveAnalysisToolResult
{
    public bool Succeeded { get; init; }
    public IReadOnlyList<ComprehensiveAnalysisSummary> Items { get; init; } = [];

    public string AgentSummary
    {
        get
        {
            if (!Succeeded) return "هیچ تحلیلی برای معیارهای درخواست‌شده یافت نشد.";

            var sb = new System.Text.StringBuilder();
            foreach (var item in Items)
            {
                sb.AppendLine($"### {item.Title}");
                sb.AppendLine($"تاریخ: {item.PersianCreatedAt} | نویسنده: {item.AuthorName}");
                sb.AppendLine(item.PlainTextSummary);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    public static ComprehensiveAnalysisToolResult Success(IReadOnlyList<ComprehensiveAnalysisSummary> items) =>
        new() { Succeeded = true, Items = items };

    public static ComprehensiveAnalysisToolResult NotFound() =>
        new() { Succeeded = false };
}

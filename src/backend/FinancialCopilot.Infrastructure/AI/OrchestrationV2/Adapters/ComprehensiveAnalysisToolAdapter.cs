using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

internal sealed class ComprehensiveAnalysisToolAdapter(
    IComprehensiveAnalysisQueryUseCase useCase)
{
    private static readonly IReadOnlyList<string> AllowedTopicSlugs = [
        "تحلیل_تکنیکال",
        "قیمت_تعادلی",
        "رصد_معاملات_عمده",
        "گزارش_فصلی",
        "گزارش_ماهانه",
        "نمودار_P_S",
        "نمودار_P_E",
    ];

    internal async Task<ComprehensiveAnalysisToolResult> QueryAsync(
        string[]? symbolNames,
        string[]? topicTags,
        string? fromDateIso,
        int limit,
        CancellationToken cancellationToken)
    {
        var clampedLimit = Math.Clamp(limit <= 0 ? 3 : limit, 1, 5);

        DateTimeOffset? fromDate = null;
        if (!string.IsNullOrWhiteSpace(fromDateIso) &&
            DateTimeOffset.TryParse(fromDateIso, out var parsedDate))
        {
            fromDate = parsedDate;
        }

        var validatedTopicTags = topicTags?
            .Where(t => AllowedTopicSlugs.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList() ?? [];

        var request = new ComprehensiveAnalysisQueryRequest(
            symbolNames?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [],
            validatedTopicTags,
            fromDate,
            clampedLimit);

        var result = await useCase.ExecuteAsync(request, cancellationToken);

        return result.Items.Count == 0
            ? ComprehensiveAnalysisToolResult.NotFound(result.UnresolvedSymbols)
            : ComprehensiveAnalysisToolResult.Success(result);
    }
}

internal sealed record ComprehensiveAnalysisToolResult
{
    public bool Succeeded { get; init; }
    public ComprehensiveAnalysisQueryResponse? QueryResponse { get; init; }
    public IReadOnlyList<string> UnresolvedSymbols { get; init; } = [];
    public string CompletionStatus { get; init; } = "Completed";

    public string AgentSummary
    {
        get
        {
            if (!Succeeded)
                return "هیچ تحلیلی برای معیارهای درخواست‌شده یافت نشد.";

            var sb = new System.Text.StringBuilder();
            foreach (var item in QueryResponse!.Items)
            {
                sb.AppendLine($"### {item.Title}");
                sb.AppendLine($"تاریخ: {item.PersianCreatedAt} | نویسنده: {item.AuthorName}");
                sb.AppendLine(item.PlainTextSummary);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    public static ComprehensiveAnalysisToolResult Success(ComprehensiveAnalysisQueryResponse response) =>
        new() { Succeeded = true, QueryResponse = response, UnresolvedSymbols = response.UnresolvedSymbols };

    public static ComprehensiveAnalysisToolResult NotFound(IReadOnlyList<string> unresolvedSymbols) =>
        new() { Succeeded = false, UnresolvedSymbols = unresolvedSymbols };
}

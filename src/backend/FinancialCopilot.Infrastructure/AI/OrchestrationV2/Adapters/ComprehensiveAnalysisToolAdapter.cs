using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

internal sealed class ComprehensiveAnalysisToolAdapter(
    IComprehensiveAnalysisQueryUseCase useCase,
    IOptions<ComprehensiveAnalysisPresentationOptions> presentation)
{
    /// <summary>The configured fixed TahlilApp analyses link (Feature 162).</summary>
    internal string AnalysesUrl => presentation.Value.TahlilAppAnalysesUrl;

    internal ComprehensiveAnalysisToolResult ToResult(ComprehensiveAnalysisQueryResponse response) =>
        ComprehensiveAnalysisToolResult.Success(response, AnalysesUrl);

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
            ? ComprehensiveAnalysisToolResult.NotFound(result.UnresolvedSymbols, AnalysesUrl)
            : ComprehensiveAnalysisToolResult.Success(result, AnalysesUrl);
    }
}

internal sealed record ComprehensiveAnalysisToolResult
{
    public bool Succeeded { get; init; }
    public ComprehensiveAnalysisQueryResponse? QueryResponse { get; init; }
    public IReadOnlyList<string> UnresolvedSymbols { get; init; } = [];
    public string CompletionStatus { get; init; } = "Completed";
    public string AnalysesUrl { get; init; } = ComprehensiveAnalysisPresentationOptions.DefaultTahlilAppAnalysesUrl;

    /// <summary>The Feature 162 card: titles and whole summaries, then the fixed TahlilApp link.</summary>
    public string AgentSummary => ComprehensiveAnalysisCardComposer.ComposeMarkdown(
        QueryResponse?.Items ?? [],
        UnresolvedSymbols,
        AnalysesUrl);

    public static ComprehensiveAnalysisToolResult Success(ComprehensiveAnalysisQueryResponse response, string analysesUrl) =>
        new() { Succeeded = true, QueryResponse = response, UnresolvedSymbols = response.UnresolvedSymbols, AnalysesUrl = analysesUrl };

    public static ComprehensiveAnalysisToolResult NotFound(IReadOnlyList<string> unresolvedSymbols, string analysesUrl) =>
        new() { Succeeded = false, UnresolvedSymbols = unresolvedSymbols, AnalysesUrl = analysesUrl };
}

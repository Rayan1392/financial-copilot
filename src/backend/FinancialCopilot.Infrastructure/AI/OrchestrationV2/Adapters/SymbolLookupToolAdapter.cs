using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

// Narrow adapter wrapping ISymbolLookupParser + ISymbolMetricLookupService.
// Only the raw user query and security context reach this adapter from the LLM closure;
// symbol resolution, metric code mapping, and data retrieval are fully deterministic.
internal sealed class SymbolLookupToolAdapter(
    ISymbolLookupParser parser,
    ISymbolMetricLookupService lookupService,
    TimeProvider timeProvider)
{
    internal async Task<SymbolLookupToolResult> LookupAsync(
        string userQuery,
        string correlationId,
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            return SymbolLookupToolResult.Clarification(
                "Please specify the symbol name and the metric you want to look up.");

        var now = timeProvider.GetUtcNow();
        var parseRequest = new SymbolLookupParseRequest(
            userQuery,
            "fa",
            correlationId,
            tenantId,
            DateOnly.FromDateTime(now.DateTime));

        var parseResult = await parser.ParseAsync(parseRequest, cancellationToken);

        if (parseResult.Status == LookupParseStatus.ClarificationRequired)
        {
            var msg = parseResult.ClarificationMessage ??
                (ContainsPersianText(userQuery)
                    ? "لطفاً نام نماد و معیار مالی موردنظر را مشخص کنید."
                    : "Please specify the symbol name and the metric you want to look up.");
            return SymbolLookupToolResult.Clarification(msg);
        }

        var lookupPairs = parseResult.Pairs
            .Where(p => p.ResolvedMetricCode is not null)
            .Select(p => (p.RawSymbolName, p.ResolvedMetricCode!))
            .ToList();

        var lookupRequest = new SymbolLookupRequest(
            lookupPairs,
            DateOnly.FromDateTime(now.DateTime),
            ActorId: actorId.ToString(),
            QueryText: userQuery);

        var table = await lookupService.LookupAsync(lookupRequest, cancellationToken);

        return SymbolLookupToolResult.Success(table);
    }

    private static bool ContainsPersianText(string text) =>
        text.Any(c => c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ');
}

internal sealed record SymbolLookupToolResult
{
    public bool Succeeded { get; init; }
    public bool ClarificationRequired { get; init; }
    public string? ClarificationMessage { get; init; }
    public SymbolLookupTableResult? Table { get; init; }
    public string CompletionStatus { get; init; } = "Completed";

    public string AgentSummary => ClarificationRequired
        ? $"Clarification needed: {ClarificationMessage}"
        : $"Found metric data for {Table?.ExecutionFacts.MatchingSymbolCount ?? 0} symbol(s)." +
          $"{(Table?.UnresolvedSymbols.Count > 0 ? $" {Table.UnresolvedSymbols.Count} unresolved." : string.Empty)}";

    public static SymbolLookupToolResult Success(SymbolLookupTableResult table) =>
        new() { Succeeded = true, Table = table };

    public static SymbolLookupToolResult Clarification(string message) =>
        new()
        {
            Succeeded = false,
            ClarificationRequired = true,
            ClarificationMessage = message,
            CompletionStatus = "ClarificationRequired"
        };
}

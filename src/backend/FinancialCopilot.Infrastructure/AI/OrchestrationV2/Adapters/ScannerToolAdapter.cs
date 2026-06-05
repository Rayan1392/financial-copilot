using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

// Narrow adapter wrapping IScannerQueryParser + IScannerExecutionService + IScannerCache.
// Only the user query, pagination intent, and security context are accepted from the LLM;
// the LLM never sees raw plan internals, SQL, or financial data beyond a row count summary.
internal sealed class ScannerToolAdapter(
    IScannerQueryParser parser,
    IScannerExecutionService executionService,
    IScannerCache cache,
    TimeProvider timeProvider)
{
    internal async Task<ScannerToolResult> SearchAsync(
        string userQuery,
        string correlationId,
        Guid tenantId,
        Guid actorId,
        Guid? apiClientId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            return ScannerToolResult.Clarification("Please specify your financial screening criteria.");

        var now = timeProvider.GetUtcNow();
        var cacheScope = new ScannerCacheScope(tenantId, actorId, apiClientId);
        var dataVersion = await cache.GetDataVersionAsync(cancellationToken);

        var parseRequest = new ScannerParseRequest(
            userQuery,
            "en",
            correlationId,
            tenantId,
            DateOnly.FromDateTime(now.DateTime));

        var parseResult =
            await cache.GetPlanAsync(cacheScope, dataVersion, parseRequest, cancellationToken)
            ?? await parser.ParseAsync(parseRequest, cancellationToken);

        if (!parseResult.Succeeded)
            return ScannerToolResult.Clarification(
                parseResult.FailureReason ?? "Unable to parse screening criteria.");

        if (!parseResult.Plan.ClarificationRequired)
        {
            await cache.SetPlanAsync(cacheScope, dataVersion, parseRequest, parseResult, cancellationToken);
        }

        if (parseResult.Plan.ClarificationRequired)
            return ScannerToolResult.Clarification(
                parseResult.Plan.ClarificationMessage ?? "Please clarify your criteria.");

        var executionRequest = new ScannerExecutionRequest(
            parseResult.Plan,
            DateOnly.FromDateTime(now.DateTime),
            Page: page,
            PageSize: pageSize,
            ActorId: actorId.ToString(),
            QueryText: userQuery);

        var cachedTable = await cache.GetResultAsync(
            cacheScope, dataVersion, executionRequest, cancellationToken);

        ScannerTableResult table;
        if (cachedTable is not null)
        {
            table = cachedTable with
            {
                ExecutionFacts = cachedTable.ExecutionFacts with { FromCache = true }
            };
        }
        else
        {
            table = await executionService.ExecuteAsync(executionRequest, cancellationToken);
            await cache.SetResultAsync(cacheScope, dataVersion, executionRequest, table, cancellationToken);
        }

        return ScannerToolResult.Success(parseResult.Plan, table, table.ExecutionFacts.FromCache);
    }
}

internal sealed record ScannerToolResult
{
    public bool Succeeded { get; init; }
    public bool ClarificationRequired { get; init; }
    public string? ClarificationMessage { get; init; }
    public ScannerQueryPlan? Plan { get; init; }
    public ScannerTableResult? Table { get; init; }
    public bool FromCache { get; init; }
    public string CompletionStatus { get; init; } = "Completed";

    public string AgentSummary => ClarificationRequired
        ? $"Clarification needed: {ClarificationMessage}"
        : $"Screener found {Table?.Rows.Count ?? 0} stock(s) matching {Plan?.Conditions.Count ?? 0} condition(s).{(FromCache ? " (cached)" : string.Empty)}";

    public static ScannerToolResult Success(
        ScannerQueryPlan plan, ScannerTableResult table, bool fromCache) =>
        new() { Succeeded = true, Plan = plan, Table = table, FromCache = fromCache };

    public static ScannerToolResult Clarification(string message) =>
        new()
        {
            Succeeded = false,
            ClarificationRequired = true,
            ClarificationMessage = message,
            CompletionStatus = "ClarificationRequired"
        };
}

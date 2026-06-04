using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.Application.Scanner;

// Raw output pair returned by the LLM before backend resolution.
public sealed record LlmLookupPairOutput(
    string SymbolName,
    string MetricTerm);

// Raw structured output from the LLM lookup parser call.
public sealed record LlmLookupParseOutput(
    string DetectedLanguage,
    IReadOnlyCollection<LlmLookupPairOutput> Pairs,
    bool ClarificationRequired,
    string? ClarificationMessage);

// A single pair after the backend resolves the metric term via IMetricAliasResolver.
// SymbolName is left raw; the lookup service resolves it to a SymbolCode.
public sealed record SymbolLookupParsedPair(
    string RawSymbolName,
    MetricCode? ResolvedMetricCode,
    string OriginalMetricTerm);

public enum LookupParseStatus
{
    Parsed,
    ClarificationRequired
}

public sealed record SymbolLookupParseResult(
    IReadOnlyCollection<SymbolLookupParsedPair> Pairs,
    LookupParseStatus Status,
    string? ClarificationMessage = null);

public sealed record SymbolLookupParseRequest(
    string Message,
    string Language,
    string CorrelationId,
    Guid TenantId,
    DateOnly AsOf);

// Input to ISymbolMetricLookupService.
// SymbolName is the raw user string; the lookup service resolves it to a SymbolCode.
public sealed record SymbolLookupRequest(
    IReadOnlyCollection<(string SymbolName, MetricCode MetricCode)> Pairs,
    DateOnly AsOf,
    string? ActorId = null,
    string? QueryText = null);

// Result re-uses ScannerTableColumn/Row/Cell contracts so the frontend renders it identically.
public sealed record SymbolLookupTableResult(
    Guid LookupId,
    IReadOnlyCollection<ScannerTableColumn> Columns,
    IReadOnlyCollection<ScannerTableRow> Rows,
    ScannerExecutionFacts ExecutionFacts,
    IReadOnlyCollection<string> MissingDataWarnings,
    IReadOnlyCollection<string> UnresolvedSymbols);

// Application-layer port implemented by Infrastructure.
public interface ISymbolNameResolver
{
    Task<SymbolCode?> ResolveAsync(string rawName, CancellationToken cancellationToken);
}

public interface ISymbolLookupParser
{
    Task<SymbolLookupParseResult> ParseAsync(
        SymbolLookupParseRequest request,
        CancellationToken cancellationToken);
}

public interface ISymbolMetricLookupService
{
    Task<SymbolLookupTableResult> LookupAsync(
        SymbolLookupRequest request,
        CancellationToken cancellationToken);
}

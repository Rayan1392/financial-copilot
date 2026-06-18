namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Resolved company record returned by <see cref="ICompanyResolverService"/>.
/// Contains only the fields that downstream consumers need; not an ORM row.
/// </summary>
public sealed record ResolvedCompany(
    Guid Id,
    string ExternalCompanyId,
    string? Ticker,
    string? EnTicker,
    string? InstrumentCode,
    string? SymbolIsin,
    string? CompanyIsin,
    string? TseSymbol = null,
    string? CompanySymbol = null);

/// <summary>
/// Resolves a company from a ticker symbol using a normalized, multi-step lookup.
/// Defined in the Application layer so use-case code can depend on it without
/// importing Infrastructure.
/// </summary>
public interface ICompanyResolverService
{
    /// <summary>
    /// Attempts to resolve <paramref name="symbol"/> to a canonical company record.
    /// Never throws — returns <c>null</c> when no match is found.
    /// </summary>
    Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default);
}

/// <summary>Summary result returned by <see cref="ICyclicalWavesCompanyMappingService.SyncMappingAsync"/>.</summary>
public sealed record CompanyMappingResult(int Matched, int Updated, int Skipped, int Unmatched);

/// <summary>
/// Syncs NADPCO Ticker/EnTicker values onto the <c>Companies</c> master table
/// (spec 067 TASK-005).
/// </summary>
public interface ICyclicalWavesCompanyMappingService
{
    Task<CompanyMappingResult> SyncMappingAsync(CancellationToken cancellationToken);
}

/// <summary>Result returned by <see cref="IBackfillCyclicalWavesCompanyIdService.RunAsync"/>.</summary>
public sealed record BackfillCompanyIdResult(int Resolved, int Unresolved);

/// <summary>
/// Backfills <c>CompanyId</c> on historical CyclicalWaves rows that predate the spec 067
/// normalizer wiring (spec 067 TASK-007).
/// </summary>
public interface IBackfillCyclicalWavesCompanyIdService
{
    Task<BackfillCompanyIdResult> RunAsync(CancellationToken cancellationToken);
}

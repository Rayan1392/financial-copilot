namespace FinancialCopilot.Application.FinancialData.Providers;

/// <summary>
/// Stable identifier kinds preferred when mapping the same issuer across archive and current Noavaran
/// sources (spec 051 Identity Resolution). Ordered most-to-least authoritative.
/// </summary>
public enum CanonicalIdentifierKind
{
    CoId,
    Isin,
    InstrumentCode,
    CompanySymbol,
    NormalizedSymbol
}

/// <summary>
/// A logged disagreement between two sources about a canonical company/security identity (AC #9):
/// the same issuer resolved differently, or two sources claim the same canonical id with conflicting
/// stable identifiers. Conflicts are recorded for review; they never silently overwrite canonical data
/// nor create duplicate canonical identities.
/// </summary>
public sealed record IdentityConflict(
    CanonicalIdentifierKind IdentifierKind,
    string IdentifierValue,
    string ExistingSourceName,
    string IncomingSourceName,
    string ExistingValue,
    string IncomingValue,
    string Detail);

/// <summary>
/// Records identity conflicts surfaced during cross-source normalization. Implementations isolate
/// failures (logging must never break an ingestion run) and bound/coalesce output to avoid log bloat.
/// </summary>
public interface IIdentityConflictLog
{
    Task RecordAsync(IdentityConflict conflict, CancellationToken cancellationToken);
}

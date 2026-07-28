using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.Domain.Financial.Services;

/// <summary>Outcome of canonical-symbol linkage: the resolved code (if any) and its basis.</summary>
public sealed record CanonicalSymbolResolution(SymbolCode? SymbolCode, SymbolLinkageBasis Basis);

public enum CanonicalSymbolLinkagePriority
{
    IsinFirst,
    InstrumentCodeFirst,
    TseSymbolFirst
}

/// <summary>
/// Domain policy that resolves a single canonical <see cref="SymbolCode"/> for an issuer from its
/// available identifiers, in a documented priority order, and records which identifier was used.
/// </summary>
/// <remarks>
/// The default order prefers ISIN so that the canonical code aligns with providers (e.g.
/// CyclicalWaves) whose symbol code is the share ISIN. When no ISIN is present the code still
/// resolves deterministically from the next-best identifier; callers can detect the absence of an
/// ISIN basis and emit a cross-provider alignment warning. Pure and stateless — no I/O.
/// </remarks>
public sealed class CanonicalSymbolLinkageResolver
{
    public CanonicalSymbolResolution Resolve(
        CompanyIdentifiers identifiers,
        CanonicalSymbolLinkagePriority priority = CanonicalSymbolLinkagePriority.IsinFirst)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        if (priority == CanonicalSymbolLinkagePriority.InstrumentCodeFirst &&
            identifiers.InstrumentCode is { } preferredInstrumentCode)
        {
            return new CanonicalSymbolResolution(
                new SymbolCode(preferredInstrumentCode),
                SymbolLinkageBasis.InstrumentCode);
        }

        if (priority == CanonicalSymbolLinkagePriority.TseSymbolFirst &&
            identifiers.TseSymbol is { } preferredTseSymbol)
        {
            return new CanonicalSymbolResolution(
                new SymbolCode(preferredTseSymbol),
                SymbolLinkageBasis.TseSymbol);
        }

        if (identifiers.SymbolIsin is { } symbolIsin)
        {
            return new CanonicalSymbolResolution(new SymbolCode(symbolIsin), SymbolLinkageBasis.SymbolIsin);
        }

        if (identifiers.CompanyIsin is { } companyIsin)
        {
            return new CanonicalSymbolResolution(new SymbolCode(companyIsin), SymbolLinkageBasis.CompanyIsin);
        }

        if (identifiers.InstrumentCode is { } instrumentCode)
        {
            return new CanonicalSymbolResolution(new SymbolCode(instrumentCode), SymbolLinkageBasis.InstrumentCode);
        }

        if (identifiers.TseSymbol is { } tseSymbol)
        {
            return new CanonicalSymbolResolution(new SymbolCode(tseSymbol), SymbolLinkageBasis.TseSymbol);
        }

        if (identifiers.CompanySymbol is { } companySymbol)
        {
            return new CanonicalSymbolResolution(new SymbolCode(companySymbol), SymbolLinkageBasis.CompanySymbol);
        }

        return new CanonicalSymbolResolution(null, SymbolLinkageBasis.None);
    }
}

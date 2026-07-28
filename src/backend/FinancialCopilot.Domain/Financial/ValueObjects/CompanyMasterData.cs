namespace FinancialCopilot.Domain.Financial.ValueObjects;

/// <summary>
/// Immutable set of provider-supplied identifiers for a single issuer. All identifiers are
/// optional; blanks are normalized to <c>null</c>. This intentionally excludes any
/// non-identifying placeholder (e.g. CodalDB's constant <c>InstrumentRef</c>).
/// </summary>
public sealed record CompanyIdentifiers
{
    public CompanyIdentifiers(
        string? companySymbol = null,
        string? tseSymbol = null,
        string? instrumentCode = null,
        string? companyIsin = null,
        string? symbolIsin = null)
    {
        CompanySymbol = Normalize(companySymbol);
        TseSymbol = Normalize(tseSymbol);
        InstrumentCode = Normalize(instrumentCode);
        CompanyIsin = Normalize(companyIsin);
        SymbolIsin = Normalize(symbolIsin);
    }

    /// <summary>Trading symbol (CodalDB <c>CompanySymbol</c>).</summary>
    public string? CompanySymbol { get; }

    /// <summary>TSE symbol (CodalDB <c>CoTSESymbol</c>) — best raw coverage.</summary>
    public string? TseSymbol { get; }

    /// <summary>TSETMC instrument code (CodalDB <c>InstCode</c>).</summary>
    public string? InstrumentCode { get; }

    /// <summary>Company ISIN (CodalDB <c>TseCIsinCode</c>).</summary>
    public string? CompanyIsin { get; }

    /// <summary>Symbol/share ISIN (CodalDB <c>TseSIsinCode</c>).</summary>
    public string? SymbolIsin { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Which identifier produced a canonical <see cref="SymbolCode"/>, recorded so the value is
/// reproducible and auditable, and so cross-provider alignment can be reasoned about.
/// </summary>
public enum SymbolLinkageBasis
{
    None,
    SymbolIsin,
    CompanyIsin,
    InstrumentCode,
    TseSymbol,
    CompanySymbol
}

/// <summary>Provider-supplied industry classification of a company.</summary>
public sealed record IndustryClassification
{
    public IndustryClassification(string sourceId, string name)
    {
        SourceId = ClassificationText.Require(sourceId, nameof(sourceId));
        Name = ClassificationText.Require(name, nameof(name));
    }

    public string SourceId { get; }

    public string Name { get; }
}

/// <summary>Provider-supplied super-sector group classification of a company.</summary>
public sealed record GroupClassification
{
    public GroupClassification(string sourceId, string name)
    {
        SourceId = ClassificationText.Require(sourceId, nameof(sourceId));
        Name = ClassificationText.Require(name, nameof(name));
    }

    public string SourceId { get; }

    public string Name { get; }
}

/// <summary>Provider-supplied market/board classification of a company.</summary>
public sealed record MarketClassification
{
    public MarketClassification(string sourceId, string name)
    {
        SourceId = ClassificationText.Require(sourceId, nameof(sourceId));
        Name = ClassificationText.Require(name, nameof(name));
    }

    public string SourceId { get; }

    public string Name { get; }
}

internal static class ClassificationText
{
    public static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Classification value is required.", parameterName)
            : value.Trim();
}

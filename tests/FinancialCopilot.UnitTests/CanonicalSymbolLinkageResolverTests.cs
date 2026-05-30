using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.UnitTests;

public sealed class CanonicalSymbolLinkageResolverTests
{
    private readonly CanonicalSymbolLinkageResolver _resolver = new();

    [Fact]
    public void Resolve_PrefersSymbolIsin_ToAlignWithCyclicalWavesEnticker()
    {
        var identifiers = new CompanyIdentifiers(
            companySymbol: "فولاد",
            tseSymbol: "فولاد",
            instrumentCode: "46348559193224090",
            companyIsin: "IRO1FOLD0006",
            symbolIsin: "IRO1FOLD0001");

        var result = _resolver.Resolve(identifiers);

        Assert.Equal(SymbolLinkageBasis.SymbolIsin, result.Basis);
        Assert.Equal("IRO1FOLD0001", result.SymbolCode!.Value);
    }

    [Fact]
    public void Resolve_FallsBackToCompanyIsin_WhenSymbolIsinMissing()
    {
        var identifiers = new CompanyIdentifiers(companyIsin: "IRO1FOLD0006", instrumentCode: "123");

        var result = _resolver.Resolve(identifiers);

        Assert.Equal(SymbolLinkageBasis.CompanyIsin, result.Basis);
        Assert.Equal("IRO1FOLD0006", result.SymbolCode!.Value);
    }

    [Fact]
    public void Resolve_FallsBackToInstrumentCode_WhenNoIsin()
    {
        var identifiers = new CompanyIdentifiers(tseSymbol: "فولاد", instrumentCode: "46348559193224090");

        var result = _resolver.Resolve(identifiers);

        Assert.Equal(SymbolLinkageBasis.InstrumentCode, result.Basis);
        Assert.Equal("46348559193224090", result.SymbolCode!.Value);
    }

    [Fact]
    public void Resolve_FallsBackToTseSymbol_ThenCompanySymbol()
    {
        var tseOnly = _resolver.Resolve(new CompanyIdentifiers(companySymbol: "X", tseSymbol: "TSE"));
        Assert.Equal(SymbolLinkageBasis.TseSymbol, tseOnly.Basis);
        Assert.Equal("TSE", tseOnly.SymbolCode!.Value);

        var companyOnly = _resolver.Resolve(new CompanyIdentifiers(companySymbol: "CS"));
        Assert.Equal(SymbolLinkageBasis.CompanySymbol, companyOnly.Basis);
        Assert.Equal("CS", companyOnly.SymbolCode!.Value);
    }

    [Fact]
    public void Resolve_NoIdentifiers_ReturnsNoneWithNullCode()
    {
        var result = _resolver.Resolve(new CompanyIdentifiers());

        Assert.Equal(SymbolLinkageBasis.None, result.Basis);
        Assert.Null(result.SymbolCode);
    }

    [Fact]
    public void Resolve_TreatsBlankIdentifiersAsAbsent()
    {
        var identifiers = new CompanyIdentifiers(symbolIsin: "   ", instrumentCode: "INST");

        var result = _resolver.Resolve(identifiers);

        Assert.Equal(SymbolLinkageBasis.InstrumentCode, result.Basis);
    }
}

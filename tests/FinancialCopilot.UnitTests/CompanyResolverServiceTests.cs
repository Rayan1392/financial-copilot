using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class CompanyResolverServiceTests
{
    private static FinancialIngestionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NormalizedCompanyRow MakeCompany(
        string externalId,
        string? ticker = null,
        string? enTicker = null,
        string? tseSymbol = null,
        string? symbolIsin = null,
        string? companyIsin = null,
        string? instrumentCode = null,
        string? companySymbol = null,
        string? name = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = "TestProvider",
            ExternalCompanyId = externalId,
            Name = name ?? externalId,
            LastSynchronizedAt = DateTimeOffset.UtcNow,
            Ticker = ticker,
            EnTicker = enTicker,
            TseSymbol = tseSymbol,
            SymbolIsin = symbolIsin,
            CompanyIsin = companyIsin,
            InstrumentCode = instrumentCode,
            CompanySymbol = companySymbol,
        };

    [Fact]
    public async Task ResolveBySymbolAsync_ExactPersianyTicker_Resolves()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany("100", ticker: "شغدیر"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("شغدیر");

        Assert.NotNull(result);
        Assert.Equal("100", result.ExternalCompanyId);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_ZwnjPollutedInput_ResolvesViaNormalization()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany("101", ticker: "شغدیر"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        // Polluted input with ZWNJ — should still resolve after normalization
        var result = await svc.ResolveBySymbolAsync("شغ‌دیر");

        Assert.NotNull(result);
        Assert.Equal("101", result.ExternalCompanyId);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_ByEnTicker_CaseInsensitive()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany("102", enTicker: "IRO7SHLP0001"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("iro7shlp0001");

        Assert.NotNull(result);
        Assert.Equal("102", result.ExternalCompanyId);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_BySymbolIsin_Resolves()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany("103", symbolIsin: "IRO7SHLP0001"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("IRO7SHLP0001");

        Assert.NotNull(result);
        Assert.Equal("103", result.ExternalCompanyId);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_ByInstrumentCode_Resolves()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany("104", instrumentCode: "7745894403636165"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("7745894403636165");

        Assert.NotNull(result);
        Assert.Equal("104", result.ExternalCompanyId);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_NoMatch_ReturnsNull()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany("105", ticker: "فولاد"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("NOTEXIST");

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_NoMatch_DoesNotThrow()
    {
        await using var db = CreateDbContext();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var ex = await Record.ExceptionAsync(() => svc.ResolveBySymbolAsync("unknown"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_EmptyString_ReturnsNull()
    {
        await using var db = CreateDbContext();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync(string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_TickerMatchBeforeEnTicker()
    {
        await using var db = CreateDbContext();
        // Two companies: one with Ticker, one with EnTicker for the same value
        var first = MakeCompany("200", ticker: "فولاد");
        var second = MakeCompany("201", enTicker: "فولاد");
        db.Companies.AddRange(first, second);
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("فولاد");

        // Ticker (step 1) should win over EnTicker (step 3)
        Assert.NotNull(result);
        Assert.Equal("200", result.ExternalCompanyId);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_ArabicYeInput_NormalizesBeforeMatch()
    {
        await using var db = CreateDbContext();
        // Company stored with Persian Ye
        db.Companies.Add(MakeCompany("300", ticker: "فولید"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        // Input uses Arabic Ye (U+064A)
        var result = await svc.ResolveBySymbolAsync("فوليد");

        Assert.NotNull(result);
        Assert.Equal("300", result.ExternalCompanyId);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_UnambiguousCompanyNameFragment_Resolves()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany(
            "3",
            ticker: "کچاد",
            tseSymbol: "کچاد",
            companySymbol: "کچاد",
            name: "معدنی و صنعتی چادرملو"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("چادرملو");

        Assert.NotNull(result);
        Assert.Equal("3", result.ExternalCompanyId);
        Assert.Equal("کچاد", result.TseSymbol);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_AmbiguousCompanyNameFragment_ReturnsNull()
    {
        await using var db = CreateDbContext();
        db.Companies.AddRange(
            MakeCompany("10", ticker: "الف", name: "شرکت نمونه چادرملو الف"),
            MakeCompany("11", ticker: "ب", name: "شرکت نمونه چادرملو ب"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("چادرملو");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("گل گهر")]
    [InlineData("گلگهر")]
    public async Task ResolveBySymbolAsync_CompanyNameSpacingVariant_Resolves(string input)
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany(
            "12",
            ticker: "کگل",
            tseSymbol: "کگل",
            companySymbol: "کگل",
            name: "معدنی و صنعتی گل گهر"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync(input);

        Assert.NotNull(result);
        Assert.Equal("12", result.ExternalCompanyId);
        Assert.Equal("کگل", result.TseSymbol);
    }

    [Fact]
    public async Task ResolveBySymbolAsync_CompanyNameIgnoresPunctuation_Resolves()
    {
        await using var db = CreateDbContext();
        db.Companies.Add(MakeCompany(
            "13",
            ticker: "فولاد",
            tseSymbol: "فولاد",
            companySymbol: "فولاد",
            name: "فولاد مبارکه اصفهان"));
        await db.SaveChangesAsync();

        var svc = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        var result = await svc.ResolveBySymbolAsync("فولاد-مبارکه، اصفهان");

        Assert.NotNull(result);
        Assert.Equal("13", result.ExternalCompanyId);
        Assert.Equal("فولاد", result.TseSymbol);
    }
}

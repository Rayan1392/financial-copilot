using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class SymbolNameResolverTests
{
    private static FinancialIngestionDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase($"symbol-resolver-{Guid.NewGuid():N}")
            .Options;
        return new FinancialIngestionDbContext(options);
    }

    private static EfCoreSymbolNameResolver BuildResolver(FinancialIngestionDbContext db) =>
        new(db, NullLogger<EfCoreSymbolNameResolver>.Instance);

    [Fact]
    public async Task ResolveAsync_ExactSymbolCodeMatch_ReturnsSymbolCode()
    {
        await using var db = CreateDb();
        SeedSymbol(db, "HAFARI", "Hafari Co", "EXT-001");
        db.SaveChanges();

        var resolver = BuildResolver(db);
        var result = await resolver.ResolveAsync("HAFARI", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("HAFARI", result.Value);
    }

    [Fact]
    public async Task ResolveAsync_CaseInsensitiveSymbolCode_ReturnsSymbolCode()
    {
        await using var db = CreateDb();
        SeedSymbol(db, "FMLCO", "Foulad Mobarakeh", "EXT-002");
        db.SaveChanges();

        var resolver = BuildResolver(db);
        var result = await resolver.ResolveAsync("fmlco", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("FMLCO", result.Value);
    }

    [Fact]
    public async Task ResolveAsync_CompanyNameSubstringMatch_ReturnsSymbolCode()
    {
        await using var db = CreateDb();
        SeedSymbol(db, "KEGEL", "Kaveh Moghavem Golestan", "EXT-003");
        db.SaveChanges();

        var resolver = BuildResolver(db);
        var result = await resolver.ResolveAsync("Kaveh Moghavem", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("KEGEL", result.Value);
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_ReturnsNull()
    {
        await using var db = CreateDb();
        SeedSymbol(db, "SOMESTOCK", "Some Company", "EXT-004");
        db.SaveChanges();

        var resolver = BuildResolver(db);
        var result = await resolver.ResolveAsync("CompanyThatDoesNotExist", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousNameMatch_ReturnsNull()
    {
        await using var db = CreateDb();
        SeedSymbol(db, "STOCK1", "Tehran Industries Alpha", "EXT-005");
        SeedSymbol(db, "STOCK2", "Tehran Industries Beta", "EXT-006");
        db.SaveChanges();

        var resolver = BuildResolver(db);
        var result = await resolver.ResolveAsync("Tehran Industries", CancellationToken.None);

        // Ambiguous — two companies match → return null
        Assert.Null(result);
    }

    private static void SeedSymbol(
        FinancialIngestionDbContext db,
        string symbolCode,
        string companyName,
        string externalCompanyId)
    {
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            Name = companyName,
            ProviderName = "test",
            ExternalCompanyId = externalCompanyId,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        db.Symbols.Add(new NormalizedSymbolRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = "test",
            ExternalSymbolId = externalCompanyId,
            SymbolCode = symbolCode,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
    }
}

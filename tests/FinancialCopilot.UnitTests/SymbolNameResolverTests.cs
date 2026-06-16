using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// EfCoreSymbolNameResolver was removed in spec 068 (Companies-First refactor).
/// Companies are now resolved directly via ICompanyResolverService using ExternalCompanyId.
/// These tests are replaced by CompanyResolverService tests.
/// </summary>
public sealed class SymbolNameResolverTests
{
    private static FinancialIngestionDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase($"company-resolver-{Guid.NewGuid():N}")
            .Options;
        return new FinancialIngestionDbContext(options);
    }

    [Fact]
    public async Task Companies_WithExternalCompanyId_CanBeQueriedByTseSymbol()
    {
        await using var db = CreateDb();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            Name = "Hafari Co",
            ProviderName = "test",
            ExternalCompanyId = "EXT-001",
            TseSymbol = "HAFARI",
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var company = await db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TseSymbol == "HAFARI");

        Assert.NotNull(company);
        Assert.Equal("EXT-001", company.ExternalCompanyId);
    }

    [Fact]
    public async Task Companies_ByExternalCompanyId_CanBeResolved()
    {
        await using var db = CreateDb();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            Name = "Chadormalu Mining",
            ProviderName = "test",
            ExternalCompanyId = "12345",
            TseSymbol = "KCHAD",
            CompanySymbol = "CHML",
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var company = await db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ExternalCompanyId == "12345");

        Assert.NotNull(company);
        Assert.Equal("KCHAD", company.TseSymbol);
        Assert.Equal("CHML", company.CompanySymbol);
    }
}

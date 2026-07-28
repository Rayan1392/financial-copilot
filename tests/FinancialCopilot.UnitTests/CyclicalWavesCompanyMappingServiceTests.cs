using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesCompanyMappingServiceTests
{
    private static FinancialIngestionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NormalizedCompanyRow MakeNadpcoCompany(
        string externalId,
        string? coSymbol = null,
        string? symbolIsin = null,
        int precedencyRight = 0,
        Guid? marketId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
            ExternalCompanyId = externalId,
            Name = externalId,
            LastSynchronizedAt = DateTimeOffset.UtcNow,
            CompanySymbol = coSymbol,
            SymbolIsin = symbolIsin,
            PrecedencyRight = precedencyRight,
            MarketId = marketId ?? NoavaranCompanyScope.BourseMarketId,
        };

    private static NormalizedCompanyRow MakeTargetCompany(
        string externalId,
        string? symbolIsin = null,
        string? ticker = null,
        string? enTicker = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = "CodalDb",
            ExternalCompanyId = externalId,
            Name = externalId,
            LastSynchronizedAt = DateTimeOffset.UtcNow,
            SymbolIsin = symbolIsin,
            Ticker = ticker,
            EnTicker = enTicker,
        };

    [Fact]
    public async Task SyncMappingAsync_PrimaryIsinMatch_UpdatesTickerAndEnTicker()
    {
        await using var db = CreateDbContext();
        var nadpco = MakeNadpcoCompany("1", coSymbol: "شغدیر", symbolIsin: "IRO7GHDR0001");
        var target = MakeTargetCompany("X1", symbolIsin: "IRO7GHDR0001");
        db.Companies.AddRange(nadpco, target);
        await db.SaveChangesAsync();

        var svc = new CyclicalWavesCompanyMappingService(db, NullLogger<CyclicalWavesCompanyMappingService>.Instance);
        var result = await svc.SyncMappingAsync(CancellationToken.None);

        await db.Entry(target).ReloadAsync();
        Assert.Equal("شغدیر", target.Ticker);
        Assert.Equal("IRO7GHDR0001", target.EnTicker);
        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Updated);
    }

    [Fact]
    public async Task SyncMappingAsync_TickerFallback_WhenIsinMissing()
    {
        await using var db = CreateDbContext();
        // NADPCO company has no SymbolIsin — only CoSymbol (Persian ticker)
        var nadpco = MakeNadpcoCompany("2", coSymbol: "فولاد", symbolIsin: null);
        // Target company already has Ticker set — this is the fallback scenario
        var target = MakeTargetCompany("X2", ticker: "فولاد");
        db.Companies.AddRange(nadpco, target);
        await db.SaveChangesAsync();

        var svc = new CyclicalWavesCompanyMappingService(db, NullLogger<CyclicalWavesCompanyMappingService>.Instance);
        var result = await svc.SyncMappingAsync(CancellationToken.None);

        // Target was found via ticker fallback (step 2). Ticker already populated → no update.
        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Updated); // Nothing to update: ticker already set, no EnTicker from NADPCO
    }

    [Fact]
    public async Task SyncMappingAsync_DoesNotOverwriteExistingTicker()
    {
        await using var db = CreateDbContext();
        var nadpco = MakeNadpcoCompany("3", coSymbol: "NewTicker", symbolIsin: "IRO7NEW0001");
        // Target already has Ticker populated — must not be overwritten
        var target = MakeTargetCompany("X3", symbolIsin: "IRO7NEW0001", ticker: "OldTicker");
        db.Companies.AddRange(nadpco, target);
        await db.SaveChangesAsync();

        var svc = new CyclicalWavesCompanyMappingService(db, NullLogger<CyclicalWavesCompanyMappingService>.Instance);
        await svc.SyncMappingAsync(CancellationToken.None);

        await db.Entry(target).ReloadAsync();
        Assert.Equal("OldTicker", target.Ticker); // must not be overwritten
        Assert.Equal("IRO7NEW0001", target.EnTicker); // EnTicker was null → gets populated
    }

    [Fact]
    public async Task SyncMappingAsync_NoMatch_IncrementsUnmatched()
    {
        await using var db = CreateDbContext();
        var nadpco = MakeNadpcoCompany("4", coSymbol: "نامعلوم", symbolIsin: "IRO7UNKN0001");
        // Target has a completely different SymbolIsin
        var target = MakeTargetCompany("X4", symbolIsin: "IRO7OTHER0001");
        db.Companies.AddRange(nadpco, target);
        await db.SaveChangesAsync();

        var svc = new CyclicalWavesCompanyMappingService(db, NullLogger<CyclicalWavesCompanyMappingService>.Instance);
        var result = await svc.SyncMappingAsync(CancellationToken.None);

        Assert.Equal(0, result.Matched);
        Assert.Equal(1, result.Unmatched);
    }

    [Fact]
    public async Task SyncMappingAsync_Idempotent_SecondRunProducesZeroUpdates()
    {
        await using var db = CreateDbContext();
        var nadpco = MakeNadpcoCompany("5", coSymbol: "فملی", symbolIsin: "IRO7FMLI0001");
        var target = MakeTargetCompany("X5", symbolIsin: "IRO7FMLI0001");
        db.Companies.AddRange(nadpco, target);
        await db.SaveChangesAsync();

        var svc = new CyclicalWavesCompanyMappingService(db, NullLogger<CyclicalWavesCompanyMappingService>.Instance);
        var first = await svc.SyncMappingAsync(CancellationToken.None);
        var second = await svc.SyncMappingAsync(CancellationToken.None);

        Assert.Equal(1, first.Updated);
        Assert.Equal(0, second.Updated); // already populated; nothing to overwrite
    }

    [Fact]
    public async Task SyncMappingAsync_PrecedencyRightNonZero_ExcludedFromCatalog()
    {
        await using var db = CreateDbContext();
        // PrecedencyRight = 1 means حق تقدم (rights) — must be excluded from eligible scope
        var nadpco = MakeNadpcoCompany("6", coSymbol: "حقوق", symbolIsin: "IRO7HQOQ0001", precedencyRight: 1);
        var target = MakeTargetCompany("X6", symbolIsin: "IRO7HQOQ0001");
        db.Companies.AddRange(nadpco, target);
        await db.SaveChangesAsync();

        var svc = new CyclicalWavesCompanyMappingService(db, NullLogger<CyclicalWavesCompanyMappingService>.Instance);
        var result = await svc.SyncMappingAsync(CancellationToken.None);

        // The NADPCO company is ineligible → not iterated → target untouched
        Assert.Equal(0, result.Matched);
        await db.Entry(target).ReloadAsync();
        Assert.Null(target.Ticker);
    }
}

using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Spec 051 — archive and current Noavaran rows coexist under one logical vendor without duplicating
/// the canonical company identity, and batch-level provenance is persisted per sync run. The same
/// issuer (CoID 1001) appears once per physical source (each row keyed by
/// (ProviderName, ExternalCompanyId)) while both map to the same logical vendor and resolve to the
/// same canonical symbol — and scanner-shaped reads work off the canonical symbol, not the source name.
/// </summary>
public sealed class NoavaranSourceCoexistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-08T09:00:00Z");

    [Fact]
    public async Task ArchiveAndCurrentCompanyRows_CoexistUnderOneVendor_WithoutDuplicateCanonicalSymbol()
    {
        await using var db = CreateIngestionDbContext();

        // Same issuer ingested from both physical sources of the Noavaran Amin vendor.
        SeedCompanyWithSymbol(db, ProviderSources.NoavaranArchiveSqlName, SourceMode.ArchiveOneTime,
            externalCompanyId: "1001", canonicalSymbol: "IRO1FOLD0001");
        SeedCompanyWithSymbol(db, ProviderSources.NoavaranCurrentApiName, SourceMode.CurrentIncremental,
            externalCompanyId: "1001", canonicalSymbol: "IRO1FOLD0001");
        await db.SaveChangesAsync();

        var companies = await db.Companies.ToListAsync();
        Assert.Equal(2, companies.Count);
        // Both rows belong to one logical vendor; the distinction is the physical source / mode.
        Assert.All(companies, c => Assert.Equal(LogicalVendor.NoavaranAmin.ToString(), c.LogicalVendor));
        Assert.Contains(companies, c => c.SourceMode == SourceMode.ArchiveOneTime.ToString());
        Assert.Contains(companies, c => c.SourceMode == SourceMode.CurrentIncremental.ToString());

        // No duplicate canonical identity: both sources resolve to one canonical symbol code.
        var canonicalSymbols = await db.Symbols.Select(s => s.SymbolCode).Distinct().ToListAsync();
        Assert.Single(canonicalSymbols);
        Assert.Equal("IRO1FOLD0001", canonicalSymbols[0]);

        // A canonical (source-agnostic) read finds the issuer regardless of physical source.
        var symbolCount = await db.Symbols.CountAsync(s => s.SymbolCode == "IRO1FOLD0001");
        Assert.Equal(2, symbolCount); // one symbol row per source, same canonical code — read joins on code, not source
    }

    [Fact]
    public async Task SyncRun_PersistsBatchLevelProvenance_DerivedFromTheSourceName()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();

        var payload = new ProviderRawPayload(
            Guid.NewGuid(), ProviderSources.NoavaranArchiveSqlName, ProviderDataset.Symbols,
            "codaldb://companies", "all", "[]", "empty-checksum", Now);
        var provider = new StubSymbolProvider(payload);
        var router = new FinancialDataProviderRouter(
            new Dictionary<string, ISymbolDataProvider> { [ProviderSources.NoavaranArchiveSqlName] = provider },
            new Dictionary<string, IFinancialStatementProvider>(),
            new Dictionary<string, IMonthlyProductionSalesProvider>());

        var processor = new FinancialDataSyncProcessor(
            ingestionDb,
            new ProviderRawPayloadStore(providerDb),
            provider, provider, provider,
            [new EmptyNormalizer(ProviderSources.NoavaranArchiveSqlName)],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(Now),
            NullLogger<FinancialDataSyncProcessor>.Instance,
            providerRouter: router);

        var result = await processor.ProcessAsync(
            new DataSyncRequest(Guid.NewGuid(), ProviderDataset.Symbols, null, Now, "archive-provenance",
                ProviderName: ProviderSources.NoavaranArchiveSqlName,
                SourceDateRangeStartJalali: "1399/01/01", SourceDateRangeEndJalali: "1402/12/29"),
            CancellationToken.None);

        Assert.Equal(DataSyncRunStatus.Completed, result.Run.Status);

        var runRow = await ingestionDb.SyncRuns.SingleAsync();
        Assert.Equal(LogicalVendor.NoavaranAmin.ToString(), runRow.LogicalVendor);
        Assert.Equal(PhysicalSource.NoavaranArchiveSql.ToString(), runRow.PhysicalSource);
        Assert.Equal(SourceMode.ArchiveOneTime.ToString(), runRow.SourceMode);
        Assert.Equal("1399/01/01", runRow.SourceDateRangeStartJalali);
        Assert.Equal("1402/12/29", runRow.SourceDateRangeEndJalali);
    }

    private static void SeedCompanyWithSymbol(
        FinancialIngestionDbContext db,
        string sourceName,
        SourceMode mode,
        string externalCompanyId,
        string canonicalSymbol)
    {
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = sourceName,
            ExternalCompanyId = externalCompanyId,
            Name = "فولاد مبارکه",
            LogicalVendor = LogicalVendor.NoavaranAmin.ToString(),
            SourceMode = mode.ToString(),
            LastSynchronizedAt = Now
        });
        db.Symbols.Add(new NormalizedSymbolRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = sourceName,
            ExternalSymbolId = externalCompanyId,
            SymbolCode = canonicalSymbol,
            LinkageBasis = "SymbolIsin",
            LastSynchronizedAt = Now
        });
    }

    private static FinancialProviderDbContext CreateProviderDbContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static FinancialIngestionDbContext CreateIngestionDbContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class StubSymbolProvider(ProviderRawPayload payload)
        : ISymbolDataProvider, IFinancialStatementProvider, IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(payload);

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult(payload);

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult(payload);
    }

    private sealed class EmptyNormalizer(string sourceName) : IFinancialPayloadNormalizer
    {
        public string ProviderName => sourceName;
        public ProviderDataset Dataset => ProviderDataset.Symbols;
        public Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken) =>
            Task.FromResult(new NormalizationOutcome(0));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

public sealed class FinancialDataIngestionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-26T09:00:00Z");

    [Fact]
    public async Task Processor_NormalizesAllSupportedDatasetsAndQueuesRecalculationRequests()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();
        var cache = new TrackingScannerCache();
        var processor = CreateProcessor(providerDb, ingestionDb, cache);

        var symbolResult = await processor.ProcessAsync(
            Request(ProviderDataset.Symbols, null, "symbols-v1"),
            CancellationToken.None);
        var statementResult = await processor.ProcessAsync(
            Request(ProviderDataset.FinancialStatements, "company-live", "statements-company-live-v1"),
            CancellationToken.None);
        var monthlyResult = await processor.ProcessAsync(
            Request(ProviderDataset.MonthlyProductionSales, "company-live", "monthly-company-live-v1"),
            CancellationToken.None);

        Assert.Equal(DataSyncRunStatus.Completed, symbolResult.Run.Status);
        Assert.Equal(2, symbolResult.Run.ProcessedRecords);
        Assert.Equal(DataSyncRunStatus.Completed, statementResult.Run.Status);
        Assert.Equal(DataSyncRunStatus.Completed, monthlyResult.Run.Status);
        Assert.Equal(2, await ingestionDb.Symbols.CountAsync());
        Assert.Single(await ingestionDb.FinancialStatements.ToListAsync());
        Assert.Single(await ingestionDb.MonthlyReports.ToListAsync());
        Assert.Equal(3, await ingestionDb.SyncRuns.CountAsync());
        Assert.Equal(3, await ingestionDb.MetricRecalculationRequests.CountAsync());
        Assert.Equal(3, await providerDb.ProviderRawPayloads.CountAsync());
        Assert.Equal(3, cache.Invalidations.Count);
        Assert.Contains(cache.Invalidations, item => item.Reason == "DataSync.Symbols");
        Assert.Contains(cache.Invalidations, item => item.Reason == "DataSync.FinancialStatements");
        Assert.Contains(cache.Invalidations, item => item.Reason == "DataSync.MonthlyProductionSales");
    }

    [Fact]
    public async Task Processor_RepeatedCompletedRequestDoesNotDuplicateNormalizedDataOrRecalculation()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();
        var processor = CreateProcessor(providerDb, ingestionDb);
        var request = Request(
            ProviderDataset.MonthlyProductionSales,
            "company-fallback",
            "monthly-company-fallback-v1");

        var first = await processor.ProcessAsync(request, CancellationToken.None);
        var repeated = await processor.ProcessAsync(request, CancellationToken.None);

        Assert.False(first.AlreadyProcessed);
        Assert.True(repeated.AlreadyProcessed);
        Assert.Single(await ingestionDb.MonthlyReports.ToListAsync());
        Assert.Single(await ingestionDb.MonthlyReportLineItems.ToListAsync());
        Assert.Single(await ingestionDb.SyncRuns.ToListAsync());
        Assert.Single(await ingestionDb.MetricRecalculationRequests.ToListAsync());
        Assert.Single(await providerDb.ProviderRawPayloads.ToListAsync());
    }

    [Fact]
    public async Task Processor_PersistsRawPayloadAndFailedRunWhenNormalizationFails()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();
        var provider = CreateProvider(providerDb);
        var processor = new FinancialDataSyncProcessor(
            ingestionDb,
            new ProviderRawPayloadStore(providerDb),
            provider,
            provider,
            provider,
            [new FailingStatementNormalizer()],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(Now),
            NullLogger<FinancialDataSyncProcessor>.Instance);

        var result = await processor.ProcessAsync(
            Request(ProviderDataset.FinancialStatements, "company-live", "failing-statement-v1"),
            CancellationToken.None);

        Assert.Equal(DataSyncRunStatus.Failed, result.Run.Status);
        Assert.Equal(1, result.Run.ErrorCount);
        Assert.Contains("normalization failed", result.Run.ErrorMessage);
        Assert.Single(await providerDb.ProviderRawPayloads.ToListAsync());
        Assert.Single(await ingestionDb.SyncRuns.ToListAsync());
        Assert.Empty(await ingestionDb.MetricRecalculationRequests.ToListAsync());
    }

    [Fact]
    public async Task SyncRunReader_ReturnsPersistedRunsInMostRecentRequestOrder()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();
        var processor = CreateProcessor(providerDb, ingestionDb);

        await processor.ProcessAsync(
            Request(ProviderDataset.Symbols, null, "oldest", Now.AddMinutes(-1)),
            CancellationToken.None);
        await processor.ProcessAsync(
            Request(ProviderDataset.FinancialStatements, "company-live", "newest", Now),
            CancellationToken.None);

        var runs = await processor.QueryRecentAsync(1, CancellationToken.None);

        Assert.Single(runs);
        Assert.Equal("newest", runs.Single().IdempotencyKey);
        Assert.Equal(DataSyncRunStatus.Completed, runs.Single().Status);
    }

    [Fact]
    public async Task Processor_RoutesCodalDbSymbolsPayload_EnrichesCompanyMasterDataAndDimensions()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();

        var payload = new ProviderRawPayload(
            Guid.NewGuid(),
            "CodalDb",
            ProviderDataset.Symbols,
            "codaldb://companies",
            "all",
            CodalDbCompaniesJson,
            "codaldb-symbols-checksum",
            Now);
        var provider = new StubCodalDbSymbolProvider(payload);

        var processor = new FinancialDataSyncProcessor(
            ingestionDb,
            new ProviderRawPayloadStore(providerDb),
            provider,
            provider,
            provider,
            [new CodalDbSymbolNormalizer(
                ingestionDb,
                new CanonicalSymbolLinkageResolver(),
                NullLogger<CodalDbSymbolNormalizer>.Instance)],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(Now),
            NullLogger<FinancialDataSyncProcessor>.Instance);

        var result = await processor.ProcessAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.Symbols,
                ExternalReference: null,
                Now,
                "codaldb-symbols-v1",
                ProviderName: "CodalDb"),
            CancellationToken.None);

        Assert.Equal(DataSyncRunStatus.Completed, result.Run.Status);
        Assert.Equal("CodalDb", result.Run.ProviderName);

        var company = await ingestionDb.Companies.SingleAsync(c => c.ProviderName == "CodalDb");
        Assert.Equal("Mobarakeh Steel", company.NameEnglish);
        Assert.Equal("IRO1FOLD0006", company.CompanyIsin);
        Assert.NotNull(company.IndustryId);
        Assert.NotNull(company.GroupId);
        Assert.NotNull(company.MarketId);

        Assert.Equal(1, await ingestionDb.Industries.CountAsync());
        Assert.Equal(1, await ingestionDb.IndustryGroups.CountAsync());
        Assert.Equal(1, await ingestionDb.Markets.CountAsync());

        var symbol = await ingestionDb.Symbols.SingleAsync(s => s.ProviderName == "CodalDb");
        Assert.Equal("IRO1FOLD0001", symbol.SymbolCode);
        Assert.Equal("SymbolIsin", symbol.LinkageBasis);

        Assert.Equal(1, await ingestionDb.MetricRecalculationRequests.CountAsync());
    }

    [Fact]
    public async Task Processor_RoutesToNamedProviderViaRouter_OverridingDefault()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();

        var codalPayload = new ProviderRawPayload(
            Guid.NewGuid(),
            "CodalDb",
            ProviderDataset.Symbols,
            "codaldb://companies",
            "all",
            CodalDbCompaniesJson,
            "codaldb-router-checksum",
            Now);
        var codalProvider = new StubCodalDbSymbolProvider(codalPayload);
        var defaultProvider = new ThrowingSymbolProvider();
        var router = new FinancialDataProviderRouter(
            new Dictionary<string, ISymbolDataProvider> { ["CodalDb"] = codalProvider },
            new Dictionary<string, IFinancialStatementProvider>(),
            new Dictionary<string, IMonthlyProductionSalesProvider>());

        var processor = new FinancialDataSyncProcessor(
            ingestionDb,
            new ProviderRawPayloadStore(providerDb),
            defaultProvider,
            defaultProvider,
            defaultProvider,
            [new CodalDbSymbolNormalizer(
                ingestionDb,
                new CanonicalSymbolLinkageResolver(),
                NullLogger<CodalDbSymbolNormalizer>.Instance)],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(Now),
            NullLogger<FinancialDataSyncProcessor>.Instance,
            scannerCache: null,
            providerRouter: router);

        var result = await processor.ProcessAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.Symbols,
                ExternalReference: null,
                Now,
                "codaldb-router-v1",
                ProviderName: "CodalDb"),
            CancellationToken.None);

        Assert.Equal(DataSyncRunStatus.Completed, result.Run.Status);
        Assert.Equal(1, await ingestionDb.Companies.CountAsync(c => c.ProviderName == "CodalDb"));
    }

    private const string CodalDbCompaniesJson = """
        [
          {
            "CoID": 1001,
            "CoName": "فولاد مبارکه",
            "CoNameEnglish": "Mobarakeh Steel",
            "CompanySymbol": "فولاد",
            "CoTSESymbol": "فولاد",
            "GroupID": 27,
            "GroupName": "فلزات اساسی",
            "IndustryID": 270,
            "IndustryName": "فلزات اساسی",
            "InstCode": "46348559193224090",
            "TseCIsinCode": "IRO1FOLD0006",
            "TseSIsinCode": "IRO1FOLD0001",
            "MarketID": 1,
            "MarketName": "بورس",
            "InstrumentRef": "9455D05D-0000-0000-0000-000000000000",
            "ModifiedDateTime": "2026-01-15T10:00:00Z"
          }
        ]
        """;

    private static FinancialDataSyncProcessor CreateProcessor(
        FinancialProviderDbContext providerDb,
        FinancialIngestionDbContext ingestionDb,
        IScannerCache? scannerCache = null)
    {
        var provider = CreateProvider(providerDb);

        return new FinancialDataSyncProcessor(
            ingestionDb,
            new ProviderRawPayloadStore(providerDb),
            provider,
            provider,
            provider,
            [
                new SymbolPayloadNormalizer(ingestionDb),
                new FinancialStatementPayloadNormalizer(ingestionDb),
                new MonthlyReportPayloadNormalizer(ingestionDb)
            ],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(Now),
            NullLogger<FinancialDataSyncProcessor>.Instance,
            scannerCache);
    }

    private static MockFinancialDataProvider CreateProvider(FinancialProviderDbContext dbContext) =>
        new(new ProviderRawPayloadStore(dbContext), new FixedTimeProvider(Now));

    private static DataSyncRequest Request(
        ProviderDataset dataset,
        string? externalReference,
        string idempotencyKey,
        DateTimeOffset? requestedAt = null) =>
        new(Guid.NewGuid(), dataset, externalReference, requestedAt ?? Now, idempotencyKey);

    private static FinancialProviderDbContext CreateProviderDbContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FinancialIngestionDbContext CreateIngestionDbContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FailingStatementNormalizer : IFinancialPayloadNormalizer
    {
        public string ProviderName => "ConfiguredFinancialProvider";

        public ProviderDataset Dataset => ProviderDataset.FinancialStatements;

        public Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("normalization failed");
    }

    private sealed class StubCodalDbSymbolProvider(ProviderRawPayload payload) :
        ISymbolDataProvider, IFinancialStatementProvider, IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(payload);

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // Default provider that must NOT be invoked when routing selects a named provider.
    private sealed class ThrowingSymbolProvider :
        ISymbolDataProvider, IFinancialStatementProvider, IMonthlyProductionSalesProvider
    {
        public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Default provider should not be used when routed.");

        public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Default provider should not be used when routed.");

        public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Default provider should not be used when routed.");
    }
}

using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
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
        var processor = CreateProcessor(providerDb, ingestionDb);

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

    private static FinancialDataSyncProcessor CreateProcessor(
        FinancialProviderDbContext providerDb,
        FinancialIngestionDbContext ingestionDb)
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
            NullLogger<FinancialDataSyncProcessor>.Instance);
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
        public ProviderDataset Dataset => ProviderDataset.FinancialStatements;

        public Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("normalization failed");
    }
}

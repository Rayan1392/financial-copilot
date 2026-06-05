using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class NadpcoApiScheduledSyncServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-03T10:00:00Z");

    [Fact]
    public async Task FullSync_EnqueuesSymbolsAndCompanyScopedDatasets()
    {
        await using var db = CreateDb();
        SeedCompanies(db, 3, 4);
        var publisher = new RecordingPublisher();
        var service = NewService(db, publisher);

        var result = await service.ExecuteAsync(fullReload: true, CancellationToken.None);

        Assert.True(result.FullReload);
        Assert.Equal(NadpcoApiSyncRunMode.FullSync, result.RunMode);
        Assert.Equal(2, result.CompaniesConsidered);
        Assert.Equal(2, result.CompaniesEnqueued);
        Assert.Equal(1 + 2 * 3, result.RequestsEnqueued);
        Assert.Contains(publisher.Requests, request => request.Dataset == ProviderDataset.Symbols && request.ProviderName == "NadpcoApi");
        Assert.Equal(3, publisher.Requests.Count(request => request.ExternalReference == "3"));
        Assert.Equal(3, publisher.Requests.Count(request => request.ExternalReference == "4"));
        Assert.Contains(publisher.Requests, request => request.Dataset == ProviderDataset.FundamentalIndexes);
    }

    [Fact]
    public async Task IncrementalSync_RecordsOverlapWindowFromLastSuccessfulSync()
    {
        await using var db = CreateDb();
        SeedCompanies(db, 3);
        db.NadpcoApiSyncStates.Add(new NadpcoApiSyncStateRow
        {
            Dataset = ProviderDataset.FinancialStatements.ToString(),
            LastSuccessfulSyncAt = DateTimeOffset.Parse("2026-06-01T10:00:00Z")
        });
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var service = NewService(db, publisher, overlapDays: 3);

        var result = await service.ExecuteAsync(fullReload: false, CancellationToken.None);

        Assert.False(result.FullReload);
        Assert.Equal(NadpcoApiSyncRunMode.IncrementalSync, result.RunMode);
        Assert.Equal(DateTimeOffset.Parse("2026-05-29T10:00:00Z"), result.OverlapFrom);
        var states = await service.QueryAsync(CancellationToken.None);
        Assert.All(states, state =>
        {
            Assert.Equal(DateTimeOffset.Parse("2026-05-29T10:00:00Z"), state.LastOverlapFrom);
            Assert.Equal("IncrementalSync", state.LastRunMode);
        });
    }

    [Fact]
    public async Task Execute_PublisherFailureForOneCompany_IsolatedAndBlocksWatermarkAdvance()
    {
        await using var db = CreateDb();
        SeedCompanies(db, 3, 4);
        var publisher = new RecordingPublisher { FailOnExternalReference = "3" };
        var service = NewService(db, publisher);

        var result = await service.ExecuteAsync(fullReload: true, CancellationToken.None);

        Assert.Equal(2, result.CompaniesConsidered);
        Assert.Equal(1, result.CompaniesEnqueued);
        Assert.Equal(1, result.FailedCompanies);
        Assert.Contains(3, result.FailedCompanyIds);
        Assert.Null(result.AdvancedWatermark);
        var states = await service.QueryAsync(CancellationToken.None);
        Assert.All(states, state =>
        {
            Assert.Equal(1, state.LastFailedCompanies);
            Assert.NotNull(state.LastError);
            Assert.Null(state.LastSuccessfulSyncAt);
        });
    }

    [Fact]
    public async Task Execute_NoKnownCompanies_StillEnqueuesCatalogAndAdvancesState()
    {
        await using var db = CreateDb();
        var publisher = new RecordingPublisher();
        var service = NewService(db, publisher);

        var result = await service.ExecuteAsync(fullReload: true, CancellationToken.None);

        Assert.Equal(0, result.CompaniesConsidered);
        Assert.Equal(0, result.CompaniesEnqueued);
        Assert.Equal(1, result.RequestsEnqueued);
        Assert.Equal(Now, result.AdvancedWatermark);
        var states = await service.QueryAsync(CancellationToken.None);
        Assert.Equal(5, states.Count);
        Assert.All(states, state => Assert.Equal(Now, state.LastSuccessfulSyncAt));
    }

    [Fact]
    public async Task CompanyCatalogRefresh_EnqueuesOnlySymbolsAndRecordsMode()
    {
        await using var db = CreateDb();
        SeedCompanies(db, 3, 4);
        var publisher = new RecordingPublisher();
        var service = NewService(db, publisher);

        var result = await service.ExecuteCompanyCatalogAsync(cleanSlate: false, CancellationToken.None);

        Assert.False(result.FullReload);
        Assert.Equal(NadpcoApiSyncRunMode.CompanyCatalogRefresh, result.RunMode);
        Assert.Null(result.CleanSlate);
        Assert.Equal(2, result.CompaniesConsidered);
        Assert.Equal(1, result.RequestsEnqueued);
        var request = Assert.Single(publisher.Requests);
        Assert.Equal(ProviderDataset.Symbols, request.Dataset);
        Assert.Null(request.ExternalReference);
        Assert.Equal("NadpcoApi", request.ProviderName);
        Assert.Contains("CompanyCatalogRefresh", request.IdempotencyKey);
        var state = Assert.Single(await service.QueryAsync(CancellationToken.None));
        Assert.Equal(ProviderDataset.Symbols.ToString(), state.Dataset);
        Assert.Equal("CompanyCatalogRefresh", state.LastRunMode);
    }

    [Fact]
    public async Task CompanyCatalogCleanSlate_ClearsExistingCatalogBeforeEnqueue()
    {
        await using var db = CreateDb();
        SeedCompanies(db, 3, 4);
        var publisher = new RecordingPublisher();
        var cleanSlate = new RecordingCleanSlateService(
            new NadpcoCompanyCatalogCleanSlateResult(1, 2, 3, 4, 5, 6, 2));
        var cache = new TrackingScannerCache();
        var service = NewService(db, publisher, cleanSlate: cleanSlate, scannerCache: cache);

        var result = await service.ExecuteCompanyCatalogAsync(cleanSlate: true, CancellationToken.None);

        Assert.True(result.FullReload);
        Assert.Equal(NadpcoApiSyncRunMode.CompanyCatalogCleanSlate, result.RunMode);
        Assert.Equal(1, cleanSlate.InvocationCount);
        Assert.NotNull(result.CleanSlate);
        Assert.Equal(2, result.CompaniesConsidered);
        Assert.Equal(1, result.RequestsEnqueued);
        Assert.Single(publisher.Requests);
        Assert.Contains("CompanyCatalogCleanSlate", publisher.Requests.Single().IdempotencyKey);
        Assert.Contains(cache.Invalidations, reason => reason == "NadpcoApi.CompanyCatalogCleanSlate");
        var state = Assert.Single(await service.QueryAsync(CancellationToken.None));
        Assert.Equal("CompanyCatalogCleanSlate", state.LastRunMode);
    }

    [Fact]
    public async Task CompanyCatalogRefresh_PublisherFailureRecordsFailedTelemetry()
    {
        await using var db = CreateDb();
        SeedCompanies(db, 3);
        var publisher = new RecordingPublisher { FailOnDataset = ProviderDataset.Symbols };
        var service = NewService(db, publisher);

        var result = await service.ExecuteCompanyCatalogAsync(cleanSlate: false, CancellationToken.None);

        Assert.Equal(NadpcoApiSyncRunMode.CompanyCatalogRefresh, result.RunMode);
        Assert.Equal(1, result.FailedCompanies);
        Assert.Equal(0, result.RequestsEnqueued);
        Assert.Null(result.AdvancedWatermark);
        var state = Assert.Single(await service.QueryAsync(CancellationToken.None));
        Assert.Equal("CompanyCatalogRefresh", state.LastRunMode);
        Assert.Equal(1, state.LastFailedCompanies);
        Assert.NotNull(state.LastError);
    }

    [Fact]
    public async Task Execute_IgnoresNonNadpcoAndNonNumericCompanyIds()
    {
        await using var db = CreateDb();
        SeedCompanies(db, 3);
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "CodalDb",
            ExternalCompanyId = "4",
            Name = "Codal",
            LastSynchronizedAt = Now
        });
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "NadpcoApi",
            ExternalCompanyId = "not-numeric",
            Name = "Bad",
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var service = NewService(db, publisher);

        var result = await service.ExecuteAsync(fullReload: true, CancellationToken.None);

        Assert.Equal(1, result.CompaniesConsidered);
        Assert.Equal(3, publisher.Requests.Count(request => request.ExternalReference == "3"));
        Assert.DoesNotContain(publisher.Requests, request => request.ExternalReference == "4");
    }

    private static NadpcoApiScheduledSyncService NewService(
        FinancialIngestionDbContext db,
        RecordingPublisher publisher,
        int overlapDays = 7,
        INadpcoCompanyCatalogCleanSlateService? cleanSlate = null,
        IScannerCache? scannerCache = null) =>
        new(
            db,
            new EfCoreNadpcoApiSyncStateStore(db),
            cleanSlate ?? new RecordingCleanSlateService(new NadpcoCompanyCatalogCleanSlateResult(0, 0, 0, 0, 0, 0, 0)),
            publisher,
            Options.Create(new NadpcoApiProviderOptions
            {
                MaxReadParallelism = 2,
                OrchestrationOverlapDays = overlapDays
            }),
            new FixedTimeProvider(Now),
            NullLogger<NadpcoApiScheduledSyncService>.Instance,
            scannerCache);

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void SeedCompanies(FinancialIngestionDbContext db, params int[] companyIds)
    {
        foreach (var companyId in companyIds)
        {
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "NadpcoApi",
                ExternalCompanyId = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = $"Company {companyId}",
                LastSynchronizedAt = Now
            });
        }

        db.SaveChanges();
    }

    private sealed class RecordingPublisher : IDataSyncRequestPublisher
    {
        public List<DataSyncRequest> Requests { get; } = [];
        public string? FailOnExternalReference { get; set; }
        public ProviderDataset? FailOnDataset { get; set; }

        public Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
        {
            if ((FailOnExternalReference is not null && request.ExternalReference == FailOnExternalReference) ||
                (FailOnDataset is not null && request.Dataset == FailOnDataset))
            {
                throw new InvalidOperationException("simulated enqueue failure");
            }

            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCleanSlateService(NadpcoCompanyCatalogCleanSlateResult result)
        : INadpcoCompanyCatalogCleanSlateService
    {
        public int InvocationCount { get; private set; }

        public Task<NadpcoCompanyCatalogCleanSlateResult> ClearAsync(CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class TrackingScannerCache : IScannerCache
    {
        public List<string> Invalidations { get; } = [];

        public Task<string> GetDataVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<ScannerParseResult?> GetPlanAsync(
            ScannerCacheScope scope,
            string dataVersion,
            ScannerParseRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScannerParseResult?>(null);

        public Task SetPlanAsync(
            ScannerCacheScope scope,
            string dataVersion,
            ScannerParseRequest request,
            ScannerParseResult result,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ScannerTableResult?> GetResultAsync(
            ScannerCacheScope scope,
            string dataVersion,
            ScannerExecutionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScannerTableResult?>(null);

        public Task SetResultAsync(
            ScannerCacheScope scope,
            string dataVersion,
            ScannerExecutionRequest request,
            ScannerTableResult result,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(ScannerCacheInvalidation invalidation, CancellationToken cancellationToken)
        {
            Invalidations.Add(invalidation.Reason);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

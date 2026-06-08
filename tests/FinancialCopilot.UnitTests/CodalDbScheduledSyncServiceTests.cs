using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbScheduledSyncServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-31T09:00:00Z");

    [Fact]
    public async Task Execute_Incremental_OnlyEnqueuesCompaniesChangedSinceWatermark()
    {
        var watermark = DateTimeOffset.Parse("2026-05-30T00:00:00Z");
        var executor = new FakeExecutor
        {
            ChangedIds = { [(true, watermark.UtcTicks)] = [101, 102] },
            MaxModifiedDateTime = DateTimeOffset.Parse("2026-05-31T08:00:00Z")
        };
        var state = new FakeStateStore { Watermark = watermark };
        var publisher = new RecordingPublisher();

        var service = NewService(executor, state, publisher);
        var result = await service.ExecuteAsync(fullReload: false, CancellationToken.None);

        Assert.Equal(2, result.CompaniesConsidered);
        Assert.Equal(2, result.CompaniesEnqueued);
        // 1 Symbols + 2 companies * 3 datasets each = 7
        Assert.Equal(7, publisher.Requests.Count);
        Assert.Contains(publisher.Requests, r =>
            r.Dataset == ProviderDataset.Symbols &&
            r.ProviderName == ProviderSources.NoavaranArchiveSqlName);
        // Spec 051: the archive orchestrator stamps one-time archive provenance on every request.
        Assert.All(publisher.Requests, r => Assert.Equal(SourceMode.ArchiveOneTime, r.Mode));
        Assert.Equal(3, publisher.Requests.Count(r => r.ExternalReference == "101"));
        Assert.Equal(3, publisher.Requests.Count(r => r.ExternalReference == "102"));
    }

    [Fact]
    public async Task Execute_FullReload_IgnoresWatermark()
    {
        var existingWatermark = DateTimeOffset.Parse("2026-05-30T00:00:00Z");
        var executor = new FakeExecutor
        {
            // Watermark-less query returns every company (full inventory).
            ChangedIds = { [(false, 0L)] = [201, 202, 203] },
            MaxModifiedDateTime = DateTimeOffset.Parse("2026-05-31T08:00:00Z")
        };
        var state = new FakeStateStore { Watermark = existingWatermark };
        var publisher = new RecordingPublisher();

        var service = NewService(executor, state, publisher);
        var result = await service.ExecuteAsync(fullReload: true, CancellationToken.None);

        Assert.True(result.FullReload);
        Assert.Equal(3, result.CompaniesConsidered);
        Assert.Equal(3, result.CompaniesEnqueued);
        Assert.Equal(1 + 3 * 3, publisher.Requests.Count);
    }

    [Fact]
    public async Task Execute_OnSuccess_AdvancesWatermark()
    {
        var executor = new FakeExecutor
        {
            ChangedIds = { [(false, 0L)] = [301] },
            MaxModifiedDateTime = DateTimeOffset.Parse("2026-05-31T09:00:00Z")
        };
        var state = new FakeStateStore();
        var publisher = new RecordingPublisher();

        var service = NewService(executor, state, publisher);
        var result = await service.ExecuteAsync(fullReload: true, CancellationToken.None);

        Assert.Equal(DateTimeOffset.Parse("2026-05-31T09:00:00Z"), result.AdvancedWatermark);
        Assert.Equal(DateTimeOffset.Parse("2026-05-31T09:00:00Z"), state.Watermark);
    }

    [Fact]
    public async Task Execute_NoChanges_LeavesWatermarkUnchanged()
    {
        var watermark = DateTimeOffset.Parse("2026-05-31T00:00:00Z");
        var executor = new FakeExecutor
        {
            ChangedIds = { [(true, watermark.UtcTicks)] = [] },
            MaxModifiedDateTime = DateTimeOffset.Parse("2026-05-31T08:00:00Z")
        };
        var state = new FakeStateStore { Watermark = watermark };
        var publisher = new RecordingPublisher();

        var service = NewService(executor, state, publisher);
        var result = await service.ExecuteAsync(fullReload: false, CancellationToken.None);

        Assert.Equal(0, result.CompaniesConsidered);
        Assert.Empty(publisher.Requests);
        Assert.Equal(watermark, state.Watermark);
    }

    [Fact]
    public async Task Execute_PublisherFailureForOne_IsolatedAndReported()
    {
        var executor = new FakeExecutor
        {
            ChangedIds = { [(false, 0L)] = [401, 402] },
            MaxModifiedDateTime = DateTimeOffset.Parse("2026-05-31T08:00:00Z")
        };
        var state = new FakeStateStore();
        var publisher = new RecordingPublisher { FailOnExternalReference = "401" };

        var service = NewService(executor, state, publisher);
        var result = await service.ExecuteAsync(fullReload: true, CancellationToken.None);

        Assert.Equal(2, result.CompaniesConsidered);
        Assert.Equal(1, result.CompaniesEnqueued);
        Assert.Equal(1, result.FailedCompanies);
        Assert.Contains(401, result.FailedCompanyIds);
        // Watermark must NOT advance because at least one company failed.
        Assert.Null(result.AdvancedWatermark);
        Assert.Null(state.Watermark);
    }

    private static CodalDbScheduledSyncService NewService(
        FakeExecutor executor,
        FakeStateStore state,
        RecordingPublisher publisher)
    {
        var options = Options.Create(new CodalDbProviderOptions
        {
            ConnectionString = "Server=.;Database=ignored;",
            MaxReadParallelism = 4
        });
        return new CodalDbScheduledSyncService(
            executor,
            state,
            publisher,
            options,
            new FixedTimeProvider(Now),
            NullLogger<CodalDbScheduledSyncService>.Instance);
    }

    private sealed class FakeExecutor : ICodalDbQueryExecutor
    {
        // Tuple key (HasSince, SinceTicks) because DateTimeOffset? cannot be a Dictionary key.
        public Dictionary<(bool HasSince, long SinceTicks), IReadOnlyList<int>> ChangedIds { get; } = new();
        public DateTimeOffset? MaxModifiedDateTime { get; init; }

        public Task<IReadOnlyList<int>> QueryChangedCompanyIdsAsync(
            DateTimeOffset? since,
            CancellationToken cancellationToken)
        {
            var key = (since.HasValue, since?.UtcTicks ?? 0L);
            return Task.FromResult(ChangedIds.TryGetValue(key, out var ids) ? ids : (IReadOnlyList<int>)[]);
        }

        public Task<DateTimeOffset?> QueryMaxModifiedDateTimeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(MaxModifiedDateTime);

        public Task<IReadOnlyList<CodalDbCompanyRecord>> QueryCompaniesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CodalStatementRow>> QueryStatementsAsync(int companyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CodalMonthlyActivityRow>> QueryMonthlyActivityAsync(int companyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CodalRatioRow>> QueryFinancialRatiosAsync(int companyId, IReadOnlyCollection<int> mappedItemIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CodalDbHealthProbe> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CodalDbHealthProbe(true, 0, null));
    }

    private sealed class FakeStateStore : ICodalDbSyncStateStore
    {
        public DateTimeOffset? Watermark { get; set; }

        public Task<DateTimeOffset?> GetWatermarkAsync(string dataset, CancellationToken cancellationToken) =>
            Task.FromResult(Watermark);

        public Task RecordRunStartAsync(string dataset, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AdvanceWatermarkAsync(string dataset, DateTimeOffset watermark, DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            Watermark = watermark;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IDataSyncRequestPublisher
    {
        public List<DataSyncRequest> Requests { get; } = new();
        public string? FailOnExternalReference { get; set; }

        public Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
        {
            if (FailOnExternalReference is not null && request.ExternalReference == FailOnExternalReference)
            {
                throw new InvalidOperationException("Simulated publisher failure.");
            }
            lock (Requests) { Requests.Add(request); }
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

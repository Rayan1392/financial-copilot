using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlyActivityBackfillOutboxRelayTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-24T09:00:00Z");

    [Fact]
    public async Task RelayPending_PublishesDurableMessageAndAdvancesBatch()
    {
        await using var db = CreateDb();
        var (batch, _) = SeedPending(db);
        var publisher = new RecordingPublisher();
        var relay = CreateRelay(db, publisher);

        var count = await relay.RelayPendingAsync(100, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Single(publisher.Requests);
        var outbox = await db.MonthlyActivityBackfillOutbox.SingleAsync();
        Assert.Equal("Published", outbox.Status);
        Assert.Equal(1, outbox.AttemptCount);
        Assert.Null(outbox.LeaseOwner);
        var refreshed = await db.MonthlyActivityBackfillBatches.SingleAsync(row => row.Id == batch.Id);
        Assert.Equal("InProgress", refreshed.Status);
        Assert.Equal(1, refreshed.PublishedCount);
        Assert.Equal(1, refreshed.ActiveSlot);
    }

    [Fact]
    public async Task Reconcile_WhenConsumerCompletes_ReleasesDurableActiveSlot()
    {
        await using var db = CreateDb();
        var (batch, request) = SeedPending(db);
        var relay = CreateRelay(db, new RecordingPublisher());
        await relay.RelayPendingAsync(100, CancellationToken.None);
        db.SyncRuns.Add(new DataSyncRunRow
        {
            Id = request.RequestId,
            IdempotencyKey = request.IdempotencyKey,
            Dataset = request.Dataset.ToString(),
            Status = DataSyncRunStatus.Completed.ToString(),
            RequestedAt = Now,
            CompletedAt = Now.AddSeconds(1),
            ProviderName = request.ProviderName,
            ExternalReference = request.ExternalReference
        });
        await db.SaveChangesAsync();

        await relay.ReconcileActiveBatchesAsync(CancellationToken.None);

        var refreshed = await db.MonthlyActivityBackfillBatches.SingleAsync(row => row.Id == batch.Id);
        Assert.Equal("Completed", refreshed.Status);
        Assert.Null(refreshed.ActiveSlot);
        Assert.Equal(1, refreshed.ProcessedCount);
    }

    [Fact]
    public async Task Reconcile_NoDataYet_IsTerminalRetryableAndAllowsFutureBatch()
    {
        await using var db = CreateDb();
        var (batch, request) = SeedPending(db);
        var relay = CreateRelay(db, new RecordingPublisher());
        await relay.RelayPendingAsync(100, CancellationToken.None);
        db.SyncRuns.Add(new DataSyncRunRow
        {
            Id = request.RequestId,
            IdempotencyKey = request.IdempotencyKey,
            Dataset = request.Dataset.ToString(),
            Status = DataSyncRunStatus.Failed.ToString(),
            RequestedAt = Now,
            CompletedAt = Now.AddSeconds(1),
            ErrorMessage = "NoDataYet - vendor returned no monthly report rows for this company/month."
        });
        await db.SaveChangesAsync();

        await relay.ReconcileActiveBatchesAsync(CancellationToken.None);

        var refreshed = await db.MonthlyActivityBackfillBatches.SingleAsync(row => row.Id == batch.Id);
        Assert.Equal("CompletedWithRetryables", refreshed.Status);
        Assert.Null(refreshed.ActiveSlot);
        Assert.Equal(1, refreshed.RetryableCount);
    }

    [Fact]
    public async Task RelayFailure_AtMaximumAttempts_DeadLettersAndReleasesBatch()
    {
        await using var db = CreateDb();
        var (batch, _) = SeedPending(db);
        var publisher = new RecordingPublisher { Failure = new InvalidOperationException("broker unavailable") };
        var relay = CreateRelay(db, publisher, maximumAttempts: 1);

        var count = await relay.RelayPendingAsync(100, CancellationToken.None);

        Assert.Equal(0, count);
        var outbox = await db.MonthlyActivityBackfillOutbox.SingleAsync();
        Assert.Equal("DeadLetter", outbox.Status);
        Assert.Contains("broker unavailable", outbox.LastError);
        var refreshed = await db.MonthlyActivityBackfillBatches.SingleAsync(row => row.Id == batch.Id);
        Assert.Equal("PublishFailed", refreshed.Status);
        Assert.Null(refreshed.ActiveSlot);
    }

    [Fact]
    public async Task Reconcile_PublishFailure_KeepsLeaseUntilPublishedMessagesFinish()
    {
        await using var db = CreateDb();
        var (batch, publishedRequest) = SeedPending(db);
        var publishedRow = await db.MonthlyActivityBackfillOutbox.SingleAsync();
        publishedRow.Status = "Published";
        publishedRow.PublishedAt = Now;
        var deadLetterRequest = publishedRequest with
        {
            RequestId = Guid.NewGuid(),
            ExternalReference = "314",
            IdempotencyKey = "nadpco-monthlybf-140505-314"
        };
        db.MonthlyActivityBackfillOutbox.Add(new MonthlyActivityBackfillOutboxRow
        {
            Id = deadLetterRequest.RequestId,
            BatchId = batch.Id,
            Sequence = 1,
            IdempotencyKey = deadLetterRequest.IdempotencyKey,
            PayloadJson = JsonSerializer.Serialize(deadLetterRequest, JsonOptions),
            Status = "DeadLetter",
            CreatedAt = Now,
            AttemptCount = 1,
            LastError = "broker unavailable"
        });
        batch.PlannedCount = 2;
        await db.SaveChangesAsync();
        var relay = CreateRelay(db, new RecordingPublisher());

        await relay.ReconcileActiveBatchesAsync(CancellationToken.None);

        var inFlight = await db.MonthlyActivityBackfillBatches.SingleAsync(row => row.Id == batch.Id);
        Assert.Equal("PublishFailed", inFlight.Status);
        Assert.Equal(1, inFlight.ActiveSlot);

        db.SyncRuns.Add(new DataSyncRunRow
        {
            Id = publishedRequest.RequestId,
            IdempotencyKey = publishedRequest.IdempotencyKey,
            Dataset = publishedRequest.Dataset.ToString(),
            Status = DataSyncRunStatus.Completed.ToString(),
            RequestedAt = Now,
            CompletedAt = Now.AddSeconds(1)
        });
        await db.SaveChangesAsync();
        await relay.ReconcileActiveBatchesAsync(CancellationToken.None);

        var completed = await db.MonthlyActivityBackfillBatches.SingleAsync(row => row.Id == batch.Id);
        Assert.Equal("PublishFailed", completed.Status);
        Assert.Null(completed.ActiveSlot);
        Assert.NotNull(completed.CompletedAt);
    }

    private static MonthlyActivityBackfillOutboxRelay CreateRelay(
        FinancialIngestionDbContext db,
        RecordingPublisher publisher,
        int maximumAttempts = 3) =>
        new(
            db,
            publisher,
            Options.Create(new MonthlyActivityBackfillOutboxOptions
            {
                BatchSize = 100,
                LeaseSeconds = 60,
                MaximumAttempts = maximumAttempts
            }),
            new FixedTimeProvider(Now),
            NullLogger<MonthlyActivityBackfillOutboxRelay>.Instance);

    private static (MonthlyActivityBackfillBatchRow Batch, DataSyncRequest Request) SeedPending(
        FinancialIngestionDbContext db)
    {
        var batch = new MonthlyActivityBackfillBatchRow
        {
            Id = Guid.NewGuid(),
            SourceName = "NadpcoApi",
            RequestedBy = "test:admin",
            Status = "Queued",
            ActiveSlot = 1,
            TargetShamsiYear = 1405,
            TargetShamsiMonth = 5,
            CreatedAt = Now,
            PlannedCount = 1
        };
        var request = new DataSyncRequest(
            Guid.NewGuid(),
            ProviderDataset.MonthlyProductionSales,
            "313",
            Now,
            "nadpco-monthlybf-140505-313",
            "NadpcoApi",
            SourceMode.CurrentIncremental,
            "1405/05/01",
            "1405/05/31");
        db.MonthlyActivityBackfillBatches.Add(batch);
        db.MonthlyActivityBackfillOutbox.Add(new MonthlyActivityBackfillOutboxRow
        {
            Id = request.RequestId,
            BatchId = batch.Id,
            Sequence = 0,
            IdempotencyKey = request.IdempotencyKey,
            PayloadJson = JsonSerializer.Serialize(request, JsonOptions),
            Status = "Pending",
            CreatedAt = Now
        });
        db.SaveChanges();
        return (batch, request);
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class RecordingPublisher : IDataSyncRequestPublisher
    {
        public List<DataSyncRequest> Requests { get; } = [];
        public Exception? Failure { get; init; }

        public Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync(
            IReadOnlyCollection<DataSyncRequest> requests,
            CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            Requests.AddRange(requests);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

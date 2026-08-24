using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class MonthlyActivityBackfillOutboxOptions
{
    public const string SectionName = "MonthlyActivityBackfillOutbox";

    public int PollSeconds { get; init; } = 2;
    public int BatchSize { get; init; } = 500;
    public int LeaseSeconds { get; init; } = 120;
    public int MaximumAttempts { get; init; } = 10;
}

public sealed class MonthlyActivityBackfillOutboxRelay(
    FinancialIngestionDbContext dbContext,
    IDataSyncRequestPublisher publisher,
    IOptions<MonthlyActivityBackfillOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<MonthlyActivityBackfillOutboxRelay> logger) : IMonthlyActivityBackfillOutboxRelay
{
    private const string Pending = "Pending";
    private const string Publishing = "Publishing";
    private const string Published = "Published";
    private const string DeadLetter = "DeadLetter";

    public async Task<int> RelayPendingAsync(int maximumCount, CancellationToken cancellationToken)
    {
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var candidateIds = await dbContext.MonthlyActivityBackfillOutbox.AsNoTracking()
            .Where(row =>
                row.AttemptCount < settings.MaximumAttempts &&
                (row.Status == Pending ||
                    row.Status == Publishing && row.LeaseExpiresAt != null && row.LeaseExpiresAt <= now))
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Sequence)
            .Select(row => row.Id)
            .Take(Math.Min(maximumCount, settings.BatchSize))
            .ToArrayAsync(cancellationToken);

        var claimedIds = new List<Guid>(candidateIds.Length);
        foreach (var candidateId in candidateIds)
        {
            if (dbContext.Database.IsRelational())
            {
                var affected = await dbContext.MonthlyActivityBackfillOutbox
                    .Where(row =>
                        row.Id == candidateId &&
                        row.AttemptCount < settings.MaximumAttempts &&
                        (row.Status == Pending ||
                            row.Status == Publishing && row.LeaseExpiresAt != null && row.LeaseExpiresAt <= now))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.Status, Publishing)
                        .SetProperty(row => row.LeaseOwner, owner)
                        .SetProperty(row => row.LeaseExpiresAt, now.AddSeconds(settings.LeaseSeconds))
                        .SetProperty(row => row.LastAttemptAt, now)
                        .SetProperty(row => row.AttemptCount, row => row.AttemptCount + 1),
                        cancellationToken);
                if (affected == 1)
                {
                    claimedIds.Add(candidateId);
                }
            }
            else
            {
                var row = await dbContext.MonthlyActivityBackfillOutbox
                    .SingleAsync(item => item.Id == candidateId, cancellationToken);
                if (row.Status == Pending || row.Status == Publishing && row.LeaseExpiresAt <= now)
                {
                    row.Status = Publishing;
                    row.LeaseOwner = owner;
                    row.LeaseExpiresAt = now.AddSeconds(settings.LeaseSeconds);
                    row.LastAttemptAt = now;
                    row.AttemptCount++;
                    claimedIds.Add(candidateId);
                }
            }
        }

        if (!dbContext.Database.IsRelational())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (claimedIds.Count == 0)
        {
            await MarkExhaustedRowsAsync(now, settings.MaximumAttempts, cancellationToken);
            return 0;
        }

        var claimed = await dbContext.MonthlyActivityBackfillOutbox
            .Where(row => claimedIds.Contains(row.Id) && row.LeaseOwner == owner)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Sequence)
            .ToArrayAsync(cancellationToken);
        var requests = claimed.Select(Deserialize).ToArray();
        var batchIds = claimed.Select(row => row.BatchId).Distinct().ToArray();
        var batches = await dbContext.MonthlyActivityBackfillBatches
            .Where(batch => batchIds.Contains(batch.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var batch in batches)
        {
            batch.PublishingStartedAt ??= now;
            batch.Status = Publishing;
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await publisher.PublishBatchAsync(requests, cancellationToken);
            var publishedAt = timeProvider.GetUtcNow();
            foreach (var row in claimed)
            {
                row.Status = Published;
                row.PublishedAt = publishedAt;
                row.LeaseOwner = null;
                row.LeaseExpiresAt = null;
                row.LastError = null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await ReconcileActiveBatchesAsync(cancellationToken);
            return claimed.Length;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = Limit(exception.Message);
            foreach (var row in claimed)
            {
                row.Status = row.AttemptCount >= settings.MaximumAttempts ? DeadLetter : Pending;
                row.LeaseOwner = null;
                row.LeaseExpiresAt = null;
                row.LastError = error;
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
            await ReconcileActiveBatchesAsync(CancellationToken.None);
            logger.LogError(
                exception,
                "Monthly-activity outbox relay failed for {Count} messages owned by {Owner}.",
                claimed.Length,
                owner);
            return 0;
        }
    }

    public async Task<int> ReconcileActiveBatchesAsync(CancellationToken cancellationToken)
    {
        var activeBatches = await dbContext.MonthlyActivityBackfillBatches
            .Where(batch => batch.ActiveSlot != null)
            .OrderBy(batch => batch.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var changed = 0;

        foreach (var batch in activeBatches)
        {
            var outbox = await dbContext.MonthlyActivityBackfillOutbox.AsNoTracking()
                .Where(row => row.BatchId == batch.Id)
                .ToArrayAsync(cancellationToken);
            var keys = outbox.Select(row => row.IdempotencyKey).ToArray();
            var runs = await dbContext.SyncRuns.AsNoTracking()
                .Where(run => keys.Contains(run.IdempotencyKey))
                .ToDictionaryAsync(run => run.IdempotencyKey, StringComparer.Ordinal, cancellationToken);

            var published = outbox.Count(row => row.Status == Published);
            var deadLettered = outbox.Count(row => row.Status == DeadLetter);
            var processed = 0;
            var failed = deadLettered;
            var retryable = 0;
            foreach (var row in outbox.Where(row => row.Status == Published))
            {
                if (!runs.TryGetValue(row.IdempotencyKey, out var run) ||
                    run.CompletedAt is null || run.CompletedAt < row.CreatedAt ||
                    run.Status is not ("Completed" or "Failed"))
                {
                    continue;
                }

                processed++;
                if (run.Status == "Failed")
                {
                    if (run.ErrorMessage?.Contains("NoDataYet", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        retryable++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            }

            batch.PublishedCount = published;
            batch.ProcessedCount = processed;
            batch.FailedCount = failed;
            batch.RetryableCount = retryable;
            batch.LastError = outbox.Where(row => row.LastError != null)
                .OrderByDescending(row => row.LastAttemptAt)
                .Select(row => row.LastError)
                .FirstOrDefault();

            if (deadLettered > 0)
            {
                batch.Status = "PublishFailed";
                if (processed == published)
                {
                    Complete(batch, "PublishFailed");
                }
            }
            else if (outbox.Length == 0)
            {
                Complete(batch, "NothingToEnqueue");
            }
            else if (published == outbox.Length && processed == outbox.Length)
            {
                Complete(batch, failed > 0
                    ? "CompletedWithFailures"
                    : retryable > 0
                        ? "CompletedWithRetryables"
                        : "Completed");
            }
            else if (published == outbox.Length)
            {
                batch.Status = "InProgress";
                batch.PublishedAt ??= outbox.Max(row => row.PublishedAt);
            }
            else if (outbox.Any(row => row.Status == Publishing))
            {
                batch.Status = Publishing;
            }
            else
            {
                batch.Status = "Queued";
            }

            changed++;
        }

        if (changed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    private async Task MarkExhaustedRowsAsync(
        DateTimeOffset now,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        var exhausted = await dbContext.MonthlyActivityBackfillOutbox
            .Where(row =>
                row.AttemptCount >= maximumAttempts &&
                (row.Status == Pending || row.Status == Publishing && row.LeaseExpiresAt <= now))
            .ToArrayAsync(cancellationToken);
        foreach (var row in exhausted)
        {
            row.Status = DeadLetter;
            row.LeaseOwner = null;
            row.LeaseExpiresAt = null;
            row.LastError ??= "Maximum RabbitMQ publication attempts exhausted.";
        }

        if (exhausted.Length > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await ReconcileActiveBatchesAsync(cancellationToken);
        }
    }

    private void Complete(MonthlyActivityBackfillBatchRow batch, string status)
    {
        batch.Status = status;
        batch.ActiveSlot = null;
        batch.CompletedAt = timeProvider.GetUtcNow();
    }

    private static DataSyncRequest Deserialize(MonthlyActivityBackfillOutboxRow row) =>
        JsonSerializer.Deserialize<DataSyncRequest>(row.PayloadJson, JsonOptions) ??
        throw new InvalidOperationException($"Monthly-activity outbox message {row.Id} has an empty payload.");

    private static string Limit(string message) => message.Length <= 1000 ? message : message[..1000];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>Run-history persistence for the all-index fundamental-index catch-up (spec 050).</summary>
public sealed class EfCoreFundamentalIndexCatchUpRunRepository(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : IFundamentalIndexCatchUpRunReader
{
    private const int LeaseSeconds = 7200;

    public async Task<FundamentalIndexCatchUpRunRow?> TryStartAsync(
        FundamentalIndexCatchUpRequest request,
        string lockOwner,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await RecoverHungRunsAsync(now, cancellationToken);

        var active = await dbContext.FundamentalIndexCatchUpRuns.AnyAsync(
            row => row.Status == FundamentalIndexCatchUpRunStatus.Running.ToString() &&
                row.LockLeaseExpiresAt != null &&
                row.LockLeaseExpiresAt > now,
            cancellationToken);
        if (active)
        {
            return null;
        }

        var row = new FundamentalIndexCatchUpRunRow
        {
            Id = Guid.NewGuid(),
            Status = FundamentalIndexCatchUpRunStatus.Running.ToString(),
            RequestedBy = Limit(request.RequestedBy, 256) ?? "unknown",
            FromShamsiYear = request.FromShamsiYear,
            ToShamsiYear = request.ToShamsiYear,
            StartedAt = now,
            LockOwner = lockOwner,
            LockLeaseExpiresAt = now.AddSeconds(LeaseSeconds)
        };
        dbContext.FundamentalIndexCatchUpRuns.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<FundamentalIndexCatchUpRun> RecordSkippedAsync(
        FundamentalIndexCatchUpRequest request,
        string diagnostics,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var row = new FundamentalIndexCatchUpRunRow
        {
            Id = Guid.NewGuid(),
            Status = FundamentalIndexCatchUpRunStatus.SkippedAlreadyRunning.ToString(),
            RequestedBy = Limit(request.RequestedBy, 256) ?? "unknown",
            FromShamsiYear = request.FromShamsiYear,
            ToShamsiYear = request.ToShamsiYear,
            StartedAt = now,
            FinishedAt = now,
            Diagnostics = Limit(diagnostics, 2000)
        };
        dbContext.FundamentalIndexCatchUpRuns.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<FundamentalIndexCatchUpRun> CompleteAsync(
        Guid runId,
        FundamentalIndexCatchUpRunStatus status,
        int companiesConsidered,
        int requestsEnqueued,
        IReadOnlyCollection<int> failedCompanyIds,
        string? diagnostics,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.FundamentalIndexCatchUpRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        row.Status = status.ToString();
        row.FinishedAt = timeProvider.GetUtcNow();
        row.CompaniesConsidered = companiesConsidered;
        row.RequestsEnqueued = requestsEnqueued;
        row.FailedCompanies = failedCompanyIds.Count;
        row.FailedCompanyIdsJson = JsonSerializer.Serialize(failedCompanyIds);
        row.Diagnostics = Limit(diagnostics, 2000);
        row.LockOwner = null;
        row.LockLeaseExpiresAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<IReadOnlyCollection<FundamentalIndexCatchUpRun>> QueryRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken) =>
        await dbContext.FundamentalIndexCatchUpRuns.AsNoTracking()
            .OrderByDescending(row => row.StartedAt)
            .Take(Math.Clamp(maximumCount, 1, 100))
            .Select(row => Map(row))
            .ToArrayAsync(cancellationToken);

    private async Task RecoverHungRunsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hung = await dbContext.FundamentalIndexCatchUpRuns
            .Where(row => row.Status == FundamentalIndexCatchUpRunStatus.Running.ToString() &&
                row.LockLeaseExpiresAt != null &&
                row.LockLeaseExpiresAt <= now)
            .ToArrayAsync(cancellationToken);
        if (hung.Length == 0)
        {
            return;
        }

        foreach (var row in hung)
        {
            row.Status = FundamentalIndexCatchUpRunStatus.Failed.ToString();
            row.FinishedAt = now;
            row.LockOwner = null;
            row.LockLeaseExpiresAt = null;
            row.Diagnostics = "Recovered expired fundamental-index catch-up lease.";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static FundamentalIndexCatchUpRun Map(FundamentalIndexCatchUpRunRow row) =>
        new(
            row.Id,
            Enum.Parse<FundamentalIndexCatchUpRunStatus>(row.Status),
            row.RequestedBy,
            row.FromShamsiYear,
            row.ToShamsiYear,
            row.StartedAt,
            row.FinishedAt,
            row.CompaniesConsidered,
            row.RequestsEnqueued,
            row.FailedCompanies,
            ParseIds(row.FailedCompanyIdsJson),
            row.Diagnostics);

    private static IReadOnlyCollection<int> ParseIds(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Limit(string? value, int length) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= length ? value : value[..length];
}

/// <summary>
/// DataAdmin all-index fundamental-index catch-up coordinator (spec 050). Enumerates every local
/// NADPCO-backed company id and enqueues bounded <see cref="ProviderDataset.FundamentalIndexCoverage"/>
/// requests for the Shamsi range (default 1403→1405) with empty <c>companyIndexIds</c>. Each request
/// flows through the existing raw-payload/normalization pipeline into the non-scannable coverage table;
/// the curated 041 promotion path is untouched. Per-company failures are isolated; a second concurrent
/// run is rejected via the run lease.
/// </summary>
public sealed class FundamentalIndexCatchUpCoordinator(
    FinancialIngestionDbContext dbContext,
    IDataSyncRequestPublisher publisher,
    EfCoreFundamentalIndexCatchUpRunRepository repository,
    IOptions<NadpcoApiProviderOptions> providerOptions,
    TimeProvider timeProvider,
    ILogger<FundamentalIndexCatchUpCoordinator> logger) : IFundamentalIndexCatchUpCoordinator
{
    public async Task<FundamentalIndexCatchUpRun> RunAsync(
        FundamentalIndexCatchUpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FromShamsiYear > request.ToShamsiYear)
        {
            return await repository.RecordSkippedAsync(
                request, "fromShamsiYear must not exceed toShamsiYear.", cancellationToken);
        }

        var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var row = await repository.TryStartAsync(request, owner, cancellationToken);
        if (row is null)
        {
            return await repository.RecordSkippedAsync(
                request, "A fundamental-index catch-up run is already active.", cancellationToken);
        }

        var providerName = providerOptions.Value.ProviderName;
        var started = timeProvider.GetUtcNow();
        var stamp = started.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var maxParallelism = Math.Max(1, providerOptions.Value.MaxReadParallelism);

        var companyIds = await QueryKnownCompanyIdsAsync(providerName, cancellationToken);
        if (companyIds.Count == 0)
        {
            return await repository.CompleteAsync(
                row.Id,
                FundamentalIndexCatchUpRunStatus.Succeeded,
                companiesConsidered: 0,
                requestsEnqueued: 0,
                failedCompanyIds: [],
                "No local NADPCO companies to enumerate.",
                cancellationToken);
        }

        var throttle = new SemaphoreSlim(maxParallelism, maxParallelism);
        var failed = new ConcurrentBag<int>();
        var enqueued = 0;

        var tasks = companyIds.Select(async companyId =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                await publisher.PublishAsync(
                    new DataSyncRequest(
                        Guid.NewGuid(),
                        ProviderDataset.FundamentalIndexCoverage,
                        companyId.ToString(CultureInfo.InvariantCulture),
                        timeProvider.GetUtcNow(),
                        IdempotencyKey:
                            $"nadpco-fi-coverage-{companyId}-{request.FromShamsiYear}-{request.ToShamsiYear}-{stamp}",
                        ProviderName: providerName,
                        Mode: SourceMode.CurrentIncremental,
                        // Coverage year range carried in the Shamsi date-range fields; the processor
                        // reads these to drive the all-index fetch window.
                        SourceDateRangeStartJalali: request.FromShamsiYear.ToString(CultureInfo.InvariantCulture),
                        SourceDateRangeEndJalali: request.ToShamsiYear.ToString(CultureInfo.InvariantCulture)),
                    cancellationToken);
                Interlocked.Increment(ref enqueued);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Fundamental-index catch-up failed to enqueue company {CompanyId}.", companyId);
                failed.Add(companyId);
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);

        var failedIds = failed.OrderBy(id => id).ToArray();
        var status = failedIds.Length == 0
            ? FundamentalIndexCatchUpRunStatus.Succeeded
            : FundamentalIndexCatchUpRunStatus.PartiallySucceeded;
        var diagnostics =
            $"Enqueued all-index coverage for {enqueued}/{companyIds.Count} companies, " +
            $"years {request.FromShamsiYear}-{request.ToShamsiYear}; failed={failedIds.Length}.";
        logger.LogInformation("Fundamental-index catch-up complete: {Diagnostics}", diagnostics);

        return await repository.CompleteAsync(
            row.Id,
            status,
            companyIds.Count,
            enqueued,
            failedIds,
            diagnostics,
            cancellationToken);
    }

    // Catch-up targets the Noavaran eligibility scope only (equities on بورس/فرابورس/پایه).
    private Task<IReadOnlyList<int>> QueryKnownCompanyIdsAsync(
        string providerName,
        CancellationToken cancellationToken) =>
        NoavaranCompanyScope.EligibleCompanyIdsAsync(dbContext, providerName, cancellationToken);
}

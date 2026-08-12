using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoScheduledSyncOptions
{
    public const string SectionName = "NadpcoScheduledSync";

    public bool Enabled { get; init; }

    public int CadenceSeconds { get; init; } = 86400;

    public string? ExecutionTimeUtc { get; init; }

    public string[] DatasetSelection { get; init; } =
    [
        "CompanyCatalog",
        "Symbols",
        "FinancialStatements",
        "FundamentalIndexes",
        "MonthlyProductionSales"
    ];

    public int BatchSize { get; init; } = 100;

    public int MaxConcurrency { get; init; } = 4;

    public int RetryCount { get; init; } = 1;

    public int RetryDelaySeconds { get; init; } = 30;

    public NadpcoMissedScheduleRecoveryPolicy MissedScheduleRecoveryPolicy { get; init; } =
        NadpcoMissedScheduleRecoveryPolicy.RunOnceImmediately;

    public int MaxMissedExecutionsToCatchUp { get; init; } = 1;

    public int MaxRunDurationSeconds { get; init; } = 3600;

    public int LockLeaseSeconds { get; init; } = 7200;

    public bool AlertingEnabled { get; init; } = true;

    public string AlertSeverity { get; init; } = "Error";
}

public sealed class EfCoreNadpcoScheduledSyncRunRepository(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : INadpcoScheduledSyncRunReader
{
    public async Task<NadpcoScheduledSyncRunRow?> TryStartAsync(
        NadpcoScheduledSyncRunRequest request,
        NadpcoScheduledSyncOptions settings,
        string scheduleSnapshotJson,
        string datasetSelectionJson,
        string lockOwner,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await RecoverHungRunsAsync(now, cancellationToken);

        var active = await dbContext.NadpcoScheduledSyncRuns
            .AnyAsync(row =>
                row.Status == NadpcoScheduledSyncRunStatus.Running.ToString() &&
                row.LockLeaseExpiresAt != null &&
                row.LockLeaseExpiresAt > now,
                cancellationToken);
        if (active)
        {
            dbContext.NadpcoScheduledSyncRuns.Add(new NadpcoScheduledSyncRunRow
            {
                Id = Guid.NewGuid(),
                TriggerSource = request.TriggerSource.ToString(),
                Status = NadpcoScheduledSyncRunStatus.SkippedAlreadyRunning.ToString(),
                StartedAt = now,
                CompletedAt = now,
                ScheduleSnapshotJson = scheduleSnapshotJson,
                DatasetSelectionJson = datasetSelectionJson,
                Diagnostics = "A NADPCO scheduled sync run is already active.",
                ManualReason = Limit(request.ManualReason, 500)
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        var row = new NadpcoScheduledSyncRunRow
        {
            Id = Guid.NewGuid(),
            TriggerSource = request.TriggerSource.ToString(),
            Status = NadpcoScheduledSyncRunStatus.Running.ToString(),
            StartedAt = now,
            ScheduleSnapshotJson = scheduleSnapshotJson,
            DatasetSelectionJson = datasetSelectionJson,
            LockOwner = lockOwner,
            LockLeaseExpiresAt = now.AddSeconds(Math.Max(1, settings.LockLeaseSeconds)),
            ManualReason = Limit(request.ManualReason, 500)
        };
        dbContext.NadpcoScheduledSyncRuns.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<NadpcoScheduledSyncRun> RecordSkipAsync(
        NadpcoScheduledSyncRunRequest request,
        NadpcoScheduledSyncRunStatus status,
        string diagnostics,
        string scheduleSnapshotJson,
        string datasetSelectionJson,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var row = new NadpcoScheduledSyncRunRow
        {
            Id = Guid.NewGuid(),
            TriggerSource = request.TriggerSource.ToString(),
            Status = status.ToString(),
            StartedAt = now,
            CompletedAt = now,
            ScheduleSnapshotJson = scheduleSnapshotJson,
            DatasetSelectionJson = datasetSelectionJson,
            Diagnostics = Limit(diagnostics, 2000),
            LastSuccessfulExecutionAt = await GetLastSuccessfulExecutionAtAsync(cancellationToken),
            ManualReason = Limit(request.ManualReason, 500)
        };
        dbContext.NadpcoScheduledSyncRuns.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<NadpcoScheduledSyncRun> CompleteAsync(
        Guid runId,
        NadpcoScheduledSyncRunStatus status,
        int processedBatches,
        int failedBatches,
        int retryAttempts,
        string? diagnostics,
        bool alertEmitted,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.NadpcoScheduledSyncRuns
            .SingleAsync(item => item.Id == runId, cancellationToken);
        var completed = timeProvider.GetUtcNow();
        row.Status = status.ToString();
        row.CompletedAt = completed;
        row.ProcessedBatches = processedBatches;
        row.FailedBatches = failedBatches;
        row.RetryAttempts = retryAttempts;
        row.Diagnostics = Limit(diagnostics, 2000);
        row.LockOwner = null;
        row.LockLeaseExpiresAt = null;
        row.AlertEmitted = alertEmitted;
        row.LastSuccessfulExecutionAt = status is NadpcoScheduledSyncRunStatus.Succeeded
            ? completed
            : await GetLastSuccessfulExecutionAtAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<NadpcoScheduledSyncRun> SetAlertEmittedAsync(
        Guid runId,
        bool alertEmitted,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.NadpcoScheduledSyncRuns
            .SingleAsync(item => item.Id == runId, cancellationToken);
        row.AlertEmitted = alertEmitted;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<NadpcoScheduledSyncStatus> GetStatusAsync(
        NadpcoScheduledSyncOptions settings,
        int recentRunLimit,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await RecoverHungRunsAsync(now, cancellationToken);
        var lastSuccess = await GetLastSuccessfulExecutionAtAsync(cancellationToken);
        var active = await dbContext.NadpcoScheduledSyncRuns.AsNoTracking()
            .Where(row => row.Status == NadpcoScheduledSyncRunStatus.Running.ToString())
            .OrderByDescending(row => row.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var recent = await QueryRecentAsync(recentRunLimit, cancellationToken);
        return new NadpcoScheduledSyncStatus(
            settings.Enabled,
            settings.Enabled && active is null,
            ResolveNextDue(lastSuccess, settings),
            lastSuccess,
            active is null ? null : Map(active),
            recent);
    }

    public async Task<IReadOnlyCollection<NadpcoScheduledSyncRun>> QueryRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken) =>
        await dbContext.NadpcoScheduledSyncRuns.AsNoTracking()
            .OrderByDescending(row => row.StartedAt)
            .Take(Math.Clamp(maximumCount, 1, 100))
            .Select(row => Map(row))
            .ToArrayAsync(cancellationToken);

    private async Task RecoverHungRunsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hung = await dbContext.NadpcoScheduledSyncRuns
            .Where(row =>
                row.Status == NadpcoScheduledSyncRunStatus.Running.ToString() &&
                row.LockLeaseExpiresAt != null &&
                row.LockLeaseExpiresAt <= now)
            .ToArrayAsync(cancellationToken);
        if (hung.Length == 0)
        {
            return;
        }

        foreach (var row in hung)
        {
            row.Status = NadpcoScheduledSyncRunStatus.HungRecovered.ToString();
            row.CompletedAt = now;
            row.LockOwner = null;
            row.LockLeaseExpiresAt = null;
            row.Diagnostics = "Recovered expired NADPCO scheduled sync lease.";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<DateTimeOffset?> GetLastSuccessfulExecutionAtAsync(CancellationToken cancellationToken) =>
        await dbContext.NadpcoScheduledSyncRuns.AsNoTracking()
            .Where(row => row.Status == NadpcoScheduledSyncRunStatus.Succeeded.ToString())
            .OrderByDescending(row => row.CompletedAt)
            .Select(row => row.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static DateTimeOffset? ResolveNextDue(DateTimeOffset? lastSuccess, NadpcoScheduledSyncOptions settings) =>
        lastSuccess?.AddSeconds(Math.Max(1, settings.CadenceSeconds));

    private static NadpcoScheduledSyncRun Map(NadpcoScheduledSyncRunRow row) =>
        new(
            row.Id,
            Enum.Parse<NadpcoScheduledSyncTriggerSource>(row.TriggerSource),
            Enum.Parse<NadpcoScheduledSyncRunStatus>(row.Status),
            row.StartedAt,
            row.CompletedAt,
            row.LastSuccessfulExecutionAt,
            row.ProcessedBatches,
            row.FailedBatches,
            row.RetryAttempts,
            row.Diagnostics,
            row.ScheduleSnapshotJson,
            row.DatasetSelectionJson,
            row.LockOwner,
            row.LockLeaseExpiresAt,
            row.AlertEmitted,
            row.ManualReason);

    private static string? Limit(string? value, int length) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= length ? value : value[..length];
}

public sealed class NadpcoScheduledSyncCoordinator(
    INadpcoApiScheduledSyncService orchestration,
    EfCoreNadpcoScheduledSyncRunRepository repository,
    INadpcoScheduledSyncAlertSink alertSink,
    IOptions<NadpcoScheduledSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<NadpcoScheduledSyncCoordinator> logger,
    IIndustryRelativeValuationOrchestrationService? industryRelativeValuation = null) : INadpcoScheduledSyncCoordinator
{
    public async Task<NadpcoScheduledSyncRun> RunAsync(
        NadpcoScheduledSyncRunRequest request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var snapshot = JsonSerializer.Serialize(settings);
        var datasets = JsonSerializer.Serialize(settings.DatasetSelection);

        var scheduledTrigger = request.TriggerSource is NadpcoScheduledSyncTriggerSource.Automatic or
            NadpcoScheduledSyncTriggerSource.MissedRecovery;
        if (!settings.Enabled && scheduledTrigger && !request.Force)
        {
            return await RecordSkippedAsync(
                request,
                NadpcoScheduledSyncRunStatus.SkippedDisabled,
                "NADPCO scheduled synchronization is disabled.",
                snapshot,
                datasets,
                cancellationToken);
        }

        if (request.TriggerSource == NadpcoScheduledSyncTriggerSource.MissedRecovery &&
            settings.MissedScheduleRecoveryPolicy == NadpcoMissedScheduleRecoveryPolicy.Skip &&
            !request.Force)
        {
            return await RecordSkippedAsync(
                request,
                NadpcoScheduledSyncRunStatus.Missed,
                "A missed NADPCO scheduled sync execution was skipped by policy.",
                snapshot,
                datasets,
                cancellationToken);
        }

        var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var row = await repository.TryStartAsync(
            request,
            settings,
            snapshot,
            datasets,
            owner,
            cancellationToken);
        if (row is null)
        {
            var recent = await repository.QueryRecentAsync(1, cancellationToken);
            var skipped = recent.Single();
            var skippedAlertEmitted = await EmitAlertIfNeededAsync(
                skipped.RunId,
                skipped.Status,
                settings,
                skipped.Diagnostics,
                cancellationToken);
            return await repository.SetAlertEmittedAsync(skipped.RunId, skippedAlertEmitted, cancellationToken);
        }

        NadpcoApiSyncResult? result = null;
        Exception? lastException = null;
        var attempts = Math.Max(0, settings.RetryCount) + 1;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.MaxRunDurationSeconds)));

        var failedAttempts = 0;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                result = await ExecuteSelectedDatasetsAsync(settings, timeout.Token);
                if (industryRelativeValuation is not null)
                {
                    await industryRelativeValuation.RunAsync($"nadpco-{row.Id:N}", timeout.Token);
                }
                lastException = null;
                break;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastException = exception;
                failedAttempts++;
                logger.LogWarning(
                    exception,
                    "NADPCO scheduled sync attempt {Attempt}/{Attempts} failed.",
                    attempt,
                    attempts);

                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, settings.RetryDelaySeconds)), cancellationToken);
                }
            }
        }

        var (status, processed, failed, diagnostics) = ResolveCompletion(result, lastException, cancellationToken);
        var alertEmitted = await EmitAlertIfNeededAsync(row.Id, status, settings, diagnostics, cancellationToken);
        return await repository.CompleteAsync(
            row.Id,
            status,
            processed,
            failed,
            failedAttempts,
            diagnostics,
            alertEmitted,
            CancellationToken.None);
    }

    public Task<NadpcoScheduledSyncStatus> GetStatusAsync(
        int recentRunLimit,
        CancellationToken cancellationToken) =>
        repository.GetStatusAsync(options.Value, recentRunLimit, cancellationToken);

    private async Task<NadpcoScheduledSyncRun> RecordSkippedAsync(
        NadpcoScheduledSyncRunRequest request,
        NadpcoScheduledSyncRunStatus status,
        string diagnostics,
        string scheduleSnapshotJson,
        string datasetSelectionJson,
        CancellationToken cancellationToken)
    {
        var run = await repository.RecordSkipAsync(
            request,
            status,
            diagnostics,
            scheduleSnapshotJson,
            datasetSelectionJson,
            cancellationToken);
        var alertEmitted = await EmitAlertIfNeededAsync(
            run.RunId,
            run.Status,
            options.Value,
            run.Diagnostics,
            cancellationToken);
        return await repository.SetAlertEmittedAsync(run.RunId, alertEmitted, cancellationToken);
    }

    private async Task<NadpcoApiSyncResult> ExecuteSelectedDatasetsAsync(
        NadpcoScheduledSyncOptions settings,
        CancellationToken cancellationToken)
    {
        var selected = settings.DatasetSelection
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includeCompanyCatalog = selected.Contains("CompanyCatalog");
        var includeIncremental = selected.Count == 0 || selected.Any(dataset => !dataset.Equals("CompanyCatalog", StringComparison.OrdinalIgnoreCase));

        NadpcoApiSyncResult? catalogResult = null;
        NadpcoApiSyncResult? incrementalResult = null;

        if (includeCompanyCatalog)
        {
            catalogResult = await orchestration.ExecuteCompanyCatalogAsync(cleanSlate: false, cancellationToken);
        }

        if (includeIncremental)
        {
            incrementalResult = await orchestration.ExecuteAsync(fullReload: false, cancellationToken);
        }

        return CombineScheduledResults(catalogResult, incrementalResult);
    }

    private static NadpcoApiSyncResult CombineScheduledResults(
        NadpcoApiSyncResult? catalogResult,
        NadpcoApiSyncResult? incrementalResult)
    {
        if (catalogResult is null && incrementalResult is null)
        {
            return new NadpcoApiSyncResult(
                FullReload: false,
                CompaniesConsidered: 0,
                CompaniesEnqueued: 0,
                FailedCompanies: 0,
                FailedCompanyIds: [],
                RequestsEnqueued: 0,
                OverlapFrom: null,
                AdvancedWatermark: null,
                Duration: TimeSpan.Zero,
                RunMode: NadpcoApiSyncRunMode.CompanyCatalogRefresh);
        }

        if (incrementalResult is null)
        {
            return catalogResult!;
        }

        if (catalogResult is null)
        {
            return incrementalResult;
        }

        return incrementalResult with
        {
            CompaniesConsidered = incrementalResult.CompaniesConsidered + catalogResult.CompaniesConsidered,
            CompaniesEnqueued = incrementalResult.CompaniesEnqueued + catalogResult.RequestsEnqueued,
            FailedCompanies = incrementalResult.FailedCompanies + catalogResult.FailedCompanies,
            FailedCompanyIds = incrementalResult.FailedCompanyIds.Concat(catalogResult.FailedCompanyIds).ToArray(),
            RequestsEnqueued = incrementalResult.RequestsEnqueued + catalogResult.RequestsEnqueued,
            Duration = incrementalResult.Duration + catalogResult.Duration
        };
    }

    private static (
        NadpcoScheduledSyncRunStatus Status,
        int Processed,
        int Failed,
        string? Diagnostics) ResolveCompletion(
            NadpcoApiSyncResult? result,
            Exception? exception,
            CancellationToken callerToken)
    {
        if (exception is OperationCanceledException)
        {
            return (
                callerToken.IsCancellationRequested
                    ? NadpcoScheduledSyncRunStatus.Cancelled
                    : NadpcoScheduledSyncRunStatus.TimedOut,
                0,
                0,
                callerToken.IsCancellationRequested
                    ? "NADPCO scheduled sync was cancelled."
                    : "NADPCO scheduled sync exceeded the configured maximum run duration.");
        }

        if (exception is not null)
        {
            return (NadpcoScheduledSyncRunStatus.Failed, 0, 1, exception.Message);
        }

        if (result is null)
        {
            return (NadpcoScheduledSyncRunStatus.Failed, 0, 1, "NADPCO scheduled sync did not produce a result.");
        }

        if (result.FailedCompanies > 0)
        {
            return (
                NadpcoScheduledSyncRunStatus.PartiallySucceeded,
                result.CompaniesEnqueued,
                result.FailedCompanies,
                $"Failed company batches: {string.Join(",", result.FailedCompanyIds)}");
        }

        var processed = result.RunMode is NadpcoApiSyncRunMode.CompanyCatalogRefresh
            ? result.RequestsEnqueued
            : result.CompaniesEnqueued;
        var diagnostics = result.RunMode is NadpcoApiSyncRunMode.CompanyCatalogRefresh
            ? $"CompanyCatalogRefresh succeeded; requests={result.RequestsEnqueued}; companiesConsidered={result.CompaniesConsidered}; cleanSlate=false."
            : null;
        return (NadpcoScheduledSyncRunStatus.Succeeded, processed, 0, diagnostics);
    }

    private async Task<bool> EmitAlertIfNeededAsync(
        Guid runId,
        NadpcoScheduledSyncRunStatus status,
        NadpcoScheduledSyncOptions settings,
        string? diagnostics,
        CancellationToken cancellationToken)
    {
        if (!settings.AlertingEnabled ||
            status is NadpcoScheduledSyncRunStatus.Succeeded or
                NadpcoScheduledSyncRunStatus.SkippedDisabled)
        {
            return false;
        }

        return await alertSink.EmitAsync(
            new NadpcoScheduledSyncAlert(
                runId,
                status,
                settings.AlertSeverity,
                $"NADPCO scheduled sync ended with status {status}.",
                diagnostics,
                timeProvider.GetUtcNow()),
            cancellationToken);
    }
}

public sealed class LoggingNadpcoScheduledSyncAlertSink(
    ILogger<LoggingNadpcoScheduledSyncAlertSink> logger) : INadpcoScheduledSyncAlertSink
{
    public Task<bool> EmitAsync(
        NadpcoScheduledSyncAlert alert,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "NADPCO scheduled sync alert severity={Severity} runId={RunId} status={Status} diagnostics={Diagnostics}",
            alert.Severity,
            alert.RunId,
            alert.Status,
            alert.Diagnostics);
        return Task.FromResult(true);
    }
}

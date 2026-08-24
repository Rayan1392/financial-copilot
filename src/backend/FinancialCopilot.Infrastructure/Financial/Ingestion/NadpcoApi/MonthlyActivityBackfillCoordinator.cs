using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Manual reverse-chronological monthly-activity backfill (spec 057 Phase A). Enqueues one
/// bounded company-month request per known NADPCO company per Shamsi month, newest month first
/// (1405/02 → … → 1404/01), through the existing ingestion pipeline. Idempotency keys are
/// deterministic per company-month (no timestamp), so completed pairs are skipped on re-start and
/// failed pairs are retried — the backfill resumes instead of restarting. Never scheduler-invoked.
/// </summary>
public sealed class MonthlyActivityBackfillCoordinator(
    FinancialIngestionDbContext dbContext,
    IMonthlyActivityBackfillOutboxRelay outboxRelay,
    IOptions<NadpcoApiProviderOptions> providerOptions,
    TimeProvider timeProvider,
    ILogger<MonthlyActivityBackfillCoordinator> logger) :
    IMonthlyActivityBackfillCoordinator,
    IMonthlyActivityBackfillStateReader
{
    private const string KeyPrefix = "nadpco-monthlybf";

    public async Task<MonthlyActivityBackfillStartResult> StartAsync(
        MonthlyActivityBackfillRequest request,
        CancellationToken cancellationToken)
    {
        var providerName = providerOptions.Value.ProviderName;
        await outboxRelay.ReconcileActiveBatchesAsync(cancellationToken);
        var activeBatch = await dbContext.MonthlyActivityBackfillBatches.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ActiveSlot != null, cancellationToken);
        if (activeBatch is not null)
        {
            return new MonthlyActivityBackfillStartResult(
                "AlreadyInProgress",
                activeBatch.TargetShamsiMonth is null ? 0 : 1,
                CompaniesPlanned: 0,
                RequestsEnqueued: activeBatch.PlannedCount,
                await GetProgressAsync(cancellationToken),
                activeBatch.Id);
        }

        var state = await GetOrCreateStateAsync(providerName, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var eligibleMonths = ShamsiMonthCalculator.DescendingMonths(
            ShamsiMonthCalculator.LatestPublishedMonth(now),
            ShamsiMonthCalculator.MonthlyActivityFloor);
        IReadOnlyList<ShamsiMonth> requestedMonths = request.TargetMonth is { } targetMonth
            ? [targetMonth]
            : eligibleMonths;
        var plannedBeforeStart = ParsePlannedMonths(state.PlannedMonthsJson);
        if (state.IsCompleted && request.TargetMonth is null)
        {
            var progress = await GetProgressAsync(cancellationToken);
            var planIncludesAllEligibleMonths = eligibleMonths.All(month =>
                plannedBeforeStart.Any(planned => planned.Year == month.Year && planned.Month == month.Month));
            if (progress.IsCompleted && planIncludesAllEligibleMonths)
            {
                return new MonthlyActivityBackfillStartResult(
                    "AlreadyCompleted",
                    MonthsPlanned: 0,
                    CompaniesPlanned: 0,
                    RequestsEnqueued: 0,
                    progress);
            }

            await dbContext.Entry(state).ReloadAsync(cancellationToken);
        }

        var companyIds = await QueryKnownCompanyIdsAsync(providerName, cancellationToken);
        if (companyIds.Count == 0)
        {
            return new MonthlyActivityBackfillStartResult(
                "NoCompanies",
                requestedMonths.Count,
                CompaniesPlanned: 0,
                RequestsEnqueued: 0,
                await GetProgressAsync(cancellationToken));
        }

        // A backfill can remain active across a Shamsi month boundary. Preserve the existing
        // plan and append newly eligible months instead of freezing the plan at the first start.
        // This also allows a completed historical plan to reopen when a new month becomes eligible.
        var plannedMonths = MergePlannedMonths(plannedBeforeStart, requestedMonths, companyIds.Count);

        state.LastStartedAt = now;
        state.RequestedBy = Limit(request.RequestedBy, 256);
        state.PlannedMonthsJson = JsonSerializer.Serialize(
            plannedMonths.ToArray());
        // Skip company-months only when a completed run also has persisted monthly report rows.
        // Completed-but-empty runs remain retryable for gradually published months.
        var completedKeys = await QueryCompletedKeysWithPersistedRowsAsync(providerName, cancellationToken);

        var monthsToEnqueue = request.TargetMonth is null
            ? plannedMonths.Select(month => new ShamsiMonth(month.Year, month.Month)).ToArray()
            : requestedMonths;
        var requestsToEnqueue = new List<DataSyncRequest>();
        foreach (var month in monthsToEnqueue)
        {
            var fromDate = month.FirstDayJalali;
            var toDate = ShamsiMonthCalculator.LastDayJalali(month);
            foreach (var companyId in companyIds)
            {
                var key = BuildKey(month, companyId);
                if (completedKeys.Contains(key))
                {
                    continue;
                }

                requestsToEnqueue.Add(
                    new DataSyncRequest(
                        Guid.NewGuid(),
                        ProviderDataset.MonthlyProductionSales,
                        companyId.ToString(CultureInfo.InvariantCulture),
                        timeProvider.GetUtcNow(),
                        IdempotencyKey: key,
                        ProviderName: providerName,
                        Mode: SourceMode.CurrentIncremental,
                        SourceDateRangeStartJalali: fromDate,
                        SourceDateRangeEndJalali: toDate));
            }
        }

        var enqueued = requestsToEnqueue.Count;

        var batch = new MonthlyActivityBackfillBatchRow
        {
            Id = Guid.NewGuid(),
            SourceName = providerName,
            RequestedBy = Limit(request.RequestedBy, 256),
            Status = enqueued == 0 ? "NothingToEnqueue" : "Queued",
            ActiveSlot = enqueued == 0 ? null : 1,
            TargetShamsiYear = request.TargetMonth?.Year,
            TargetShamsiMonth = request.TargetMonth?.Month,
            CreatedAt = now,
            CompletedAt = enqueued == 0 ? now : null,
            PlannedCount = enqueued
        };
        dbContext.MonthlyActivityBackfillBatches.Add(batch);
        dbContext.MonthlyActivityBackfillOutbox.AddRange(requestsToEnqueue.Select((syncRequest, sequence) =>
            new MonthlyActivityBackfillOutboxRow
            {
                Id = syncRequest.RequestId,
                BatchId = batch.Id,
                Sequence = sequence,
                IdempotencyKey = syncRequest.IdempotencyKey,
                PayloadJson = JsonSerializer.Serialize(syncRequest, JsonOptions),
                Status = "Pending",
                CreatedAt = now
            }));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            activeBatch = await dbContext.MonthlyActivityBackfillBatches.AsNoTracking()
                .SingleOrDefaultAsync(row => row.ActiveSlot != null, cancellationToken);
            if (activeBatch is null)
            {
                throw;
            }

            return new MonthlyActivityBackfillStartResult(
                "AlreadyInProgress",
                activeBatch.TargetShamsiMonth is null ? 0 : 1,
                CompaniesPlanned: 0,
                RequestsEnqueued: activeBatch.PlannedCount,
                await GetProgressAsync(cancellationToken),
                activeBatch.Id);
        }

        logger.LogInformation(
            "Monthly-activity backfill batch {BatchId} durably planned {Enqueued} company-month requests across {Months} months " +
            "({Newest} → {Oldest}) for {Companies} companies, requested by {RequestedBy}.",
            batch.Id,
            enqueued,
            monthsToEnqueue.Count,
            monthsToEnqueue[0],
            monthsToEnqueue[^1],
            companyIds.Count,
            request.RequestedBy);

        return new MonthlyActivityBackfillStartResult(
            enqueued == 0 ? "NothingToEnqueue" : "Started",
            monthsToEnqueue.Count,
            companyIds.Count,
            enqueued,
            await GetProgressAsync(cancellationToken),
            batch.Id);
    }

    public async Task<MonthlyActivityBackfillBatch?> GetBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await outboxRelay.ReconcileActiveBatchesAsync(cancellationToken);
        var row = await dbContext.MonthlyActivityBackfillBatches.AsNoTracking()
            .SingleOrDefaultAsync(batch => batch.Id == batchId, cancellationToken);
        return row is null ? null : MapBatch(row);
    }

    public async Task<IReadOnlyCollection<MonthlyActivityBackfillBatch>> ListBatchesAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await outboxRelay.ReconcileActiveBatchesAsync(cancellationToken);
        return await dbContext.MonthlyActivityBackfillBatches.AsNoTracking()
            .OrderByDescending(batch => batch.CreatedAt)
            .Take(limit)
            .Select(batch => new MonthlyActivityBackfillBatch(
                batch.Id,
                batch.Status,
                batch.RequestedBy,
                batch.CreatedAt,
                batch.PublishingStartedAt,
                batch.PublishedAt,
                batch.CompletedAt,
                batch.TargetShamsiYear,
                batch.TargetShamsiMonth,
                batch.PlannedCount,
                batch.PublishedCount,
                batch.ProcessedCount,
                batch.FailedCount,
                batch.RetryableCount,
                batch.LastError))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<MonthlyActivityBackfillProgress> GetProgressAsync(CancellationToken cancellationToken)
    {
        var providerName = providerOptions.Value.ProviderName;
        var state = await dbContext.MonthlyActivityBackfillStates
            .SingleOrDefaultAsync(row => row.SourceName == providerName, cancellationToken);
        if (state is null)
        {
            return new MonthlyActivityBackfillProgress(false, false, "Pending", null, null, null, []);
        }

        var planned = ParsePlannedMonths(state.PlannedMonthsJson);
        var persistedCompanyMonths = await QueryPersistedCompanyMonthKeysAsync(providerName, cancellationToken);
        var runs = await dbContext.SyncRuns.AsNoTracking()
            .Where(run => run.IdempotencyKey.StartsWith(KeyPrefix))
            .Select(run => new
            {
                run.IdempotencyKey,
                run.Status,
                run.ExternalReference,
                run.ErrorMessage
            })
            .ToListAsync(cancellationToken);
        var byMonthToken = runs
            .GroupBy(run => MonthTokenOf(run.IdempotencyKey))
            .ToDictionary(group => group.Key, group => group.ToArray());

        var months = planned.Select(month =>
        {
            byMonthToken.TryGetValue(MonthToken(month.Year, month.Month), out var monthRuns);
            var completed = monthRuns?.Count(run =>
                run.Status == DataSyncRunStatus.Completed.ToString() &&
                HasPersistedRows(run.IdempotencyKey, run.ExternalReference, persistedCompanyMonths)) ?? 0;
            var noDataYet = monthRuns?.Count(run =>
                (run.Status == DataSyncRunStatus.Completed.ToString() &&
                    !HasPersistedRows(run.IdempotencyKey, run.ExternalReference, persistedCompanyMonths)) ||
                (run.Status == DataSyncRunStatus.Failed.ToString() && IsNoDataYet(run.ErrorMessage))) ?? 0;
            var failed = monthRuns?.Count(run =>
                run.Status == DataSyncRunStatus.Failed.ToString() && !IsNoDataYet(run.ErrorMessage)) ?? 0;
            var terminal = completed + noDataYet + failed;
            var status = completed >= month.Companies
                ? "Completed"
                : terminal >= month.Companies
                    ? completed > 0 && noDataYet > 0 && failed == 0
                        ? "CompletedWithRetryables"
                        : noDataYet > 0 && failed == 0
                            ? "NoDataYet"
                            : noDataYet == 0
                                ? "CompletedWithFailures"
                                : "CompletedWithRetryables"
                    : monthRuns is { Length: > 0 }
                        ? "InProgress"
                        : "Pending";
            return new MonthlyActivityBackfillMonthProgress(
                month.Year, month.Month, month.Companies, completed, noDataYet, failed, status);
        }).ToArray();

        var started = state.LastStartedAt is not null;
        var isCompleted = months.Length > 0 && months.All(month => month.Status == "Completed");
        var status = DeriveBackfillStatus(started, isCompleted, months);

        // Durable completion marker (Phase B gate): every planned month fully completed.
        if (state.IsCompleted != isCompleted)
        {
            state.IsCompleted = isCompleted;
            state.CompletedAt = isCompleted ? timeProvider.GetUtcNow() : null;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (isCompleted)
            {
                logger.LogInformation(
                    "Monthly-activity backfill completed across {Months} months; steady-state previous-month refresh is now active.",
                    months.Length);
            }
            else
            {
                logger.LogInformation(
                    "Monthly-activity backfill completion marker reopened because retryable company-months remain. Backfill status: {Status}.",
                    status);
            }
        }

        var outputTypeCounts = await dbContext.MonthlyReports.AsNoTracking()
            .Where(row => row.ProviderName == providerName && row.OutputType != null)
            .GroupBy(row => row.OutputType!.Value)
            .Select(group => new { OutputType = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var outputTypeCountsDict = outputTypeCounts.Count > 0
            ? (IReadOnlyDictionary<int, int>)outputTypeCounts.ToDictionary(x => x.OutputType, x => x.Count)
            : null;

        return new MonthlyActivityBackfillProgress(
            Started: started,
            isCompleted,
            status,
            state.CompletedAt,
            state.LastStartedAt,
            state.RequestedBy,
            months,
            outputTypeCountsDict);
    }

    public async Task<bool> IsBackfillCompletedAsync(CancellationToken cancellationToken) =>
        (await GetProgressAsync(cancellationToken)).IsCompleted;

    private async Task<MonthlyActivityBackfillStateRow> GetOrCreateStateAsync(
        string providerName,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.MonthlyActivityBackfillStates
            .SingleOrDefaultAsync(row => row.SourceName == providerName, cancellationToken);
        if (state is null)
        {
            state = new MonthlyActivityBackfillStateRow { SourceName = providerName };
            dbContext.MonthlyActivityBackfillStates.Add(state);
        }

        return state;
    }

    // Backfill targets the Noavaran eligibility scope only (equities on بورس/فرابورس/پایه).
    private Task<IReadOnlyList<int>> QueryKnownCompanyIdsAsync(
        string providerName,
        CancellationToken cancellationToken) =>
        NoavaranCompanyScope.EligibleCompanyIdsAsync(dbContext, providerName, cancellationToken);

    private static string MonthToken(int year, int month) =>
        string.Create(CultureInfo.InvariantCulture, $"{year:D4}{month:D2}");

    internal static string BuildKey(ShamsiMonth month, int companyId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{KeyPrefix}-{month.Year:D4}{month.Month:D2}-{companyId}");

    // Key shape: nadpco-monthlybf-{yyyyMM}-{companyId}.
    private static string MonthTokenOf(string idempotencyKey)
    {
        var parts = idempotencyKey.Split('-');
        return parts.Length >= 3 ? parts[2] : string.Empty;
    }

    private async Task<HashSet<string>> QueryCompletedKeysWithPersistedRowsAsync(
        string providerName,
        CancellationToken cancellationToken)
    {
        var persistedCompanyMonths = await QueryPersistedCompanyMonthKeysAsync(providerName, cancellationToken);
        var completedRuns = await dbContext.SyncRuns.AsNoTracking()
            .Where(run => run.IdempotencyKey.StartsWith(KeyPrefix) &&
                run.Status == DataSyncRunStatus.Completed.ToString())
            .Select(run => new { run.IdempotencyKey, run.ExternalReference })
            .ToListAsync(cancellationToken);

        return completedRuns
            .Where(run => HasPersistedRows(run.IdempotencyKey, run.ExternalReference, persistedCompanyMonths))
            .Select(run => run.IdempotencyKey)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<HashSet<string>> QueryPersistedCompanyMonthKeysAsync(
        string providerName,
        CancellationToken cancellationToken)
    {
        var reports = await dbContext.MonthlyReports.AsNoTracking()
            .Where(row => row.ProviderName == providerName)
            .Select(row => new { row.ExternalCompanyId, row.PeriodStart })
            .ToListAsync(cancellationToken);

        return reports
            .Where(row => !string.IsNullOrWhiteSpace(row.ExternalCompanyId))
            .Select(row => CompanyMonthToken(row.ExternalCompanyId!, JalaliMonthToken(row.PeriodStart)))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasPersistedRows(
        string idempotencyKey,
        string? externalReference,
        HashSet<string> persistedCompanyMonths) =>
        !string.IsNullOrWhiteSpace(externalReference) &&
        persistedCompanyMonths.Contains(CompanyMonthToken(externalReference, MonthTokenOf(idempotencyKey)));

    private static bool IsNoDataYet(string? errorMessage) =>
        !string.IsNullOrWhiteSpace(errorMessage) &&
        errorMessage.Contains("NoDataYet", StringComparison.OrdinalIgnoreCase);

    private static string DeriveBackfillStatus(
        bool started,
        bool isCompleted,
        IReadOnlyCollection<MonthlyActivityBackfillMonthProgress> months)
    {
        if (!started || months.Count == 0)
        {
            return "Pending";
        }

        if (isCompleted)
        {
            return "Completed";
        }

        if (months.Any(month => month.Status == "InProgress"))
        {
            return "InProgress";
        }

        var hasRetryable = months.Any(month => month.Status is "NoDataYet" or "CompletedWithFailures" or "CompletedWithRetryables");
        var hasPending = months.Any(month => month.Status == "Pending");

        if (hasRetryable)
        {
            return hasPending ? "Retryable" : "CompletedWithFailures";
        }

        return hasPending ? "Pending" : "InProgress";
    }

    private static string CompanyMonthToken(string externalCompanyId, string monthToken) =>
        string.Create(CultureInfo.InvariantCulture, $"{monthToken}:{externalCompanyId}");

    private static string JalaliMonthToken(DateOnly periodStart)
    {
        var dateTime = periodStart.ToDateTime(TimeOnly.MinValue);
        var calendar = new PersianCalendar();
        return MonthToken(calendar.GetYear(dateTime), calendar.GetMonth(dateTime));
    }

    private static IReadOnlyList<PlannedMonth> ParsePlannedMonths(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PlannedMonth[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<PlannedMonth> MergePlannedMonths(
        IReadOnlyList<PlannedMonth> existing,
        IReadOnlyList<ShamsiMonth> eligibleMonths,
        int companyCount)
    {
        var byMonth = existing
            .GroupBy(month => (month.Year, month.Month))
            .ToDictionary(group => group.Key, group => group.First() with { Companies = companyCount });

        foreach (var month in eligibleMonths)
        {
            byMonth.TryAdd((month.Year, month.Month), new PlannedMonth(month.Year, month.Month, companyCount));
        }

        return byMonth.Values
            .OrderByDescending(month => month.Year)
            .ThenByDescending(month => month.Month)
            .ToArray();
    }

    private static string Limit(string value, int length) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Length <= length ? value : value[..length];

    private static MonthlyActivityBackfillBatch MapBatch(MonthlyActivityBackfillBatchRow batch) =>
        new(
            batch.Id,
            batch.Status,
            batch.RequestedBy,
            batch.CreatedAt,
            batch.PublishingStartedAt,
            batch.PublishedAt,
            batch.CompletedAt,
            batch.TargetShamsiYear,
            batch.TargetShamsiMonth,
            batch.PlannedCount,
            batch.PublishedCount,
            batch.ProcessedCount,
            batch.FailedCount,
            batch.RetryableCount,
            batch.LastError);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record PlannedMonth(int Year, int Month, int Companies);
}

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
    IDataSyncRequestPublisher publisher,
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
        var state = await GetOrCreateStateAsync(providerName, cancellationToken);
        if (state.IsCompleted)
        {
            return new MonthlyActivityBackfillStartResult(
                "AlreadyCompleted",
                MonthsPlanned: 0,
                CompaniesPlanned: 0,
                RequestsEnqueued: 0,
                await GetProgressAsync(cancellationToken));
        }

        var now = timeProvider.GetUtcNow();
        var months = ShamsiMonthCalculator.DescendingMonths(
            ShamsiMonthCalculator.LatestPublishedMonth(now),
            ShamsiMonthCalculator.MonthlyActivityFloor);
        var companyIds = await QueryKnownCompanyIdsAsync(providerName, cancellationToken);
        if (companyIds.Count == 0)
        {
            return new MonthlyActivityBackfillStartResult(
                "NoCompanies",
                months.Count,
                CompaniesPlanned: 0,
                RequestsEnqueued: 0,
                await GetProgressAsync(cancellationToken));
        }

        state.LastStartedAt = now;
        state.RequestedBy = Limit(request.RequestedBy, 256);
        state.PlannedMonthsJson = JsonSerializer.Serialize(
            months.Select(month => new PlannedMonth(month.Year, month.Month, companyIds.Count)).ToArray());
        await dbContext.SaveChangesAsync(cancellationToken);

        // Skip company-months whose deterministic run already completed: resume, don't re-enqueue.
        var completedKeys = (await dbContext.SyncRuns.AsNoTracking()
                .Where(run => run.IdempotencyKey.StartsWith(KeyPrefix) &&
                    run.Status == DataSyncRunStatus.Completed.ToString())
                .Select(run => run.IdempotencyKey)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var enqueued = 0;
        foreach (var month in months)
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

                await publisher.PublishAsync(
                    new DataSyncRequest(
                        Guid.NewGuid(),
                        ProviderDataset.MonthlyProductionSales,
                        companyId.ToString(CultureInfo.InvariantCulture),
                        timeProvider.GetUtcNow(),
                        IdempotencyKey: key,
                        ProviderName: providerName,
                        Mode: SourceMode.CurrentIncremental,
                        SourceDateRangeStartJalali: fromDate,
                        SourceDateRangeEndJalali: toDate),
                    cancellationToken);
                enqueued++;
            }
        }

        logger.LogInformation(
            "Monthly-activity backfill enqueued {Enqueued} company-month requests across {Months} months " +
            "({Newest} → {Oldest}) for {Companies} companies, requested by {RequestedBy}.",
            enqueued,
            months.Count,
            months[0],
            months[^1],
            companyIds.Count,
            request.RequestedBy);

        return new MonthlyActivityBackfillStartResult(
            enqueued == 0 ? "NothingToEnqueue" : "Started",
            months.Count,
            companyIds.Count,
            enqueued,
            await GetProgressAsync(cancellationToken));
    }

    public async Task<MonthlyActivityBackfillProgress> GetProgressAsync(CancellationToken cancellationToken)
    {
        var providerName = providerOptions.Value.ProviderName;
        var state = await dbContext.MonthlyActivityBackfillStates
            .SingleOrDefaultAsync(row => row.SourceName == providerName, cancellationToken);
        if (state is null)
        {
            return new MonthlyActivityBackfillProgress(false, false, null, null, null, []);
        }

        var planned = ParsePlannedMonths(state.PlannedMonthsJson);
        var runs = await dbContext.SyncRuns.AsNoTracking()
            .Where(run => run.IdempotencyKey.StartsWith(KeyPrefix))
            .Select(run => new { run.IdempotencyKey, run.Status })
            .ToListAsync(cancellationToken);
        var byMonthToken = runs
            .GroupBy(run => MonthTokenOf(run.IdempotencyKey))
            .ToDictionary(group => group.Key, group => group.ToArray());

        var months = planned.Select(month =>
        {
            byMonthToken.TryGetValue(MonthToken(month.Year, month.Month), out var monthRuns);
            var completed = monthRuns?.Count(run => run.Status == DataSyncRunStatus.Completed.ToString()) ?? 0;
            var failed = monthRuns?.Count(run => run.Status == DataSyncRunStatus.Failed.ToString()) ?? 0;
            var status = completed >= month.Companies
                ? "Completed"
                : completed + failed >= month.Companies
                    ? "CompletedWithFailures"
                    : monthRuns is { Length: > 0 }
                        ? "InProgress"
                        : "Pending";
            return new MonthlyActivityBackfillMonthProgress(
                month.Year, month.Month, month.Companies, completed, failed, status);
        }).ToArray();

        // Durable completion marker (Phase B gate): every planned month fully completed.
        if (!state.IsCompleted && months.Length > 0 && months.All(month => month.Status == "Completed"))
        {
            state.IsCompleted = true;
            state.CompletedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Monthly-activity backfill completed across {Months} months; steady-state previous-month refresh is now active.",
                months.Length);
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
            Started: state.LastStartedAt is not null,
            state.IsCompleted,
            state.CompletedAt,
            state.LastStartedAt,
            state.RequestedBy,
            months,
            outputTypeCountsDict);
    }

    public async Task<bool> IsBackfillCompletedAsync(CancellationToken cancellationToken) =>
        await dbContext.MonthlyActivityBackfillStates.AsNoTracking()
            .AnyAsync(
                row => row.SourceName == providerOptions.Value.ProviderName && row.IsCompleted,
                cancellationToken);

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

    private static string Limit(string value, int length) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Length <= length ? value : value[..length];

    private sealed record PlannedMonth(int Year, int Month, int Companies);
}

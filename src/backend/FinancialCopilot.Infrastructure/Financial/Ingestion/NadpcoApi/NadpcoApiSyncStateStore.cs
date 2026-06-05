using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public interface INadpcoApiSyncStateStore : INadpcoApiSyncStateReader
{
    Task<DateTimeOffset?> GetLastSuccessfulSyncAsync(string dataset, CancellationToken cancellationToken);

    Task RecordRunStartAsync(
        IReadOnlyCollection<string> datasets,
        DateTimeOffset startedAt,
        DateTimeOffset? overlapFrom,
        NadpcoApiSyncRunMode runMode,
        CancellationToken cancellationToken);

    Task RecordRunCompletionAsync(
        IReadOnlyCollection<string> datasets,
        DateTimeOffset completedAt,
        DateTimeOffset successfulSyncAt,
        int companiesConsidered,
        int companiesEnqueued,
        int failedCompanies,
        NadpcoApiSyncRunMode runMode,
        string? error,
        CancellationToken cancellationToken);
}

public sealed class EfCoreNadpcoApiSyncStateStore(
    FinancialIngestionDbContext dbContext) : INadpcoApiSyncStateStore
{
    public async Task<DateTimeOffset?> GetLastSuccessfulSyncAsync(
        string dataset,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.NadpcoApiSyncStates.AsNoTracking()
            .SingleOrDefaultAsync(state => state.Dataset == dataset, cancellationToken);
        return row?.LastSuccessfulSyncAt;
    }

    public async Task RecordRunStartAsync(
        IReadOnlyCollection<string> datasets,
        DateTimeOffset startedAt,
        DateTimeOffset? overlapFrom,
        NadpcoApiSyncRunMode runMode,
        CancellationToken cancellationToken)
    {
        foreach (var dataset in datasets)
        {
            var row = await FindOrCreateAsync(dataset, cancellationToken);
            row.LastRunStartedAt = startedAt;
            row.LastOverlapFrom = overlapFrom;
            row.LastRunMode = runMode.ToString();
            row.LastError = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordRunCompletionAsync(
        IReadOnlyCollection<string> datasets,
        DateTimeOffset completedAt,
        DateTimeOffset successfulSyncAt,
        int companiesConsidered,
        int companiesEnqueued,
        int failedCompanies,
        NadpcoApiSyncRunMode runMode,
        string? error,
        CancellationToken cancellationToken)
    {
        foreach (var dataset in datasets)
        {
            var row = await FindOrCreateAsync(dataset, cancellationToken);
            row.LastRunCompletedAt = completedAt;
            row.LastCompaniesConsidered = companiesConsidered;
            row.LastCompaniesEnqueued = companiesEnqueued;
            row.LastFailedCompanies = failedCompanies;
            row.LastRunMode = runMode.ToString();
            row.LastError = error is null ? null : Limit(error);

            if (error is null && (row.LastSuccessfulSyncAt is null || successfulSyncAt > row.LastSuccessfulSyncAt))
            {
                row.LastSuccessfulSyncAt = successfulSyncAt;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<NadpcoApiSyncState>> QueryAsync(CancellationToken cancellationToken) =>
        await dbContext.NadpcoApiSyncStates.AsNoTracking()
            .OrderBy(row => row.Dataset)
            .Select(row => new NadpcoApiSyncState(
                row.Dataset,
                row.LastSuccessfulSyncAt,
                row.LastOverlapFrom,
                row.LastRunStartedAt,
                row.LastRunCompletedAt,
                row.LastCompaniesConsidered,
                row.LastCompaniesEnqueued,
                row.LastFailedCompanies,
                row.LastRunMode,
                row.LastError))
            .ToArrayAsync(cancellationToken);

    private async Task<NadpcoApiSyncStateRow> FindOrCreateAsync(
        string dataset,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.NadpcoApiSyncStates
            .SingleOrDefaultAsync(state => state.Dataset == dataset, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new NadpcoApiSyncStateRow { Dataset = dataset };
        dbContext.NadpcoApiSyncStates.Add(row);
        return row;
    }

    private static string Limit(string message) => message.Length <= 1000 ? message : message[..1000];
}

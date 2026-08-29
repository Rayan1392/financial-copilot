using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesAcquisitionCompanySource(FinancialIngestionDbContext dbContext) :
    ICyclicalWavesAcquisitionCompanySource
{
    public async Task<IReadOnlyList<CyclicalWavesAcquisitionCompany>> GetCompaniesAsync(
        CancellationToken cancellationToken) =>
        await dbContext.NoavaranEligibleCompanies
            .AsNoTracking()
            .Select(row => new CyclicalWavesAcquisitionCompany(
                row.Id,
                row.ExternalCompanyId,
                row.CompanySymbol,
                row.SymbolIsin))
            .ToListAsync(cancellationToken);
}

public sealed class CyclicalWavesDataAcquisitionRepository(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : ICyclicalWavesDataAcquisitionRepository
{
    private const string ProviderName = "CyclicalWaves";
    private const string PredecessorConstraint = "UX_CyclicalWavesMetricSnapshots_Predecessor";

    public Task<DateOnly?> GetLatestProviderObservationDateAsync(
        Guid companyId,
        CyclicalWavesMetricType metricType,
        CancellationToken cancellationToken) =>
        dbContext.CyclicalWavesMetricSnapshots
            .AsNoTracking()
            .Where(row => row.CompanyId == companyId &&
                         row.ProviderName == ProviderName &&
                         row.MetricType == metricType.ToString() &&
                         row.ProviderObservationDate != null)
            .Select(row => row.ProviderObservationDate)
            .MaxAsync(cancellationToken);

    public Task<bool> HasSuccessfulCheckAsync(
        DateOnly cycleDateUtc,
        Guid companyId,
        CyclicalWavesMetricType metricType,
        CancellationToken cancellationToken)
    {
        var metric = metricType.ToString();
        return dbContext.CyclicalWavesAcquisitionChecks
            .AsNoTracking()
            .AnyAsync(
                row => row.CycleDateUtc == cycleDateUtc &&
                       row.CompanyId == companyId &&
                       row.MetricType == metric &&
                       (row.Result == nameof(CyclicalWavesAcquisitionResult.Changed) ||
                        row.Result == nameof(CyclicalWavesAcquisitionResult.NoChange)),
                cancellationToken);
    }

    public async Task<CyclicalWavesPersistenceResult> PersistAcceptedAsync(
        CyclicalWavesAcceptedAcquisition acquisition,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 2;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var metric = acquisition.MetricType.ToString();
                var existingProviderDateSnapshot = acquisition.ProviderObservationDate is null
                    ? null
                    : await dbContext.CyclicalWavesMetricSnapshots.FirstOrDefaultAsync(
                        row => row.CompanyId == acquisition.CompanyId &&
                               row.ProviderName == ProviderName &&
                               row.MetricType == metric &&
                               row.ProviderObservationDate == acquisition.ProviderObservationDate,
                        cancellationToken);
                var providerDateAlreadyStored = existingProviderDateSnapshot is not null;
                var latest = await dbContext.CyclicalWavesMetricSnapshots
                    .Where(row => row.CompanyId == acquisition.CompanyId &&
                                  row.ProviderName == ProviderName &&
                                  row.MetricType == metric)
                    .OrderByDescending(row => row.AcquisitionDateUtc)
                    .ThenByDescending(row => row.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                CyclicalWavesMetricSnapshotRow snapshot;
                CyclicalWavesAcquisitionResult result;
                if (latest is not null &&
                    (providerDateAlreadyStored ||
                     (acquisition.ProviderObservationDate is null &&
                      latest.ProviderObservationDate is null &&
                      string.Equals(latest.ResponseHash, acquisition.ResponseHash, StringComparison.Ordinal))))
                {
                    snapshot = existingProviderDateSnapshot ?? latest;
                    result = CyclicalWavesAcquisitionResult.NoChange;
                }
                else
                {
                    snapshot = new CyclicalWavesMetricSnapshotRow
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = acquisition.CompanyId,
                        SymbolIsin = acquisition.SymbolIsin,
                        ProviderName = ProviderName,
                        MetricType = metric,
                        RawResponseJson = acquisition.RawResponseJson,
                        ResponseHash = acquisition.ResponseHash,
                        AcquisitionDateUtc = acquisition.AcquisitionDateUtc,
                        ProviderObservationDate = acquisition.ProviderObservationDate,
                        SourceEndpoint = acquisition.SourceEndpoint,
                        PreviousSnapshotId = latest?.Id,
                        CreatedAtUtc = timeProvider.GetUtcNow()
                    };
                    dbContext.CyclicalWavesMetricSnapshots.Add(snapshot);
                    result = CyclicalWavesAcquisitionResult.Changed;
                }

                var check = CreateSuccessfulCheck(acquisition, snapshot.Id, result);
                dbContext.CyclicalWavesAcquisitionChecks.Add(check);

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new CyclicalWavesPersistenceResult(check.Id, snapshot.Id, result);
            }
            catch (DbUpdateException exception) when (
                attempt < maximumAttempts && IsPredecessorConflict(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("CyclicalWaves persistence conflict recovery was exhausted.");
    }

    public async Task<Guid> PersistFailedAsync(
        CyclicalWavesFailedAcquisition acquisition,
        CancellationToken cancellationToken)
    {
        var row = new CyclicalWavesAcquisitionCheckRow
        {
            Id = Guid.NewGuid(),
            CycleDateUtc = acquisition.CycleDateUtc,
            CompanyId = acquisition.CompanyId,
            SymbolIsin = acquisition.SymbolIsin,
            ProviderName = ProviderName,
            MetricType = acquisition.MetricType.ToString(),
            CheckedAtUtc = acquisition.CheckedAtUtc,
            RequestedAtUtc = acquisition.RequestedAtUtc,
            CompletedAtUtc = acquisition.CompletedAtUtc,
            ResponseHash = null,
            Result = nameof(CyclicalWavesAcquisitionResult.Failed),
            SnapshotId = null,
            SourceEndpoint = acquisition.SourceEndpoint,
            HttpStatusCode = acquisition.HttpStatusCode is null
                ? null
                : checked((short)acquisition.HttpStatusCode.Value),
            AttemptCount = acquisition.AttemptCount,
            FailureCode = acquisition.FailureCode,
            FailureMessage = Bound(acquisition.FailureMessage, 1_000),
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        dbContext.CyclicalWavesAcquisitionChecks.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    private CyclicalWavesAcquisitionCheckRow CreateSuccessfulCheck(
        CyclicalWavesAcceptedAcquisition acquisition,
        Guid snapshotId,
        CyclicalWavesAcquisitionResult result) =>
        new()
        {
            Id = Guid.NewGuid(),
            CycleDateUtc = acquisition.CycleDateUtc,
            CompanyId = acquisition.CompanyId,
            SymbolIsin = acquisition.SymbolIsin,
            ProviderName = ProviderName,
            MetricType = acquisition.MetricType.ToString(),
            CheckedAtUtc = acquisition.CheckedAtUtc,
            RequestedAtUtc = acquisition.RequestedAtUtc,
            CompletedAtUtc = acquisition.CompletedAtUtc,
            ResponseHash = acquisition.ResponseHash,
            Result = result.ToString(),
            SnapshotId = snapshotId,
            SourceEndpoint = acquisition.SourceEndpoint,
            HttpStatusCode = checked((short)acquisition.HttpStatusCode),
            AttemptCount = acquisition.AttemptCount,
            FailureCode = null,
            FailureMessage = null,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

    private static bool IsPredecessorConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PredecessorConstraint
        };

    private static string Bound(string value, int maximumLength)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine[..Math.Min(singleLine.Length, maximumLength)];
    }
}

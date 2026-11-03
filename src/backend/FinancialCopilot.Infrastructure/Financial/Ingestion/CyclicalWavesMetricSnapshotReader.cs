using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class CyclicalWavesMetricSnapshotReader(FinancialIngestionDbContext db)
    : ICyclicalWavesMetricSnapshotReader
{
    private const string ProviderName = "CyclicalWaves";
    private static readonly string[] SupportedMetricTypes =
    [
        nameof(CyclicalWavesMetricType.PS),
        nameof(CyclicalWavesMetricType.PE),
        nameof(CyclicalWavesMetricType.Equilibrium)
    ];

    public async Task<IReadOnlyList<CyclicalWavesMetricSnapshot>> ReadLatestAsync(
        IReadOnlyCollection<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        if (companyIds.Count == 0)
            return [];

        var snapshots = await db.CyclicalWavesMetricSnapshots.AsNoTracking()
            .Where(snapshot => companyIds.Contains(snapshot.CompanyId) &&
                               snapshot.ProviderName == ProviderName &&
                               SupportedMetricTypes.Contains(snapshot.MetricType))
            .ToArrayAsync(cancellationToken);
        var selectedSnapshots = snapshots
            .GroupBy(snapshot => new
            {
                snapshot.CompanyId,
                snapshot.ProviderName,
                snapshot.MetricType
            })
            .Select(group => group
                .OrderByDescending(snapshot => snapshot.AcquisitionDateUtc)
                .ThenByDescending(snapshot => snapshot.CreatedAtUtc)
                .ThenByDescending(snapshot => snapshot.Id)
                .First())
            .ToArray();
        var selectedSnapshotIds = selectedSnapshots.Select(snapshot => snapshot.Id).ToArray();
        var checks = await db.CyclicalWavesAcquisitionChecks.AsNoTracking()
            .Where(check => check.SnapshotId.HasValue &&
                            selectedSnapshotIds.Contains(check.SnapshotId.Value) &&
                            (check.Result == nameof(CyclicalWavesAcquisitionResult.Changed) ||
                             check.Result == nameof(CyclicalWavesAcquisitionResult.NoChange)))
            .ToArrayAsync(cancellationToken);

        return selectedSnapshots
            .Select(snapshot => new
            {
                Snapshot = snapshot,
                Check = checks
                    .Where(check => check.CompanyId == snapshot.CompanyId &&
                                    check.ProviderName == snapshot.ProviderName &&
                                    check.MetricType == snapshot.MetricType &&
                                    check.SnapshotId == snapshot.Id &&
                                    check.ResponseHash == snapshot.ResponseHash)
                    .OrderByDescending(check => check.CompletedAtUtc)
                    .ThenByDescending(check => check.CreatedAtUtc)
                    .ThenByDescending(check => check.Id)
                    .FirstOrDefault()
            })
            .Where(candidate => candidate.Check is not null)
            .OrderBy(candidate => candidate.Snapshot.CompanyId)
            .ThenBy(candidate => candidate.Snapshot.MetricType, StringComparer.Ordinal)
            .Select(candidate => new CyclicalWavesMetricSnapshot(
                candidate.Snapshot.Id,
                candidate.Snapshot.CompanyId,
                candidate.Snapshot.ProviderName,
                Enum.Parse<CyclicalWavesMetricType>(candidate.Snapshot.MetricType, ignoreCase: false),
                candidate.Snapshot.RawResponseJson,
                candidate.Snapshot.ResponseHash,
                candidate.Snapshot.AcquisitionDateUtc,
                candidate.Snapshot.CreatedAtUtc,
                candidate.Check!.Id,
                candidate.Check.CompletedAtUtc,
                candidate.Check.CreatedAtUtc))
            .ToArray();
    }
}

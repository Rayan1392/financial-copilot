using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class IndustryRelativeValuationSourceFactStore(
    FinancialIngestionDbContext db,
    TimeProvider clock) : IFeature126SourceFactStore
{
    private const string ProviderName = "CyclicalWaves";

    public async Task<Feature126SourceFactWriteResult> PersistAcceptedAsync(
        Guid companyId,
        RelativeValuationProviderResult result,
        LeaseHandle owner,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess || result.CurrentValue is not > 0m || result.ReferenceValue is not > 0m)
            return Feature126SourceFactWriteResult.Rejected;

        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var atomicNow = clock.GetUtcNow();
            var lease = await db.IndustryRelativeValuationSourceLeases
                .FromSqlInterpolated($"SELECT * FROM \"IndustryRelativeValuationSourceLeases\" WHERE \"LeaseName\" = {owner.LeaseName} FOR UPDATE")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            if (lease is null ||
                lease.ExpiresAtUtc <= atomicNow ||
                lease.Owner != owner.RunningOwner.Envelope)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Feature126SourceFactWriteResult.Rejected;
            }

            var providerName = ProviderName;
            var sourceKind = result.SourceKind.ToString();
            var fetchedAt = result.FetchedAtUtc ?? atomicNow;
            var watermark = result.SourceWatermark ?? result.SourceObservationId;
            var id = Guid.NewGuid();
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "IndustryRelativeValuationSourceFacts"
                ("Id", "CompanyId", "ProviderName", "SourceKind", "SourceObservationId",
                 "CurrentValue", "ReferenceValue", "FetchedAtUtc", "PersistedAtUtc",
                 "SourceEndpoint", "SourceWatermark", "PayloadHash", "Readiness",
                 "QualityCode", "IdentityEvidence", "RawPayload")
                VALUES ({id}, {companyId}, {providerName}, {sourceKind}, {result.SourceObservationId},
                        {result.CurrentValue}, {result.ReferenceValue}, {fetchedAt}, {atomicNow},
                        {result.SourceEndpoint}, {watermark}, {result.PayloadHash},
                        {result.Readiness.ToString()}, {result.QualityCode},
                        {result.IdentityEvidence}, {result.RawPayload})
                ON CONFLICT ("ProviderName", "SourceKind", "SourceObservationId") DO NOTHING
                """, cancellationToken);

            if (affected == 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return Feature126SourceFactWriteResult.Persisted;
            }

            var unchanged = await db.IndustryRelativeValuationSourceFacts
                .AsNoTracking()
                .AnyAsync(row => row.ProviderName == providerName &&
                                 row.SourceKind == sourceKind &&
                                 row.SourceObservationId == result.SourceObservationId,
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return unchanged
                ? Feature126SourceFactWriteResult.Unchanged
                : Feature126SourceFactWriteResult.Rejected;
        }

        var existing = await db.IndustryRelativeValuationSourceFacts
            .AsNoTracking()
            .AnyAsync(row => row.ProviderName == ProviderName &&
                             row.SourceKind == result.SourceKind.ToString() &&
                             row.SourceObservationId == result.SourceObservationId,
                cancellationToken);
        if (existing)
            return Feature126SourceFactWriteResult.Unchanged;

        var now = clock.GetUtcNow();
        db.IndustryRelativeValuationSourceFacts.Add(new IndustryRelativeValuationSourceFactRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = ProviderName,
            SourceKind = result.SourceKind.ToString(),
            SourceObservationId = result.SourceObservationId,
            CurrentValue = result.CurrentValue,
            ReferenceValue = result.ReferenceValue,
            FetchedAtUtc = result.FetchedAtUtc ?? now,
            PersistedAtUtc = now,
            SourceEndpoint = result.SourceEndpoint,
            SourceWatermark = result.SourceWatermark ?? result.SourceObservationId,
            PayloadHash = result.PayloadHash,
            Readiness = result.Readiness.ToString(),
            QualityCode = result.QualityCode,
            IdentityEvidence = result.IdentityEvidence,
            RawPayload = result.RawPayload
        });
        await db.SaveChangesAsync(cancellationToken);
        return Feature126SourceFactWriteResult.Persisted;
    }

    public async Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(
        DateOnly calculationDate,
        CancellationToken cancellationToken)
    {
        var rows = await db.IndustryRelativeValuationSourceFacts
            .AsNoTracking()
            .Where(row => row.ProviderName == ProviderName && row.Readiness == RelativeValuationFactReadiness.Ready.ToString())
            .OrderByDescending(row => row.FetchedAtUtc)
            .ThenByDescending(row => row.PersistedAtUtc)
            .ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);

        var facts = rows
            .GroupBy(row => new { row.CompanyId, row.SourceKind })
            .Select(group => group.First())
            .Select(row => new Feature126SourceFactEvidence(
                row.CompanyId,
                Enum.Parse<RelativeValuationSourceKind>(row.SourceKind, ignoreCase: false),
                row.Id,
                $"{row.SourceObservationId}|{row.PayloadHash}"))
            .ToArray();

        return Feature126SourceSnapshotEvidence.Create(calculationDate, facts);
    }

    public async Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(
        DateOnly calculationDate,
        IReadOnlyList<RelativeValuationEligibleSymbol> admitted,
        CancellationToken cancellationToken)
    {
        var admittedByCompany = admitted
            .Where(x => x.CompanyId.HasValue && !string.IsNullOrWhiteSpace(x.SymbolIsin))
            .GroupBy(x => x.CompanyId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SymbolIsin, StringComparer.Ordinal).First().SymbolIsin!);
        var rows = await db.IndustryRelativeValuationSourceFacts.AsNoTracking()
            .Where(row => row.ProviderName == ProviderName &&
                          row.Readiness == RelativeValuationFactReadiness.Ready.ToString() &&
                          admittedByCompany.Keys.Contains(row.CompanyId))
            .OrderByDescending(row => row.FetchedAtUtc).ThenByDescending(row => row.PersistedAtUtc).ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);
        var facts = new List<Feature126SourceFactEvidence>();
        foreach (var company in admittedByCompany.OrderBy(x => x.Value, StringComparer.Ordinal).ThenBy(x => x.Key))
        foreach (var kind in new[] { RelativeValuationSourceKind.PSGauge, RelativeValuationSourceKind.PEGauge, RelativeValuationSourceKind.EquilibriumGauge })
        {
            var row = rows.FirstOrDefault(x => x.CompanyId == company.Key && x.SourceKind == kind.ToString());
            facts.Add((row is null
                ? new Feature126SourceFactEvidence(company.Key, kind, null, "Missing")
                : new Feature126SourceFactEvidence(company.Key, kind, row.Id, $"{row.SourceObservationId}|{row.PayloadHash}")) with { SymbolIsin = company.Value });
        }
        return Feature126SourceSnapshotEvidence.Create(calculationDate, facts);
    }
}

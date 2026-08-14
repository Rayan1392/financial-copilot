using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed record IndustryRelativeValuationCalculationInput(
    Guid IndustryId,
    string IndustryExternalId,
    string IndustryTitle,
    IReadOnlyList<CanonicalIndustryMember> Members,
    RelativeValuationSourceBarrier SourceBarrier,
    IndustryRelativeValuationResult Result);

/// <summary>
/// Builds a calculation from the latest acceptable persisted fact for each
/// company/metric independently. It deliberately does not join or compare
/// provider business dates or provider generations.
/// </summary>
public sealed class IndustryRelativeValuationCalculationInputBuilder(FinancialIngestionDbContext db)
{
    public async Task<IReadOnlyList<IndustryRelativeValuationCalculationInput>> BuildAsync(
        string canonicalProviderName,
        DateTimeOffset calculatedAtUtc,
        TimeSpan freshnessWindow,
        Feature126SourceSnapshotEvidence manifest,
        CancellationToken cancellationToken)
    {
        var admittedCompanyIds = manifest.Facts.Select(fact => fact.CompanyId).Distinct().ToArray();
        var companies = await db.Companies.AsNoTracking()
            .Where(row => row.ProviderName == canonicalProviderName && row.IndustryId != null)
            .Where(row => admittedCompanyIds.Contains(row.Id))
            .Select(row => new { row.Id, row.IndustryId })
            .ToArrayAsync(cancellationToken);
        var industryIds = companies.Select(row => row.IndustryId!.Value).Distinct().ToArray();
        var industries = await db.Industries.AsNoTracking()
            .Where(row => row.ProviderName == canonicalProviderName && industryIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var admittedFactIds = manifest.Facts.Where(fact => fact.FactId.HasValue).Select(fact => fact.FactId!.Value).ToArray();
        var persistedFacts = await db.IndustryRelativeValuationSourceFacts.AsNoTracking()
            .Where(row => row.ProviderName == "CyclicalWaves" && admittedFactIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        var facts = persistedFacts
            .Select(IndustryRelativeValuationSourceFactMapper.Map)
            .Where(fact => fact is not null)
            .Select(fact => fact!)
            .ToArray();

        var results = new List<IndustryRelativeValuationCalculationInput>();
        foreach (var group in companies.GroupBy(row => row.IndustryId!.Value).OrderBy(group => group.Key))
        {
            if (!industries.TryGetValue(group.Key, out var industry)) continue;
            var members = group
                .Select(row => new CanonicalIndustryMember(row.Id, group.Key, industry.ExternalId, industry.Name))
                .OrderBy(member => member.CompanyId)
                .ToArray();
            var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(
                members, facts, calculatedAtUtc, freshnessWindow);
            var calculation = IndustryRelativeValuationEngine.Calculate(
                members,
                barrier.SelectedFacts,
                new(canonicalProviderName, calculatedAtUtc, freshnessWindow));
            results.Add(new(group.Key, industry.ExternalId, industry.Name, members, barrier, calculation));
        }

        return results;
    }
}

public static class IndustryRelativeValuationSourceFactMapper
{
    public static RelativeValuationSourceFact? Map(IndustryRelativeValuationSourceFactRow row)
    {
        if (!Enum.TryParse<RelativeValuationSourceKind>(row.SourceKind, out var sourceKind)) return null;
        var metric = sourceKind switch
        {
            RelativeValuationSourceKind.PEGauge => RelativeValuationMetric.Pe,
            RelativeValuationSourceKind.PSGauge => RelativeValuationMetric.Ps,
            RelativeValuationSourceKind.EquilibriumGauge => RelativeValuationMetric.Equilibrium,
            _ => (RelativeValuationMetric?)null
        };
        if (metric is null) return null;

        return new(
            row.CompanyId,
            metric.Value,
            row.CurrentValue,
            row.ReferenceValue,
            row.Readiness == RelativeValuationFactReadiness.Ready.ToString(),
            true,
            row.IdentityEvidence.Length > 0,
            row.FetchedAtUtc,
            row.PersistedAtUtc,
            row.SourceObservationId,
            row.Id,
            row.Id.ToString("D"),
            row.SourceWatermark);
    }
}

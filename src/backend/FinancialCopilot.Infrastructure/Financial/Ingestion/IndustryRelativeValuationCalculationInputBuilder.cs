using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed record IndustryRelativeValuationCalculationInput(
    Guid GroupId,
    string GroupExternalId,
    string GroupTitle,
    IReadOnlyList<CanonicalIndustryMember> Members,
    RelativeValuationSourceBarrier SourceBarrier,
    IndustryRelativeValuationResult Result,
    Guid IndustryId,
    string IndustryExternalId,
    string IndustryTitle);

/// <summary>
/// Builds a calculation from the latest acceptable persisted snapshot for each
/// company/metric independently. It deliberately does not join or compare
/// provider business dates or provider generations.
/// </summary>
public sealed class IndustryRelativeValuationCalculationInputBuilder(
    FinancialIngestionDbContext db,
    ICyclicalWavesMetricSnapshotReader snapshotReader)
{
    public IndustryRelativeValuationCalculationInputBuilder(FinancialIngestionDbContext db)
        : this(db, new CyclicalWavesMetricSnapshotReader(db))
    {
    }

    public async Task<IReadOnlyList<IndustryRelativeValuationCalculationInput>> BuildAsync(
        string canonicalProviderName,
        DateTimeOffset calculatedAtUtc,
        TimeSpan freshnessWindow,
        CancellationToken cancellationToken)
    {
        var eligibleCompanies = await (
                from eligible in db.NoavaranEligibleCompanies.AsNoTracking()
                join company in db.Companies.AsNoTracking()
                    on new { eligible.Id, eligible.ProviderName }
                    equals new { company.Id, company.ProviderName }
                join industryGroup in db.IndustryGroups.AsNoTracking()
                    on new { Id = eligible.GroupId!.Value, eligible.ProviderName }
                    equals new { industryGroup.Id, industryGroup.ProviderName }
                where eligible.ProviderName == canonicalProviderName && eligible.GroupId != null
                select new CompanyMembership(
                    company.Id,
                    industryGroup.Id,
                    industryGroup.ExternalId,
                    industryGroup.Name,
                    company.IndustryId))
            .ToArrayAsync(cancellationToken);
        var companies = eligibleCompanies
            .GroupBy(row => row.Id)
            .Where(group => group.Select(row => row.GroupId).Distinct().Count() == 1)
            .Select(group => group.First())
            .OrderBy(row => row.GroupId)
            .ThenBy(row => row.Id)
            .ToArray();
        var snapshots = await snapshotReader.ReadLatestAsync(
            companies.Select(row => row.Id).ToArray(),
            cancellationToken);
        var facts = snapshots.Select(IndustryRelativeValuationSourceMapper.Map).ToArray();

        return await BuildInputsAsync(
            canonicalProviderName,
            calculatedAtUtc,
            freshnessWindow,
            companies,
            facts,
            cancellationToken);
    }

    // Retained only so historical handoff replays remain source-compatible. The registered
    // calculation path uses the persisted-snapshot overload above.
    public async Task<IReadOnlyList<IndustryRelativeValuationCalculationInput>> BuildAsync(
        string canonicalProviderName,
        DateTimeOffset calculatedAtUtc,
        TimeSpan freshnessWindow,
        Feature126SourceSnapshotEvidence manifest,
        CancellationToken cancellationToken)
    {
        var admittedCompanyIds = manifest.Facts.Select(fact => fact.CompanyId).Distinct().ToArray();
        var companies = await (
                from company in db.Companies.AsNoTracking()
                join industryGroup in db.IndustryGroups.AsNoTracking()
                    on new { Id = company.GroupId!.Value, company.ProviderName }
                    equals new { industryGroup.Id, industryGroup.ProviderName }
                where company.ProviderName == canonicalProviderName &&
                      company.GroupId != null && admittedCompanyIds.Contains(company.Id)
                select new CompanyMembership(
                    company.Id,
                    industryGroup.Id,
                    industryGroup.ExternalId,
                    industryGroup.Name,
                    company.IndustryId))
            .ToArrayAsync(cancellationToken);
        var admittedFactIds = manifest.Facts.Where(fact => fact.FactId.HasValue).Select(fact => fact.FactId!.Value).ToArray();
        var persistedFacts = await db.IndustryRelativeValuationSourceFacts.AsNoTracking()
            .Where(row => row.ProviderName == "CyclicalWaves" && admittedFactIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        var facts = persistedFacts
            .Select(IndustryRelativeValuationSourceFactMapper.Map)
            .Where(fact => fact is not null)
            .Select(fact => fact!)
            .ToArray();

        return await BuildInputsAsync(
            canonicalProviderName,
            calculatedAtUtc,
            freshnessWindow,
            companies,
            facts,
            cancellationToken);
    }

    private async Task<IReadOnlyList<IndustryRelativeValuationCalculationInput>> BuildInputsAsync(
        string canonicalProviderName,
        DateTimeOffset calculatedAtUtc,
        TimeSpan freshnessWindow,
        IReadOnlyCollection<CompanyMembership> companies,
        IReadOnlyCollection<RelativeValuationSourceFact> facts,
        CancellationToken cancellationToken)
    {
        var industryIds = companies
            .Where(row => row.IndustryId.HasValue)
            .Select(row => row.IndustryId!.Value)
            .Distinct()
            .ToArray();
        var industries = await db.Industries.AsNoTracking()
            .Where(row => row.ProviderName == canonicalProviderName && industryIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        var results = new List<IndustryRelativeValuationCalculationInput>();
        foreach (var group in companies.GroupBy(row => row.GroupId).OrderBy(group => group.Key))
        {
            var groupIdentity = group.First();
            var groupIndustryIds = group
                .Where(row => row.IndustryId.HasValue)
                .Select(row => row.IndustryId!.Value)
                .Distinct()
                .ToArray();
            var industry = groupIndustryIds.Length == 1 && industries.TryGetValue(groupIndustryIds[0], out var matchedIndustry)
                ? matchedIndustry
                : null;
            if (industry is null) continue;
            var members = group
                .Select(row => new CanonicalIndustryMember(
                    row.Id,
                    group.Key,
                    groupIdentity.GroupExternalId,
                    groupIdentity.GroupTitle,
                    industry.Id,
                    industry.ExternalId,
                    industry.Name))
                .OrderBy(member => member.CompanyId)
                .ToArray();
            var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(
                members, facts, calculatedAtUtc, freshnessWindow);
            var admittedCompanyIds = barrier.Selections
                .Select(selection => selection.CompanyId)
                .ToHashSet();
            members = members
                .Where(member => admittedCompanyIds.Contains(member.CompanyId))
                .ToArray();
            if (members.Length == 0) continue;

            var calculation = IndustryRelativeValuationEngine.Calculate(
                members,
                barrier.SelectedFacts,
                new(canonicalProviderName, calculatedAtUtc, freshnessWindow));
            results.Add(new(
                group.Key,
                groupIdentity.GroupExternalId,
                groupIdentity.GroupTitle,
                members,
                barrier,
                calculation,
                industry.Id,
                industry.ExternalId,
                industry.Name));
        }

        return results;
    }

    private sealed record CompanyMembership(
        Guid Id,
        Guid GroupId,
        string GroupExternalId,
        string GroupTitle,
        Guid? IndustryId);
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

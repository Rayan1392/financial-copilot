using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class IndustryRelativeValuationReadRepository(FinancialIngestionDbContext db, ILogger<IndustryRelativeValuationReadRepository>? logger = null) : IIndustryRelativeValuationReadRepository
{
    public async Task<IndustryRelativeValuationReadModel?> ReadAsync(IndustryRelativeValuationReadRequest request, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        logger?.LogDebug("Feature 125 read started for {CapabilityCode}, member count {MemberCount}, limit {Limit}.", request.CapabilityCode, request.CompanyIds.Count, request.Limit);
        var groupId = request.GroupId;
        if (groupId is null && request.CompanyIds.Count > 0)
        {
            var resolvedGroups = await db.NoavaranEligibleCompanies.AsNoTracking()
                .Where(row => request.CompanyIds.Contains(row.Id) && row.GroupId != null)
                .Select(row => row.GroupId!.Value)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            if (resolvedGroups.Length == 1) groupId = resolvedGroups[0];
        }
        if (groupId is null) { logger?.LogInformation("Feature 125 read unavailable: comparison group could not be resolved."); return null; }

        var calculation = await db.IndustryRelativeValuationCalculations.AsNoTracking()
            .Where(x => x.GroupId == groupId && x.Status == "Published" && x.IsSelectedCurrent)
            .OrderByDescending(x => x.CalculationDate).ThenByDescending(x => x.CalculationVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (calculation is null) { logger?.LogInformation("Feature 125 read unavailable for group {GroupId}: no selected Published snapshot.", groupId); return null; }
        var members = await db.CompanyIndustryRelativeValuations.AsNoTracking().Where(x => x.CalculationId == calculation.Id).ToArrayAsync(cancellationToken);
        var companies = await db.Companies.AsNoTracking().Where(x => members.Select(m => m.CompanyId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var metrics = await db.IndustryRelativeValuationMetrics.AsNoTracking().Where(x => x.CalculationId == calculation.Id).ToArrayAsync(cancellationToken);
        var totalRanked = members.Count(x => x.GlobalRank.HasValue);
        var selected = members;
        if (request.CompanyIds.Count > 0 && !request.CompanyIds.All(companyId => members.Any(member => member.CompanyId == companyId)))
        {
            logger?.LogWarning("Feature 125 read rejected members not present in selected snapshot for group {GroupId}.", groupId);
            return null;
        }
        var ranked = selected.OrderBy(x => x.GlobalRank ?? int.MaxValue).ThenBy(x => x.CompanyId).ToArray();
        var ordered = ranked.Take(request.Limit).ToList();
        if (request.CompanyIds.Count > 0)
        {
            var topIds = ordered.Select(row => row.CompanyId).ToHashSet();
            ordered.AddRange(ranked.Where(row => request.CompanyIds.Contains(row.CompanyId) && !topIds.Contains(row.CompanyId)));
        }
        var totalMembers = members.Length;
        var resultMembers = ordered.Select(row => new RelativeValuationMemberReadModel(
            row.CompanyId,
            companies.TryGetValue(row.CompanyId, out var company) ? company.Ticker ?? company.TseSymbol ?? company.CompanySymbol ?? company.Name : row.CompanyId.ToString("D"),
            company?.Name ?? string.Empty,
            row.GlobalRank,
            totalMembers,
            Metric(row.PEPercent, row.PEClassification, row.PEIsOutlier, row.PEReason, metrics, "PE", Evidence(row, "PE")),
            Metric(row.PSPercent, row.PSClassification, row.PSIsOutlier, row.PSReason, metrics, "PS", Evidence(row, "PS")),
            Metric(row.EquilibriumPercent, row.EquilibriumClassification, row.EquilibriumIsOutlier, row.EquilibriumReason, metrics, "Equilibrium", Evidence(row, "Equilibrium")),
            Evidence(row))).ToArray();
        var benchmarks = metrics.Select(x => new RelativeValuationMetricReadModel(
            x.CleanAverage,
            x.CleanAverage,
            x.Readiness,
            false,
            x.Reason,
            x.Readiness,
            x.MetricKind,
            x.Readiness,
            x.CleanCount,
            x.OutlierCount,
            x.CleanCount < 2 ? x.Reason : string.Empty)).ToArray();
        var sourceEvidence = resultMembers.SelectMany(member => member.SourceEvidence ?? [])
            .Concat(resultMembers.Length == 0 ? [] : resultMembers[0].SourceEvidence ?? [])
            .GroupBy(item => $"{item.MetricKind}:{item.ObservationId}")
            .Select(group => group.First())
            .ToArray();
        var rankVersion = ordered.Select(row => row.RankVersion).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var readiness = string.Join(',', metrics.Select(metric => $"{metric.MetricKind}:{metric.Readiness}"));
        var insufficientReason = metrics.FirstOrDefault(metric => metric.CleanCount < 2)?.Reason ?? string.Empty;
        var result = new IndustryRelativeValuationReadModel(
            request.CapabilityCode,
            calculation.GroupId!.Value,
            calculation.GroupExternalId ?? string.Empty,
            calculation.GroupTitleSnapshot ?? string.Empty,
            calculation.CalculationDate,
            calculation.Id,
            calculation.CalculationVersion,
            calculation.Status,
            calculation.PublishedAtUtc,
            calculation.AlgorithmVersion,
            rankVersion,
            totalMembers,
            totalRanked,
            resultMembers,
            benchmarks,
            calculation.Status,
            calculation.CalculatedAtUtc,
            sourceEvidence,
            calculation.Status,
            readiness,
            insufficientReason,
            calculation.IndustryId,
            calculation.IndustryExternalId,
            calculation.IndustryTitleSnapshot);
        logger?.LogInformation("Feature 125 read completed for group {GroupId}, calculation {CalculationId}, returned {ReturnedMembers}/{TotalMembers} members in {ElapsedMs} ms; status {Status}.", groupId, calculation.Id, result.Members.Count, result.TotalMembers, Stopwatch.GetElapsedTime(started).TotalMilliseconds, result.PublicationStatus);
        return result;
    }

    private static RelativeValuationMetricReadModel Metric(decimal? percent, string classification, bool outlier, string reason, IReadOnlyCollection<IndustryRelativeValuationMetricRow> metrics, string kind, IReadOnlyList<RelativeValuationSourceEvidence> evidence)
    {
        var benchmark = metrics.FirstOrDefault(x => x.MetricKind.Equals(kind, StringComparison.OrdinalIgnoreCase));
        var quality = string.IsNullOrWhiteSpace(reason) ? classification : reason;
        return new(percent, benchmark?.CleanAverage, classification, outlier, reason, quality,
            kind, benchmark?.Readiness ?? "Unavailable", benchmark?.CleanCount ?? 0,
            benchmark?.OutlierCount ?? 0, benchmark is { CleanCount: < 2 } ? benchmark.Reason : string.Empty);
    }

    private static IReadOnlyList<RelativeValuationSourceEvidence> Evidence(CompanyIndustryRelativeValuationRow row) =>
        Evidence(row, "PE").Concat(Evidence(row, "PS")).Concat(Evidence(row, "Equilibrium")).ToArray();

    private static IReadOnlyList<RelativeValuationSourceEvidence> Evidence(CompanyIndustryRelativeValuationRow row, string kind) => kind switch
    {
        "PE" => [new("PE", row.PeSourceObservationId, row.PeSourceObservationTimestamp, row.PePersistedAtUtc, row.PeSourceVersion, row.PeSourceWatermark)],
        "PS" => [new("PS", row.PsSourceObservationId, row.PsSourceObservationTimestamp, row.PsPersistedAtUtc, row.PsSourceVersion, row.PsSourceWatermark)],
        _ => [new("Equilibrium", row.EquilibriumSourceObservationId, row.EquilibriumSourceObservationTimestamp, row.EquilibriumPersistedAtUtc, row.EquilibriumSourceVersion, row.EquilibriumSourceWatermark)]
    };
}

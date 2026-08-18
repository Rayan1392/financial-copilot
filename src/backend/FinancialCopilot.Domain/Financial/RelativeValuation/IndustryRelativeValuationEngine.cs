using System.Security.Cryptography;
using System.Text;

namespace FinancialCopilot.Domain.Financial.RelativeValuation;

public enum RelativeValuationMetric { Pe, Ps, Equilibrium }
public enum RelativeValuationClassification { Green, Red, Unclassifiable }

public enum RelativeValuationQuality
{
    Valid,
    Missing,
    InvalidNonPositiveInput,
    InvalidBaseline,
    InvalidIdentity,
    Unavailable,
    Stale,
    ExcludedFromIndustryBenchmark,
    InsufficientBenchmark
}

public sealed record RelativeValuationCatalogCompany(
    Guid CompanyId,
    string ProviderName,
    Guid? GroupId,
    bool IsActive = true);

public sealed record RelativeValuationCatalogGroup(
    Guid GroupId,
    string ProviderName,
    string ExternalId,
    string DisplayName);

public sealed record CanonicalIndustryMember(
    Guid CompanyId,
    Guid GroupId,
    string GroupExternalId,
    string GroupDisplayName,
    Guid? IndustryId = null,
    string? IndustryExternalId = null,
    string? IndustryDisplayName = null);

public sealed record CanonicalMembershipSnapshot(
    IReadOnlyList<CanonicalIndustryMember> Members,
    string MembershipHash);

public sealed record RelativeValuationSourceFact(
    Guid CompanyId,
    RelativeValuationMetric Metric,
    decimal? CurrentValue,
    decimal? ReferenceValue,
    bool IsAvailable = true,
    bool IsFresh = true,
    bool IdentityValid = true,
    DateTimeOffset? SourceObservationTimestamp = null,
    DateTimeOffset? PersistedAtUtc = null,
    string? SourceObservationId = null,
    Guid? SourceFactId = null,
    string? SourceVersion = null,
    string? SourceWatermark = null);

public sealed record RelativeValuationCalculationContext(
    string CanonicalProviderName,
    DateTimeOffset CalculatedAtUtc,
    TimeSpan FreshnessWindow);

public sealed record NormalizedRelativeMetric(
    RelativeValuationMetric Metric,
    decimal? Percent,
    RelativeValuationQuality Quality,
    bool IsValid)
{
    public bool IsMissing => Quality == RelativeValuationQuality.Missing;
}

public sealed record IndustryBenchmark(
    Guid GroupId,
    RelativeValuationMetric Metric,
    int CandidateCount,
    int CleanCount,
    int OutlierCount,
    decimal? Q1,
    decimal? Q3,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal? CleanAverage,
    bool IsAvailable,
    string Reason,
    string AlgorithmVersion = IndustryRelativeValuationEngine.AlgorithmVersion);

public sealed record CompanyRelativeMetric(
    RelativeValuationMetric Metric,
    decimal? Percent,
    RelativeValuationQuality Quality,
    RelativeValuationClassification Classification,
    bool IsOutlier,
    string? ExclusionReason);

public sealed record CompanyRelativeValuation(
    Guid CompanyId,
    Guid GroupId,
    IReadOnlyList<CompanyRelativeMetric> Metrics,
    int PositiveMetricCount,
    int ValidMetricCount,
    bool IsRankEligible,
    int? GlobalRank);

public sealed record IndustryRelativeValuationResult(
    IReadOnlyList<IndustryBenchmark> Benchmarks,
    IReadOnlyList<CompanyRelativeValuation> Companies);

/// <summary>Pure Feature 125 calculation, benchmark, classification, and ranking engine.</summary>
public static class IndustryRelativeValuationEngine
{
    public const string AlgorithmVersion = "IQR-R7-1.5-v1";
    private const decimal IqrMultiplier = 1.5m;

    public static IReadOnlyList<CanonicalIndustryMember> ResolveCanonicalMembership(
        IEnumerable<RelativeValuationCatalogCompany> companies,
        IEnumerable<RelativeValuationCatalogGroup> groups,
        string canonicalProviderName)
    {
        var groupById = groups
            .Where(x => string.Equals(x.ProviderName, canonicalProviderName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.GroupId).ToDictionary(x => x.Key, x => x.Single());

        return companies
            .Where(x => x.IsActive && string.Equals(x.ProviderName, canonicalProviderName, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.GroupId is not null && groupById.ContainsKey(x.GroupId.Value))
            .Select(x =>
            {
                var group = groupById[x.GroupId!.Value];
                return new CanonicalIndustryMember(x.CompanyId, group.GroupId, group.ExternalId, group.DisplayName);
            })
            .OrderBy(x => x.GroupId).ThenBy(x => x.CompanyId)
            .ToArray();
    }

    public static CanonicalMembershipSnapshot ResolveCanonicalMembershipSnapshot(
        IEnumerable<RelativeValuationCatalogCompany> companies,
        IEnumerable<RelativeValuationCatalogGroup> groups,
        string canonicalProviderName)
    {
        var members = ResolveCanonicalMembership(companies, groups, canonicalProviderName);
        var canonical = string.Join("\n", members.Select(x =>
            $"{x.GroupId:D}|{x.GroupExternalId}|{x.CompanyId:D}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(members, hash);
    }

    public static NormalizedRelativeMetric Normalize(RelativeValuationSourceFact? fact, RelativeValuationMetric? expectedMetric = null)
    {
        if (fact is null || fact.CurrentValue is null || fact.ReferenceValue is null)
            return new(fact?.Metric ?? expectedMetric ?? RelativeValuationMetric.Pe, null, RelativeValuationQuality.Missing, false);
        if (!fact.IdentityValid) return new(fact.Metric, null, RelativeValuationQuality.InvalidIdentity, false);
        if (!fact.IsAvailable) return new(fact.Metric, null, RelativeValuationQuality.Unavailable, false);
        if (!fact.IsFresh) return new(fact.Metric, null, RelativeValuationQuality.Stale, false);
        if (fact.CurrentValue <= 0 || fact.ReferenceValue <= 0)
            return new(fact.Metric, null, RelativeValuationQuality.InvalidNonPositiveInput, false);
        try
        {
            var percent = checked(fact.CurrentValue.Value / fact.ReferenceValue.Value * 100m);
            return new(fact.Metric, percent, RelativeValuationQuality.Valid, true);
        }
        catch (OverflowException)
        {
            return new(fact.Metric, null, RelativeValuationQuality.InvalidBaseline, false);
        }
    }

    public static IndustryRelativeValuationResult Calculate(
        IEnumerable<CanonicalIndustryMember> members,
        IEnumerable<RelativeValuationSourceFact> facts,
        RelativeValuationCalculationContext context)
    {
        var memberList = members.OrderBy(x => x.GroupId).ThenBy(x => x.CompanyId).ToArray();
        var factMap = facts
            .GroupBy(x => (x.CompanyId, x.Metric))
            .ToDictionary(x => x.Key, x => SelectCanonicalFact(x));
        var normalized = memberList.ToDictionary(
            x => x.CompanyId,
            x => Enum.GetValues<RelativeValuationMetric>().Select(metric =>
                Normalize(factMap.TryGetValue((x.CompanyId, metric), out var fact) ? fact : null, metric)).ToArray());

        var benchmarks = new List<IndustryBenchmark>();
        var allResults = new List<CompanyRelativeValuation>();
        foreach (var group in memberList.GroupBy(x => x.GroupId))
        {
            var benchmarkData = Enum.GetValues<RelativeValuationMetric>()
                .Select(metric => BuildBenchmark(group.Key, metric, group.Select(x => normalized[x.CompanyId].Single(y => y.Metric == metric)).ToArray()))
                .ToArray();
            benchmarks.AddRange(benchmarkData.Select(x => x.Benchmark));

            var companies = group.Select(member =>
            {
                var metrics = normalized[member.CompanyId].Select(value =>
                {
                    var benchmarkDataForMetric = benchmarkData.Single(x => x.Benchmark.Metric == value.Metric);
                    var benchmark = benchmarkDataForMetric.Benchmark;
                    var isOutlier = value.IsValid && benchmarkDataForMetric.OutlierValues.Contains(value.Percent!.Value);
                    var quality = isOutlier ? RelativeValuationQuality.ExcludedFromIndustryBenchmark : value.Quality;
                    var classification = !value.IsValid || isOutlier || !benchmark.IsAvailable
                        ? (value.Quality == RelativeValuationQuality.InvalidNonPositiveInput ? RelativeValuationClassification.Red : RelativeValuationClassification.Unclassifiable)
                        : value.Percent <= benchmark.CleanAverage ? RelativeValuationClassification.Green : RelativeValuationClassification.Red;
                    return new CompanyRelativeMetric(value.Metric, value.Percent, quality, classification, isOutlier,
                        isOutlier ? "ExcludedFromIndustryBenchmark" : null);
                }).ToArray();
                return new CompanyRelativeValuation(member.CompanyId, member.GroupId, metrics,
                    metrics.Count(x => x.Classification == RelativeValuationClassification.Green),
                    metrics.Count(x => x.Percent is not null && x.Quality is RelativeValuationQuality.Valid or RelativeValuationQuality.ExcludedFromIndustryBenchmark),
                    metrics.Any(x => x.Classification is RelativeValuationClassification.Green or RelativeValuationClassification.Red), null);
            }).ToList();

            var ranked = companies.Where(x => x.IsRankEligible)
                .OrderByDescending(x => x.PositiveMetricCount)
                .ThenBy(x => x.Metrics.Single(y => y.Metric == RelativeValuationMetric.Pe).Percent is null)
                .ThenBy(x => x.Metrics.Single(y => y.Metric == RelativeValuationMetric.Pe).Percent)
                .ThenBy(x => x.Metrics.Single(y => y.Metric == RelativeValuationMetric.Ps).Percent is null)
                .ThenBy(x => x.Metrics.Single(y => y.Metric == RelativeValuationMetric.Ps).Percent)
                .ThenBy(x => x.Metrics.Single(y => y.Metric == RelativeValuationMetric.Equilibrium).Percent is null)
                .ThenBy(x => x.Metrics.Single(y => y.Metric == RelativeValuationMetric.Equilibrium).Percent)
                .ThenByDescending(x => x.ValidMetricCount).ThenBy(x => x.CompanyId).ToArray();
            var ranks = ranked.Select((company, index) => (company.CompanyId, Rank: index + 1)).ToDictionary(x => x.CompanyId, x => x.Rank);
            allResults.AddRange(companies.Select(x => x with { GlobalRank = ranks.TryGetValue(x.CompanyId, out var rank) ? rank : null }));
        }
        return new(benchmarks, allResults.OrderBy(x => x.GroupId).ThenBy(x => x.GlobalRank ?? int.MaxValue).ThenBy(x => x.CompanyId).ToArray());
    }

    public const int DefaultResultLimit = 3;
    public const int MaximumResultLimit = 100;

    public static IReadOnlyList<CompanyRelativeValuation> TopN(
        IndustryRelativeValuationResult result, Guid groupId, int? topN = null)
    {
        var limit = topN ?? DefaultResultLimit;
        if (limit is < 1 or > MaximumResultLimit)
            throw new ArgumentOutOfRangeException(nameof(topN), $"Top-N must be between 1 and {MaximumResultLimit}.");
        return result.Companies.Where(x => x.GroupId == groupId && x.GlobalRank is not null)
            .OrderBy(x => x.GlobalRank).Take(limit).ToArray();
    }

    private sealed record BenchmarkData(IndustryBenchmark Benchmark, HashSet<decimal> OutlierValues);

    private static RelativeValuationSourceFact SelectCanonicalFact(IEnumerable<RelativeValuationSourceFact> facts) =>
        facts
            .OrderByDescending(x => x.SourceObservationTimestamp.HasValue)
            .ThenByDescending(x => x.SourceObservationTimestamp.GetValueOrDefault())
            .ThenByDescending(x => x.PersistedAtUtc.HasValue)
            .ThenByDescending(x => x.PersistedAtUtc.GetValueOrDefault())
            .ThenByDescending(x => x.SourceObservationId, StringComparer.Ordinal)
            // The source metadata above is the approved ordering. These canonical
            // value/flag keys make an otherwise complete metadata tie deterministic.
            .ThenByDescending(x => x.CurrentValue.HasValue)
            .ThenByDescending(x => x.CurrentValue.GetValueOrDefault())
            .ThenByDescending(x => x.ReferenceValue.HasValue)
            .ThenByDescending(x => x.ReferenceValue.GetValueOrDefault())
            .ThenByDescending(x => x.IsAvailable)
            .ThenByDescending(x => x.IsFresh)
            .ThenByDescending(x => x.IdentityValid)
            .First();

    private static BenchmarkData BuildBenchmark(Guid groupId, RelativeValuationMetric metric, IEnumerable<NormalizedRelativeMetric> values)
    {
        var valid = values.Where(x => x.IsValid && x.Percent is not null).Select(x => x.Percent!.Value).OrderBy(x => x).ToArray();
        if (valid.Length == 0) return new(new(groupId, metric, 0, 0, 0, null, null, null, null, null, false, "NoValidObservations"), []);
        var q1 = R7(valid, .25m); var q3 = R7(valid, .75m); var iqr = q3 - q1;
        var lower = q1 - IqrMultiplier * iqr; var upper = q3 + IqrMultiplier * iqr;
        var clean = valid.Where(x => x >= lower && x <= upper).ToArray();
        var outliers = valid.Where(x => x < lower || x > upper).ToHashSet();
        var available = clean.Length >= 2;
        return new(new(groupId, metric, valid.Length, clean.Length, outliers.Count, q1, q3, lower, upper,
            available ? clean.Average() : null, available, available ? "Ready" : "InsufficientCleanObservations"), outliers);
    }

    private static decimal R7(IReadOnlyList<decimal> values, decimal percentile)
    {
        var h = (values.Count - 1) * percentile;
        var lower = (int)decimal.Floor(h); var upper = (int)decimal.Ceiling(h);
        if (lower == upper) return values[lower];
        return values[lower] + (values[upper] - values[lower]) * (h - lower);
    }
}

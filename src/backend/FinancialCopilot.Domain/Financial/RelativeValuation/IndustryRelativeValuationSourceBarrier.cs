using System.Security.Cryptography;
using System.Text;

namespace FinancialCopilot.Domain.Financial.RelativeValuation;

/// <summary>
/// The exact source evidence selected for one calculation input. There is no
/// business observation date in the CyclicalWaves gauge contract; PersistedAtUtc
/// is therefore the freshness and selection boundary.
/// </summary>
public sealed record RelativeValuationSourceSelection(
    Guid CompanyId,
    RelativeValuationMetric Metric,
    RelativeValuationSourceFact Fact,
    Guid? SourceFactId,
    string SourceVersion,
    DateTimeOffset? SourceObservationTimestamp,
    DateTimeOffset PersistedAtUtc,
    string SourceObservationId,
    string SourceWatermark);

public sealed record RelativeValuationSourceBarrier(
    IReadOnlyList<RelativeValuationSourceSelection> Selections,
    string SourceBarrierHash,
    bool IsComplete,
    int RequiredSelectionCount,
    string? IncompleteReason)
{
    public IReadOnlyList<RelativeValuationSourceFact> SelectedFacts =>
        Selections.Select(x => x.Fact).ToArray();
}

public static class IndustryRelativeValuationSourceBarrierBuilder
{
    public static RelativeValuationSourceBarrier Build(
        IEnumerable<CanonicalIndustryMember> members,
        IEnumerable<RelativeValuationSourceFact> facts,
        DateTimeOffset calculatedAtUtc,
        TimeSpan freshnessWindow)
    {
        if (freshnessWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(freshnessWindow));

        var memberList = members
            .OrderBy(x => x.IndustryId)
            .ThenBy(x => x.CompanyId)
            .ToArray();
        var factList = facts.ToArray();
        var selections = new List<RelativeValuationSourceSelection>();

        foreach (var member in memberList)
        foreach (var metric in Enum.GetValues<RelativeValuationMetric>())
        {
            var selected = factList
                .Where(fact => fact.CompanyId == member.CompanyId && fact.Metric == metric)
                .Where(fact => IsLatestValidCandidate(fact, calculatedAtUtc, freshnessWindow))
                .OrderByDescending(fact => fact.PersistedAtUtc!.Value)
                .ThenByDescending(fact => fact.SourceObservationTimestamp ?? DateTimeOffset.MinValue)
                .ThenByDescending(fact => fact.SourceObservationId ?? string.Empty, StringComparer.Ordinal)
                .ThenByDescending(fact => fact.SourceVersion ?? string.Empty, StringComparer.Ordinal)
                .ThenByDescending(fact => fact.SourceFactId ?? Guid.Empty)
                .FirstOrDefault();

            if (selected is null) continue;
            selections.Add(new(
                selected.CompanyId,
                selected.Metric,
                selected,
                selected.SourceFactId,
                selected.SourceVersion ?? selected.SourceFactId?.ToString("D") ?? selected.SourceObservationId ?? string.Empty,
                selected.SourceObservationTimestamp,
                selected.PersistedAtUtc!.Value,
                selected.SourceObservationId ?? string.Empty,
                selected.SourceWatermark ?? string.Empty));
        }

        var orderedSelections = selections
            .OrderBy(x => x.CompanyId)
            .ThenBy(x => x.Metric)
            .ThenBy(x => x.SourceFactId ?? Guid.Empty)
            .ThenBy(x => x.SourceVersion, StringComparer.Ordinal)
            .ThenBy(x => x.SourceObservationId, StringComparer.Ordinal)
            .ToArray();
        var canonical = string.Join("\n", orderedSelections.Select(x =>
            $"{x.CompanyId:D}|{x.Metric}|{x.SourceFactId?.ToString("D") ?? string.Empty}|{x.SourceVersion}|" +
            $"{x.SourceObservationId}|{x.SourceObservationTimestamp?.ToUniversalTime():O}|{x.PersistedAtUtc.ToUniversalTime():O}|{x.SourceWatermark}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var required = memberList.Length * Enum.GetValues<RelativeValuationMetric>().Length;
        var complete = orderedSelections.Length == required;

        return new(
            orderedSelections,
            hash,
            complete,
            required,
            complete ? null : "MissingOrStaleLatestValidMetricObservation");
    }

    private static bool IsLatestValidCandidate(
        RelativeValuationSourceFact fact,
        DateTimeOffset calculatedAtUtc,
        TimeSpan freshnessWindow) =>
        fact.IsAvailable &&
        fact.IsFresh &&
        fact.IdentityValid &&
        fact.CurrentValue is > 0m &&
        fact.ReferenceValue is > 0m &&
        fact.PersistedAtUtc is not null &&
        fact.PersistedAtUtc.Value <= calculatedAtUtc &&
        fact.PersistedAtUtc.Value >= calculatedAtUtc - freshnessWindow;
}

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

        var memberIds = members
            .Select(member => member.CompanyId)
            .ToHashSet();
        var selections = facts
            .Where(fact => memberIds.Contains(fact.CompanyId))
            .Where(fact => IsUsableCandidate(fact, calculatedAtUtc, freshnessWindow))
            .GroupBy(fact => new { fact.CompanyId, fact.Metric })
            .Select(group => group
                .OrderByDescending(fact => fact.PersistedAtUtc!.Value)
                .ThenByDescending(fact => fact.SourceObservationTimestamp ?? DateTimeOffset.MinValue)
                .ThenByDescending(fact => fact.SourceObservationId ?? string.Empty, StringComparer.Ordinal)
                .ThenByDescending(fact => fact.SourceVersion ?? string.Empty, StringComparer.Ordinal)
                .ThenByDescending(fact => fact.SourceFactId ?? Guid.Empty)
                .First())
            .Select(selected => new RelativeValuationSourceSelection(
                selected.CompanyId,
                selected.Metric,
                selected,
                selected.SourceFactId,
                selected.SourceVersion ?? selected.SourceFactId?.ToString("D") ?? selected.SourceObservationId ?? string.Empty,
                selected.SourceObservationTimestamp,
                selected.PersistedAtUtc!.Value,
                selected.SourceObservationId ?? string.Empty,
                selected.SourceWatermark ?? string.Empty))
            .ToArray();

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
        var selectedCount = orderedSelections.Length;

        return new(
            orderedSelections,
            hash,
            IsComplete: true,
            RequiredSelectionCount: selectedCount,
            IncompleteReason: null);
    }

    private static bool IsUsableCandidate(
        RelativeValuationSourceFact fact,
        DateTimeOffset calculatedAtUtc,
        TimeSpan freshnessWindow)
    {
        if (!fact.IsAvailable ||
            !fact.IsFresh ||
            !fact.IdentityValid ||
            fact.CurrentValue is not > 0m ||
            fact.ReferenceValue is not > 0m ||
            fact.PersistedAtUtc is null ||
            fact.PersistedAtUtc.Value > calculatedAtUtc ||
            fact.PersistedAtUtc.Value < calculatedAtUtc - freshnessWindow)
            return false;

        try
        {
            _ = checked(fact.CurrentValue.Value / fact.ReferenceValue.Value * 100m);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.FinancialData.BalanceSheetReport;

public sealed record BalanceSheetIneligibleStatement(BalanceSheetStatementHeader Header, string Reason);

public sealed record BalanceSheetEligibility(
    IReadOnlyList<BalanceSheetStatementHeader> Eligible,
    IReadOnlyList<BalanceSheetIneligibleStatement> Ineligible);

/// <summary>The selected version at one date, or the reason no version can be selected.</summary>
public sealed record BalanceSheetVersionPick(BalanceSheetStatementHeader? Winner, string? ConflictReason)
{
    public bool IsConflict => ConflictReason is not null;
}

/// <summary>
/// Eligibility, distinct-date grouping, PD-09 version ranking and conflict detection (spec §5). Dates are
/// chosen first; versions are ranked per date. A conflict is never resolved by sorting on an id and never
/// falls back to another date.
/// </summary>
public static class BalanceSheetStatementSelector
{
    public const string ConsolidatedScope = "consolidated_scope";
    public const string ScopeMetadataDisagrees = "scope_metadata_disagrees";
    public const string UnsupportedPeriodType = "unsupported_period_type";
    public const string IneligibleStorageScale = "ineligible_storage_scale";

    public static int PeriodMonths(string periodType) => periodType switch
    {
        "ThreeMonths" => 3,
        "SixMonths" => 6,
        "NineMonths" => 9,
        "TwelveMonths" => 12,
        _ => 0
    };

    public static BalanceSheetEligibility Filter(IEnumerable<BalanceSheetStatementHeader> headers)
    {
        var eligible = new List<BalanceSheetStatementHeader>();
        var ineligible = new List<BalanceSheetIneligibleStatement>();
        foreach (var header in headers.DistinctBy(item => item.StatementId))
        {
            var reason = header.IsComposing ? ConsolidatedScope
                : header.SourceIsComposing is { } scope && scope != header.IsComposing ? ScopeMetadataDisagrees
                : PeriodMonths(header.PeriodType) == 0 ? UnsupportedPeriodType
                : header.AssumedScale != "MillionRials" ? IneligibleStorageScale
                : null;
            if (reason is null) eligible.Add(header);
            else ineligible.Add(new BalanceSheetIneligibleStatement(header, reason));
        }

        return new BalanceSheetEligibility(eligible, ineligible);
    }

    public static IReadOnlyList<DateOnly> DistinctDatesDescending(IEnumerable<BalanceSheetStatementHeader> eligible) =>
        eligible.Select(item => item.PeriodEnd).Distinct().OrderDescending().ToArray();

    /// <summary>Audited first, then restated, then the later publication date (PD-09).</summary>
    public static IReadOnlyList<BalanceSheetStatementHeader> RankedAt(
        IEnumerable<BalanceSheetStatementHeader> eligible, DateOnly periodEnd, string? periodType = null) =>
        FinancialStatementVersionPolicy.Rank(
            eligible.Where(item => item.PeriodEnd == periodEnd && (periodType is null || item.PeriodType == periodType)), Rank);

    /// <summary>The equally preferred versions at the top of a ranked list.</summary>
    public static IReadOnlyList<BalanceSheetStatementHeader> TopTieGroup(IReadOnlyList<BalanceSheetStatementHeader> ranked)
    {
        return FinancialStatementVersionPolicy.TopTieGroup(ranked, Rank);
    }

    /// <summary>
    /// Picks one version from an equally ranked group. Versions that differ on any registry source item give a
    /// conflict; identical ones are separated by the highest ExternalStatementId, then the highest Id.
    /// </summary>
    public static BalanceSheetVersionPick Pick(
        IReadOnlyList<BalanceSheetStatementHeader> topGroup,
        IReadOnlyDictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>> lineItems)
    {
        var pick = FinancialStatementVersionPolicy.Pick(topGroup,
            item => Signature(lineItems.GetValueOrDefault(item.StatementId) ?? []),
            item => item.ExternalStatementId, item => item.StatementId);
        return new BalanceSheetVersionPick(pick.Winner, pick.IsConflict ? BalanceSheetOutcomes.ConflictingReportRevision : null);
    }

    /// <summary>Up to five latest distinct dates on or before the anchor, oldest first (PD-17).</summary>
    public static IReadOnlyList<DateOnly> ChartDates(IReadOnlyList<DateOnly> datesDescending, DateOnly anchorDate) =>
        datesDescending.Where(date => date <= anchorDate).Take(BalanceSheetReportContract.MaxChartDates).Order().ToArray();

    public static (DateOnly? Earlier, DateOnly? Later) Nearest(IReadOnlyList<DateOnly> dates, DateOnly requested) =>
        (dates.Where(date => date < requested).Order().Cast<DateOnly?>().LastOrDefault(),
         dates.Where(date => date > requested).Order().Cast<DateOnly?>().FirstOrDefault());

    private static string Signature(IReadOnlyList<BalanceSheetItemObservation> items)
    {
        var registered = BalanceSheetRowRegistry.RegisteredSourceItemIds.ToHashSet();
        return string.Join(';', items.Where(item => registered.Contains(item.SourceItemId))
            .OrderBy(item => item.SourceItemId).ThenBy(item => item.Value)
            .Select(item => string.Create(CultureInfo.InvariantCulture, $"{item.SourceItemId}={item.Value?.ToString(CultureInfo.InvariantCulture) ?? "null"}")));
    }

    private static FinancialStatementVersionRank Rank(BalanceSheetStatementHeader item) =>
        new(item.IsAudited, item.IsRepresented, item.PublishedAt);
}

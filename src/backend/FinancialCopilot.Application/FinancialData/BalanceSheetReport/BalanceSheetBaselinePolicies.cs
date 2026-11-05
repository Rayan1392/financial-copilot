using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.FinancialData.BalanceSheetReport;

/// <summary>
/// The date a baseline kind points to, or why it has none. <see cref="PeriodType"/> restricts the versions
/// eligible at that date (same-stage baselines need the anchor's report length).
/// </summary>
public sealed record BalanceSheetBaselineTarget(string Kind, DateOnly? Date, string? Reason, string? PeriodType = null);

/// <summary>
/// Resolves the five baseline kinds of spec §8.2 deterministically from eligible snapshot dates and
/// persisted fiscal evidence. Fiscal knowledge comes only from <see cref="FiscalCalendarPolicy"/>. Nothing is
/// ever snapped to a nearby date, 29 Esfand is never assumed, and no balance is derived by subtraction.
/// </summary>
public static class BalanceSheetBaselineResolver
{
    public const string MissingFiscalMetadata = "missing_fiscal_metadata";
    public const string FiscalCalendarChanged = "fiscal_calendar_changed";
    public const string AnchorNotOnFiscalGrid = "anchor_not_on_fiscal_grid";
    public const string NoEarlierReport = "no_earlier_report";
    public const string NoMatchingStage = "no_matching_stage";
    public const string NoSnapshotAtDate = BalanceSheetOutcomes.NoSnapshotAtDate;

    public static IReadOnlyList<BalanceSheetBaselineTarget> ResolveStandardTargets(
        BalanceSheetStatementHeader anchor,
        IReadOnlyList<DateOnly> eligibleDatesDescending,
        IReadOnlyList<BalanceSheetFiscalEvidence> fiscalEvidence)
    {
        var evidence = fiscalEvidence
            .Select(item => FiscalCalendarPolicy.TryParseJalaliDate(item.JalaliFiscalYearEnd, out var end)
                ? (Parsed: true, item.PeriodEnd, YearEnd: end)
                : (Parsed: false, item.PeriodEnd, YearEnd: default))
            .Where(item => item.Parsed)
            .Select(item => (item.PeriodEnd, item.YearEnd))
            .ToArray();
        var effectiveStarts = FiscalCalendarPolicy.ResolveEffectiveFiscalStarts(
            evidence, item => FiscalCalendarPolicy.FiscalStartFromYearEnd(item.YearEnd), item => item.PeriodEnd);
        bool IsRebased(DateOnly start) => effectiveStarts.TryGetValue(start, out var effective) && effective != start;

        var previousReport = eligibleDatesDescending.Where(date => date < anchor.PeriodEnd).Cast<DateOnly?>().FirstOrDefault();
        var previousReportTarget = previousReport is { } earlier
            ? new BalanceSheetBaselineTarget(BalanceSheetBaselineKinds.PreviousReport, earlier, null)
            : new BalanceSheetBaselineTarget(BalanceSheetBaselineKinds.PreviousReport, null, NoEarlierReport);

        if (!FiscalCalendarPolicy.TryParseJalaliDate(anchor.JalaliFiscalYearEnd, out var anchorYearEnd))
        {
            return
            [
                new(BalanceSheetBaselineKinds.FiscalYearEnd, null, MissingFiscalMetadata),
                new(BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, null, MissingFiscalMetadata),
                previousReportTarget,
                new(BalanceSheetBaselineKinds.SameStagePreviousYear, null, MissingFiscalMetadata)
            ];
        }

        // §8.2: the latest persisted JalaliFiscalYearEnd strictly earlier than the anchor's, from the company's
        // statements of any type. A transitional (rebased) anchor year still has a real previous year end.
        DateOnly? priorYearEnd = evidence.Select(item => item.YearEnd).Where(end => end < anchorYearEnd)
            .Distinct().OrderDescending().Cast<DateOnly?>().FirstOrDefault();

        var fiscalYearEnd = priorYearEnd is { } prior
            ? new BalanceSheetBaselineTarget(BalanceSheetBaselineKinds.FiscalYearEnd, prior, null)
            : new BalanceSheetBaselineTarget(BalanceSheetBaselineKinds.FiscalYearEnd, null, MissingFiscalMetadata);

        var anchorStart = FiscalCalendarPolicy.FiscalStartFromYearEnd(anchorYearEnd);
        var anchorYearIsRegular = !IsRebased(anchorStart) &&
                                  FiscalCalendarPolicy.JalaliEndAfterMonths(anchorStart, 12) == anchorYearEnd;
        BalanceSheetBaselineTarget previousQuarter;
        if (!anchorYearIsRegular)
        {
            previousQuarter = new(BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, null, FiscalCalendarChanged);
        }
        else
        {
            var grid = Enumerable.Range(1, 4).Select(k => FiscalCalendarPolicy.JalaliEndAfterMonths(anchorStart, 3 * k)).ToArray();
            var index = Array.IndexOf(grid, anchor.PeriodEnd);
            previousQuarter = index switch
            {
                < 0 => new(BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, null, AnchorNotOnFiscalGrid),
                0 => priorYearEnd is { } previousYearEnd
                    ? new(BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, previousYearEnd, null)
                    : new(BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, null, MissingFiscalMetadata),
                _ => new(BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, grid[index - 1], null)
            };
        }

        BalanceSheetBaselineTarget sameStage;
        var months = BalanceSheetStatementSelector.PeriodMonths(anchor.PeriodType);
        if (priorYearEnd is not { } previousEnd)
        {
            sameStage = new(BalanceSheetBaselineKinds.SameStagePreviousYear, null, MissingFiscalMetadata);
        }
        else
        {
            var previousStart = FiscalCalendarPolicy.FiscalStartFromYearEnd(previousEnd);
            var previousYearIsRegular = !IsRebased(previousStart) &&
                                        FiscalCalendarPolicy.JalaliEndAfterMonths(previousStart, 12) == previousEnd &&
                                        FiscalCalendarPolicy.JalaliMonth(previousEnd) == FiscalCalendarPolicy.JalaliMonth(anchorYearEnd) &&
                                        anchorYearIsRegular;
            if (!previousYearIsRegular)
                sameStage = new(BalanceSheetBaselineKinds.SameStagePreviousYear, null, FiscalCalendarChanged);
            else if (months == 0 || FiscalCalendarPolicy.JalaliEndAfterMonths(anchorStart, months) != anchor.PeriodEnd)
                sameStage = new(BalanceSheetBaselineKinds.SameStagePreviousYear, null, NoMatchingStage);
            else
                sameStage = new(BalanceSheetBaselineKinds.SameStagePreviousYear,
                    FiscalCalendarPolicy.JalaliEndAfterMonths(previousStart, months), null, anchor.PeriodType);
        }

        return [fiscalYearEnd, previousQuarter, previousReportTarget, sameStage];
    }

    public static BalanceSheetBaselineTarget ResolveExplicitTarget(DateOnly baselineDate) =>
        new(BalanceSheetBaselineKinds.Explicit, baselineDate, null);
}

/// <summary>
/// v1 baseline source policy (spec §8.3, PD-26): the baseline column is always the separately stored
/// snapshot for the exact baseline date, chosen by the same version ranking, one record per column. The
/// anchor-link wording and the post-v1 embedded restated comparative plug in here only after E-2 verifies a
/// deterministic link; in v1 <see cref="AnchorLinkVerified"/> is always false.
/// </summary>
public static class BalanceSheetBaselineSourcePolicy
{
    public const string StoredSnapshot = "stored_snapshot";
    public const bool AnchorLinkVerified = false;
}

/// <summary>Per-row changes and the ratio difference between an anchor and one baseline snapshot (spec §8.4).</summary>
public static class BalanceSheetComparisonBuilder
{
    public static IEnumerable<BalanceSheetRow> AllRows(BalanceSheetSnapshot snapshot) =>
        snapshot.Sections.SelectMany(section => section.Groups.SelectMany(group =>
                    group.Subtotal is null ? group.Rows : group.Rows.Append(group.Subtotal))
                .Concat(section.Total is null ? [] : [section.Total]))
            .Append(snapshot.FinalTotals.TotalAssets)
            .Append(snapshot.FinalTotals.TotalLiabilitiesAndEquity)
            .Concat(snapshot.AnalyticalItems);

    public static IReadOnlyDictionary<string, BalanceSheetChange> RowChanges(BalanceSheetSnapshot anchor, BalanceSheetSnapshot baseline)
    {
        var baselineRows = AllRows(baseline).ToDictionary(row => row.RowId);
        return AllRows(anchor).ToDictionary(row => row.RowId, row => Change(row, baselineRows.GetValueOrDefault(row.RowId)));
    }

    public static BalanceSheetChange Change(BalanceSheetRow anchor, BalanceSheetRow? baseline)
    {
        if (baseline is null)
            return new BalanceSheetChange(null, null, BalanceSheetChangeStates.Unavailable, "baseline_not_observed");
        if (anchor.Cell.State != BalanceSheetCellStates.Available || anchor.Cell.Value is not { } current)
            return new BalanceSheetChange(null, null, BalanceSheetChangeStates.Unavailable, $"anchor_{anchor.Cell.State}");
        if (baseline.Cell.State != BalanceSheetCellStates.Available || baseline.Cell.Value is not { } previous)
            return new BalanceSheetChange(null, null, BalanceSheetChangeStates.Unavailable, $"baseline_{baseline.Cell.State}");
        if (!string.Equals(anchor.Unit, baseline.Unit, StringComparison.Ordinal))
            return new BalanceSheetChange(null, null, BalanceSheetChangeStates.Unavailable, "unit_mismatch");
        var amount = current - previous;
        if (previous > 0m)
            return new BalanceSheetChange(amount, amount / previous * 100m, BalanceSheetChangeStates.Available, null);
        return new BalanceSheetChange(amount, null, BalanceSheetChangeStates.PercentUnavailable,
            previous == 0m ? "zero_baseline" : "negative_baseline");
    }

    /// <summary>Percentage-point difference from unrounded ratios.</summary>
    public static BalanceSheetRatioChange RatioChange(BalanceSheetRatio anchor, BalanceSheetRatio baseline)
    {
        if (anchor.Value is not { } current)
            return new BalanceSheetRatioChange(null, baseline.Value, null, BalanceSheetChangeStates.Unavailable, $"anchor_{anchor.Reason}");
        if (baseline.Value is not { } previous)
            return new BalanceSheetRatioChange(current, null, null, BalanceSheetChangeStates.Unavailable, $"baseline_{baseline.Reason}");
        return new BalanceSheetRatioChange(current, previous, current - previous, BalanceSheetChangeStates.Available, null);
    }
}

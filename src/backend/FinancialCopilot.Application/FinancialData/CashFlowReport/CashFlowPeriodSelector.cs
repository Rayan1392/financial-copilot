using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public sealed record CashFlowStatementHeader(
    Guid StatementId, string ExternalStatementId, DateOnly PeriodStart, DateOnly PeriodEnd, string PeriodType,
    bool IsAudited, bool IsRepresented, bool IsComposing, DateOnly? PublishedAt, string? JalaliPublishedAt,
    string? JalaliFiscalYearEnd, string? AssumedScale, bool? SourceIsComposing);

public sealed record CashFlowItemObservation(int SourceItemId, Guid LineItemId, decimal? Value, string? SourceTitle);
public sealed record CashFlowFiscalEvidence(DateOnly PeriodEnd, string? JalaliFiscalYearEnd);

public sealed record CashFlowPeriodRequest(
    string View = "default", int? RequestedMonths = null, int? RequestedFiscalYear = null,
    int? PeriodCount = null, DateOnly? RequestedDate = null, bool PreviousReport = false);

public sealed record CashFlowPeriodColumn(
    string Kind, int? FiscalYear, int PeriodMonths, DateOnly PeriodEnd, CashFlowStatementHeader? Statement,
    CashFlowStatementHeader? Predecessor, string State, string? Reason, DateOnly? FiscalStart);

public sealed record CashFlowPeriodSelection(
    string Outcome, CashFlowStatementHeader? Anchor, IReadOnlyList<CashFlowPeriodColumn> Columns,
    IReadOnlyList<DateOnly> NearestPeriods, bool WindowTruncated);

/// <summary>Selects stored fiscal slots; it never derives financial amounts.</summary>
public static class CashFlowPeriodSelector
{
    private sealed record Slot(CashFlowStatementHeader Header, int Months, DateOnly? Start, int? Year, bool Boundary);
    private sealed record Pick(CashFlowStatementHeader? Header, string? Error);

    public static int PeriodMonths(string periodType) => periodType switch
    {
        "ThreeMonths" => 3, "SixMonths" => 6, "NineMonths" => 9, "TwelveMonths" => 12, _ => 0
    };

    public static CashFlowPeriodSelection Select(
        IReadOnlyList<CashFlowStatementHeader> headers,
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>> observations,
        CashFlowPeriodRequest request, bool windowTruncated = false, IReadOnlyList<CashFlowFiscalEvidence>? fiscalEvidence = null)
    {
        var eligible = headers.DistinctBy(x => x.StatementId)
            .Where(x => !x.IsComposing && (x.SourceIsComposing is null || x.SourceIsComposing == x.IsComposing) &&
                        x.AssumedScale == "MillionRials" && PeriodMonths(x.PeriodType) != 0).ToArray();
        if (eligible.Length == 0)
        {
            var standaloneWithUnsupportedScale = headers.Any(x => !x.IsComposing &&
                (x.SourceIsComposing is null || x.SourceIsComposing == x.IsComposing) &&
                PeriodMonths(x.PeriodType) != 0 && x.AssumedScale != "MillionRials");
            return new(standaloneWithUnsupportedScale ? "ineligible_storage_scale" : "no_standalone_cash_flow", null, [], [], windowTruncated);
        }

        var evidence = eligible.Select(x => new CashFlowFiscalEvidence(x.PeriodEnd, x.JalaliFiscalYearEnd))
            .Concat(fiscalEvidence ?? []).Select(item =>
                FiscalCalendarPolicy.TryParseJalaliDate(item.JalaliFiscalYearEnd, out var end)
                    ? (Valid: true, item.PeriodEnd, YearEnd: end) : (Valid: false, item.PeriodEnd, YearEnd: default))
            .Where(item => item.Valid).ToArray();
        var effective = FiscalCalendarPolicy.ResolveEffectiveFiscalStarts(evidence,
            x => FiscalCalendarPolicy.FiscalStartFromYearEnd(x.YearEnd), x => x.PeriodEnd);
        var slots = eligible.Select(header =>
        {
            var months = PeriodMonths(header.PeriodType);
            if (!FiscalCalendarPolicy.TryParseJalaliDate(header.JalaliFiscalYearEnd, out _))
                return new Slot(header, months, null, null, false);
            var original = FiscalCalendarPolicy.ResolveFiscalStart(header.JalaliFiscalYearEnd, header.PeriodStart);
            var start = effective[original];
            return new Slot(header, months, start, FiscalCalendarPolicy.FiscalYearLabel(start),
                FiscalCalendarPolicy.JalaliEndAfterMonths(start, months) == header.PeriodEnd);
        }).ToArray();

        var matches = slots.Where(x => (request.RequestedMonths is null || x.Months == request.RequestedMonths) &&
                                        (request.RequestedFiscalYear is null || x.Year == request.RequestedFiscalYear) &&
                                        (request.RequestedDate is null || x.Header.PeriodEnd == request.RequestedDate) &&
                                        (request.View != "annual" || x.Months == 12))
            .OrderByDescending(x => x.Header.PeriodEnd).ToArray();
        if (request.PreviousReport && matches.Length > 0)
        {
            var latest = matches[0].Header.PeriodEnd;
            matches = matches.Where(x => x.Header.PeriodEnd < latest).ToArray();
        }
        if (matches.Length == 0)
        {
            var nearest = slots.OrderBy(x => request.RequestedDate is { } date ? Math.Abs(x.Header.PeriodEnd.DayNumber - date.DayNumber) :
                    request.RequestedFiscalYear is { } year && x.Year is { } slotYear ? Math.Abs(year - slotYear) * 12 + Math.Abs((request.RequestedMonths ?? x.Months) - x.Months) : 0)
                .ThenByDescending(x => x.Header.PeriodEnd).Select(x => x.Header.PeriodEnd).Distinct().Take(3).ToArray();
            return new("no_statement_for_period", null, [], nearest, windowTruncated);
        }

        Pick Choose(IEnumerable<Slot> candidates)
        {
            var ordered = FinancialStatementVersionPolicy.Rank(candidates.Select(x => x.Header),
                x => new(x.IsAudited, x.IsRepresented, x.PublishedAt));
            var ties = FinancialStatementVersionPolicy.TopTieGroup(ordered, x => new(x.IsAudited, x.IsRepresented, x.PublishedAt));
            if (ties.Count > 1 && ties.Any(x => !observations.ContainsKey(x.StatementId))) return new(null, "line_items_unavailable");
            var pick = FinancialStatementVersionPolicy.Pick(ties, x => Signature(observations.GetValueOrDefault(x.StatementId) ?? []),
                x => x.ExternalStatementId, x => x.StatementId);
            return new(pick.Winner, pick.IsConflict ? "conflicting_report_revision" : null);
        }

        var anchorSlot = matches[0];
        var anchor = Choose(matches.Where(x => x.Header.PeriodEnd == anchorSlot.Header.PeriodEnd && x.Months == anchorSlot.Months));
        if (anchor.Error is not null) return new(anchor.Error, null, [], [], windowTruncated);
        var selectedAnchor = anchor.Header!;
        anchorSlot = slots.Single(x => x.Header.StatementId == selectedAnchor.StatementId);

        CashFlowPeriodColumn Column(string kind, Slot? slot, Pick chosen, string? missingReason = null) =>
            new(kind, slot?.Year, slot?.Months ?? 0, slot?.Header.PeriodEnd ?? default,
                chosen.Header, null, chosen.Error is null && chosen.Header is not null ? "available" : "unavailable",
                chosen.Error ?? (chosen.Header is null ? missingReason : null), slot?.Start);

        if (request.View == "default")
        {
            var columns = new List<CashFlowPeriodColumn> { Column("anchor", anchorSlot, anchor) };
            DateOnly? target = null;
            string? reason = null;
            if (!FiscalCalendarPolicy.TryParseJalaliDate(selectedAnchor.JalaliFiscalYearEnd, out var anchorEnd))
                reason = "missing_fiscal_metadata";
            else
            {
                var originalStart = FiscalCalendarPolicy.FiscalStartFromYearEnd(anchorEnd);
                var priorEnd = evidence.Select(x => x.YearEnd).Where(end => end < anchorEnd).Distinct().OrderDescending().Cast<DateOnly?>().FirstOrDefault();
                if (priorEnd is null) reason = "missing_fiscal_metadata";
                else
                {
                    var priorStart = FiscalCalendarPolicy.FiscalStartFromYearEnd(priorEnd.Value);
                    var changed = effective.GetValueOrDefault(originalStart, originalStart) != originalStart ||
                        effective.GetValueOrDefault(priorStart, priorStart) != priorStart ||
                        FiscalCalendarPolicy.JalaliEndAfterMonths(originalStart, 12) != anchorEnd ||
                        FiscalCalendarPolicy.JalaliEndAfterMonths(priorStart, 12) != priorEnd.Value ||
                        FiscalCalendarPolicy.JalaliMonth(anchorEnd) != FiscalCalendarPolicy.JalaliMonth(priorEnd.Value);
                    if (changed) reason = "fiscal_calendar_changed";
                    else if (!anchorSlot.Boundary) reason = "fiscal_boundary_mismatch";
                    else target = FiscalCalendarPolicy.JalaliEndAfterMonths(priorStart, anchorSlot.Months);
                }
            }
            var previous = target is null ? [] : slots.Where(x => x.Header.PeriodEnd == target && x.Months == anchorSlot.Months && x.Boundary).ToArray();
            var priorSlot = previous.FirstOrDefault();
            var prior = priorSlot is null ? new Pick(null, null) : Choose(previous);
            if (priorSlot is not null) columns.Add(Column("same_stage_previous_year", priorSlot, prior));
            else columns.Add(new("same_stage_previous_year", null, anchorSlot.Months, target ?? default, null, null,
                "unavailable", reason ?? "no_matching_stage", null));
            return new("available", selectedAnchor, columns, [], windowTruncated);
        }

        if (request.View == "annual")
        {
            var count = Math.Clamp(request.PeriodCount ?? 3, 1, 5);
            var annual = slots.Where(x => x.Months == 12 && x.Header.PeriodEnd <= selectedAnchor.PeriodEnd)
                .GroupBy(x => x.Header.PeriodEnd).OrderByDescending(x => x.Key).Take(count).OrderBy(x => x.Key)
                .Select(group => Column("fiscal_year", group.First(), Choose(group))).ToArray();
            return new("available", selectedAnchor, annual, [], windowTruncated);
        }

        if (request.View == "quarterly")
        {
            if (anchorSlot.Start is null)
                return new("available", selectedAnchor,
                    [new("standalone_quarter", null, anchorSlot.Months, selectedAnchor.PeriodEnd, selectedAnchor, null,
                        "unavailable", "missing_fiscal_metadata", null)], [], windowTruncated);
            if (!anchorSlot.Boundary)
                return new("available", selectedAnchor,
                    [new("standalone_quarter", anchorSlot.Year, anchorSlot.Months, selectedAnchor.PeriodEnd, selectedAnchor, null,
                        "unavailable", "fiscal_boundary_mismatch", anchorSlot.Start)], [], windowTruncated);
            var count = Math.Clamp(request.PeriodCount ?? 4, 1, 8);
            var grid = slots.Where(x => x.Start is not null).Select(x => x.Start!.Value).Distinct()
                .SelectMany(start => new[] { 3, 6, 9, 12 }.Select(months => (Start: start, Months: months, End: FiscalCalendarPolicy.JalaliEndAfterMonths(start, months))))
                .Where(x => x.End <= selectedAnchor.PeriodEnd).OrderByDescending(x => x.End).Take(count).OrderBy(x => x.End).ToArray();
            var columns = new List<CashFlowPeriodColumn>();
            foreach (var cell in grid)
            {
                var candidates = slots.Where(x => x.Start == cell.Start && x.Months == cell.Months && x.Header.PeriodEnd == cell.End && x.Boundary).ToArray();
                var chosen = Choose(candidates);
                var previous = cell.Months == 3 ? new Pick(null, null) : Choose(slots.Where(x => x.Start == cell.Start && x.Months == cell.Months - 3 && x.Boundary));
                var reason = chosen.Error ?? previous.Error ?? (chosen.Header is null || cell.Months > 3 && previous.Header is null ? "missing_predecessor" : null);
                columns.Add(new("standalone_quarter", FiscalCalendarPolicy.FiscalYearLabel(cell.Start), cell.Months, cell.End,
                    chosen.Header, previous.Header, reason is null ? "available" : "unavailable", reason, cell.Start));
            }
            return new("available", selectedAnchor, columns, [], windowTruncated);
        }
        return new("unsupported_request", selectedAnchor, [], [], windowTruncated);
    }

    private static string Signature(IReadOnlyList<CashFlowItemObservation> items)
    {
        var ids = CashFlowLineRegistry.ReadableSourceItemIds.ToHashSet();
        return string.Join(';', items.Where(item => ids.Contains(item.SourceItemId)).OrderBy(item => item.SourceItemId).ThenBy(item => item.Value)
            .Select(item => string.Create(CultureInfo.InvariantCulture, $"{item.SourceItemId}={item.Value?.ToString(CultureInfo.InvariantCulture) ?? "null"}")));
    }
}

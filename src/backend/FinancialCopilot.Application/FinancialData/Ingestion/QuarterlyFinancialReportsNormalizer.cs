using System.Globalization;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public static class QuarterlyFinancialReportsNormalizer
{
    private static readonly (int Id, string Label, string CatalogId, QuarterlyRowPolicy Policy)[] DirectRows =
    [
        (15, "فروش", "07c03a70-a1fa-4b9b-861c-937748e2a1dc", QuarterlyRowPolicy.CumulativeFlow),
        (1, "بهای تمام‌شده کالای فروش‌رفته", "57940eb5-27f8-42b1-8827-d64dc49eab2d", QuarterlyRowPolicy.CumulativeFlow),
        (139, "سود (زیان) ناخالص", "917b2bbb-3859-42f0-9864-2eac3f98a3b3", QuarterlyRowPolicy.CumulativeFlow),
        (5, "هزینه‌های عمومی، اداری و تشکیلاتی", "c04b4fae-69db-48d9-a63e-c96a932e7b89", QuarterlyRowPolicy.CumulativeFlow),
        (39, "خالص سایر درآمدها (هزینه‌ها)ی عملیاتی", "17f91e30-ba52-4789-89d0-3ffd3ac61c17", QuarterlyRowPolicy.CumulativeFlow),
        (140, "سود (زیان) عملیاتی", "f2a6e460-176f-4426-a4e7-78d4ffd86ba6", QuarterlyRowPolicy.CumulativeFlow),
        (12, "هزینه‌های مالی", "b8342891-ad45-4c4b-a4dd-361bb5f37adb", QuarterlyRowPolicy.CumulativeFlow),
        (40, "خالص سایر درآمدها و هزینه‌های غیرعملیاتی", "e08f0ce7-ab6a-4a24-8fab-efc3f93c140f", QuarterlyRowPolicy.CumulativeFlow),
        (142, "سود (زیان) عملیات در حال تداوم قبل از مالیات", "1cbc0ee5-21bb-4b9d-b5ba-72c3b84731b3", QuarterlyRowPolicy.CumulativeFlow),
        (334, "مالیات", "31f4454a-78d2-41fb-8caa-b86564423cd6", QuarterlyRowPolicy.CumulativeFlow),
        (279, "سود (زیان) خالص عملیات در حال تداوم", "abdbd529-7e27-43cc-a955-10ae19aea4cb", QuarterlyRowPolicy.CumulativeFlow),
        (278, "سود (زیان) عملیات متوقف‌شده پس از اثر مالیاتی", "cb6c92c3-cff6-40c1-a85a-db9fde4ba791", QuarterlyRowPolicy.CumulativeFlow),
        (143, "سود (زیان) خالص", "bd36909d-abf8-44c2-b7db-5e53dd678b53", QuarterlyRowPolicy.CumulativeFlow),
        (160, "سود هر سهم پس از کسر مالیات", "2146da26-b28e-4d04-b50c-f49fc9b70e61", QuarterlyRowPolicy.CumulativeEps),
        (153, "تعداد سهام شرکت (هزار سهم)", "e98e6c00-7204-44b1-a279-bbbb68781d3c", QuarterlyRowPolicy.ReportedPointInTime)
    ];

    private static readonly PersianCalendar Jalali = new();

    public static IReadOnlyList<(int SourceItemId, string CatalogId)> ApprovedMappings =>
        DirectRows.Select(row => (row.Id, row.CatalogId)).ToArray();

    public static QuarterlyFinancialReportTableResult Normalize(
        string companySymbol,
        string? companyName,
        string externalCompanyId,
        IReadOnlyList<QuarterlyReportObservation> reports,
        int quarterCount,
        DateOnly? endingPeriod,
        DateTimeOffset generatedAtUtc)
    {
        if (quarterCount is < 1 or > 12)
            return Unavailable(companySymbol, companyName, externalCompanyId, quarterCount, generatedAtUtc,
                "invalid_quarter_count", "Quarter count must be from 1 to 12.");

        var candidates = reports.Where(report => report.ProviderName == "NoavaranCurrentApi" &&
                report.StatementType == "IncomeStatement" && report.ExternalCompanyId == externalCompanyId)
            .ToList();
        var requestedSlots = candidates
            .Where(report => report.PeriodMonths is 3 or 6 or 9 or 12)
            .GroupBy(report => (report.PeriodStart, report.PeriodMonths))
            .Select(group => new
            {
                group.Key.PeriodStart,
                group.Key.PeriodMonths,
                PeriodEnd = group.Max(report => report.PeriodEnd)
            })
            .OrderByDescending(slot => slot.PeriodEnd)
            .Take(quarterCount)
            .ToList();
        var requiredSlots = requestedSlots
            .Select(slot => (slot.PeriodStart, slot.PeriodMonths))
            .Concat(requestedSlots.Where(slot => slot.PeriodMonths > 3)
                .Select(slot => (slot.PeriodStart, PeriodMonths: slot.PeriodMonths - 3)))
            .ToHashSet();
        var compatible = candidates.Where(report => requiredSlots.Contains((report.PeriodStart, report.PeriodMonths)))
            .ToList();
        if (compatible.Count == 0)
            return Unavailable(companySymbol, companyName, externalCompanyId, quarterCount, generatedAtUtc,
                "no_compatible_reports", "No persisted Noavaran income-statement periods match the requested fiscal range.");
        var scopeGroups = compatible.GroupBy(report => report.IsComposing).ToList();
        if (scopeGroups.Count != 1)
            return Unavailable(companySymbol, companyName, externalCompanyId, quarterCount, generatedAtUtc,
                "conflicting_report_scope", "Separate and consolidated reports cannot be combined; specify one statement scope.");

        var isComposing = scopeGroups[0].Key;
        var reportByYearAndDuration = new Dictionary<(DateOnly FiscalStart, int Months), QuarterlyReportObservation>();
        foreach (var group in compatible.GroupBy(report => (report.PeriodStart, report.PeriodMonths)))
        {
            var ranked = group.OrderByDescending(report => report.IsAudited)
                .ThenByDescending(report => report.IsRepresented)
                .ThenByDescending(report => report.AnnouncementDate, StringComparer.Ordinal)
                .ThenByDescending(report => report.ExternalReportId, StringComparer.Ordinal)
                .ToList();
            var winner = ranked[0];
            var unresolved = ranked.Where(report => report.IsAudited == winner.IsAudited &&
                report.IsRepresented == winner.IsRepresented &&
                string.Equals(report.AnnouncementDate, winner.AnnouncementDate, StringComparison.Ordinal) &&
                string.Equals(report.ExternalReportId, winner.ExternalReportId, StringComparison.Ordinal)).ToArray();
            if (unresolved.Length > 1 && unresolved.Skip(1).Any(report => !SourceValuesEqual(winner, report)))
                return Unavailable(companySymbol, companyName, externalCompanyId, quarterCount, generatedAtUtc,
                    "conflicting_report_revision", "Top-priority reports have conflicting source values and no deterministic version winner.");
            reportByYearAndDuration[group.Key] = unresolved.OrderByDescending(report => report.ReportId).First();
        }

        var allPeriods = new List<(QuarterlyFiscalPeriod Period, QuarterlyReportObservation? Current, QuarterlyReportObservation? Previous)>();
        foreach (var yearGroup in reportByYearAndDuration.Keys.GroupBy(key => key.FiscalStart))
        {
            var year = yearGroup.Key;
            var lastReportedQuarter = yearGroup.Max(key => key.Months) / 3;
            for (var quarter = 1; quarter <= lastReportedQuarter; quarter++)
            {
                var duration = quarter * 3;
                reportByYearAndDuration.TryGetValue((year, duration), out var current);
                reportByYearAndDuration.TryGetValue((year, duration - 3), out var previous);
                if (quarter == 1) previous = null;
                var isCompatiblePair = current is not null && (quarter == 1 ||
                    previous is not null && previous.PeriodStart == current.PeriodStart &&
                    current.PeriodEnd > previous.PeriodEnd &&
                    MonthsBetween(previous.PeriodEnd, current.PeriodEnd) == 3);
                var end = current?.PeriodEnd ?? previous?.PeriodEnd.AddMonths(3) ?? JalaliEndAfterMonths(year, quarter * 3);
                var start = quarter == 1 ? year : (previous?.PeriodEnd.AddDays(1) ?? year);
                if (current is not null && !IsExpectedYtdBoundary(year, current.PeriodEnd, duration))
                    isCompatiblePair = false;
                var fiscalYear = Jalali.GetYear(year.ToDateTime(TimeOnly.MinValue));
                var period = new QuarterlyFiscalPeriod(fiscalYear, quarter, start, end);
                allPeriods.Add((period, current,
                    quarter == 1 || isCompatiblePair ? previous : null));
            }
        }

        allPeriods = allPeriods.OrderByDescending(item => item.Period.End).ToList();
        if (endingPeriod.HasValue)
            allPeriods = allPeriods.Where(item => item.Period.End <= endingPeriod.Value).ToList();
        var selected = allPeriods.Take(quarterCount).ToList();
        var cellMap = new Dictionary<(string RowId, string Period), QuarterlyFinancialCell>();
        foreach (var item in selected)
        {
            foreach (var row in DirectRows)
            {
                var key = ($"source_{row.Id}", item.Period.Label);
                var unit = Unit(row.Id);
                if (item.Current is null)
                {
                    cellMap[key] = Missing(QuarterlyCellState.MissingPredecessor, "Adjacent compatible cumulative report is unavailable.", unit, item.Current, item.Previous, row.Id);
                    continue;
                }

                var currentValue = FindItem(item.Current, row.Id);
                var previousValue = item.Previous is null
                    ? (Present: false, Value: (decimal?)null, LineId: (Guid?)null, CatalogId: (Guid?)null, Unit: (string?)null)
                    : FindItem(item.Previous, row.Id);
                if (row.Policy != QuarterlyRowPolicy.ReportedPointInTime &&
                    !IsExpectedYtdBoundary(item.Current.PeriodStart, item.Current.PeriodEnd, item.Current.PeriodMonths))
                {
                    cellMap[key] = Missing(QuarterlyCellState.UnsupportedFiscalPeriod,
                        "The report period does not match its fiscal year-to-date boundary.", unit, item.Current, item.Previous, row.Id);
                    continue;
                }
                if (!currentValue.Present)
                {
                    cellMap[key] = Missing(QuarterlyCellState.MissingObservation, "A required source line item is missing.", unit,
                        item.Current, item.Previous, row.Id);
                    continue;
                }
                if (!StorageScaleIsSupported(item.Current))
                {
                    cellMap[key] = Missing(QuarterlyCellState.IncompatibleObservation,
                        "The report's persisted amount scale or source amount unit is missing or unsupported.", unit,
                        item.Current, item.Previous, row.Id);
                    continue;
                }
                if (currentValue.CatalogId != Guid.Parse(row.CatalogId) || string.IsNullOrWhiteSpace(currentValue.Unit))
                {
                    cellMap[key] = Missing(QuarterlyCellState.IncompatibleObservation,
                        "The source row does not carry its approved catalog identity and declared unit.", unit,
                        item.Current, item.Previous, row.Id);
                    continue;
                }
                if (row.Policy != QuarterlyRowPolicy.ReportedPointInTime && item.Period.Quarter > 1 && !previousValue.Present)
                {
                    cellMap[key] = Missing(QuarterlyCellState.MissingPredecessor,
                        "The adjacent cumulative predecessor report or source line item is missing.", unit,
                        item.Current, item.Previous, row.Id);
                    continue;
                }
                if (row.Policy != QuarterlyRowPolicy.ReportedPointInTime && item.Period.Quarter > 1 &&
                    (currentValue.CatalogId != previousValue.CatalogId || string.IsNullOrWhiteSpace(previousValue.Unit) ||
                     !string.Equals(currentValue.Unit, previousValue.Unit, StringComparison.Ordinal)))
                {
                    cellMap[key] = Missing(QuarterlyCellState.IncompatibleObservation,
                        "Cumulative operands use different source catalog identities or units.", unit,
                        item.Current, item.Previous, row.Id);
                    continue;
                }
                if (row.Policy != QuarterlyRowPolicy.ReportedPointInTime && item.Period.Quarter > 1 &&
                    !StorageScaleIsSupported(item.Previous!))
                {
                    cellMap[key] = Missing(QuarterlyCellState.IncompatibleObservation,
                        "The adjacent report's persisted amount scale or source amount unit is missing or unsupported.", unit,
                        item.Current, item.Previous, row.Id);
                    continue;
                }

                var value = row.Policy == QuarterlyRowPolicy.ReportedPointInTime || item.Period.Quarter == 1
                    ? currentValue.Value
                    : currentValue.Value!.Value - previousValue.Value!.Value;
                cellMap[key] = new QuarterlyFinancialCell(value, QuarterlyCellState.Available, null, unit, value == 0,
                    item.PerviousReports(), item.SourceLineIds(row.Id), row.Policy.ToString(),
                    item.Period.Quarter == 1 || row.Policy == QuarterlyRowPolicy.ReportedPointInTime
                        ? [currentValue.Value]
                        : [currentValue.Value, previousValue.Value],
                    CurrencyCode($"source_{row.Id}"), ValueScale($"source_{row.Id}"));
            }
        }

        foreach (var item in selected)
        {
            var key = ("gross_margin", item.Period.Label);
            cellMap[key] = GrossMargin(cellMap.GetValueOrDefault(("source_139", item.Period.Label)),
                cellMap.GetValueOrDefault(("source_15", item.Period.Label)), item.Period);
        }

        var rows = new List<QuarterlyFinancialRow>();
        var display = new List<(string Id, string Label, int? SourceId, string? Catalog, QuarterlyRowPolicy Policy, string Unit)>();
        display.Add(("source_15", "فروش", 15, DirectRows.Single(row => row.Id == 15).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(15)));
        display.Add(("source_1", "بهای تمام‌شده کالای فروش‌رفته", 1, DirectRows.Single(row => row.Id == 1).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(1)));
        display.Add(("source_139", "سود (زیان) ناخالص", 139, DirectRows.Single(row => row.Id == 139).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(139)));
        display.Add(("gross_margin", "مارجین ناخالص", null, null, QuarterlyRowPolicy.DerivedGrossMargin, "percent"));
        display.AddRange(DirectRows.Where(row => row.Id is not (15 or 1 or 139)).Select(row =>
            ($"source_{row.Id}", row.Label, (int?)row.Id, (string?)row.CatalogId, row.Policy, Unit(row.Id))));

        for (var index = 0; index < display.Count; index++)
        {
            var row = display[index];
            var cells = selected.ToDictionary(item => item.Period.Label,
                item => cellMap.GetValueOrDefault((row.Id, item.Period.Label)) ?? Missing(
                    QuarterlyCellState.MissingObservation, "No source observation was available.", row.Unit, null, null, row.SourceId));
            rows.Add(new QuarterlyFinancialRow(row.Id, index + 1, row.Label, row.SourceId,
                row.Catalog is null ? null : Guid.Parse(row.Catalog), row.Policy, row.Unit, cells,
                row.SourceId is null ? null : selected.SelectMany(item => item.Current?.Items ?? [])
                    .FirstOrDefault(source => source.SourceItemId == row.SourceId.Value)?.CatalogTitleFa));
        }

        var comparisons = new List<QuarterlyQoQComparison>();
        for (var i = 0; i < selected.Count - 1; i++)
        {
            var current = selected[i];
            var previous = selected[i + 1];
            foreach (var row in rows)
            {
                var currentCell = row.Cells[current.Period.Label];
                var previousCell = row.Cells[previous.Period.Label];
                comparisons.Add(Compare(row.RowId, current.Period, previous.Period, currentCell, previousCell));
            }
        }

        var coverageStatus = selected.Count < quarterCount ? "Partial" : "Available";
        var coverageReason = selected.Count < quarterCount
            ? $"Only {selected.Count} fiscal quarters could be established from persisted report evidence; {quarterCount} were requested."
            : null;
        return new QuarterlyFinancialReportTableResult(companySymbol, companyName, externalCompanyId,
            "NoavaranCurrentApi", "IncomeStatement", isComposing, quarterCount,
            selected.Select(item => item.Period).ToArray(), rows, comparisons,
            compatible.Select(report => (DateTimeOffset?)report.LastSynchronizedAt).Max(), coverageStatus, coverageReason, generatedAtUtc,
            selected.SelectMany(item => new[] { item.Current, item.Previous })
                .Where(report => report is not null).Cast<QuarterlyReportObservation>().DistinctBy(report => report.ReportId)
                .Select(report => new QuarterlySelectedReportReference(report.ReportId, report.ExternalReportId,
                    report.ProviderName, report.StatementType, report.PeriodStart, report.PeriodEnd, report.PeriodMonths,
                    report.IsAudited, report.IsRepresented, report.IsComposing, report.SourcePayloadChecksum,
                    report.LastSynchronizedAt, report.AssumedScale, report.SourceAmountUnit, report.CurrencyCode,
                    report.AccountingBasis, report.ReportRevisionLineage, report.FiscalYearEvidence)).ToArray());
    }

    private static QuarterlyFinancialReportTableResult Unavailable(string symbol, string? name, string id,
        int count, DateTimeOffset now, string reason, string detail)
    {
        var orderedRows = new List<(string Id, string Label, int? SourceId, string? Catalog, QuarterlyRowPolicy Policy, string Unit)>
        {
            ("source_15", DirectRows.Single(row => row.Id == 15).Label, 15, DirectRows.Single(row => row.Id == 15).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(15)),
            ("source_1", DirectRows.Single(row => row.Id == 1).Label, 1, DirectRows.Single(row => row.Id == 1).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(1)),
            ("source_139", DirectRows.Single(row => row.Id == 139).Label, 139, DirectRows.Single(row => row.Id == 139).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(139)),
            ("gross_margin", "Ù…Ø§Ø±Ø¬ÛŒÙ† Ù†Ø§Ø®Ø§Ù„Øµ", null, null, QuarterlyRowPolicy.DerivedGrossMargin, "percent")
        };
        orderedRows.AddRange(DirectRows.Where(row => row.Id is not (15 or 1 or 139)).Select(row =>
            ($"source_{row.Id}", row.Label, (int?)row.Id, (string?)row.CatalogId, row.Policy, Unit(row.Id))));
        var rows = orderedRows.Select((row, index) => new QuarterlyFinancialRow(row.Id, index + 1, row.Label,
            row.SourceId, row.Catalog is null ? null : Guid.Parse(row.Catalog), row.Policy, row.Unit,
            new Dictionary<string, QuarterlyFinancialCell>())).ToArray();
        return new(symbol, name, id, "NoavaranCurrentApi", "IncomeStatement", null, count,
            [], rows, [], null, "Unavailable", $"{reason}: {detail}", now, []);
    }

    private static bool IsExpectedYtdBoundary(DateOnly fiscalStart, DateOnly end, int months)
    {
        return JalaliEndAfterMonths(fiscalStart, months) == end;
    }

    private static DateOnly JalaliEndAfterMonths(DateOnly fiscalStart, int months) =>
        DateOnly.FromDateTime(Jalali.AddMonths(fiscalStart.ToDateTime(TimeOnly.MinValue), months).AddDays(-1));

    private static int MonthsBetween(DateOnly previousEnd, DateOnly currentEnd) =>
        (currentEnd.Year - previousEnd.Year) * 12 + currentEnd.Month - previousEnd.Month;

    private static bool SourceValuesEqual(QuarterlyReportObservation left, QuarterlyReportObservation right)
    {
        var leftValues = left.Items.GroupBy(item => item.SourceItemId).ToDictionary(group => group.Key, group => group.ToArray());
        var rightValues = right.Items.GroupBy(item => item.SourceItemId).ToDictionary(group => group.Key, group => group.ToArray());
        if (!leftValues.Keys.Order().SequenceEqual(rightValues.Keys.Order())) return false;
        return leftValues.All(pair => rightValues.TryGetValue(pair.Key, out var other) &&
            pair.Value.Length == other.Length && pair.Value.Select(item => item.Value).SequenceEqual(other.Select(item => item.Value)));
    }

    private static bool StorageScaleIsSupported(QuarterlyReportObservation report) =>
        report.AssumedScale == "MillionRials" && !string.IsNullOrWhiteSpace(report.SourceAmountUnit);

    private static string? CurrencyCode(string rowId) => rowId is "gross_margin" or "source_153" ? null : "IRR";

    private static int? ValueScale(string rowId) => rowId switch
    {
        "gross_margin" => null,
        "source_153" => 3,
        "source_160" => 0,
        _ => 6
    };

    private static (bool Present, decimal? Value, Guid? LineId, Guid? CatalogId, string? Unit) FindItem(QuarterlyReportObservation report, int id)
    {
        var items = report.Items.Where(item => item.SourceItemId == id).ToList();
        if (items.Count != 1) return (false, null, null, null, null);
        return (items[0].Value.HasValue, items[0].Value, items[0].LineItemId, items[0].CatalogId, items[0].Unit);
    }

    private static QuarterlyFinancialCell Missing(QuarterlyCellState state, string reason, string unit,
        QuarterlyReportObservation? current, QuarterlyReportObservation? previous, int? sourceId) =>
        new(null, state, reason, unit, false,
            new[] { current?.ReportId, previous?.ReportId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray(),
            new[] { current is null || sourceId is null ? null : FindItem(current, sourceId.Value).LineId,
                previous is null || sourceId is null ? null : FindItem(previous, sourceId.Value).LineId }
                .Where(id => id.HasValue).Select(id => id!.Value).ToArray(),
            "AdjacentCumulativeDifference", [],
            sourceId is null ? null : CurrencyCode($"source_{sourceId}"),
            sourceId is null ? null : ValueScale($"source_{sourceId}"));

    private static QuarterlyFinancialCell GrossMargin(QuarterlyFinancialCell? grossProfit,
        QuarterlyFinancialCell? revenue, QuarterlyFiscalPeriod period)
    {
        if (grossProfit is null || revenue is null || grossProfit.State != QuarterlyCellState.Available ||
            revenue.State != QuarterlyCellState.Available || !grossProfit.Value.HasValue || !revenue.Value.HasValue || revenue.Value == 0)
            return new(null, QuarterlyCellState.CalculationUnavailable, "Gross profit/revenue is missing or revenue is zero.",
                "percent", false, [], [], "GrossProfit / Revenue * 100", []);
        var value = grossProfit.Value.Value / revenue.Value.Value * 100m;
        return new(value, QuarterlyCellState.Available, null, "percent", value == 0,
            grossProfit.SourceReportIds.Concat(revenue.SourceReportIds).Distinct().ToArray(),
            grossProfit.SourceLineItemIds.Concat(revenue.SourceLineItemIds).Distinct().ToArray(),
            "GrossProfit / Revenue * 100", [grossProfit.Value, revenue.Value]);
    }

    private static QuarterlyQoQComparison Compare(string rowId, QuarterlyFiscalPeriod currentPeriod,
        QuarterlyFiscalPeriod previousPeriod, QuarterlyFinancialCell current, QuarterlyFinancialCell previous)
    {
        if (current.State != QuarterlyCellState.Available || !current.Value.HasValue)
            return new(rowId, currentPeriod, previousPeriod, null, null, null,
                IsUnavailable(current.State)
                    ? QuarterlyComparisonState.Unavailable
                    : QuarterlyComparisonState.MissingCurrent);
        if (previous.State != QuarterlyCellState.Available || !previous.Value.HasValue)
            return new(rowId, currentPeriod, previousPeriod, null, null, null,
                IsUnavailable(previous.State)
                    ? QuarterlyComparisonState.Unavailable
                    : QuarterlyComparisonState.MissingPrevious);
        var absolute = current.Value.Value - previous.Value.Value;
        if (rowId == "gross_margin")
        {
            decimal? relative = previous.Value.Value > 0m
                ? (current.Value.Value / previous.Value.Value - 1m) * 100m
                : null;
            return new(rowId, currentPeriod, previousPeriod, absolute, relative, absolute,
                previous.Value.Value > 0m ? QuarterlyComparisonState.Available : QuarterlyComparisonState.NonPositiveBase);
        }
        if (current.Value.Value < 0m && previous.Value.Value > 0m ||
            current.Value.Value > 0m && previous.Value.Value < 0m)
            return new(rowId, currentPeriod, previousPeriod, absolute, null, null, QuarterlyComparisonState.SignChanged);
        if (previous.Value <= 0)
            return new(rowId, currentPeriod, previousPeriod, absolute, null, null, QuarterlyComparisonState.NonPositiveBase);
        return new(rowId, currentPeriod, previousPeriod, absolute,
            (current.Value.Value / previous.Value.Value - 1m) * 100m, null, QuarterlyComparisonState.Available);
    }

    private static bool IsUnavailable(QuarterlyCellState state) => state is
        QuarterlyCellState.CalculationUnavailable or QuarterlyCellState.UnsupportedFiscalPeriod or
        QuarterlyCellState.IncompatibleObservation or QuarterlyCellState.ConflictingObservation;

    private static string Unit(int sourceId) => sourceId switch
    {
        153 => "thousand_shares",
        160 => "rial_per_share",
        _ => "million_rial"
    };

    private static Guid[] PerviousReports(this (QuarterlyFiscalPeriod Period, QuarterlyReportObservation? Current, QuarterlyReportObservation? Previous) item) =>
        new[] { item.Current?.ReportId, item.Previous?.ReportId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();

    private static Guid[] SourceLineIds(this (QuarterlyFiscalPeriod Period, QuarterlyReportObservation? Current, QuarterlyReportObservation? Previous) item, int rowId) =>
        new[] { item.Current is null ? null : FindItem(item.Current, rowId).LineId,
            item.Previous is null ? null : FindItem(item.Previous, rowId).LineId }
            .Where(id => id.HasValue).Select(id => id!.Value).ToArray();
}

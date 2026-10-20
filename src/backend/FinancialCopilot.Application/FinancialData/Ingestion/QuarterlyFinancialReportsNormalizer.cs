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
        (160, "سود هر سهم پس از کسر مالیات", "2146da26-b28e-4d04-b50c-f49fc9b70e61", QuarterlyRowPolicy.ComparableEps),
        (153, "تعداد سهام شرکت (هزار سهم)", "e98e6c00-7204-44b1-a279-bbbb68781d3c", QuarterlyRowPolicy.ComparableShareBasis)
    ];

    private static readonly PersianCalendar Jalali = new();

    public static IReadOnlyList<(int SourceItemId, string CatalogId)> ApprovedMappings =>
        DirectRows.Select(row => (row.Id, row.CatalogId)).ToArray();

    public static IReadOnlyList<(int SourceItemId, string CatalogId)> RequiredMappings =>
        ApprovedMappings.Concat([(144, "52a19de6-4a0a-4be9-8dbc-3b487a05c08d"),
            (317, "1d244aae-7e5d-44e6-9ec4-c1659828856d")]).ToArray();

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
                "invalid_quarter_count", "Quarter count must be from 1 to 12.", QuarterlyFinancialReportOutcome.InvalidQuery);

        var candidates = reports.Where(report => report.ProviderName == "NoavaranCurrentApi" &&
                report.StatementType == "IncomeStatement" && report.ExternalCompanyId == externalCompanyId &&
                !report.IsComposing)
            .ToList();
        // A fiscal year-end change can leave a transitional statement whose provider period claims a
        // full year that overlaps reports of the preceding fiscal year (for example the one-month 1404/09
        // statement reported as twelve months ending 1404/09/30 after a fiscal year ending 1404/08/30).
        // Its year-to-date duration cannot be trusted, so it never forms fiscal quarters. A preceding
        // fiscal year that was cut short (Esfand 1404 ending at 1404/09/30) has no report reaching into
        // the new fiscal year, so the new year is kept. An overlapping fiscal year really starts the day
        // after the preceding accepted fiscal year ends (a shortened transitional year), and only its
        // reports that land on year-to-date boundaries from that start can form quarters.
        var effectiveFiscalStarts = ResolveEffectiveFiscalStarts(candidates);
        DateOnly FiscalStartOf(QuarterlyReportObservation report) => effectiveFiscalStarts[ResolveFiscalStart(report)];
        var eligible = candidates
            .Where(report => ResolveFiscalStart(report) == FiscalStartOf(report) ||
                             IsExpectedYtdBoundary(FiscalStartOf(report), report.PeriodEnd, report.PeriodMonths))
            .ToList();
        var requestedSlots = eligible
            .Where(report => report.PeriodMonths is 3 or 6 or 9 or 12)
            .GroupBy(report => (FiscalStart: FiscalStartOf(report), report.PeriodMonths))
            .Select(group => new
            {
                group.Key.FiscalStart,
                group.Key.PeriodMonths,
                PeriodEnd = group.Max(report => report.PeriodEnd)
            })
            .OrderByDescending(slot => slot.PeriodEnd)
            .ToList();
        var requiredSlots = requestedSlots
            .Select(slot => (slot.FiscalStart, slot.PeriodMonths))
            .Concat(requestedSlots.Where(slot => slot.PeriodMonths > 3)
                .Select(slot => (slot.FiscalStart, PeriodMonths: slot.PeriodMonths - 3)))
            .ToHashSet();
        var compatible = eligible.Where(report => requiredSlots.Contains((FiscalStartOf(report), report.PeriodMonths)))
            .ToList();
        if (compatible.Count == 0)
            return Unavailable(companySymbol, companyName, externalCompanyId, quarterCount, generatedAtUtc,
                "no_compatible_reports", "No persisted Noavaran income-statement periods match the requested fiscal range.",
                candidates.Count == 0
                    ? QuarterlyFinancialReportOutcome.CompanyFoundNoPersistedReports
                    : QuarterlyFinancialReportOutcome.SourceObservationsUnavailable);
        var scopeGroups = compatible.GroupBy(report => report.IsComposing).ToList();
        if (scopeGroups.Count != 1)
            return Unavailable(companySymbol, companyName, externalCompanyId, quarterCount, generatedAtUtc,
                "conflicting_report_scope", "Separate and consolidated reports cannot be combined; specify one statement scope.",
                QuarterlyFinancialReportOutcome.SourceObservationsUnavailable);

        var isComposing = scopeGroups[0].Key;
        var reportByYearAndDuration = new Dictionary<(DateOnly FiscalStart, int Months), QuarterlyReportObservation>();
        foreach (var group in compatible.GroupBy(report => (FiscalStart: FiscalStartOf(report), report.PeriodMonths)))
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
                    "conflicting_report_revision", "Top-priority reports have conflicting source values and no deterministic version winner.",
                    QuarterlyFinancialReportOutcome.SourceObservationsUnavailable);
            reportByYearAndDuration[group.Key] = unresolved.OrderByDescending(report => report.ReportId).First();
        }

        var allPeriods = new List<(QuarterlyFiscalPeriod Period, QuarterlyReportObservation? Current, QuarterlyReportObservation? Previous)>();
        foreach (var yearGroup in reportByYearAndDuration.Keys.GroupBy(key => key.FiscalStart))
        {
            var year = yearGroup.Key;
            var fiscalYear = FiscalYearLabel(year);
            var lastReportedQuarter = yearGroup.Max(key => key.Months) / 3;
            for (var quarter = 1; quarter <= lastReportedQuarter; quarter++)
            {
                var duration = quarter * 3;
                reportByYearAndDuration.TryGetValue((year, duration), out var current);
                reportByYearAndDuration.TryGetValue((year, duration - 3), out var previous);
                if (quarter == 1) previous = null;
                var isCompatiblePair = current is not null && (quarter == 1 ||
                    previous is not null && FiscalStartOf(previous) == FiscalStartOf(current) &&
                    current.PeriodEnd > previous.PeriodEnd &&
                    MonthsBetween(previous.PeriodEnd, current.PeriodEnd) == 3);
                var end = current?.PeriodEnd ?? previous?.PeriodEnd.AddMonths(3) ?? JalaliEndAfterMonths(year, quarter * 3);
                var start = quarter == 1 ? year : (previous?.PeriodEnd.AddDays(1) ?? year);
                if (current is not null && !IsExpectedYtdBoundary(FiscalStartOf(current), current.PeriodEnd, duration))
                    isCompatiblePair = false;
                var period = new QuarterlyFiscalPeriod(fiscalYear, quarter, start, end);
                allPeriods.Add((period, current,
                    quarter == 1 || isCompatiblePair ? previous : null));
            }
        }

        // Inconsistent provider fiscal-year evidence can still give two fiscal years the same name.
        // Keep one column per label: the one backed by a report on its fiscal boundary, then the newest.
        allPeriods = allPeriods
            .GroupBy(item => item.Period.Label)
            .Select(group => group
                .OrderByDescending(item => item.Current is not null &&
                    IsExpectedYtdBoundary(FiscalStartOf(item.Current), item.Current.PeriodEnd, item.Current.PeriodMonths))
                .ThenByDescending(item => item.Period.End)
                .First())
            .OrderByDescending(item => item.Period.End).ToList();
        var selected = allPeriods.Where(item => !endingPeriod.HasValue || item.Period.End <= endingPeriod.Value)
            .Take(quarterCount).ToList();
        var cellMap = new Dictionary<(string RowId, string Period), QuarterlyFinancialCell>();
        foreach (var item in selected)
        {
            foreach (var row in DirectRows)
            {
                if (row.Id is 160 or 153) continue; // EPS and displayed comparison shares have independent derivation.
                var key = ($"source_{row.Id}", item.Period.Label);
                var unit = Unit(row.Id);
                if (item.Current is null)
                {
                    cellMap[key] = Missing(QuarterlyCellState.MissingPredecessor, "Adjacent compatible cumulative report is unavailable.", unit, item.Current, item.Previous, row.Id);
                    continue;
                }

                var currentValue = FindItem(item.Current, row.Id);
                var previousValue = item.Previous is null
                    ? (Present: false, Value: (decimal?)null, LineId: (Guid?)null, CatalogId: (Guid?)null,
                        Unit: (string?)null, CatalogTitleFa: (string?)null)
                    : FindItem(item.Previous, row.Id);
                if (row.Policy != QuarterlyRowPolicy.ReportedPointInTime &&
                    !IsExpectedYtdBoundary(FiscalStartOf(item.Current), item.Current.PeriodEnd, item.Current.PeriodMonths))
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
            var reference = allPeriods.FirstOrDefault(candidate =>
                candidate.Period.FiscalYear == item.Period.FiscalYear + 1 &&
                candidate.Period.Quarter == item.Period.Quarter && candidate.Current is not null);
            var hasNextYearReference = reference.Period is not null;
            var basisReport = hasNextYearReference ? reference.Current : item.Current;
            var shareKey = ("source_153", item.Period.Label);
            var epsKey = ("source_160", item.Period.Label);
            var rawShares = item.Current is null ? default : FindItem(item.Current, 153);
            var rawEps = item.Current is null ? default : FindItem(item.Current, 160);
            var previousEps = item.Previous is null ? default : FindItem(item.Previous, 160);
            var rawCodalEps = item.Current is null ? default : FindItem(item.Current, 317);
            var previousCodalEps = item.Previous is null ? default : FindItem(item.Previous, 317);
            var comparableEvidence = new QuarterlyComparableEvidence(rawShares.Value, null,
                rawCodalEps.Value, null, item.Current?.ReportId, null, rawShares.LineId, null,
                rawCodalEps.LineId, null, null, null,
                "reported_period_end_shares_for_comparison", hasNextYearReference ? reference.Period?.FiscalYear : item.Period.FiscalYear,
                item.Period.Quarter, basisReport?.ReportId, basisReport?.ExternalReportId,
                hasNextYearReference ? "following_year_same_quarter" : "own_period_fallback",
                "nearest_whole_rial_away_from_zero", null,
                RawCurrentShareCountThousand: rawShares.Value);

            var basisShares = basisReport is null ? default : FindItem(basisReport, 153);
            var shareFailure = hasNextYearReference && basisReport is null
                ? "share_basis_reference_report_missing"
                : ValidateShareObservation(basisReport);
            var basis = shareFailure is null ? basisShares.Value : null;
            var basisEvidence = comparableEvidence with
            {
                ShareBasisThousand = basis,
                RawReferenceShareCountThousand = basisShares.Value,
                Reconciliation = shareFailure ?? "reported_share_observation_verified"
            };
            var shareReports = new[] { item.Current?.ReportId, basisReport?.ReportId }
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            var shareLines = new[] { rawShares.LineId, basisShares.LineId }
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            cellMap[shareKey] = new QuarterlyFinancialCell(basis, shareFailure is null ? QuarterlyCellState.Available : QuarterlyCellState.CalculationUnavailable,
                shareFailure, Unit(153), basis == 0, shareReports, shareLines,
                hasNextYearReference ? "FollowingYearSameQuarterShareBasis" : "OwnPeriodShareBasisFallback",
                [rawShares.Value, basisShares.Value], ValueScale: ValueScale("source_153"), ComparableEvidence: basisEvidence);

            var currentProfit = item.Current is null ? default : FindAttributableProfit(item.Current);
            var previousProfit = item.Previous is null ? default : FindAttributableProfit(item.Previous);
            var referenceProfit = basisReport is null ? default : FindItem(basisReport, 144);
            var referenceEps = basisReport is null ? default : FindItem(basisReport, 160);
            var referenceCodalEps = basisReport is null ? default : FindItem(basisReport, 317);
            var ownValidation = ValidateEpsBasis(item.Current);
            var predecessorValidation = item.Period.Quarter == 1 ? null : ValidateEpsBasis(item.Previous);
            var numeratorBasisFailure = item.Period.Quarter > 1 && currentProfit.SourceItemId != previousProfit.SourceItemId
                ? "attributable_profit_source_changed_between_cumulative_reports"
                : null;
            var epsFailure = ownValidation ?? predecessorValidation ?? numeratorBasisFailure;
            var failure = epsFailure ?? shareFailure;
            var profit = failure is null && currentProfit.Value.HasValue
                ? currentProfit.Value - (item.Period.Quarter == 1 ? 0m : previousProfit.Value)
                : null;
            if (failure is null && !profit.HasValue) failure = "standalone_attributable_profit_missing";
            var eps = failure is null && basis > 0 && profit.HasValue
                ? decimal.Round(profit.Value * 1000m / basis.Value, 0, MidpointRounding.AwayFromZero)
                : (decimal?)null;
            var epsEvidence = basisEvidence with
            {
                RawCurrentValue = rawEps.Value, RawPreviousValue = previousEps.Value,
                RawCodalCurrentValue = rawCodalEps.Value, RawCodalPreviousValue = previousCodalEps.Value,
                RawCurrentLineItemId = rawEps.LineId, RawPreviousLineItemId = previousEps.LineId,
                RawCodalCurrentLineItemId = rawCodalEps.LineId, RawCodalPreviousLineItemId = previousCodalEps.LineId,
                RawPreviousReportId = item.Previous?.ReportId,
                StandaloneAttributableProfitMillionRials = profit,
                Reconciliation = failure ?? basisEvidence.Reconciliation,
                RawCurrentAttributableProfitMillionRials = currentProfit.Value,
                RawPreviousAttributableProfitMillionRials = previousProfit.Value,
                RawReferenceAttributableProfitMillionRials = referenceProfit.Value,
                RawReferenceEpsYtd = referenceEps.Value,
                RawReferenceCodalEpsYtd = referenceCodalEps.Value,
                AttributableProfitSourceItemId = currentProfit.SourceItemId,
                PreviousAttributableProfitSourceItemId = previousProfit.SourceItemId
            };
            var epsReports = new[] { item.Current?.ReportId, item.Previous?.ReportId, basisReport?.ReportId }
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            var epsLines = new[] { rawEps.LineId, previousEps.LineId, currentProfit.LineId,
                    previousProfit.LineId, basisShares.LineId, referenceProfit.LineId,
                    referenceEps.LineId, referenceCodalEps.LineId }
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            cellMap[epsKey] = new QuarterlyFinancialCell(eps,
                failure is null ? QuarterlyCellState.Available : QuarterlyCellState.CalculationUnavailable,
                failure, Unit(160), eps == 0, epsReports, epsLines,
                "ComparableStandaloneAttributableProfitPerReportedShareBasis",
                [currentProfit.Value, previousProfit.Value, basis], CurrencyCode("source_160"),
                ValueScale("source_160"), epsEvidence);
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
            ($"source_{row.Id}", row.Label,
                (int?)row.Id, (string?)row.CatalogId, row.Policy, Unit(row.Id))));

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
        var tableOutcome = rows.SelectMany(row => row.Cells.Values)
            .Any(cell => cell.State == QuarterlyCellState.Available)
            ? (selected.Count < quarterCount
                ? QuarterlyFinancialReportOutcome.PartialTable
                : QuarterlyFinancialReportOutcome.SuccessfulTable)
            : QuarterlyFinancialReportOutcome.SourceObservationsUnavailable;
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
                    report.AccountingBasis, report.ReportRevisionLineage, report.FiscalYearEvidence,
                    report.PeriodType)).ToArray(),
            Outcome: tableOutcome);
    }

    private static QuarterlyFinancialReportTableResult Unavailable(string symbol, string? name, string id,
        int count, DateTimeOffset now, string reason, string detail, QuarterlyFinancialReportOutcome outcome)
    {
        var orderedRows = new List<(string Id, string Label, int? SourceId, string? Catalog, QuarterlyRowPolicy Policy, string Unit)>
        {
            ("source_15", DirectRows.Single(row => row.Id == 15).Label, 15, DirectRows.Single(row => row.Id == 15).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(15)),
            ("source_1", DirectRows.Single(row => row.Id == 1).Label, 1, DirectRows.Single(row => row.Id == 1).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(1)),
            ("source_139", DirectRows.Single(row => row.Id == 139).Label, 139, DirectRows.Single(row => row.Id == 139).CatalogId, QuarterlyRowPolicy.CumulativeFlow, Unit(139)),
            ("gross_margin", "مارجین ناخالص", null, null, QuarterlyRowPolicy.DerivedGrossMargin, "percent")
        };
        orderedRows.AddRange(DirectRows.Where(row => row.Id is not (15 or 1 or 139)).Select(row =>
            ($"source_{row.Id}", row.Label,
                (int?)row.Id, (string?)row.CatalogId, row.Policy, Unit(row.Id))));
        var rows = orderedRows.Select((row, index) => new QuarterlyFinancialRow(row.Id, index + 1, row.Label,
            row.SourceId, row.Catalog is null ? null : Guid.Parse(row.Catalog), row.Policy, row.Unit,
            new Dictionary<string, QuarterlyFinancialCell>())).ToArray();
        return new(symbol, name, id, "NoavaranCurrentApi", "IncomeStatement", null, count,
            [], rows, [], null, "Unavailable", $"{reason}: {detail}", now, [], Outcome: outcome);
    }

    private static bool IsExpectedYtdBoundary(DateOnly fiscalStart, DateOnly end, int months)
    {
        return JalaliEndAfterMonths(fiscalStart, months) == end;
    }

    private static DateOnly JalaliEndAfterMonths(DateOnly fiscalStart, int months) =>
        DateOnly.FromDateTime(Jalali.AddMonths(fiscalStart.ToDateTime(TimeOnly.MinValue), months).AddDays(-1));

    private static DateOnly ResolveFiscalStart(QuarterlyReportObservation report)
    {
        if (TryParseJalaliDate(report.FiscalYearEvidence, out var fiscalYearEnd))
        {
            var endYear = Jalali.GetYear(fiscalYearEnd.ToDateTime(TimeOnly.MinValue));
            var endMonth = Jalali.GetMonth(fiscalYearEnd.ToDateTime(TimeOnly.MinValue));
            return endMonth == 12
                ? DateOnly.FromDateTime(Jalali.ToDateTime(endYear, 1, 1, 0, 0, 0, 0))
                : DateOnly.FromDateTime(Jalali.ToDateTime(endYear - 1, endMonth + 1, 1, 0, 0, 0, 0));
        }

        return report.PeriodStart;
    }

    // A fiscal year is named by the Jalali year in which it ends, with or without fiscal evidence,
    // so disjoint fiscal years never share a period label.
    private static int FiscalYearLabel(DateOnly fiscalStart) =>
        Jalali.GetYear(JalaliEndAfterMonths(fiscalStart, 12).ToDateTime(TimeOnly.MinValue));

    // Maps each provider-derived fiscal start to the start its quarters are measured from. Fiscal years
    // are visited oldest first; one that begins on or before the last report of an accepted fiscal year
    // is a transitional year and is rebased to the day after that report. Rebased years never push the
    // accepted boundary, so a regular fiscal year sharing the rebased start is still accepted.
    private static Dictionary<DateOnly, DateOnly> ResolveEffectiveFiscalStarts(IEnumerable<QuarterlyReportObservation> reports)
    {
        var effective = new Dictionary<DateOnly, DateOnly>();
        var latestAcceptedEnd = DateOnly.MinValue;
        foreach (var fiscalYear in reports.GroupBy(ResolveFiscalStart).OrderBy(group => group.Key))
        {
            if (latestAcceptedEnd >= fiscalYear.Key)
            {
                effective[fiscalYear.Key] = latestAcceptedEnd.AddDays(1);
                continue;
            }

            effective[fiscalYear.Key] = fiscalYear.Key;
            var fiscalYearEnd = fiscalYear.Max(report => report.PeriodEnd);
            if (fiscalYearEnd > latestAcceptedEnd) latestAcceptedEnd = fiscalYearEnd;
        }

        return effective;
    }

    private static bool TryParseJalaliDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
            return false;

        try
        {
            date = DateOnly.FromDateTime(Jalali.ToDateTime(year, month, day, 0, 0, 0, 0));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

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

    private static string? ValidateEpsBasis(QuarterlyReportObservation? report)
    {
        if (report is null) return "required_report_missing";
        if (report.IsComposing) return "separate_statement_scope_required";
        if (!StorageScaleIsSupported(report)) return "unsupported_report_scale";
        var numerator = FindAttributableProfit(report);
        if (numerator.SourceItemId is null) return "source_144_or_143_missing_or_unverified";
        foreach (var id in new[] { numerator.SourceItemId.Value, 153, 160 })
        {
            var item = FindItem(report, id);
            if (!HasVerifiedCatalogObservation(item))
                return $"source_{id}_missing_or_unverified";
        }
        // Source item 153 explicitly names the provider's unit in its official catalog title
        // ("... به هزار سهم"). Its raw Unit field is commonly N/A, so treating that field as
        // the semantic unit made valid share observations disappear for issuers whose catalog
        // entry was created dynamically.
        var shareObservation = FindItem(report, 153);
        if (!TitleDeclaresThousandShares(shareObservation.CatalogTitleFa))
            return "source_153_missing_or_unverified";
        var profit = numerator.Value!.Value;
        var shares = FindItem(report, 153).Value!.Value;
        var sourceEps = FindItem(report, 160).Value!.Value;
        if (shares <= 0) return "share_basis_nonpositive";
        var implied = profit * 1000m / shares;
        if (decimal.Abs(implied - sourceEps) > 1m) return "source_eps_share_basis_reconciliation_failed";
        var codalLines = report.Items.Where(item => item.SourceItemId == 317).ToArray();
        if (codalLines.Length > 1) return "source_317_ambiguous";
        if (codalLines.Length == 1 && codalLines[0].Value is decimal codalValue)
        {
            if (codalLines[0].CatalogId == Guid.Empty || string.IsNullOrWhiteSpace(codalLines[0].Unit) ||
                decimal.Abs(implied - codalValue) > 1m)
                return "codal_eps_share_basis_reconciliation_failed";
        }
        return null;
    }

    // Item 144 is the explicit attributable-profit observation. Some Noavaran separate statements
    // omit that duplicate line while still reporting net profit as item 143. In a separate statement
    // those amounts have the same ordinary-shareholder scope; accept item 143 only when item 144 is
    // absent, and retain the chosen source identity in the cell provenance.
    private static (bool Present, decimal? Value, Guid? LineId, Guid? CatalogId, string? Unit, int? SourceItemId)
        FindAttributableProfit(QuarterlyReportObservation report)
    {
        var attributable = FindItem(report, 144);
        if (report.Items.Count(item => item.SourceItemId == 144) == 1)
            return (attributable.Present, attributable.Value, attributable.LineId, attributable.CatalogId,
                attributable.Unit, 144);

        if (report.Items.Any(item => item.SourceItemId == 144))
            return (false, null, null, null, null, 144);

        var netProfit = FindItem(report, 143);
        return (netProfit.Present, netProfit.Value, netProfit.LineId, netProfit.CatalogId, netProfit.Unit, 143);
    }

    private static string? ValidateShareObservation(QuarterlyReportObservation? report)
    {
        if (report is null) return "share_basis_report_missing";
        if (report.IsComposing) return "separate_statement_scope_required";
        var item = FindItem(report, 153);
        if (!HasVerifiedCatalogObservation(item) || !TitleDeclaresThousandShares(item.CatalogTitleFa))
            return "share_basis_missing_or_unverified";
        return item.Value <= 0 ? "share_basis_nonpositive" : null;
    }

    private static bool HasVerifiedCatalogObservation(
        (bool Present, decimal? Value, Guid? LineId, Guid? CatalogId, string? Unit, string? CatalogTitleFa) item) =>
        item.Present && item.CatalogId is { } catalogId && catalogId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(item.Unit);

    private static bool TitleDeclaresThousandShares(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var normalized = new string(title.Select(character => character switch
        {
            '\u064a' => '\u06cc',
            '\u0643' => '\u06a9',
            _ => character
        }).Where(character => !char.IsWhiteSpace(character) && character != '-').ToArray());
        return normalized.Contains("\u0647\u0632\u0627\u0631\u0633\u0647\u0645", StringComparison.Ordinal);
    }

    private static string? CurrencyCode(string rowId) => rowId is "gross_margin" or "source_153" ? null : "IRR";

    private static int? ValueScale(string rowId) => rowId switch
    {
        "gross_margin" => null,
        "source_153" => 3,
        "source_160" => 0,
        _ => 6
    };

    private static (bool Present, decimal? Value, Guid? LineId, Guid? CatalogId, string? Unit, string? CatalogTitleFa) FindItem(QuarterlyReportObservation report, int id)
    {
        var items = report.Items.Where(item => item.SourceItemId == id).ToList();
        if (items.Count != 1) return (false, null, null, null, null, null);
        return (items[0].Value.HasValue, items[0].Value, items[0].LineItemId, items[0].CatalogId, items[0].Unit,
            items[0].CatalogTitleFa);
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

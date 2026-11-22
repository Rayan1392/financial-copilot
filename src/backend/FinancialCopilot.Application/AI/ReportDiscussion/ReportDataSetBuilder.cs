using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>
/// Projects the active report's persisted payload into facts with evidence ids and computes the D5 calculations
/// deterministically. Values are copied, never estimated; a calculation with a missing operand is omitted.
/// </summary>
public static class ReportDataSetBuilder
{
    // Quarterly income-statement rows by Noavaran source item (QuarterlyFinancialReportsNormalizer.DirectRows).
    private const string Revenue = "source_15";
    private const string CostOfSales = "source_1";
    private const string GrossProfit = "source_139";
    private const string GeneralAdministrative = "source_5";
    private const string OperatingProfit = "source_140";
    private const string FinancialExpense = "source_12";
    private const string Tax = "source_334";
    private const string NetProfit = "source_143";

    public static ReportDataSet? Build(ActiveReport report) => report.Family switch
    {
        // Feature 163: a hidden grounding report is discussed exactly like a shown quarterly report.
        ReportFamily.QuarterlyIncomeStatement => (report.Payload.FinancialStatementTableResult ?? report.Payload.GroundingReport)?.QuarterlyResult is { } quarterly
            ? Quarterly(report, quarterly)
            : null,
        ReportFamily.BalanceSheet => report.Payload.FinancialStatementTableResult?.BalanceSheetReport is { } balance
            ? BalanceSheet(Describe(report), balance)
            : null,
        ReportFamily.MonthlySales => report.Payload.MonthlyActivityTrendResult is { } monthly
            ? Monthly(Describe(report), monthly)
            : null,
        _ => null
    };

    // ---- Change «local-company-data-context» (T05, T06): the same projections from a plain query result, for the
    // supporting sections of the company data pack. Null when the result is not usable or has no fact. ----

    /// <summary>A Feature 154 balance sheet result as a section.</summary>
    public static ReportDataSet? BuildBalanceSheet(BalanceSheetReportResult? balance) =>
        balance is { Outcome: BalanceSheetOutcomes.Available, Snapshots.Count: > 0 }
            ? WithFacts(BalanceSheet(new DiscussableReport(ReportFamily.BalanceSheet, balance.Company.Symbol, balance.Company.Name,
                balance.Company.ProviderName), balance))
            : null;

    /// <summary>A Feature 120 monthly activity trend (company totals) as a section.</summary>
    public static ReportDataSet? BuildMonthly(MonthlyActivityTrendResponse? trend) =>
        trend is not null && (trend.LatestMonthlySalesAmount is not null || trend.ChartPoints.Count > 0)
            ? WithFacts(Monthly(new DiscussableReport(ReportFamily.MonthlySales, trend.CompanySymbol, trend.CompanyName,
                trend.SourceProviderName), trend))
            : null;

    /// <summary>
    /// A Feature 161 cash flow statement as a section: every reported cell of every available column as a fact, and the
    /// report's own available or unverified metrics as calculations (copied, never recomputed).
    /// </summary>
    public static ReportDataSet? BuildCashFlow(CashFlowReportResult? report)
    {
        if (report is null || !string.Equals(report.Outcome, CashFlowAvailable, StringComparison.Ordinal)) return null;
        var columns = report.Columns
            .Where(column => string.Equals(column.State, CashFlowAvailable, StringComparison.Ordinal))
            .OrderByDescending(column => column.PeriodEnd ?? DateOnly.MinValue)
            .ThenByDescending(column => column.PeriodMonths)
            .ToArray();
        if (columns.Length == 0) return null;

        var unit = report.Metadata?.Unit ?? report.Statements.Values.FirstOrDefault()?.Unit ?? "million_irr";
        var ids = new EvidenceIds();
        var periodLabels = columns.Select(column => Clean(column.Label)).ToArray();
        var rankByColumn = columns.Select((column, rank) => (column.ColumnId, rank))
            .ToDictionary(item => item.ColumnId, item => item.rank, StringComparer.Ordinal);
        var facts = new List<ReportFact>();
        foreach (var row in report.Sections.SelectMany(section => section.Rows))
        {
            for (var rank = 0; rank < columns.Length; rank++)
            {
                if (!row.Cells.TryGetValue(columns[rank].ColumnId, out var cell) || cell.Value is not { } value) continue;
                if (!IsUsableCashFlowState(cell.State)) continue;
                facts.Add(new ReportFact(ids.NextFact(), CashFlowLabel(row.Label, cell.State), periodLabels[rank], value, unit, rank));
            }
        }

        var calculations = new List<ReportCalculation>();
        foreach (var kpi in report.Kpis)
        {
            if (kpi.Value is not { } value || !IsUsableCashFlowState(kpi.State) ||
                !rankByColumn.TryGetValue(kpi.ColumnId, out var rank))
                continue;
            calculations.Add(new ReportCalculation(ids.NextCalculation(), CashFlowLabel(kpi.Label, kpi.State),
                periodLabels[rank], Round(value), kpi.Unit, kpi.Formula, [], rank));
        }

        if (facts.Count == 0) return null;
        return new ReportDataSet(ReportFamily.CashFlowStatement, report.Company.Symbol ?? string.Empty, report.Company.Name,
            report.Metadata?.BasisLabel ?? "جداگانه (شرکت اصلی)", report.Company.ProviderName, periodLabels, facts, calculations);
    }

    private const string CashFlowAvailable = "available";
    private const string CashFlowUnverified = "unverified";

    private static bool IsUsableCashFlowState(string state) =>
        string.Equals(state, CashFlowAvailable, StringComparison.Ordinal) ||
        string.Equals(state, CashFlowUnverified, StringComparison.Ordinal);

    /// <summary>The report's own caveat (CashFlowAnalysisFacts) travels with an unverified value.</summary>
    private static string CashFlowLabel(string label, string state) =>
        string.Equals(state, CashFlowUnverified, StringComparison.Ordinal) ? $"{label} (تطبیق کامل ممکن نیست)" : label;

    private static string Clean(string text) => text.ReplaceLineEndings(" ").Trim();

    private static ReportDataSet? WithFacts(ReportDataSet data) => data.Facts.Count > 0 ? data : null;

    private static DiscussableReport Describe(ActiveReport report) =>
        new(report.Family, report.CompanySymbol, report.CompanyName, report.ProviderName);

    public static string UnitLabel(string unit) => unit switch
    {
        "million_rial" or "million_irr" => "میلیون ریال",
        "rial" or "irr" => "ریال",
        "percent" => "درصد",
        "percentage_point" => "واحد درصد",
        "thousand_shares" => "هزار سهم",
        "rial_per_share" => "ریال به ازای هر سهم",
        "times" => "برابر",
        ReportDisplayUnits.Hemat => "همت",
        ReportDisplayUnits.BillionToman => ReportDisplayUnits.BillionTomanLabelFa,
        _ => unit
    };

    public static string FamilyLabel(ReportFamily family) => family switch
    {
        ReportFamily.QuarterlyIncomeStatement => "صورت سود و زیان فصلی",
        ReportFamily.BalanceSheet => "ترازنامه",
        ReportFamily.MonthlySales => "گزارش فروش ماهانه",
        ReportFamily.CashFlowStatement => "صورت جریان وجوه نقد",
        _ => "گزارش"
    };

    /// <summary>Feature 163: the same projection for a quarterly table that is not (yet) a conversation's active report.</summary>
    public static ReportDataSet? BuildQuarterly(FinancialStatementTableResult? table) =>
        ActiveReportResolver.Discussable(table, null) is { Family: ReportFamily.QuarterlyIncomeStatement } source
            ? Quarterly(source, table!.QuarterlyResult!)
            : null;

    private static ReportDataSet Quarterly(ActiveReport report, QuarterlyFinancialReportTableResult table) =>
        Quarterly(new DiscussableReport(report.Family, report.CompanySymbol, report.CompanyName, report.ProviderName), table);

    private static ReportDataSet Quarterly(DiscussableReport report, QuarterlyFinancialReportTableResult table)
    {
        var ids = new EvidenceIds();
        var periods = table.PeriodsNewestFirst;
        var periodLabels = periods.Select(QuarterLabel).ToArray();
        var facts = new List<ReportFact>();
        var factIndex = new Dictionary<(string RowId, int Rank), ReportFact>();

        foreach (var row in table.Rows.OrderBy(row => row.Order))
        {
            for (var rank = 0; rank < periods.Count; rank++)
            {
                if (!row.Cells.TryGetValue(periods[rank].Label, out var cell) ||
                    cell.State != QuarterlyCellState.Available || cell.Value is not { } value)
                    continue;
                var fact = new ReportFact(ids.NextFact(), row.LabelFa, periodLabels[rank], value, row.Unit, rank);
                facts.Add(fact);
                factIndex[(row.RowId, rank)] = fact;
            }
        }

        var calculations = new List<ReportCalculation>();
        var rowsById = table.Rows.ToDictionary(row => row.RowId, StringComparer.Ordinal);

        // Quarter-over-quarter changes already computed by the report (Feature 144), with their operands.
        foreach (var comparison in table.Comparisons.Where(item => item.State == QuarterlyComparisonState.Available))
        {
            if (!rowsById.TryGetValue(comparison.RowId, out var row)) continue;
            var rank = IndexOf(periods, comparison.TargetPeriod);
            var baseRank = IndexOf(periods, comparison.BasePeriod);
            if (rank < 0 || baseRank < 0) continue;
            var operands = Operands(factIndex, (row.RowId, rank), (row.RowId, baseRank));
            if (operands is null) continue;
            if (row.Unit == "percent")
            {
                if (comparison.PercentagePointChange is { } pp)
                    calculations.Add(new ReportCalculation(ids.NextCalculation(), $"تغییر فصلی {row.LabelFa}",
                        periodLabels[rank], Round(pp), "percentage_point",
                        $"{row.LabelFa} ({periodLabels[rank]}) − {row.LabelFa} ({periodLabels[baseRank]})", operands, rank));
            }
            else if (comparison.RelativeChangePercent is { } percent)
            {
                calculations.Add(new ReportCalculation(ids.NextCalculation(), $"رشد فصلی {row.LabelFa}",
                    periodLabels[rank], Round(percent), "percent",
                    $"({row.LabelFa} {periodLabels[rank]} − {row.LabelFa} {periodLabels[baseRank]}) ÷ |{row.LabelFa} {periodLabels[baseRank]}| × 100",
                    operands, rank));
            }
        }

        for (var rank = 0; rank < periods.Count; rank++)
        {
            // Margins on revenue (D5). The report already carries gross margin as its own row.
            AddRatio(calculations, ids, factIndex, OperatingProfit, Revenue, rank, periodLabels[rank], "حاشیه سود عملیاتی", "percent", 100m);
            AddRatio(calculations, ids, factIndex, NetProfit, Revenue, rank, periodLabels[rank], "حاشیه سود خالص", "percent", 100m);
            if (!rowsById.ContainsKey("gross_margin"))
                AddRatio(calculations, ids, factIndex, GrossProfit, Revenue, rank, periodLabels[rank], "حاشیه سود ناخالص", "percent", 100m);
            foreach (var share in new[] { CostOfSales, GeneralAdministrative, FinancialExpense, Tax })
            {
                if (rowsById.TryGetValue(share, out var shareRow))
                    AddRatio(calculations, ids, factIndex, share, Revenue, rank, periodLabels[rank], $"نسبت {shareRow.LabelFa} به فروش", "percent", 100m);
            }

            // Year-over-year growth against the same fiscal quarter of the previous year, when that quarter is in the table.
            var sameQuarterLastYear = FindPeriod(periods, periods[rank].FiscalYear - 1, periods[rank].Quarter);
            if (sameQuarterLastYear < 0) continue;
            foreach (var rowId in new[] { Revenue, GrossProfit, OperatingProfit, NetProfit })
            {
                if (!rowsById.TryGetValue(rowId, out var row)) continue;
                AddGrowth(calculations, ids, factIndex, row, rank, sameQuarterLastYear, periodLabels, $"رشد سالانه {row.LabelFa} (نسبت به فصل مشابه سال قبل)");
            }
        }

        return new ReportDataSet(ReportFamily.QuarterlyIncomeStatement, report.CompanySymbol, report.CompanyName,
            table.IsComposing == true ? "تلفیقی" : "جداگانه (شرکت اصلی)", table.ProviderName,
            periodLabels, facts, calculations);
    }

    private static ReportDataSet BalanceSheet(DiscussableReport report, BalanceSheetReportResult balance)
    {
        var ids = new EvidenceIds();
        var snapshots = balance.Snapshots.Values
            .OrderByDescending(snapshot => snapshot.Statement.PeriodEnd)
            .ToArray();
        var periodLabels = snapshots.Select(snapshot => snapshot.Statement.JalaliPeriodEnd).ToArray();
        var facts = new List<ReportFact>();
        var factIndex = new Dictionary<(string RowId, int Rank), ReportFact>();
        var calculations = new List<ReportCalculation>();

        for (var rank = 0; rank < snapshots.Length; rank++)
        {
            var snapshot = snapshots[rank];
            foreach (var row in Rows(snapshot))
            {
                if (factIndex.ContainsKey((row.RowId, rank)) ||
                    row.Cell.State != BalanceSheetCellStates.Available || row.Cell.Value is not { } value)
                    continue;
                var fact = new ReportFact(ids.NextFact(), row.Label, periodLabels[rank], value, row.Unit, rank);
                facts.Add(fact);
                factIndex[(row.RowId, rank)] = fact;
            }

            if (snapshot.Ratio.Value is { } equityRatio)
            {
                var operands = Operands(factIndex, ("total_equity", rank), ("total_assets", rank)) ?? [];
                calculations.Add(new ReportCalculation(ids.NextCalculation(), "نسبت مالکانه (حقوق صاحبان سهام به کل دارایی‌ها)",
                    periodLabels[rank], Round(equityRatio), "percent", "جمع حقوق صاحبان سهام ÷ جمع کل دارایی‌ها × 100", operands, rank));
            }

            AddRatio(calculations, ids, factIndex, "current_assets_total", "current_liabilities_total", rank, periodLabels[rank],
                "نسبت جاری", "times", 1m);
            AddRatio(calculations, ids, factIndex, "total_liabilities", "total_assets", rank, periodLabels[rank],
                "نسبت بدهی (کل بدهی‌ها به کل دارایی‌ها)", "percent", 100m);
        }

        // Changes against the report's selected comparison baseline, as the report computed them.
        if (balance.Comparison is { } comparison && snapshots.Length > 0)
        {
            var anchorRows = Rows(snapshots[0]).GroupBy(row => row.RowId).ToDictionary(group => group.Key, group => group.First());
            var selected = comparison.Candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Kind, comparison.SelectedBaselineKind, StringComparison.Ordinal));
            if (selected is not null)
            {
                foreach (var (rowId, change) in selected.RowChanges)
                {
                    if (!anchorRows.TryGetValue(rowId, out var row) || change.Percent is not { } percent) continue;
                    calculations.Add(new ReportCalculation(ids.NextCalculation(),
                        $"تغییر {row.Label} نسبت به {selected.Label}", periodLabels[0], Round(percent), "percent",
                        $"({row.Label} {periodLabels[0]} − {row.Label} {selected.JalaliBaselineDate ?? selected.Label}) ÷ |مقدار پایه| × 100",
                        factIndex.TryGetValue((rowId, 0), out var anchorFact) ? [anchorFact.EvidenceId] : [], 0));
                }
            }
        }

        return new ReportDataSet(ReportFamily.BalanceSheet, report.CompanySymbol, report.CompanyName,
            "جداگانه (شرکت اصلی)", balance.Company.ProviderName, periodLabels, facts, calculations);
    }

    private static ReportDataSet Monthly(DiscussableReport report, MonthlyActivityTrendResponse trend)
    {
        var ids = new EvidenceIds();
        var unit = trend.UnitLabelFa;
        var latest = $"{trend.LatestReportYear}/{trend.LatestReportMonth:D2}";
        var facts = new List<ReportFact>();
        var calculations = new List<ReportCalculation>();

        void AddSummary(string label, decimal? value, string factUnit)
        {
            if (value is { } amount)
                facts.Add(new ReportFact(ids.NextFact(), label, latest, amount, factUnit, 0));
        }

        AddSummary("فروش آخرین ماه گزارش‌شده", trend.LatestMonthlySalesAmount, unit);
        AddSummary("فروش ماه قبل", trend.PreviousMonthSalesAmount, unit);
        AddSummary("فروش ماه مشابه سال قبل", trend.SameMonthPreviousYearSalesAmount, unit);
        AddSummary("میانگین فروش ۱۲ ماه اخیر", trend.Average12MonthSalesAmount, unit);
        AddSummary("فروش تجمعی از ابتدای سال مالی تا آخرین ماه", trend.YtdSalesAmount, unit);
        AddSummary("فروش تجمعی از ابتدای سال مالی تا ماه قبل", trend.YtdPreviousMonthSalesAmount, unit);

        // Percentages the report computed from the same persisted values (Feature 120); copied, not recomputed.
        void AddPercent(string label, decimal? value, string formula)
        {
            if (value is { } percent)
                calculations.Add(new ReportCalculation(ids.NextCalculation(), label, latest, Round(percent), "percent", formula, [], 0));
        }

        AddPercent("رشد فروش نسبت به ماه قبل", trend.SalesAmountMomGrowthPercent, "(فروش آخرین ماه − فروش ماه قبل) ÷ فروش ماه قبل × 100");
        AddPercent("رشد فروش نسبت به ماه مشابه سال قبل", trend.SalesAmountYoYGrowthPercent, "(فروش آخرین ماه − فروش ماه مشابه سال قبل) ÷ فروش ماه مشابه سال قبل × 100");
        AddPercent("فروش آخرین ماه نسبت به میانگین ۱۲ ماهه", trend.SalesVsAverage12MonthPercent, "(فروش آخرین ماه − میانگین ۱۲ ماهه) ÷ میانگین ۱۲ ماهه × 100");

        // Monthly series from the chart, newest month first by (fiscal year, fiscal month).
        var series = new List<(int Year, int Month, string MonthName, decimal Value)>();
        foreach (var point in trend.ChartPoints)
        {
            if (point.CurrentFiscalYear is { } currentYear && point.CurrentFiscalYearSalesAmount is { } current && point.IsCurrentYearReported)
                series.Add((currentYear, point.FiscalMonthIndex, point.FiscalMonthNameFa, current));
            if (point.PreviousFiscalYear is { } previousYear && point.PreviousFiscalYearSalesAmount is { } previous && point.IsPreviousYearReported)
                series.Add((previousYear, point.FiscalMonthIndex, point.FiscalMonthNameFa, previous));
        }

        var ordered = series.OrderByDescending(item => item.Year).ThenByDescending(item => item.Month).ToArray();
        var periods = new List<string> { latest };
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            var label = $"{item.MonthName} سال مالی {item.Year}";
            facts.Add(new ReportFact(ids.NextFact(), "فروش ماهانه", label, item.Value, unit, index));
            if (!periods.Contains(label)) periods.Add(label);
        }

        return new ReportDataSet(ReportFamily.MonthlySales, report.CompanySymbol, report.CompanyName,
            "گزارش فعالیت ماهانه شرکت", trend.SourceProviderName, periods, facts, calculations);
    }

    private static IEnumerable<BalanceSheetRow> Rows(BalanceSheetSnapshot snapshot)
    {
        foreach (var section in snapshot.Sections)
        {
            foreach (var group in section.Groups)
            {
                foreach (var row in group.Rows) yield return row;
                if (group.Subtotal is not null) yield return group.Subtotal;
            }
            if (section.Total is not null) yield return section.Total;
        }
        yield return snapshot.FinalTotals.TotalAssets;
        yield return snapshot.FinalTotals.TotalLiabilitiesAndEquity;
    }

    private static void AddRatio(List<ReportCalculation> calculations, EvidenceIds ids,
        Dictionary<(string RowId, int Rank), ReportFact> facts, string numeratorRow, string denominatorRow,
        int rank, string period, string label, string unit, decimal multiplier)
    {
        if (!facts.TryGetValue((numeratorRow, rank), out var numerator) ||
            !facts.TryGetValue((denominatorRow, rank), out var denominator) ||
            denominator.Value == 0m)
            return;
        calculations.Add(new ReportCalculation(ids.NextCalculation(), label, period,
            Round(numerator.Value / denominator.Value * multiplier), unit,
            multiplier == 100m ? $"{numerator.Label} ÷ {denominator.Label} × 100" : $"{numerator.Label} ÷ {denominator.Label}",
            [numerator.EvidenceId, denominator.EvidenceId], rank));
    }

    private static void AddGrowth(List<ReportCalculation> calculations, EvidenceIds ids,
        Dictionary<(string RowId, int Rank), ReportFact> facts, QuarterlyFinancialRow row, int rank, int baseRank,
        IReadOnlyList<string> periodLabels, string label)
    {
        if (!facts.TryGetValue((row.RowId, rank), out var current) ||
            !facts.TryGetValue((row.RowId, baseRank), out var previous) ||
            previous.Value == 0m)
            return;
        calculations.Add(new ReportCalculation(ids.NextCalculation(), label, periodLabels[rank],
            Round((current.Value - previous.Value) / Math.Abs(previous.Value) * 100m), "percent",
            $"({row.LabelFa} {periodLabels[rank]} − {row.LabelFa} {periodLabels[baseRank]}) ÷ |{row.LabelFa} {periodLabels[baseRank]}| × 100",
            [current.EvidenceId, previous.EvidenceId], rank));
    }

    private static IReadOnlyList<string>? Operands(
        Dictionary<(string RowId, int Rank), ReportFact> facts, params (string RowId, int Rank)[] keys)
    {
        var result = new List<string>(keys.Length);
        foreach (var key in keys)
        {
            if (!facts.TryGetValue(key, out var fact)) return null;
            result.Add(fact.EvidenceId);
        }
        return result;
    }

    private static int IndexOf(IReadOnlyList<QuarterlyFiscalPeriod> periods, QuarterlyFiscalPeriod period) =>
        FindPeriod(periods, period.FiscalYear, period.Quarter);

    private static int FindPeriod(IReadOnlyList<QuarterlyFiscalPeriod> periods, int fiscalYear, int quarter)
    {
        for (var index = 0; index < periods.Count; index++)
        {
            if (periods[index].FiscalYear == fiscalYear && periods[index].Quarter == quarter) return index;
        }
        return -1;
    }

    private static string QuarterLabel(QuarterlyFiscalPeriod period) => $"فصل {period.Quarter} سال مالی {period.FiscalYear}";

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed class EvidenceIds
    {
        private int _facts;
        private int _calculations;
        public string NextFact() => $"F{++_facts}";
        public string NextCalculation() => $"C{++_calculations}";
    }
}

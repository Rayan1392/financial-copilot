using System.Text.Json;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using static FinancialCopilot.UnitTests.BalanceSheet154Fixtures;

namespace FinancialCopilot.UnitTests;

public sealed class BalanceSheetReport154RegistryTests
{
    [Fact]
    public void Templates_FixOrderIdsAndPd22Placement()
    {
        Assert.Equal(
        [
            "cash", "bank_receivables_central_bank", "bank_receivables_banks", "bank_loans_granted", "current_assets_total",
            "non_current_assets_total", "total_assets", "bank_deposits", "bank_due_to_banks", "current_liabilities_total",
            "non_current_liabilities_total", "total_liabilities", "share_capital", "bank_treasury_shares", "retained_earnings",
            "total_equity", "total_liabilities_and_equity"
        ], BalanceSheetRowRegistry.Banking.Select(row => row.RowId));
        Assert.Equal(
        [
            "cash", "trade_receivables", "inventories", "orders_prepayments", "ppe", "current_assets_total", "non_current_assets_total",
            "total_assets", "trade_payables", "advances_received", "short_term_borrowings", "current_liabilities_total",
            "long_term_borrowings", "non_current_liabilities_total", "total_liabilities", "share_capital", "capital_increase_funds",
            "revaluation_surplus", "retained_earnings", "total_equity", "total_liabilities_and_equity"
        ], BalanceSheetRowRegistry.General.Select(row => row.RowId));

        var generalDetailsInSubgroups = BalanceSheetRowRegistry.General
            .Where(row => row.Kind == BalanceSheetRowKind.Detail && row.Placement != BalanceSheetPlacement.Section)
            .Select(row => (row.PrimarySourceItemId, row.Placement)).ToArray();
        Assert.Equal([(47, BalanceSheetPlacement.Current), (51, BalanceSheetPlacement.NonCurrent)], generalDetailsInSubgroups);
        foreach (var id in new[] { 1, 306, 18, 19, 21, 307, 45 })
            Assert.Equal(BalanceSheetPlacement.Section, BalanceSheetRowRegistry.General.Single(row => row.PrimarySourceItemId == id).Placement);
    }

    [Fact]
    public void Registry_PrimariesFallbacksAndExclusions()
    {
        var all = BalanceSheetRowRegistry.Banking.Concat(BalanceSheetRowRegistry.General).ToArray();
        Assert.All(all.Where(row => row.RowId == "share_capital"), row => Assert.Equal(57, row.PrimarySourceItemId));
        foreach (var (primary, fallback) in new[] { (135, 298), (137, 299), (138, 300), (139, 301), (140, 302), (143, 303), (147, 304), (148, 305), (306, 293), (307, 308) })
        {
            var row = all.First(item => item.PrimarySourceItemId == primary);
            Assert.Equal(fallback, row.Fallback!.SourceItemId);
            Assert.False(row.Fallback.Verified); // T01: E-1 not verified, every fallback stays disabled.
        }

        foreach (var excluded in new[] { 286, 60, 146, 160, 188 })
        {
            Assert.DoesNotContain(all, row => row.PrimarySourceItemId == excluded || row.Fallback?.SourceItemId == excluded);
            Assert.DoesNotContain(excluded, BalanceSheetRowRegistry.RegisteredSourceItemIds);
        }

        Assert.DoesNotContain(BalanceSheetRowRegistry.Banking, row => row.RowId is "trade_receivables" or "inventories" or "ppe");
        Assert.DoesNotContain(BalanceSheetRowRegistry.General, row => row.RowId.StartsWith("bank_", StringComparison.Ordinal));
        Assert.Equal(["net_working_capital", "interest_bearing_liabilities"], BalanceSheetRowRegistry.GeneralAnalytical.Select(row => row.RowId));
    }
}

public sealed class BalanceSheetReport154BuilderTests
{
    private static BalanceSheetSnapshot Build(string template, IReadOnlyList<BalanceSheetItemObservation> items) =>
        BalanceSheetReportBuilder.Build(template, Header("409865", J(1405, 6, 31), "SixMonths", "1405/12/29"), items);

    private static BalanceSheetRow Row(BalanceSheetSnapshot snapshot, string id) =>
        BalanceSheetComparisonBuilder.AllRows(snapshot).Single(row => row.RowId == id);

    [Fact]
    public void WebMellat_BankingTemplate_ReportedValuesRatioAndChecks()
    {
        var snapshot = Build(BalanceSheetTemplates.Banking, WebMellatItems());

        Assert.Equal(409_287_621m, Row(snapshot, "cash").Cell.Value);
        Assert.Equal(3_382_000_000m, Row(snapshot, "share_capital").Cell.Value);
        Assert.Equal(57, Row(snapshot, "share_capital").Cell.SelectedSourceItemId);
        Assert.Equal(45_209_234_799m, Row(snapshot, "bank_loans_granted").Cell.Value);
        Assert.Equal(27_590_264_477m, Row(snapshot, "bank_deposits").Cell.Value);
        Assert.Equal(-181_354_538m, Row(snapshot, "bank_treasury_shares").Cell.Value);
        Assert.Equal(59_110_304_681m, snapshot.FinalTotals.TotalAssets.Cell.Value);
        Assert.Equal(59_110_304_681m, snapshot.FinalTotals.TotalLiabilitiesAndEquity.Cell.Value);
        Assert.Contains(snapshot.Sections[0].Groups, group => group.GroupId == "current" && group.Subtotal!.Cell.Value == 51_498_227_670m);
        Assert.Contains(snapshot.Sections[1].Groups, group => group.GroupId == "non_current" && group.Subtotal!.Cell.Value == 126_830_069m);
        // Absent banking addition (2) is not rendered; no General row appears.
        Assert.DoesNotContain(BalanceSheetComparisonBuilder.AllRows(snapshot), row => row.RowId is "bank_receivables_central_bank" or "trade_receivables");
        Assert.StartsWith("10.96545763717478", snapshot.Ratio.Value!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("۱۰٫۹۷٪", BalanceSheetFormatting.Percent(snapshot.Ratio.Value));
        Assert.Equal(BalanceSheetCheckStates.Reconciled, snapshot.Reconciliation.R1.State);
        Assert.Equal(BalanceSheetCheckStates.Reconciled, snapshot.Reconciliation.R2.State);
        Assert.All(snapshot.Sections, section => Assert.Equal("اقلام منتخب", section.Caption));
    }

    [Fact]
    public void ShegolAndFazar_GeneralTemplate_PlacementZeroMissingAndExclusions()
    {
        var shegol = Build(BalanceSheetTemplates.General, ShegolItems());
        var fazar = Build(BalanceSheetTemplates.General, FazarItems());

        Assert.Equal(6_149_600m, Row(shegol, "share_capital").Cell.Value);
        Assert.Equal(18_126_475m, Row(fazar, "share_capital").Cell.Value);
        Assert.Equal(26_549_068m, Row(shegol, "trade_receivables").Cell.Value);
        Assert.Equal(306, Row(shegol, "trade_receivables").Cell.SelectedSourceItemId);
        Assert.Equal(29_966_070m, Row(fazar, "trade_payables").Cell.Value);
        Assert.Equal(0m, Row(shegol, "long_term_borrowings").Cell.Value);
        Assert.Equal("۰", BalanceSheetFormatting.Amount(Row(shegol, "long_term_borrowings").Cell.Value));
        // Common cash row with no observation shows «—»; an unobserved addition is not rendered.
        Assert.Equal(BalanceSheetCellStates.Missing, Row(fazar, "cash").Cell.State);
        Assert.Equal("—", BalanceSheetFormatting.Amount(Row(fazar, "cash").Cell.Value));
        Assert.DoesNotContain(BalanceSheetComparisonBuilder.AllRows(fazar), row => row.RowId == "inventories");

        var liabilities = shegol.Sections.Single(section => section.SectionId == "liabilities");
        Assert.Equal(["trade_payables"], liabilities.Groups.Single(group => group.GroupId == "none").Rows.Select(row => row.RowId));
        Assert.Equal(["short_term_borrowings"], liabilities.Groups.Single(group => group.GroupId == "current").Rows.Select(row => row.RowId));
        Assert.Equal(["long_term_borrowings"], liabilities.Groups.Single(group => group.GroupId == "non_current").Rows.Select(row => row.RowId));
        Assert.Equal(["cash", "trade_receivables", "inventories"], shegol.Sections[0].Groups.Single(group => group.GroupId == "none").Rows.Select(row => row.RowId));
        Assert.Empty(shegol.Sections[0].Groups.Single(group => group.GroupId == "current").Rows);

        Assert.Equal("۳۵٫۴۳٪", BalanceSheetFormatting.Percent(shegol.Ratio.Value));
        Assert.Equal("۵۸٫۷۵٪", BalanceSheetFormatting.Percent(fazar.Ratio.Value));
        Assert.DoesNotContain(BalanceSheetComparisonBuilder.AllRows(fazar), row => row.Cell.SelectedSourceItemId == 146);
    }

    [Fact]
    public void ExcludedItems_NeverRender_And188NeverReplaces57()
    {
        var snapshot = Build(BalanceSheetTemplates.General, Items((188, 5m), (60, 0m), (286, 6m), (160, 7m), (138, 10m), (148, 10m)));
        Assert.Equal(BalanceSheetCellStates.Missing, Row(snapshot, "share_capital").Cell.State);
        Assert.DoesNotContain(BalanceSheetComparisonBuilder.AllRows(snapshot),
            row => row.Cell.Candidates.Any(candidate => candidate.SourceItemId is 188 or 60 or 286 or 160));
    }

    [Fact]
    public void AliasMatrix_EveryRow()
    {
        var unverified = new BalanceSheetRowDefinition("total_assets", "جمع کل دارایی‌ها", BalanceSheetSectionId.Assets,
            BalanceSheetPlacement.Section, BalanceSheetRowKind.FinalTotal, 138, new BalanceSheetFallback(300, false), true, "definition");
        var verified = unverified with { Fallback = new BalanceSheetFallback(300, true) };
        BalanceSheetCell Cell(BalanceSheetRowDefinition definition, params (int, decimal?)[] values) =>
            BalanceSheetReportBuilder.BuildCell(definition, Guid.NewGuid(), Items(values).ToLookup(item => item.SourceItemId));

        Assert.Equal((BalanceSheetCellStates.Available, 100m, 138), Shape(Cell(unverified, (138, 100m))));
        var equal = Cell(unverified, (138, 100m), (300, 100m));
        Assert.Equal((BalanceSheetCellStates.Available, 100m, 138), Shape(equal));
        Assert.Equal(2, equal.Candidates.Count);
        var unverifiedDifferent = Cell(unverified, (138, 100m), (300, 90m));
        Assert.Null(unverifiedDifferent.AliasDiscrepancy);
        Assert.Contains(unverifiedDifferent.Candidates, candidate => candidate.SourceItemId == 300 && !candidate.Verified);
        var verifiedDifferent = Cell(verified, (138, 100m), (300, 90m));
        Assert.Equal((BalanceSheetCellStates.Available, 100m, 138), Shape(verifiedDifferent));
        Assert.Equal(new BalanceSheetAliasDiscrepancy(300, 90m, 10m), verifiedDifferent.AliasDiscrepancy);
        Assert.Equal((BalanceSheetCellStates.Missing, null, null), Shape(Cell(unverified, (300, 90m))));
        Assert.Equal("unverified_fallback_only", Cell(unverified, (300, 90m)).Reason);
        Assert.Equal((BalanceSheetCellStates.Available, 90m, 300), Shape(Cell(verified, (300, 90m))));
        Assert.Equal((BalanceSheetCellStates.Missing, null, null), Shape(Cell(unverified)));
        Assert.Equal(BalanceSheetCellStates.Ambiguous, Cell(unverified, (138, 1m), (138, 2m)).State);
    }

    private static (string, decimal?, int?) Shape(BalanceSheetCell cell) => (cell.State, cell.Value, cell.SelectedSourceItemId);

    [Fact]
    public void ReportedTotalsOnly_AndSeparateReconciliationChecks()
    {
        // Details 1 + 18 do not sum to 138: the reported total is shown unchanged (AC-M07).
        var snapshot = Build(BalanceSheetTemplates.General, Items((1, 1m), (18, 2m), (138, 100m), (143, 60m), (147, 30m), (148, 95m)));
        Assert.Equal(100m, snapshot.FinalTotals.TotalAssets.Cell.Value);
        Assert.Equal(new BalanceSheetCheck(BalanceSheetCheckStates.Discrepancy, 100m, 95m, 5m), snapshot.Reconciliation.R1);
        Assert.Equal(new BalanceSheetCheck(BalanceSheetCheckStates.Discrepancy, 100m, 90m, 10m), snapshot.Reconciliation.R2);
        var notices = BalanceSheetReportTextRenderer.Notices(snapshot).ToArray();
        Assert.Equal(2, notices.Length);

        var r1Only = Build(BalanceSheetTemplates.General, Items((138, 100m), (143, 60m), (147, 40m), (148, 99m)));
        Assert.Equal(BalanceSheetCheckStates.Discrepancy, r1Only.Reconciliation.R1.State);
        Assert.Equal(BalanceSheetCheckStates.Reconciled, r1Only.Reconciliation.R2.State);
        var missing = Build(BalanceSheetTemplates.General, Items((138, 100m)));
        Assert.Equal(BalanceSheetCheckStates.Unavailable, missing.Reconciliation.R1.State);
        Assert.Equal(BalanceSheetCheckStates.Unavailable, missing.Reconciliation.R2.State);
    }

    [Fact]
    public void Ratio_UnavailableZeroAndNegativeCases()
    {
        Assert.Equal(BalanceSheetReportBuilder.RatioMissingTotalEquity, Build(BalanceSheetTemplates.General, Items((138, 100m))).Ratio.Reason);
        Assert.Equal(BalanceSheetReportBuilder.RatioMissingTotalAssets, Build(BalanceSheetTemplates.General, Items((147, 100m))).Ratio.Reason);
        Assert.Equal(BalanceSheetReportBuilder.RatioZeroTotalAssets, Build(BalanceSheetTemplates.General, Items((138, 0m), (147, 1m))).Ratio.Reason);
        Assert.Equal("۰٫۰۰٪", BalanceSheetFormatting.Percent(Build(BalanceSheetTemplates.General, Items((138, 100m), (147, 0m))).Ratio.Value));
        var negative = Build(BalanceSheetTemplates.General, Totals(100_000m, 105_000m, -5_000m)).Ratio; // S-8
        Assert.Equal(-5m, negative.Value);
        Assert.Equal("−۵٫۰۰٪", BalanceSheetFormatting.Percent(negative.Value));
    }
}

public sealed class BalanceSheetReport154SelectionTests
{
    [Fact]
    public void Eligibility_ExcludesConsolidatedScaleAndScopeDisagreement()
    {
        var headers = new[]
        {
            Header("ok", J(1405, 6, 31), "SixMonths", "1405/12/29"),
            Header("consolidated-later", J(1405, 9, 30), "NineMonths", "1405/12/29", composing: true),
            Header("rials", J(1405, 6, 31), "SixMonths", "1405/12/29", scale: "Rials"),
            Header("scope", J(1405, 6, 31), "SixMonths", "1405/12/29", sourceScope: true)
        };
        var eligibility = BalanceSheetStatementSelector.Filter(headers);
        Assert.Equal(["ok"], eligibility.Eligible.Select(item => item.ExternalStatementId));
        Assert.Equal([BalanceSheetStatementSelector.ConsolidatedScope, BalanceSheetStatementSelector.IneligibleStorageScale,
            BalanceSheetStatementSelector.ScopeMetadataDisagrees], eligibility.Ineligible.Select(item => item.Reason));
        Assert.Equal([J(1405, 6, 31)], BalanceSheetStatementSelector.DistinctDatesDescending(eligibility.Eligible));
    }

    [Fact]
    public void Ranking_AuditedThenRestatedThenPublication_ConflictAndStableId()
    {
        var date = J(1405, 6, 31);
        var ranked = BalanceSheetStatementSelector.RankedAt(new[]
        {
            Header("a", date, "SixMonths", null, published: new DateOnly(2026, 10, 1)),
            Header("b", date, "SixMonths", null, audited: true),
            Header("c", date, "SixMonths", null, represented: true, published: new DateOnly(2026, 9, 1)),
            Header("d", date, "SixMonths", null, represented: true, published: new DateOnly(2026, 10, 2))
        }, date);
        Assert.Equal(["b", "d", "c", "a"], ranked.Select(item => item.ExternalStatementId));

        var x = Header("409865", date, "SixMonths", null);
        var y = Header("409866", date, "SixMonths", null);
        var identical = new Dictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>> { [x.StatementId] = Items((138, 1m)), [y.StatementId] = Items((138, 1m)) };
        Assert.Equal("409866", BalanceSheetStatementSelector.Pick([x, y], identical).Winner!.ExternalStatementId);
        var differing = new Dictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>> { [x.StatementId] = Items((138, 1m)), [y.StatementId] = Items((138, 2m)) };
        Assert.True(BalanceSheetStatementSelector.Pick([x, y], differing).IsConflict);
    }

    [Fact]
    public void ChartDates_FiveLatestDistinctOnOrBeforeAnchor_Ascending()
    {
        var dates = Enumerable.Range(0, 8).Select(index => J(1405, 6, 31).AddMonths(-3 * index)).ToArray();
        var chart = BalanceSheetStatementSelector.ChartDates(dates, dates[0]);
        Assert.Equal(5, chart.Count);
        Assert.Equal(dates.Take(5).Order(), chart);
    }
}

public sealed class BalanceSheetReport154BaselineTests
{
    private static BalanceSheetFiscalEvidence E(DateOnly end, string fye) => new(end, fye);

    private static IReadOnlyDictionary<string, BalanceSheetBaselineTarget> Resolve(BalanceSheetStatementHeader anchor,
        IReadOnlyList<DateOnly> dates, params BalanceSheetFiscalEvidence[] evidence) =>
        BalanceSheetBaselineResolver.ResolveStandardTargets(anchor, dates.OrderDescending().ToArray(), evidence)
            .ToDictionary(target => target.Kind);

    [Fact]
    public void EsfandIssuer_FiscalEvidenceDrivesEveryBaseline()
    {
        var anchor = Header("409865", J(1405, 6, 31), "SixMonths", "1405/12/29");
        var targets = Resolve(anchor, [J(1405, 6, 31), J(1405, 3, 31), J(1404, 12, 29)],
            E(J(1405, 6, 31), "1405/12/29"), E(J(1405, 3, 31), "1405/12/29"), E(J(1404, 12, 29), "1404/12/29"));
        Assert.Equal(J(1404, 12, 29), targets[BalanceSheetBaselineKinds.FiscalYearEnd].Date);
        Assert.Equal(J(1405, 3, 31), targets[BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd].Date);
        Assert.Equal(J(1405, 3, 31), targets[BalanceSheetBaselineKinds.PreviousReport].Date);
        Assert.Equal(J(1404, 6, 31), targets[BalanceSheetBaselineKinds.SameStagePreviousYear].Date);
        Assert.Equal("SixMonths", targets[BalanceSheetBaselineKinds.SameStagePreviousYear].PeriodType);
    }

    [Fact]
    public void NonEsfandIssuer_UsesEvidenceNot29Esfand()
    {
        var anchor = Header("s", J(1405, 6, 31), "NineMonths", "1405/09/30");
        var targets = Resolve(anchor, [J(1405, 6, 31), J(1405, 3, 31), J(1404, 9, 30)],
            E(J(1405, 6, 31), "1405/09/30"), E(J(1404, 9, 30), "1404/09/30"));
        Assert.Equal(J(1404, 9, 30), targets[BalanceSheetBaselineKinds.FiscalYearEnd].Date);
        Assert.NotEqual(J(1404, 12, 29), targets[BalanceSheetBaselineKinds.FiscalYearEnd].Date);
        Assert.Equal(J(1404, 6, 31), targets[BalanceSheetBaselineKinds.SameStagePreviousYear].Date); // previous FY starts 1403/10/01
        Assert.Equal(J(1405, 3, 31), targets[BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd].Date);
    }

    [Fact]
    public void MissingFiscalEvidence_DisablesOnlyFiscalKinds()
    {
        var anchor = Header("x", J(1405, 6, 31), "SixMonths", null);
        var targets = Resolve(anchor, [J(1405, 6, 31), J(1404, 6, 31)]);
        foreach (var kind in new[] { BalanceSheetBaselineKinds.FiscalYearEnd, BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, BalanceSheetBaselineKinds.SameStagePreviousYear })
            Assert.Equal(BalanceSheetBaselineResolver.MissingFiscalMetadata, targets[kind].Reason);
        Assert.Equal(J(1404, 6, 31), targets[BalanceSheetBaselineKinds.PreviousReport].Date); // more than three months back
    }

    [Fact]
    public void FirstQuarterAnchor_PreviousQuarterIsPreviousFiscalYearEnd()
    {
        var anchor = Header("403733", J(1405, 3, 31), "ThreeMonths", "1405/12/29");
        var targets = Resolve(anchor, [J(1405, 3, 31)], E(J(1405, 3, 31), "1405/12/29"), E(J(1404, 12, 29), "1404/12/29"));
        Assert.Equal(J(1404, 12, 29), targets[BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd].Date);
        Assert.Equal(BalanceSheetBaselineResolver.NoEarlierReport, targets[BalanceSheetBaselineKinds.PreviousReport].Reason);
    }

    [Fact]
    public void TransitionalPreviousYear_SameStageUnavailable_S6()
    {
        var anchor = Header("t", J(1405, 6, 31), "SixMonths", "1405/12/29");
        var targets = Resolve(anchor, [J(1405, 6, 31), J(1404, 12, 29), J(1404, 9, 30)],
            E(J(1405, 6, 31), "1405/12/29"), E(J(1404, 12, 29), "1404/12/29"), E(J(1404, 9, 30), "1404/09/30"));
        Assert.Equal(BalanceSheetBaselineResolver.FiscalCalendarChanged, targets[BalanceSheetBaselineKinds.SameStagePreviousYear].Reason);
        Assert.Equal(J(1404, 12, 29), targets[BalanceSheetBaselineKinds.FiscalYearEnd].Date);
    }
}

public sealed class BalanceSheetReport154ComparisonTests
{
    [Fact]
    public void S1_PreviousReport_ExactChangesAndPpFromUnroundedRatios()
    {
        var anchor = BalanceSheetReportBuilder.Build(BalanceSheetTemplates.Banking, Header("409865", J(1405, 6, 31), "SixMonths", "1405/12/29"), WebMellatItems());
        var baseline = BalanceSheetReportBuilder.Build(BalanceSheetTemplates.Banking, Header("403733", J(1405, 3, 31), "ThreeMonths", "1405/12/29"), WebMellatS1Items());
        var changes = BalanceSheetComparisonBuilder.RowChanges(anchor, baseline);
        Assert.Equal(9_110_304_681m, changes["total_assets"].Amount);
        Assert.Equal("+۱۸٫۲۲٪", BalanceSheetFormatting.Percent(changes["total_assets"].Percent, signed: true));
        Assert.Equal(8_105_589_262m, changes["total_liabilities"].Amount);
        Assert.Equal("+۱۸٫۲۱٪", BalanceSheetFormatting.Percent(changes["total_liabilities"].Percent, signed: true));
        Assert.Equal(1_004_715_419m, changes["total_equity"].Amount);
        Assert.Equal("+۱۸٫۳۴٪", BalanceSheetFormatting.Percent(changes["total_equity"].Percent, signed: true));
        var ratio = BalanceSheetComparisonBuilder.RatioChange(anchor.Ratio, baseline.Ratio);
        Assert.Equal("+۰٫۰۱ واحد درصد", BalanceSheetFormatting.PercentagePoints(ratio.PpDifference));
        Assert.NotEqual("+۰٫۰۲ واحد درصد", BalanceSheetFormatting.PercentagePoints(ratio.PpDifference));
        // Missing baseline addition gives no change.
        Assert.Equal(BalanceSheetChangeStates.Unavailable, changes["bank_loans_granted"].State);
    }

    [Fact]
    public void S2_NonEsfandFiscalYearEnd_Changes()
    {
        var anchor = BalanceSheetReportBuilder.Build(BalanceSheetTemplates.General, Header("s", J(1405, 6, 31), "NineMonths", "1405/09/30"), ShegolItems());
        var baseline = BalanceSheetReportBuilder.Build(BalanceSheetTemplates.General, Header("b", J(1404, 9, 30), "TwelveMonths", "1404/09/30"), ShegolS2Items());
        var changes = BalanceSheetComparisonBuilder.RowChanges(anchor, baseline);
        Assert.Equal(("+۵٫۸۷٪", 3_522_534m), (BalanceSheetFormatting.Percent(changes["total_assets"].Percent, true), changes["total_assets"].Amount!.Value));
        Assert.Equal("+۲٫۵۴٪", BalanceSheetFormatting.Percent(changes["total_liabilities"].Percent, true));
        Assert.Equal("+۱۲٫۵۴٪", BalanceSheetFormatting.Percent(changes["total_equity"].Percent, true));
        Assert.Equal("+۲٫۱۰ واحد درصد", BalanceSheetFormatting.PercentagePoints(BalanceSheetComparisonBuilder.RatioChange(anchor.Ratio, baseline.Ratio).PpDifference));
    }

    [Fact]
    public void S4_ZeroAndNegativeBaselines_KeepAmountWithoutPercent()
    {
        var anchor = BalanceSheetReportBuilder.Build(BalanceSheetTemplates.General, Header("a", J(1405, 6, 31), "SixMonths", null), Items((71, 500m), (57, 200m)));
        var baseline = BalanceSheetReportBuilder.Build(BalanceSheetTemplates.General, Header("b", J(1404, 6, 31), "SixMonths", null), Items((71, -1_000m), (57, 0m)));
        var changes = BalanceSheetComparisonBuilder.RowChanges(anchor, baseline);
        Assert.Equal(new BalanceSheetChange(1_500m, null, BalanceSheetChangeStates.PercentUnavailable, "negative_baseline"), changes["retained_earnings"]);
        Assert.Equal(new BalanceSheetChange(200m, null, BalanceSheetChangeStates.PercentUnavailable, "zero_baseline"), changes["share_capital"]);
        Assert.Equal(BalanceSheetChangeStates.Unavailable, changes["total_assets"].State);
    }
}

public sealed class BalanceSheetReport154UseCaseTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static (BalanceSheetReportQueryUseCase UseCase, FakeBalanceSheetReader Reader) Create(Dictionary<string, Guid?[]>? groups = null)
    {
        var reader = new FakeBalanceSheetReader();
        var useCase = new BalanceSheetReportQueryUseCase(reader, new FakeCompanyResolution(),
            new FakeClassification(groups ?? new() { ["452"] = [BankGroup], ["167"] = [ShegolGroup], ["290"] = [FazarGroup] }),
            new BankingFinancialReportsConfiguration(BankGroup), Clock);
        return (useCase, reader);
    }

    private static FakeBalanceSheetReader SeedWebMellat(FakeBalanceSheetReader reader)
    {
        reader.Add(Header("409865", J(1405, 6, 31), "SixMonths", "1405/12/29", published: new DateOnly(2026, 10, 22)), WebMellatItems());
        reader.Add(Header("403733", J(1405, 3, 31), "ThreeMonths", "1405/12/29", published: new DateOnly(2026, 7, 21)), WebMellatS1Items());
        reader.Add(Header("fye-1404", J(1404, 12, 29), "TwelveMonths", "1404/12/29", audited: true), Totals(48_000_000_000m, 42_000_000_000m, 6_000_000_000m)); // SYNTHETIC S-3
        reader.Add(Header("9m-1404", J(1404, 9, 30), "NineMonths", "1404/12/29"), Totals(35_000_000_000m, 30_000_000_000m, 5_000_000_000m)); // SYNTHETIC
        reader.Add(Header("6m-1404", J(1404, 6, 31), "SixMonths", "1404/12/29"), Totals(30_000_000_000m, 26_000_000_000m, 4_000_000_000m)); // SYNTHETIC
        reader.Add(Header("3m-1404", J(1404, 3, 31), "ThreeMonths", "1404/12/29"), Totals(28_000_000_000m, 24_000_000_000m, 4_000_000_000m)); // SYNTHETIC
        return reader;
    }

    [Fact]
    public async Task DefaultReport_WebMellat_CarriesStandardCandidatesWithinBounds()
    {
        var (useCase, reader) = Create();
        SeedWebMellat(reader);

        var result = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت"));

        Assert.Equal(BalanceSheetOutcomes.Available, result.Outcome);
        Assert.Equal(BalanceSheetTemplates.Banking, result.TemplateId);
        Assert.Equal("452", result.Company.ExternalCompanyId);
        var anchor = result.Snapshots[result.AnchorStatementId!];
        Assert.Equal("409865", anchor.Statement.ExternalStatementId);
        Assert.Equal("1405/07/30", anchor.Statement.JalaliPublishedAt);
        Assert.Equal(BalanceSheetViews.Default, result.RequestedView);
        Assert.Equal(BalanceSheetBaselineKinds.FiscalYearEnd, result.Comparison!.SelectedBaselineKind);
        Assert.Equal(BalanceSheetBaselineKinds.Standard, result.Comparison.Candidates.Select(item => item.Kind));
        Assert.All(result.Comparison.Candidates, candidate => Assert.Equal("available", candidate.State));
        var fiscal = result.Comparison.Candidates[0];
        Assert.Equal("پایان سال مالی قبل (۱۴۰۴/۱۲/۲۹)", BalanceSheetFormatting.BaselineLabel(fiscal));
        Assert.False(fiscal.AnchorLinkVerified);
        Assert.Equal(BalanceSheetBaselineSourcePolicy.StoredSnapshot, fiscal.Source);
        Assert.Equal(J(1405, 3, 31), result.Comparison.Candidates.Single(item => item.Kind == BalanceSheetBaselineKinds.PreviousReport).BaselineDate);
        Assert.Equal("«+۰٫۰۱ واحد درصد»", $"«{BalanceSheetFormatting.PercentagePoints(result.Comparison.Candidates.Single(item => item.Kind == BalanceSheetBaselineKinds.PreviousReport).RatioChange!.PpDifference)}»");
        Assert.True(result.Snapshots.Count <= BalanceSheetReportContract.MaxSnapshots);
        Assert.Equal(5, result.Chart.Points.Count);
        Assert.Equal(result.Chart.Points.Select(point => point.PeriodEnd).Order(), result.Chart.Points.Select(point => point.PeriodEnd));
        Assert.All(result.Chart.Points, point => Assert.True(result.Snapshots.ContainsKey(point.StatementId!)));
        Assert.True(reader.Calls <= 6, $"reader round trips {reader.Calls}");
        Assert.True(reader.LineItemRequests.Single().Count <= 9);
    }

    [Fact]
    public async Task SingleDate_ExactMatchOrNamedNoSnapshot_S10()
    {
        var (useCase, reader) = Create();
        SeedWebMellat(reader);

        var exact = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.SingleDate, Dates: [J(1405, 3, 31)]));
        Assert.Equal("403733", exact.Snapshots[exact.AnchorStatementId!].Statement.ExternalStatementId);
        Assert.Equal(BalanceSheetViews.SingleDate, exact.RequestedView);
        Assert.Equal(4, exact.Comparison!.Candidates.Count);

        var missing = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.SingleDate, Dates: [J(1405, 4, 31)]));
        Assert.Equal(BalanceSheetOutcomes.NoSnapshotAtDate, missing.Outcome);
        Assert.Contains("۱۴۰۵/۰۴/۳۱", missing.OutcomeMessage);
        Assert.Equal(J(1405, 3, 31), missing.NearestAvailableDates!.Earlier);
        Assert.Equal(J(1405, 6, 31), missing.NearestAvailableDates.Later);
        Assert.Empty(missing.Snapshots);
    }

    [Fact]
    public async Task ExplicitDates_LaterIsAnchor_AndMissingDateIsNeverSnapped()
    {
        var (useCase, reader) = Create();
        SeedWebMellat(reader);

        var reversed = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.Comparison,
            BalanceSheetBaselineKinds.Explicit, [J(1405, 3, 31), J(1405, 6, 31)]));
        Assert.Equal("409865", reversed.Snapshots[reversed.AnchorStatementId!].Statement.ExternalStatementId);
        var explicitCandidate = reversed.Comparison!.Candidates.Single(item => item.Kind == BalanceSheetBaselineKinds.Explicit);
        Assert.Equal(J(1405, 3, 31), explicitCandidate.BaselineDate);
        Assert.Equal(BalanceSheetBaselineKinds.Explicit, reversed.Comparison.SelectedBaselineKind);

        var s7 = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.Comparison,
            BalanceSheetBaselineKinds.Explicit, [J(1405, 6, 31), J(1405, 4, 31)]));
        var unavailable = s7.Comparison!.Candidates.Single(item => item.Kind == BalanceSheetBaselineKinds.Explicit);
        Assert.Equal(("unavailable", BalanceSheetOutcomes.NoSnapshotAtDate, J(1405, 4, 31)), (unavailable.State, unavailable.Reason, unavailable.BaselineDate));
        Assert.NotEmpty(unavailable.Alternatives);
    }

    [Fact]
    public async Task S9_PreviousQuarterEndUnavailable_OffersPreviousReportWithoutSelectingIt()
    {
        var (useCase, reader) = Create();
        reader.Add(Header("9m", J(1405, 9, 30), "NineMonths", "1405/12/29"), WebMellatItems());
        reader.Add(Header("3m", J(1405, 3, 31), "ThreeMonths", "1405/12/29"), WebMellatS1Items());

        var result = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.Comparison,
            BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd));

        var quarter = result.Comparison!.Candidates.Single(item => item.Kind == BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd);
        Assert.Equal(("unavailable", J(1405, 6, 31)), (quarter.State, quarter.BaselineDate!.Value));
        Assert.Equal("پایان فصل قبل (۱۴۰۵/۰۶/۳۱)", BalanceSheetFormatting.BaselineLabel(quarter));
        Assert.Equal(BalanceSheetBaselineKinds.PreviousReport, quarter.Alternatives[0]);
        Assert.Equal(BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd, result.Comparison.SelectedBaselineKind);
        Assert.Equal(J(1405, 3, 31), result.Comparison.Candidates.Single(item => item.Kind == BalanceSheetBaselineKinds.PreviousReport).BaselineDate);
    }

    [Fact]
    public async Task S5_RestatedBaselineWinsAndIsLabelledByItsOwnFlag()
    {
        var (useCase, reader) = Create();
        reader.Add(Header("anchor", J(1405, 6, 31), "SixMonths", "1405/12/29"), WebMellatItems());
        reader.Add(Header("orig", J(1404, 12, 29), "TwelveMonths", "1404/12/29", audited: true), Totals(10m, 6m, 4m));
        var restated = reader.Add(Header("rest", J(1404, 12, 29), "TwelveMonths", "1404/12/29", audited: true, represented: true), Totals(12m, 7m, 5m));

        var result = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت"));

        var fiscal = result.Comparison!.Candidates[0];
        Assert.Equal(restated.StatementId.ToString("D"), fiscal.StatementId);
        Assert.True(result.Snapshots[fiscal.StatementId!].Statement.IsRepresented);
        Assert.False(fiscal.AnchorLinkVerified);
    }

    [Fact]
    public async Task Outcomes_ClassificationAmbiguousNoStandaloneConflictAndVariant()
    {
        var (missingGroup, _) = Create(new() { ["452"] = [] });
        Assert.Equal(BalanceSheetOutcomes.ClassificationUnavailable, (await missingGroup.ExecuteAsync(new("وبملت"))).Outcome);
        var (duplicateGroup, _) = Create(new() { ["452"] = [BankGroup, ShegolGroup] });
        Assert.Equal(BalanceSheetOutcomes.ClassificationUnavailable, (await duplicateGroup.ExecuteAsync(new("وبملت"))).Outcome);

        var (useCase, reader) = Create();
        Assert.Equal(BalanceSheetOutcomes.CompanyAmbiguous, (await useCase.ExecuteAsync(new("مبهم"))).Outcome);
        Assert.Equal(BalanceSheetOutcomes.CompanyUnresolved, (await useCase.ExecuteAsync(new("ناشناخته"))).Outcome);

        var none = await useCase.ExecuteAsync(new("شگل"));
        Assert.Equal((BalanceSheetOutcomes.NoStandaloneBalanceSheet, false), (none.Outcome, none.ConsolidatedAlternativeAvailable));
        Assert.Contains("ترازنامه جداگانه (غیرتلفیقی)", none.OutcomeMessage);
        reader.HasConsolidated = true;
        var consolidatedOnly = await useCase.ExecuteAsync(new("شگل"));
        Assert.True(consolidatedOnly.ConsolidatedAlternativeAvailable);
        Assert.Empty(consolidatedOnly.Snapshots);

        var date = J(1405, 6, 31);
        reader.Add(Header("409865", date, "SixMonths", "1405/12/29"), WebMellatItems());
        reader.Add(Header("409864", date, "SixMonths", "1405/12/29"), WebMellatS1Items());
        Assert.Equal(BalanceSheetOutcomes.ConflictingReportRevision, (await useCase.ExecuteAsync(new("وبملت"))).Outcome);

        var variant = await useCase.ExecuteAsync(new("وبملت", BalanceSheetViews.SingleDate, Dates: [J(1405, 3, 31)], UnsupportedVariantFilter: "تلفیقی"));
        Assert.Equal(BalanceSheetOutcomes.UnsupportedDateWithVariant, variant.Outcome);
        Assert.Contains("۱۴۰۵/۰۳/۳۱", variant.OutcomeMessage);
        Assert.Contains("تلفیقی", variant.OutcomeMessage);
    }

    [Fact]
    public async Task GeneralTemplate_ForBothNonBanks()
    {
        var (useCase, reader) = Create();
        reader.Add(Header("s", J(1405, 6, 31), "NineMonths", "1405/09/30"), ShegolItems());
        var result = await useCase.ExecuteAsync(new("شگل"));
        Assert.Equal(BalanceSheetTemplates.General, result.TemplateId);
        Assert.Equal("گزارش ۹ ماهه", BalanceSheetFormatting.PeriodLength(result.Snapshots[result.AnchorStatementId!].Statement.PeriodMonths));
    }

    [Fact]
    public async Task TypedResult_RoundTripsThroughPersistenceSerializer_WithoutLoss()
    {
        var (useCase, reader) = Create();
        SeedWebMellat(reader);
        var result = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت"));
        var table = new FinancialStatementTableResult(null, [], [], [], "x", DateTimeOffset.UtcNow, BalanceSheetReport: result);

        foreach (var options in new[] { new JsonSerializerOptions(), new JsonSerializerOptions(JsonSerializerDefaults.Web) })
        {
            var json = JsonSerializer.Serialize(table, options);
            var replayed = JsonSerializer.Deserialize<FinancialStatementTableResult>(json, options)!.BalanceSheetReport!;
            Assert.Equal(JsonSerializer.Serialize(result, options), JsonSerializer.Serialize(replayed, options));
            Assert.Equal(4, replayed.Comparison!.Candidates.Count);
        }

        var zeroAndNull = BalanceSheetReportBuilder.Build(BalanceSheetTemplates.General, Header("z", J(1405, 6, 31), "SixMonths", null), Items((51, 0m)));
        var replayedZero = JsonSerializer.Deserialize<BalanceSheetSnapshot>(JsonSerializer.Serialize(zeroAndNull))!;
        var rows = BalanceSheetComparisonBuilder.AllRows(replayedZero).ToDictionary(row => row.RowId);
        Assert.Equal(0m, rows["long_term_borrowings"].Cell.Value);
        Assert.Null(rows["cash"].Cell.Value);
        Assert.Equal(BalanceSheetCellStates.Missing, rows["cash"].Cell.State);
    }
}

public sealed class BalanceSheetReport154FrontendFixtureWriter
{
    private static BalanceSheetReportQueryUseCase UseCaseFor(out FakeBalanceSheetReader reader, FakeBalanceSheetReader? existing = null)
    {
        reader = existing ?? new FakeBalanceSheetReader();
        return new BalanceSheetReportQueryUseCase(reader, new FakeCompanyResolution(),
            new FakeClassification(new() { ["290"] = [FazarGroup] }), new BankingFinancialReportsConfiguration(BankGroup), TimeProvider.System);
    }

    /// <summary>Writes the frontend/browser fixtures from the real use case (camelCase, as served by the API).</summary>
    [Fact]
    public async Task WriteFrontendFixtures_OnDemand()
    {
        if (Environment.GetEnvironmentVariable("FEATURE154_WRITE_FRONTEND_FIXTURE") != "1") return;
        var bankReader = new FakeBalanceSheetReader();
        bankReader.Add(Header("409865", J(1405, 6, 31), "SixMonths", "1405/12/29", published: new DateOnly(2026, 10, 22)), WebMellatItems());
        bankReader.Add(Header("403733", J(1405, 3, 31), "ThreeMonths", "1405/12/29", published: new DateOnly(2026, 7, 21)), WebMellatS1Items());
        bankReader.Add(Header("403749", J(1404, 12, 29), "TwelveMonths", "1404/12/29", audited: true), Items(
            (1, 380_000_000m), (57, 3_382_000_000m), (291, 40_000_000_000m), (223, 25_000_000_000m), (170, -181_354_538m),
            (135, 41_900_074_927m), (137, 6_047_565_407m), (139, 29_029_135_553m), (140, 13_309_523_743m),
            (138, 47_947_640_334m), (143, 42_338_659_296m), (147, 5_608_981_038m), (148, 47_947_640_334m))); // SYNTHETIC
        bankReader.Add(Header("385967", J(1404, 9, 30), "NineMonths", "1404/12/29"), Totals(34_618_505_972m, 29_386_809_853m, 5_231_696_119m)); // SYNTHETIC
        bankReader.Add(Header("384526", J(1404, 6, 31), "SixMonths", "1404/12/29", audited: true), Totals(30_000_000_000m, 26_000_000_000m, 4_000_000_000m)); // SYNTHETIC
        var bank = new BalanceSheetReportQueryUseCase(bankReader, new FakeCompanyResolution(),
            new FakeClassification(new() { ["452"] = [BankGroup], ["290"] = [FazarGroup] }), new BankingFinancialReportsConfiguration(BankGroup), TimeProvider.System);
        var webMellat = await bank.ExecuteAsync(new BalanceSheetReportRequest("وبملت"));

        var fazarReader = new FakeBalanceSheetReader();
        fazarReader.Add(Header("f-1405-06", J(1405, 6, 31), "SixMonths", "1405/12/29"), FazarItems());
        fazarReader.Add(Header("f-1404-12", J(1404, 12, 29), "TwelveMonths", "1404/12/29"), Items(
            (57, 18_126_475m), (21, 23_000_000m), (45, 10_000_000m), (51, 0m), (64, 0m), (306, 30_000_000m), (307, 25_000_000m),
            (138, 67_103_318m), (143, 45_479_778m), (147, 21_623_540m), (148, 67_103_318m))); // SYNTHETIC
        var fazarUseCase = new BalanceSheetReportQueryUseCase(fazarReader, new FakeCompanyResolution(),
            new FakeClassification(new() { ["290"] = [FazarGroup] }), new BankingFinancialReportsConfiguration(BankGroup), TimeProvider.System);
        var fazar = await fazarUseCase.ExecuteAsync(new BalanceSheetReportRequest("فاذر"));

        var negativeReader = new FakeBalanceSheetReader();
        negativeReader.Add(Header("n-2", J(1405, 6, 31), "SixMonths", "1405/12/29"), Totals(100_000m, 105_000m, -5_000m)); // SYNTHETIC S-8
        negativeReader.Add(Header("n-1", J(1405, 3, 31), "ThreeMonths", "1405/12/29"), Items((138, 100_000m), (143, 90_000m), (147, 9_000m), (148, 99_000m))); // SYNTHETIC R1/R2
        var negative = await new BalanceSheetReportQueryUseCase(negativeReader, new FakeCompanyResolution(),
            new FakeClassification(new() { ["290"] = [FazarGroup] }), new BankingFinancialReportsConfiguration(BankGroup), TimeProvider.System)
            .ExecuteAsync(new BalanceSheetReportRequest("فاذر"));

        UseCaseFor(out var singleReader);
        singleReader.Add(Header("only", J(1405, 6, 31), "SixMonths", "1405/12/29"), Totals(100_000m, 60_000m, 40_000m)); // SYNTHETIC G-04
        var single = await UseCaseFor(out _, singleReader).ExecuteAsync(new BalanceSheetReportRequest("فاذر"));

        var gapReader = new FakeBalanceSheetReader(); // SYNTHETIC G-05 (missing equity) and G-08 (conflicting versions)
        gapReader.Add(Header("g5", J(1405, 6, 31), "SixMonths", "1405/12/29"), Totals(100_000m, 60_000m, 40_000m));
        gapReader.Add(Header("g4", J(1405, 3, 31), "ThreeMonths", "1405/12/29"), Items((138, 95_000m), (143, 58_000m), (148, 95_000m), (57, 1m)));
        gapReader.Add(Header("g3a", J(1404, 12, 29), "TwelveMonths", "1404/12/29"), Totals(90_000m, 55_000m, 35_000m));
        gapReader.Add(Header("g3b", J(1404, 12, 29), "TwelveMonths", "1404/12/29"), Totals(91_000m, 55_000m, 36_000m));
        gapReader.Add(Header("g2", J(1404, 9, 30), "NineMonths", "1404/12/29"), Totals(85_000m, 52_000m, 33_000m));
        gapReader.Add(Header("g1", J(1404, 6, 31), "SixMonths", "1404/12/29"), Totals(80_000m, 50_000m, 30_000m));
        var gaps = await UseCaseFor(out _, gapReader).ExecuteAsync(new BalanceSheetReportRequest("فاذر"));

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "specs"))) directory = directory.Parent;
        var output = Path.Combine(directory!.FullName, "src", "frontend", "src", "components", "app", "__tests__", "fixtures");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "balance-sheet-154.json"), JsonSerializer.Serialize(new { webMellat, fazar, negative, single, gaps }, options));
    }
}

public sealed class BalanceSheetReport154TextTests
{
    [Fact]
    public async Task Render_DefaultAndComparison_ValueRulesAndLength()
    {
        var reader = new FakeBalanceSheetReader();
        reader.Add(Header("409865", J(1405, 6, 31), "SixMonths", "1405/12/29", published: new DateOnly(2026, 10, 22)), WebMellatItems());
        reader.Add(Header("403733", J(1405, 3, 31), "ThreeMonths", "1405/12/29"), WebMellatS1Items());
        var useCase = new BalanceSheetReportQueryUseCase(reader, new FakeCompanyResolution(),
            new FakeClassification(new() { ["452"] = [BankGroup] }), new BankingFinancialReportsConfiguration(BankGroup), TimeProvider.System);

        var report = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت"));
        var text = BalanceSheetReportTextRenderer.Render(report);
        Assert.Contains("ترازنامه (صورت وضعیت مالی)", text);
        Assert.Contains("۵۹٬۱۱۰٬۳۰۴٬۶۸۱", text);
        Assert.Contains("(۱۸۱٬۳۵۴٬۵۳۸)", text);
        Assert.Contains("۱۰٫۹۷٪", text);
        Assert.Contains("گزارش ۶ ماهه", text);
        Assert.Contains("حسابرسی‌نشده", text);
        Assert.Contains("میلیون ریال", text);
        // Owner instruction 2026-11-05: the standalone basis is not labelled in the header.
        Assert.DoesNotContain("جداگانه", text);
        Assert.DoesNotContain("تلفیقی", text);
        Assert.DoesNotContain("فصل دوم", text);
        Assert.True(text.Length <= BalanceSheetReportTextRenderer.MaxLength);

        var comparison = await useCase.ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.Comparison, BalanceSheetBaselineKinds.PreviousReport));
        var comparisonText = BalanceSheetReportTextRenderer.Render(comparison);
        Assert.Contains("مبنای مقایسه: گزارش قبلی (۱۴۰۵/۰۳/۳۱)", comparisonText);
        Assert.Contains("درصد تغییر +۱۸٫۲۲٪", comparisonText);
        Assert.Contains("+۰٫۰۱ واحد درصد", comparisonText);
        Assert.DoesNotContain("به فصل قبل", comparisonText);
        Assert.True(comparisonText.Length <= BalanceSheetReportTextRenderer.MaxLength);
    }
}

public sealed class FiscalCalendarPolicy154Tests
{
    [Theory]
    [InlineData("1405/12/29", 1405, 1, 1)]
    [InlineData("1405/09/30", 1404, 10, 1)]
    [InlineData("1404/08/30", 1403, 9, 1)]
    public void FiscalStart_FromJalaliYearEnd(string yearEnd, int year, int month, int day) =>
        Assert.Equal(J(year, month, day), FiscalCalendarPolicy.ResolveFiscalStart(yearEnd, default));

    [Fact]
    public void MissingEvidence_FallsBackToPeriodStart_AndGridUsesJalaliMonths()
    {
        Assert.Equal(new DateOnly(2025, 3, 21), FiscalCalendarPolicy.ResolveFiscalStart(null, new DateOnly(2025, 3, 21)));
        Assert.Equal(J(1405, 6, 31), FiscalCalendarPolicy.JalaliEndAfterMonths(J(1405, 1, 1), 6));
        Assert.Equal(J(1405, 6, 31), FiscalCalendarPolicy.JalaliEndAfterMonths(J(1404, 10, 1), 9));
        Assert.Equal("1405/06/31", FiscalCalendarPolicy.ToJalali(new DateOnly(2026, 9, 22)));
    }

    [Fact]
    public void TransitionalYear_IsRebased()
    {
        var reports = new[] { (Start: J(1403, 10, 1), End: J(1404, 9, 30)), (Start: J(1404, 1, 1), End: J(1404, 12, 29)) };
        var effective = FiscalCalendarPolicy.ResolveEffectiveFiscalStarts(reports, item => item.Start, item => item.End);
        Assert.Equal(J(1404, 10, 1), effective[J(1404, 1, 1)]);
        Assert.Equal(J(1403, 10, 1), effective[J(1403, 10, 1)]);
    }
}

public sealed class BalanceSheetReport154CyclicalWavesTests
{
    private sealed class NoQuarterly : IQuarterlyFinancialReportsQueryUseCase
    {
        public Task<QuarterlyFinancialReportTableResult> ExecuteAsync(QuarterlyFinancialReportTableQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not expected");
    }

    [Fact] // AC-R12: ChatProvider=CyclicalWaves keeps the existing provider-unavailable message for balance sheets.
    public async Task CyclicalWavesMode_ReturnsExistingUnavailableMessage()
    {
        var useCase = new FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2.CyclicalWavesV2FinancialStatementTableQueryUseCase(
            new NoQuarterly(), TimeProvider.System);
        var query = FinancialCopilot.Application.AI.Orchestration.FinancialStatementTableIntentRules.BuildV2Query("ترازنامه وبملت");
        Assert.True(query.UseBalanceSheetReport);
        var result = await useCase.ExecuteAsync(query);
        Assert.Equal(FinancialCopilot.Application.AI.Orchestration.ChatCapabilityAvailability.UnavailableForProviderMessageFa, result!.RenderedAnswer);
        Assert.Null(result.BalanceSheetReport);
    }
}

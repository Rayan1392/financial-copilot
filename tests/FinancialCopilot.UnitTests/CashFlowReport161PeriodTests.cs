using System.Globalization;
using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161PeriodTests
{
    private static readonly PersianCalendar Calendar = new();
    private static DateOnly J(int y, int m, int d) => DateOnly.FromDateTime(Calendar.ToDateTime(y, m, d, 0, 0, 0, 0));
    private static CashFlowStatementHeader Header(int months, DateOnly end, string? yearEnd = "1405/12/29", string? id = null) =>
        new(Guid.NewGuid(), id ?? Guid.NewGuid().ToString(), end.AddMonths(-months), end,
            months switch { 3 => "ThreeMonths", 6 => "SixMonths", 9 => "NineMonths", _ => "TwelveMonths" },
            false, false, false, null, null, yearEnd, "MillionRials", false);

    private static CashFlowPeriodSelection Select(CashFlowStatementHeader[] headers, CashFlowPeriodRequest request,
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>>? observations = null, bool truncated = false) =>
        CashFlowPeriodSelector.Select(headers, observations ?? new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), request, truncated);

    [Fact]
    public void Default_SelectsAnchorAndSameFiscalStageOfPreviousYear()
    {
        var latest = Header(6, J(1405, 6, 31));
        var earlierStage = Header(6, J(1404, 6, 31), "1404/12/29");
        var other = Header(12, J(1404, 12, 29), "1404/12/29");
        var selection = Select([latest, earlierStage, other], new());
        Assert.Equal("available", selection.Outcome);
        Assert.Equal(latest, selection.Anchor);
        Assert.Equal(earlierStage, selection.Columns[1].Statement);
    }

    [Fact]
    public void Quarterly_UsesFiscalGridAndMissingPredecessorWithoutDifferencingBalances()
    {
        var q1 = Header(3, J(1405, 3, 31));
        var q3 = Header(9, J(1405, 9, 30));
        var q4 = Header(12, J(1405, 12, 29));
        var result = Select([q1, q3, q4], new(View: "quarterly", PeriodCount: 4));
        Assert.Equal([3, 6, 9, 12], result.Columns.Select(c => c.PeriodMonths));
        Assert.Equal("missing_predecessor", result.Columns[1].Reason);
        Assert.Equal("missing_predecessor", result.Columns[2].Reason);
        Assert.Equal(q3, result.Columns[2].Statement);
        Assert.Equal(q3, result.Columns[3].Predecessor);
    }

    [Fact]
    public void NonEsfandFiscalYear_RequestedStageMatchesYearEndLabel()
    {
        var nonEsfand = Header(6, J(1405, 3, 31), "1405/09/30");
        var result = Select([nonEsfand], new(RequestedMonths: 6, RequestedFiscalYear: 1405));
        Assert.Equal(nonEsfand, result.Anchor);
        Assert.Equal(1405, result.Columns[0].FiscalYear);
    }

    [Fact]
    public void MissingFiscalEvidence_PreventsQuarterDifferencing()
    {
        var header = Header(6, J(1405, 6, 31), null);
        var result = Select([header], new(View: "quarterly"), truncated: true);
        Assert.True(result.WindowTruncated);
        Assert.Equal("missing_fiscal_metadata", Assert.Single(result.Columns).Reason);
        Assert.Equal("missing_fiscal_metadata", Select([header], new()).Columns[1].Reason);
    }

    [Fact]
    public void NonBoundaryStatement_CannotBeDifferencedAsQuarter()
    {
        var header = Header(6, J(1405, 6, 30));
        var result = Select([header], new(View: "quarterly"));
        Assert.Equal("fiscal_boundary_mismatch", Assert.Single(result.Columns).Reason);
    }

    [Fact]
    public void SameRankConflict_RejectsAnchorWithoutFallback()
    {
        var first = Header(6, J(1405, 6, 31));
        var second = first with { StatementId = Guid.NewGuid(), ExternalStatementId = "2" };
        var values = new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>
        {
            [first.StatementId] = [new(121, Guid.NewGuid(), 1m, null)],
            [second.StatementId] = [new(121, Guid.NewGuid(), 2m, null)]
        };
        Assert.Equal("conflicting_report_revision", Select([first, second], new(), values).Outcome);
    }

    [Fact]
    public void PreviousReportAndMissingExplicitDate_KeepExactPeriods()
    {
        var current = Header(6, J(1405, 6, 31));
        var prior = Header(3, J(1405, 3, 31));
        Assert.Equal(prior, Select([current, prior], new(PreviousReport: true)).Anchor);
        var missing = J(1405, 4, 1);
        var result = Select([current, prior], new(RequestedDate: missing));
        Assert.Equal("no_statement_for_period", result.Outcome);
        Assert.Equal(prior.PeriodEnd, result.NearestPeriods[0]);
    }

    [Fact]
    public void PriorFiscalEvidenceFromAnotherStatement_ProvidesExactMissingTarget()
    {
        var current = Header(6, J(1405, 6, 31));
        var evidence = new[] { new CashFlowFiscalEvidence(J(1404, 12, 29), "1404/12/29") };
        var result = CashFlowPeriodSelector.Select([current], new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), new(), fiscalEvidence: evidence);
        Assert.Equal("no_matching_stage", result.Columns[1].Reason);
        Assert.Equal(J(1404, 6, 31), result.Columns[1].PeriodEnd);
    }

    [Fact]
    public void ChangedFiscalYearEnd_DoesNotInventSameStageComparison()
    {
        var current = Header(6, J(1405, 6, 31));
        var evidence = new[] { new CashFlowFiscalEvidence(J(1404, 9, 30), "1404/09/30") };
        var result = CashFlowPeriodSelector.Select([current], new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), new(), fiscalEvidence: evidence);
        Assert.Equal("fiscal_calendar_changed", result.Columns[1].Reason);
        Assert.Null(result.Columns[1].Statement);
    }

    [Fact]
    public void Eligibility_RejectsConsolidatedWrongScaleAndContradictoryScope()
    {
        var valid = Header(6, J(1405, 6, 31));
        var newer = Header(9, J(1405, 9, 30));
        var selected = Select([valid, newer with { IsComposing = true }, newer with { StatementId = Guid.NewGuid(), AssumedScale = "Rials" }, newer with { StatementId = Guid.NewGuid(), SourceIsComposing = true }], new());
        Assert.Equal(valid, selected.Anchor);
        Assert.Equal("ineligible_storage_scale", Select([newer with { AssumedScale = "Rials" }], new()).Outcome);
    }

    [Fact]
    public void TransitionalYear_IsRebasedAndQuarterBoundariesRemainExplicit()
    {
        var prior = Header(12, J(1404, 12, 29), "1404/12/29");
        var transition = Header(6, J(1405, 6, 31), "1405/09/30");
        var result = Select([prior, transition], new(View: "quarterly", PeriodCount: 2));
        Assert.Equal(J(1405, 1, 1), result.Columns[^1].FiscalStart);
        Assert.Equal(transition, result.Columns[^1].Statement);
        Assert.Equal("missing_predecessor", result.Columns[^1].Reason);
    }

    [Fact]
    public void AnnualView_UsesOnlyYearlyReportsAndCapsTheRequestedWindow()
    {
        var annual = Enumerable.Range(1400, 6).Select(year => Header(12, J(year, 12, 29), $"{year}/12/29")).ToArray();
        var currentPartial = Header(6, J(1406, 6, 31), "1406/12/29");
        var result = Select([.. annual, currentPartial], new(View: "annual", PeriodCount: 20));
        Assert.Equal(5, result.Columns.Count);
        Assert.Equal(annual[^1], result.Anchor);
        Assert.All(result.Columns, column => Assert.Equal(12, column.PeriodMonths));
        Assert.Equal(result.Columns.OrderBy(c => c.PeriodEnd).Select(c => c.PeriodEnd), result.Columns.Select(c => c.PeriodEnd));
    }

    [Fact]
    public void QuarterlyView_CapsTheRequestedWindowAtEight()
    {
        var earlier = Header(12, J(1404, 12, 29), "1404/12/29");
        var latest = Header(12, J(1405, 12, 29));
        var result = Select([earlier, latest], new(View: "quarterly", PeriodCount: 20));
        Assert.Equal(8, result.Columns.Count);
        Assert.All(result.Columns, column => Assert.Equal("standalone_quarter", column.Kind));
    }
}

using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161BuilderTests
{
    private static CashFlowStatementHeader Header(int months) => new(Guid.NewGuid(), $"s{months}", new DateOnly(2026, 3, 21),
        new DateOnly(2026, months == 3 ? 6 : 9, months == 3 ? 21 : 22), months == 3 ? "ThreeMonths" : "SixMonths",
        false, false, false, null, null, "1405/12/29", "MillionRials", false);
    private static IReadOnlyList<CashFlowItemObservation> Items(params (int Id, decimal? Value)[] values) =>
        values.Select(x => new CashFlowItemObservation(x.Id, Guid.NewGuid(), x.Value, null)).ToArray();
    private static CashFlowPeriodColumn Column(CashFlowStatementHeader? statement, CashFlowStatementHeader? previous = null,
        string kind = "anchor", string? reason = null) => new(kind, 1405, statement?.PeriodType == "ThreeMonths" ? 3 : 6,
            statement?.PeriodEnd ?? default, statement, previous, reason is null ? "available" : "unavailable", reason, new DateOnly(2026, 3, 21));

    [Fact]
    public void FixtureA_ResidualAndProvenanceUseOnlyVisibleVerifiedDetails()
    {
        var header = Header(6);
        var items = Items((128, 6_918_890), (121, -359_104), (123, 9_025_064), (124, 15_584_850),
            (33, 6_692_454), (35, 0), (125, 22_277_304), (10, -359_535), (14, 300), (3, 131),
            (23, 24_635_600), (24, -13_908_702), (5, -1_457_520), (6, -244_314), (1, 999));
        var result = CashFlowStatementBuilder.Build(CashFlowLineRegistry.GeneralTemplate, [Column(header)],
            new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>> { [header.StatementId] = items });
        var residual = Assert.Single(result.Rows, row => row.RowId == "investing_residual");
        Assert.Equal(131m, Assert.Single(residual.Cells).Value);
        Assert.Equal("subtotal - displayed_details", residual.Cells[0].Provenance.Formula);
        Assert.DoesNotContain(3, residual.SourceItemIds);
        Assert.DoesNotContain(1, result.Columns[0].Values.Keys);
        Assert.DoesNotContain(result.Rows, row => row.RowId == "financing_residual");
        Assert.DoesNotContain(result.Rows, row => row.RowId == "item_35");
        Assert.DoesNotContain(result.Rows, row => row.RowId == "item_48");
        var investingRows = result.Rows.Where(row => row.Section == CashFlowSection.Investing).Select(row => row.RowId).ToArray();
        Assert.Equal(["item_10", "item_14", "investing_residual", "item_121"], investingRows);
        var bank = CashFlowStatementBuilder.Build(CashFlowLineRegistry.BankingTemplate, [Column(header)],
            new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>> { [header.StatementId] = items });
        Assert.Contains(bank.Rows, row => row.RowId == "item_35");
    }

    [Fact]
    public void Quarter_DifferencesFlowsButUsesReportedBalances()
    {
        var q1 = Header(3);
        var q2 = Header(6);
        var items = new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>
        {
            [q1.StatementId] = Items((128, 400), (121, -100), (123, 50), (124, 350), (35, 0), (33, 1000), (125, 1350)),
            [q2.StatementId] = Items((128, 750), (121, -250), (123, 150), (124, 650), (35, 20), (33, 1000), (125, 1670), (120, 0))
        };
        var result = CashFlowStatementBuilder.Build(CashFlowLineRegistry.GeneralTemplate, [Column(q2, q1, "standalone_quarter")], items);
        var values = Assert.Single(result.Columns).Values;
        Assert.Equal(350m, values[128].Value);
        Assert.Equal(-150m, values[121].Value);
        Assert.Equal(100m, values[123].Value);
        Assert.Equal(300m, values[124].Value);
        Assert.Equal(20m, values[35].Value);
        Assert.Equal(1350m, values[33].Value);
        Assert.Equal(1670m, values[125].Value);
        Assert.Equal(0m, values[120].Value);
        Assert.Equal([q1.StatementId], values[33].Provenance.StatementIds);
        Assert.Equal([125], values[33].Provenance.SourceItemIds);
        Assert.Equal(2, values[128].Provenance.StatementIds.Count);
    }

    [Fact]
    public void MissingPredecessor_DoesNotHideReportedClosingBalance()
    {
        var header = Header(6);
        var result = CashFlowStatementBuilder.Build(CashFlowLineRegistry.GeneralTemplate,
            [Column(header, kind: "standalone_quarter", reason: "missing_predecessor")],
            new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>> { [header.StatementId] = Items((125, 1670), (128, 750)) });
        var values = Assert.Single(result.Columns).Values;
        Assert.Equal(1670m, values[125].Value);
        Assert.Equal("missing_predecessor", values[128].Reason);
        Assert.Null(values[128].Value);
    }

    [Fact]
    public void DuplicateDifferingValues_AreAmbiguousAndNeverSummed()
    {
        var header = Header(6);
        var result = CashFlowStatementBuilder.Build(CashFlowLineRegistry.GeneralTemplate, [Column(header)],
            new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>> { [header.StatementId] = Items((123, 1), (123, 2)) });
        var cell = Assert.Single(result.Columns).Values[123];
        Assert.Equal("ambiguous", cell.State);
        Assert.Null(cell.Value);
        Assert.Equal(2, cell.Provenance.LineItemIds.Count);
    }

    [Fact]
    public void SyntheticS8_AllFourQuartersUseAdjacentYearToDateStatements()
    {
        var headers = new[] { Header(3), Header(6), Header(6) with { PeriodType = "NineMonths", PeriodEnd = new(2026, 12, 21) },
            Header(6) with { PeriodType = "TwelveMonths", PeriodEnd = new(2027, 3, 20) } };
        var items = new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>
        {
            [headers[0].StatementId] = Items((128, 400), (121, -100), (123, 50), (124, 350), (35, 0), (33, 1000), (125, 1350)),
            [headers[1].StatementId] = Items((128, 750), (121, -250), (123, 150), (124, 650), (35, 20), (33, 1000), (125, 1670)),
            [headers[2].StatementId] = Items((128, 1000), (121, -400), (123, 100), (124, 700), (35, 20), (33, 1000), (125, 1720)),
            [headers[3].StatementId] = Items((128, 1400), (121, -500), (123, -200), (124, 700), (35, 30), (33, 1000), (125, 1730))
        };
        var columns = headers.Select((header, index) => new CashFlowPeriodColumn("standalone_quarter", 1405, (index + 1) * 3,
            header.PeriodEnd, header, index == 0 ? null : headers[index - 1], "available", null, new DateOnly(2026, 3, 21))).ToArray();
        var result = CashFlowStatementBuilder.Build(CashFlowLineRegistry.GeneralTemplate, columns, items);
        Assert.Equal([400m, 350m, 250m, 400m], result.Columns.Select(c => c.Values[128].Value));
        Assert.Equal([-100m, -150m, -150m, -100m], result.Columns.Select(c => c.Values[121].Value));
        Assert.Equal([50m, 100m, -50m, -300m], result.Columns.Select(c => c.Values[123].Value));
        Assert.Equal([350m, 300m, 50m, 0m], result.Columns.Select(c => c.Values[124].Value));
        Assert.Equal([0m, 20m, 0m, 10m], result.Columns.Select(c => c.Values[35].Value));
        Assert.Equal([1000m, 1350m, 1670m, 1720m], result.Columns.Select(c => c.Values[33].Value));
        Assert.Equal([1350m, 1670m, 1720m, 1730m], result.Columns.Select(c => c.Values[125].Value));
    }
}

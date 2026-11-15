using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161DetailCoverageTests
{
    private static CashFlowCell Cell(decimal? value, string state = "available") =>
        new(value, state, null, new([], [], []));

    private static CashFlowBuiltColumn Column(params (int Id, decimal? Value, string State)[] items)
    {
        var period = new CashFlowPeriodColumn("anchor", 1405, 6, new DateOnly(2026, 9, 22),
            null, null, "available", null, new DateOnly(2026, 3, 21));
        return new(period, items.ToDictionary(item => item.Id, item => Cell(item.Value, item.State)));
    }

    [Fact]
    public void CoverageDistinguishesCompleteAndDocumentedDifferences()
    {
        var complete = CashFlowDetailCoverageCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate,
            [Column((121, -90m, "available"), (10, -100m, "available"), (14, 10m, "available"))],
            new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), true);
        var investing = Assert.Single(complete, item => item.SectionId == "investing");
        Assert.Equal("complete", investing.Status);
        Assert.Equal(-90m, investing.ReportedSubtotal);
        Assert.Equal(-90m, investing.VerifiedDetailTotal);
        Assert.Equal(0m, investing.Difference);

        var difference = CashFlowDetailCoverageCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate,
            [Column((121, -89m, "available"), (10, -100m, "available"), (14, 10m, "available"))],
            new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), true);
        investing = Assert.Single(difference, item => item.SectionId == "investing");
        Assert.Equal("documented_difference", investing.Status);
        Assert.Equal(1m, investing.Difference);
    }

    [Fact]
    public void CoverageFailsClosedForUnclassifiedIncompleteAndConflictingDetails()
    {
        var id = Guid.NewGuid();
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>> unclassified = new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>
        {
            [id] = [new(46, Guid.NewGuid(), -5m, "دارایی زیستی")]
        };
        var statement = new CashFlowStatementHeader(id, "fixture", new DateOnly(2026, 3, 21), new DateOnly(2026, 9, 22),
            "SixMonths", false, false, false, null, null, "1405/12/29", "MillionRials", false);
        var period = new CashFlowPeriodColumn("anchor", 1405, 6, new DateOnly(2026, 9, 22), statement,
            null, "available", null, new DateOnly(2026, 3, 21));
        var column = new CashFlowBuiltColumn(period, new Dictionary<int, CashFlowCell>
        {
            [121] = Cell(-90m), [10] = Cell(-90m)
        });
        var insufficient = Assert.Single(CashFlowDetailCoverageCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate,
            [column], unclassified, true), item => item.SectionId == "investing");
        Assert.Equal("insufficient_detail", insufficient.Status);
        Assert.Equal(46, Assert.Single(insufficient.UnclassifiedItems).SourceItemId);

        var incomplete = Assert.Single(CashFlowDetailCoverageCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate,
            [column], new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), false), item => item.SectionId == "investing");
        Assert.Equal("insufficient_detail", incomplete.Status);
        Assert.False(incomplete.SourceReadComplete);

        var inconsistentColumn = Column((121, -90m, "available"), (10, null, "ambiguous"));
        var inconsistent = Assert.Single(CashFlowDetailCoverageCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate,
            [inconsistentColumn], new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), true), item => item.SectionId == "investing");
        Assert.Equal("inconsistent_source_data", inconsistent.Status);
    }
}

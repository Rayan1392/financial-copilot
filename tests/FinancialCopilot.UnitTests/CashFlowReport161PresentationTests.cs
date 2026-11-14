using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161PresentationTests
{
    private static Dictionary<int, IReadOnlyList<decimal?>> Valid() => new()
    {
        [128] = [6_918_890m], [121] = [-359_104m], [123] = [9_025_064m], [122] = [6_559_786m]
    };

    [Fact]
    public void LegacyMarkerTakesPrecedenceOverMissingCodalAndOperatingTotals()
    {
        var items = Valid();
        items[120] = [500m];
        items.Remove(123);
        items.Remove(128);
        Assert.Equal(CashFlowPresentationPolicy.UnsupportedPresentation, CashFlowPresentationPolicy.Classify(items).Outcome);
    }

    [Fact]
    public void MissingTotalsNeverUseNewOrDerivedFallbacks()
    {
        var items = Valid();
        items.Remove(128);
        Assert.Equal(CashFlowPresentationPolicy.OperatingTotalMissing, CashFlowPresentationPolicy.Classify(items).Outcome);
        items[128] = [0m];
        items[123] = [null];
        items[127] = [9_025_064m];
        Assert.Equal(CashFlowPresentationPolicy.CodalTotalsMissing, CashFlowPresentationPolicy.Classify(items).Outcome);
        items[123] = [0m];
        items.Remove(122);
        Assert.Equal(CashFlowPresentationPolicy.Available, CashFlowPresentationPolicy.Classify(items).Outcome);
    }

    [Fact]
    public void ConflictingDuplicateIsAmbiguousRatherThanMissing_AndConsolidatedItemIsWarned()
    {
        var items = Valid();
        items[123] = [10m, 11m];
        items[136] = [0m];
        var selected = CashFlowPresentationPolicy.Classify(items);
        Assert.Equal(CashFlowPresentationPolicy.Available, selected.Outcome);
        Assert.Contains(CashFlowPresentationPolicy.DuplicateLineItem, selected.Warnings);
        Assert.Contains(CashFlowPresentationPolicy.ConsolidatedItemOnStandalone, selected.Warnings);
        Assert.Equal(CashFlowValueState.Ambiguous, CashFlowObservedValue.From(items[123]).State);
        Assert.Equal(CashFlowValueState.Available, CashFlowObservedValue.From([0m, 0m]).State);
        Assert.Equal(CashFlowValueState.NotReported, CashFlowObservedValue.From([null]).State);
    }

    [Fact]
    public void AConflictingNonzeroLegacyMarkerDoesNotPassAsCodal()
    {
        var items = Valid();
        items[120] = [0m, 1m];
        var selected = CashFlowPresentationPolicy.Classify(items);
        Assert.Equal(CashFlowPresentationPolicy.UnsupportedPresentation, selected.Outcome);
        Assert.Contains(CashFlowPresentationPolicy.DuplicateLineItem, selected.Warnings);
    }
}

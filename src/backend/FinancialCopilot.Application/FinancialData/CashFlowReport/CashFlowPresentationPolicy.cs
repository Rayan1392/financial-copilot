namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public enum CashFlowValueState { NotReported, Available, Ambiguous }

public sealed record CashFlowObservedValue(CashFlowValueState State, decimal? Value)
{
    public static CashFlowObservedValue From(IReadOnlyList<decimal?>? values)
    {
        if (values is null || values.Count == 0 || values.All(value => value is null))
            return new(CashFlowValueState.NotReported, null);
        var distinct = values.Distinct().ToArray();
        return distinct.Length == 1
            ? new(CashFlowValueState.Available, distinct[0])
            : new(CashFlowValueState.Ambiguous, null);
    }
}

public sealed record CashFlowPresentationSelection(string Outcome, IReadOnlyList<string> Warnings);

/// <summary>Classifies the stored presentation without deriving or substituting published totals.</summary>
public static class CashFlowPresentationPolicy
{
    public const string Available = "available";
    public const string UnsupportedPresentation = "unsupported_presentation";
    public const string CodalTotalsMissing = "codal_totals_missing";
    public const string OperatingTotalMissing = "operating_total_missing";
    public const string ConsolidatedItemOnStandalone = "consolidated_item_on_standalone";
    public const string DuplicateLineItem = "duplicate_line_item";

    public static CashFlowPresentationSelection Classify(IReadOnlyDictionary<int, IReadOnlyList<decimal?>> observations)
    {
        var warnings = new List<string>();
        if (new[] { 22, 136, 137, 138, 139 }.Any(id => observations.TryGetValue(id, out var rows) && rows.Count > 0))
            warnings.Add(ConsolidatedItemOnStandalone);
        if (CashFlowLineRegistry.ReadableSourceItemIds.Any(id =>
            CashFlowObservedValue.From(observations.GetValueOrDefault(id)).State == CashFlowValueState.Ambiguous))
            warnings.Add(DuplicateLineItem);

        // Any reported nonzero legacy marker is sufficient to reject the presentation, including conflicting rows.
        if (observations.TryGetValue(CashFlowLineRegistry.LegacyPresentationMarker, out var marker) &&
            marker.Any(value => value.HasValue && value.Value != 0))
            return new(UnsupportedPresentation, warnings);

        bool Missing(int id) => CashFlowObservedValue.From(observations.GetValueOrDefault(id)).State == CashFlowValueState.NotReported;
        if (Missing(CashFlowLineRegistry.InvestingTotal) || Missing(CashFlowLineRegistry.FinancingTotal))
            return new(CodalTotalsMissing, warnings);
        if (Missing(CashFlowLineRegistry.OperatingTotal)) return new(OperatingTotalMissing, warnings);
        return new(Available, warnings);
    }
}

namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public sealed record CashFlowCellProvenance(
    IReadOnlyList<Guid> StatementIds, IReadOnlyList<Guid> LineItemIds, IReadOnlyList<int> SourceItemIds, string? Formula = null);

public sealed record CashFlowCell(decimal? Value, string State, string? Reason, CashFlowCellProvenance Provenance);

public sealed record CashFlowBuiltColumn(CashFlowPeriodColumn Period, IReadOnlyDictionary<int, CashFlowCell> Values);

public sealed record CashFlowBuiltRow(string RowId, string Label, CashFlowSection Section, string Kind,
    IReadOnlyList<int> SourceItemIds, IReadOnlyList<CashFlowCell> Cells);

public sealed record CashFlowBuiltStatement(IReadOnlyList<CashFlowBuiltColumn> Columns, IReadOnlyList<CashFlowBuiltRow> Rows);

/// <summary>Builds cells and provenance from persisted observations; only flow values are differenced.</summary>
public static class CashFlowStatementBuilder
{
    public static CashFlowBuiltStatement Build(string template, IReadOnlyList<CashFlowPeriodColumn> columns,
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>> observations)
    {
        CashFlowCell Read(CashFlowStatementHeader? statement, int item)
        {
            if (statement is null) return Missing("unavailable", "missing_predecessor", item);
            var lines = observations.GetValueOrDefault(statement.StatementId) ?? [];
            var matches = lines.Where(line => line.SourceItemId == item).ToArray();
            var value = CashFlowObservedValue.From(matches.Select(line => line.Value).ToArray());
            var provenance = new CashFlowCellProvenance([statement.StatementId], matches.Select(line => line.LineItemId).ToArray(), [item]);
            return value.State switch
            {
                CashFlowValueState.Available => new(value.Value, "available", null, provenance),
                CashFlowValueState.Ambiguous => new(null, "ambiguous", "duplicate_line_item", provenance),
                _ => new(null, "not_reported", null, provenance)
            };
        }

        string Presentation(CashFlowStatementHeader statement)
        {
            var rows = observations.GetValueOrDefault(statement.StatementId) ?? [];
            var byId = rows.Where(row => !CashFlowLineRegistry.ExcludedValueItems.Contains(row.SourceItemId))
                .GroupBy(row => row.SourceItemId).ToDictionary(group => group.Key, group => (IReadOnlyList<decimal?>)group.Select(row => row.Value).ToArray());
            return CashFlowPresentationPolicy.Classify(byId).Outcome;
        }

        CashFlowCell Value(CashFlowPeriodColumn column, int item)
        {
            var current = Read(column.Statement, item);
            if (column.Kind != "standalone_quarter") return current;
            if (item == CashFlowLineRegistry.ClosingCash || item == CashFlowLineRegistry.LegacyPresentationMarker ||
                item is 22 or 136 or 137 or 138 or 139) return current;
            if (item == CashFlowLineRegistry.OpeningCash)
                return column.PeriodMonths == 3 ? current : Read(column.Predecessor, CashFlowLineRegistry.ClosingCash);
            if (column.Reason is not null) return Missing("unavailable", column.Reason, item);
            if (column.PeriodMonths == 3) return current;
            if (column.Statement is null || column.Predecessor is null) return Missing("unavailable", "missing_predecessor", item);
            if (Presentation(column.Statement) != CashFlowPresentationPolicy.Available || Presentation(column.Predecessor) != CashFlowPresentationPolicy.Available)
                return Missing("unavailable", "presentation_changed", item);
            var previous = Read(column.Predecessor, item);
            if (current.State == "ambiguous" || previous.State == "ambiguous")
                return Combine(null, "ambiguous", "duplicate_line_item", current, previous, "current_ytd - previous_ytd");
            if (current.Value is null || previous.Value is null)
                return Combine(null, "unavailable", "not_reported", current, previous, "current_ytd - previous_ytd");
            return Combine(current.Value.Value - previous.Value.Value, "available", null, current, previous, "current_ytd - previous_ytd");
        }

        var builtColumns = columns.Select(column => new CashFlowBuiltColumn(column,
            CashFlowLineRegistry.ReadableSourceItemIds.ToDictionary(id => id, id => Value(column, id)))).ToArray();
        var rows = CashFlowLineRegistry.Displayable(template)
            .Where(definition => definition.SourceItemId != CashFlowLineRegistry.FxEffect || template == CashFlowLineRegistry.BankingTemplate ||
                builtColumns.Any(column => column.Values[CashFlowLineRegistry.FxEffect].Value is not null and not 0m))
            .Where(definition => definition.SourceItemId == CashFlowLineRegistry.FxEffect && template == CashFlowLineRegistry.BankingTemplate ||
                builtColumns.Any(column => column.Values[definition.SourceItemId].Value.HasValue || column.Values[definition.SourceItemId].State == "ambiguous"))
            .Select(definition => new CashFlowBuiltRow(
            $"item_{definition.SourceItemId}", definition.ReportLabel, definition.Section,
            definition.SourceItemId is CashFlowLineRegistry.OpeningCash or CashFlowLineRegistry.ClosingCash ? "balance" :
                definition.Visibility == "of-which" ? "of_which" : definition.Visibility,
            [definition.SourceItemId], builtColumns.Select(column => column.Values[definition.SourceItemId]).ToArray())).ToList();

        foreach (var residual in CashFlowLineRegistry.ResidualLines)
        {
            var total = residual.Section == CashFlowSection.Investing ? CashFlowLineRegistry.InvestingTotal : CashFlowLineRegistry.FinancingTotal;
            var details = CashFlowLineRegistry.Displayable(template).Where(line => line.Section == residual.Section && line.Role == "detail")
                .Select(line => line.SourceItemId).ToArray();
            var cells = builtColumns.Select(column => Residual(column.Values[total], details.Select(id => column.Values[id]).ToArray())).ToArray();
            if (cells.Any(cell => cell.Value is not null and not 0m))
                rows.Add(new CashFlowBuiltRow(residual.RowId, residual.Label, residual.Section, "residual", [total, .. details], cells));
        }
        var ordered = rows.Select((row, index) => (row, index)).OrderBy(x => x.row.Section)
            .ThenBy(x => x.row.Section is CashFlowSection.Investing or CashFlowSection.Financing
                ? x.row.Kind == "subtotal" ? 2 : x.row.Kind == "residual" ? 1 : 0 : 0)
            .ThenBy(x => x.index).Select(x => x.row).ToArray();
        return new(builtColumns, ordered);
    }

    private static CashFlowCell Missing(string state, string reason, int item) =>
        new(null, state, reason, new([], [], [item]));

    private static CashFlowCell Combine(decimal? value, string state, string? reason, CashFlowCell current, CashFlowCell previous, string formula) =>
        new(value, state, reason, new(current.Provenance.StatementIds.Concat(previous.Provenance.StatementIds).Distinct().ToArray(),
            current.Provenance.LineItemIds.Concat(previous.Provenance.LineItemIds).Distinct().ToArray(),
            current.Provenance.SourceItemIds.Concat(previous.Provenance.SourceItemIds).Distinct().ToArray(), formula));

    private static CashFlowCell Residual(CashFlowCell total, IReadOnlyList<CashFlowCell> details)
    {
        var inputs = new[] { total }.Concat(details).ToArray();
        var provenance = new CashFlowCellProvenance(inputs.SelectMany(x => x.Provenance.StatementIds).Distinct().ToArray(),
            inputs.SelectMany(x => x.Provenance.LineItemIds).Distinct().ToArray(),
            inputs.SelectMany(x => x.Provenance.SourceItemIds).Distinct().ToArray(), "subtotal - displayed_details");
        if (total.Value is null || details.Any(x => x.State is "ambiguous" or "unavailable"))
            return new(null, "unavailable", "component_unavailable", provenance);
        return new(total.Value.Value - details.Sum(x => x.Value ?? 0m), "available", null, provenance);
    }
}

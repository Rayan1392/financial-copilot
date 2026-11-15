namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

/// <summary>Compares Codal section totals with verified mapped details; residuals are disclosed, never promoted.</summary>
public static class CashFlowDetailCoverageCalculator
{
    private static readonly (CashFlowSection Section, string SectionId, int? SubtotalId)[] Sections =
    [
        (CashFlowSection.Operating, "operating", CashFlowLineRegistry.OperatingTotal),
        (CashFlowSection.Investing, "investing", CashFlowLineRegistry.InvestingTotal),
        (CashFlowSection.Financing, "financing", CashFlowLineRegistry.FinancingTotal)
    ];

    public static IReadOnlyList<CashFlowReportDetailCoverage> Calculate(string template,
        IReadOnlyList<CashFlowBuiltColumn> columns,
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>> allItems, bool sourceReadComplete)
    {
        return columns.SelectMany((column, index) => Sections.Select(section => CalculateOne(template, column,
            $"column_{index}", section, allItems, sourceReadComplete))).ToArray();
    }

    private static CashFlowReportDetailCoverage CalculateOne(string template, CashFlowBuiltColumn column, string columnId,
        (CashFlowSection Section, string SectionId, int? SubtotalId) section,
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>> allItems, bool sourceReadComplete)
    {
        CashFlowCell? subtotalCell = section.SubtotalId is { } subtotalId
            ? column.Values.GetValueOrDefault(subtotalId)
            : null;
        if (section.Section == CashFlowSection.Operating)
        {
            var beforeFinancing = column.Values.GetValueOrDefault(CashFlowLineRegistry.CodalBeforeFinancing);
            var investing = column.Values.GetValueOrDefault(CashFlowLineRegistry.InvestingTotal);
            subtotalCell = beforeFinancing?.Value is { } before && investing?.Value is { } invested
                ? new(before - invested, "available", null, new([], [], [122, 121], "122 - 121"))
                : null;
        }

        var detailDefinitions = CashFlowLineRegistry.Lines.Where(line => line.Section == section.Section && line.Role == "detail").ToArray();
        var verifiedDefinitions = detailDefinitions.Where(line => line.AppliesTo(template) && line.VerifiedFor(template)).ToArray();
        var detailCells = verifiedDefinitions.Select(definition =>
        {
            var cell = column.Values.GetValueOrDefault(definition.SourceItemId);
            return (definition, cell);
        }).ToArray();
        var ambiguous = detailCells.Any(x => x.cell?.State == "ambiguous");
        var verified = detailCells.Where(x => x.cell?.Value is not null).ToArray();
        var detailTotal = verified.Sum(x => x.cell!.Value!.Value);
        var unknownItems = GetUnclassifiedItems(section.Section, template, column, detailDefinitions, allItems,
            out var ambiguousUnclassified);
        ambiguous |= ambiguousUnclassified;

        var expected = subtotalCell?.Value;
        var actual = verified.Length > 0 ? detailTotal : (decimal?)null;
        var difference = expected is { } total && actual is { } recognized ? total - recognized : (decimal?)null;
        var status = ambiguous ? "inconsistent_source_data"
            : !sourceReadComplete || expected is null || unknownItems.Count > 0 ? "insufficient_detail"
            : actual is null ? "insufficient_detail"
            : difference == 0m && section.Section != CashFlowSection.Operating ? "complete"
            : difference != 0m ? "documented_difference" : "insufficient_detail";

        return new(columnId, section.SectionId, status, expected, actual,
            difference, verified.Length, unknownItems.Count, unknownItems, sourceReadComplete,
            new[] { column.Period.Statement, column.Period.Predecessor }.Where(x => x is not null)
                .Cast<CashFlowStatementHeader>().Select(x => x.StatementId).Distinct().ToArray());
    }

    private static IReadOnlyList<CashFlowReportUnclassifiedItem> GetUnclassifiedItems(CashFlowSection section,
        string template, CashFlowBuiltColumn column, IReadOnlyList<CashFlowLineDefinition> sectionDetails,
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>> allItems, out bool ambiguous)
    {
        ambiguous = false;
        var reportedIds = new HashSet<int>();
        foreach (var statement in new[] { column.Period.Statement, column.Period.Predecessor }.Where(x => x is not null))
            foreach (var item in allItems.GetValueOrDefault(statement!.StatementId) ?? [])
                reportedIds.Add(item.SourceItemId);

        var unclassified = new List<CashFlowReportUnclassifiedItem>();
        foreach (var sourceItemId in reportedIds)
        {
            var definition = CashFlowLineRegistry.Find(sourceItemId);
            if (definition is not null && (definition.Role != "detail" ||
                definition.Section != section || definition.AppliesTo(template) && definition.VerifiedFor(template)))
                continue;
            if (definition is not null && definition.Section != section) continue;
            if (sectionDetails.Any(x => x.SourceItemId == sourceItemId && x.AppliesTo(template) && x.VerifiedFor(template)))
                continue;

            var value = PeriodValue(column.Period, sourceItemId, allItems);
            ambiguous |= value.State == CashFlowValueState.Ambiguous;
            var label = (allItems.GetValueOrDefault(column.Period.Statement?.StatementId ?? Guid.Empty) ?? [])
                .Concat(allItems.GetValueOrDefault(column.Period.Predecessor?.StatementId ?? Guid.Empty) ?? [])
                .FirstOrDefault(item => item.SourceItemId == sourceItemId)?.SourceTitle;
            unclassified.Add(new(sourceItemId, label, value.Value));
        }
        return unclassified.OrderBy(x => x.SourceItemId).Take(64).ToArray();
    }

    private static CashFlowObservedValue PeriodValue(CashFlowPeriodColumn period, int sourceItemId,
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>> allItems)
    {
        CashFlowObservedValue Read(CashFlowStatementHeader? statement) => statement is null
            ? new(CashFlowValueState.NotReported, null)
            : CashFlowObservedValue.From((allItems.GetValueOrDefault(statement.StatementId) ?? [])
                .Where(item => item.SourceItemId == sourceItemId).Select(item => item.Value).ToArray());

        var current = Read(period.Statement);
        if (period.Kind != "standalone_quarter" || period.PeriodMonths == 3) return current;
        var previous = Read(period.Predecessor);
        if (current.State == CashFlowValueState.Ambiguous || previous.State == CashFlowValueState.Ambiguous)
            return new(CashFlowValueState.Ambiguous, null);
        return current.Value is { } currentValue && previous.Value is { } previousValue
            ? new(CashFlowValueState.Available, currentValue - previousValue)
            : new(CashFlowValueState.NotReported, null);
    }
}

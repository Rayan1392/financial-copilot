namespace FinancialCopilot.Application.FinancialData.BalanceSheetReport;

/// <summary>
/// Builds one snapshot from one selected statement record (no I/O): cell states and the PD-13 alias matrix,
/// reported totals only, the equity-to-assets ratio and the separate R1/R2 reconciliation checks.
/// Visible items are never summed into totals.
/// </summary>
public static class BalanceSheetReportBuilder
{
    public const string RatioMissingTotalAssets = "missing_total_assets";
    public const string RatioMissingTotalEquity = "missing_total_equity";
    public const string RatioZeroTotalAssets = "zero_total_assets";

    public static BalanceSheetSnapshot Build(
        string templateId,
        BalanceSheetStatementHeader header,
        IReadOnlyList<BalanceSheetItemObservation> items)
    {
        var bySource = items.ToLookup(item => item.SourceItemId);
        var definitions = BalanceSheetRowRegistry.Rows(templateId);
        var rows = definitions.ToDictionary(definition => definition.RowId,
            definition => ToRow(definition, BuildCell(definition, header.StatementId, bySource)));

        var sections = new[] { BalanceSheetSectionId.Assets, BalanceSheetSectionId.Liabilities, BalanceSheetSectionId.Equity }
            .Select(section => BuildSection(section, definitions, rows))
            .ToArray();
        var totalAssets = rows["total_assets"];
        var totalLiabilitiesAndEquity = rows["total_liabilities_and_equity"];
        var totalLiabilities = rows["total_liabilities"];
        var totalEquity = rows["total_equity"];
        var analytical = BalanceSheetRowRegistry.Analytical(templateId)
            .Select(definition => ToRow(definition, BuildCell(definition, header.StatementId, bySource)))
            .Where(row => row.Cell.Candidates.Count > 0)
            .ToArray();

        return new BalanceSheetSnapshot(
            ToInfo(header),
            sections,
            new BalanceSheetFinalTotals(totalAssets, totalLiabilitiesAndEquity),
            EquityToAssetsRatio(totalEquity.Cell, totalAssets.Cell),
            new BalanceSheetReconciliation(
                Check(totalAssets.Cell, totalLiabilitiesAndEquity.Cell.State == BalanceSheetCellStates.Available
                    ? totalLiabilitiesAndEquity.Cell.Value
                    : null),
                Check(totalAssets.Cell,
                    totalLiabilities.Cell.State == BalanceSheetCellStates.Available &&
                    totalEquity.Cell.State == BalanceSheetCellStates.Available &&
                    totalLiabilities.Cell.Value is { } liabilities && totalEquity.Cell.Value is { } equity
                        ? liabilities + equity
                        : null)),
            analytical);
    }

    public static BalanceSheetStatementInfo ToInfo(BalanceSheetStatementHeader header) => new(
        header.StatementId,
        header.ExternalStatementId,
        header.PeriodEnd,
        Ingestion.FiscalCalendarPolicy.ToJalali(header.PeriodEnd),
        header.PeriodType,
        BalanceSheetStatementSelector.PeriodMonths(header.PeriodType),
        header.IsAudited,
        header.IsRepresented,
        header.IsComposing,
        header.PublishedAt,
        // Shown only from the persisted PublishedAt that also drives version ranking (PD-09, PD-10).
        header.PublishedAt is { } published ? Ingestion.FiscalCalendarPolicy.ToJalali(published) : null,
        header.JalaliFiscalYearEnd,
        BalanceSheetReportContract.Unit,
        header.AssumedScale ?? string.Empty);

    /// <summary>The PD-13 alias matrix (spec §6.4) for one row of one statement.</summary>
    public static BalanceSheetCell BuildCell(
        BalanceSheetRowDefinition definition,
        Guid statementId,
        ILookup<int, BalanceSheetItemObservation> bySource)
    {
        var primary = bySource[definition.PrimarySourceItemId].ToArray();
        var fallback = definition.Fallback is null ? [] : bySource[definition.Fallback.SourceItemId].ToArray();
        var fallbackVerified = definition.Fallback?.Verified == true;
        var candidates = primary.Select(item => new BalanceSheetCellCandidate(item.SourceItemId, "primary", item.LineItemId,
                item.Value, true, item.SourceTitle))
            .Concat(fallback.Select(item => new BalanceSheetCellCandidate(item.SourceItemId, "fallback", item.LineItemId,
                item.Value, fallbackVerified, item.SourceTitle)))
            .ToArray();

        if (primary.Length > 1)
            return new BalanceSheetCell(null, BalanceSheetCellStates.Ambiguous, "duplicate_observation",
                null, null, statementId, null, candidates);

        if (primary.Length == 1 && primary[0].Value is { } primaryValue)
        {
            BalanceSheetAliasDiscrepancy? discrepancy = null;
            if (fallbackVerified && fallback.Length == 1 && fallback[0].Value is { } fallbackValue && fallbackValue != primaryValue)
                discrepancy = new BalanceSheetAliasDiscrepancy(fallback[0].SourceItemId, fallbackValue, primaryValue - fallbackValue);
            return new BalanceSheetCell(primaryValue, BalanceSheetCellStates.Available, null,
                primary[0].SourceItemId, primary[0].LineItemId, statementId, discrepancy, candidates);
        }

        var primaryReason = primary.Length == 1 ? "null_observation" : "not_observed";
        if (fallback.Length == 0)
            return new BalanceSheetCell(null, BalanceSheetCellStates.Missing, primaryReason, null, null, statementId, null, candidates);
        if (!fallbackVerified)
            return new BalanceSheetCell(null, BalanceSheetCellStates.Missing, "unverified_fallback_only",
                null, null, statementId, null, candidates);
        if (fallback.Length > 1)
            return new BalanceSheetCell(null, BalanceSheetCellStates.Ambiguous, "duplicate_observation",
                null, null, statementId, null, candidates);
        return fallback[0].Value is { } value
            ? new BalanceSheetCell(value, BalanceSheetCellStates.Available, "verified_fallback",
                fallback[0].SourceItemId, fallback[0].LineItemId, statementId, null, candidates)
            : new BalanceSheetCell(null, BalanceSheetCellStates.Missing, "null_observation", null, null, statementId, null, candidates);
    }

    /// <summary>Reported total equity / reported total assets × 100 at full decimal precision (PD-14).</summary>
    public static BalanceSheetRatio EquityToAssetsRatio(BalanceSheetCell totalEquity, BalanceSheetCell totalAssets)
    {
        if (totalAssets.State != BalanceSheetCellStates.Available || totalAssets.Value is not { } assets)
            return new BalanceSheetRatio(null, "unavailable", RatioMissingTotalAssets);
        if (totalEquity.State != BalanceSheetCellStates.Available || totalEquity.Value is not { } equity)
            return new BalanceSheetRatio(null, "unavailable", RatioMissingTotalEquity);
        if (assets == 0m)
            return new BalanceSheetRatio(null, "unavailable", RatioZeroTotalAssets);
        return new BalanceSheetRatio(equity / assets * 100m, "available", null);
    }

    /// <summary>Exact decimal equality; a check with a missing input is unavailable and raises no notice.</summary>
    public static BalanceSheetCheck Check(BalanceSheetCell totalAssets, decimal? right)
    {
        if (totalAssets.State != BalanceSheetCellStates.Available || totalAssets.Value is not { } left || right is null)
            return new BalanceSheetCheck(BalanceSheetCheckStates.Unavailable, totalAssets.Value, right, null);
        return left == right.Value
            ? new BalanceSheetCheck(BalanceSheetCheckStates.Reconciled, left, right, 0m)
            : new BalanceSheetCheck(BalanceSheetCheckStates.Discrepancy, left, right, left - right.Value);
    }

    private static BalanceSheetRow ToRow(BalanceSheetRowDefinition definition, BalanceSheetCell cell) =>
        new(definition.RowId, definition.Label, BalanceSheetRowRegistry.KindKey(definition.Kind),
            BalanceSheetReportContract.Unit, cell);

    private static BalanceSheetSection BuildSection(
        BalanceSheetSectionId section,
        IReadOnlyList<BalanceSheetRowDefinition> definitions,
        IReadOnlyDictionary<string, BalanceSheetRow> rows)
    {
        // Common rows always render; template additions render only when observed (a reported zero counts).
        bool Visible(BalanceSheetRowDefinition definition) =>
            definition.Common || rows[definition.RowId].Cell.Candidates.Count > 0;

        var inSection = definitions.Where(definition => definition.Section == section).ToArray();
        var groups = new List<BalanceSheetGroup>();
        foreach (var placement in new[] { BalanceSheetPlacement.Section, BalanceSheetPlacement.Current, BalanceSheetPlacement.NonCurrent })
        {
            var members = inSection.Where(definition => definition.Placement == placement).ToArray();
            var details = members.Where(definition => definition.Kind == BalanceSheetRowKind.Detail && Visible(definition))
                .Select(definition => rows[definition.RowId]).ToArray();
            var subtotal = members.FirstOrDefault(definition => definition.Kind == BalanceSheetRowKind.Subtotal);
            if (details.Length == 0 && subtotal is null) continue;
            groups.Add(new BalanceSheetGroup(
                placement switch
                {
                    BalanceSheetPlacement.Current => "current",
                    BalanceSheetPlacement.NonCurrent => "non_current",
                    _ => "none"
                },
                GroupLabel(section, placement),
                details,
                subtotal is null ? null : rows[subtotal.RowId]));
        }

        var total = inSection.FirstOrDefault(definition => definition.Kind == BalanceSheetRowKind.Total);
        return new BalanceSheetSection(
            BalanceSheetRowRegistry.SectionKey(section),
            BalanceSheetRowRegistry.SectionLabel(section),
            "اقلام منتخب",
            groups,
            total is null ? null : rows[total.RowId]);
    }

    private static string? GroupLabel(BalanceSheetSectionId section, BalanceSheetPlacement placement) => (section, placement) switch
    {
        (BalanceSheetSectionId.Assets, BalanceSheetPlacement.Current) => "دارایی‌های جاری",
        (BalanceSheetSectionId.Assets, BalanceSheetPlacement.NonCurrent) => "دارایی‌های غیرجاری",
        (BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Current) => "بدهی‌های جاری",
        (BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.NonCurrent) => "بدهی‌های غیرجاری",
        _ => null
    };
}

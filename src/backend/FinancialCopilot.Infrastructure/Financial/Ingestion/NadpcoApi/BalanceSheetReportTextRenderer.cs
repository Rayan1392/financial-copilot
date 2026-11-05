using System.Text;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Deterministic text rendering of a Feature 154 result (spec §9.6) for Telegram, history previews and
/// accessibility: metadata, sections and totals, the ratio, notices and, for comparisons, the baseline line
/// and each row's two balances with its percentage change. A pure function of the typed result; no chart.
/// </summary>
public static class BalanceSheetReportTextRenderer
{
    public const int MaxLength = 3900;

    public static string Render(BalanceSheetReportResult result)
    {
        if (result.Outcome != BalanceSheetOutcomes.Available || result.AnchorStatementId is null ||
            !result.Snapshots.TryGetValue(result.AnchorStatementId, out var anchor))
            return result.OutcomeMessage ?? $"{result.Title} در دسترس نیست.";

        var comparison = result.RequestedView == BalanceSheetViews.Comparison ? SelectedCandidate(result) : null;
        var baseline = comparison?.StatementId is { } baselineId ? result.Snapshots.GetValueOrDefault(baselineId) : null;
        var text = Build(result, anchor, comparison, baseline, includeDetails: true);
        return text.Length <= MaxLength ? text : Build(result, anchor, comparison, baseline, includeDetails: false);
    }

    private static string Build(BalanceSheetReportResult result, BalanceSheetSnapshot anchor,
        BalanceSheetBaselineCandidate? comparison, BalanceSheetSnapshot? baseline, bool includeDetails)
    {
        var builder = new StringBuilder();
        var company = string.IsNullOrWhiteSpace(result.Company.Name) || result.Company.Name == result.Company.Symbol
            ? result.Company.Symbol
            : $"{result.Company.Name} ({result.Company.Symbol})";
        builder.AppendLine($"### {result.Title} — {company}");
        builder.AppendLine(Metadata(anchor.Statement));

        if (result.RequestedView == BalanceSheetViews.Comparison)
        {
            if (comparison is null)
                builder.AppendLine("مبنای مقایسه در دسترس نیست.");
            else if (comparison.State != "available" || baseline is null)
                builder.AppendLine($"مبنای مقایسه «{BalanceSheetFormatting.BaselineLabel(comparison)}» در دسترس نیست ({ReasonText(comparison.Reason)})." +
                                   Alternatives(result, comparison));
            else
                builder.AppendLine($"مبنای مقایسه: {BalanceSheetFormatting.BaselineLabel(comparison)} — {Metadata(baseline.Statement)}");
        }

        var anchorDate = BalanceSheetFormatting.JalaliDate(anchor.Statement.PeriodEnd);
        var baselineDate = baseline is null ? null : BalanceSheetFormatting.JalaliDate(baseline.Statement.PeriodEnd);
        var baselineRows = baseline is null ? null : BalanceSheetComparisonBuilder.AllRows(baseline).ToDictionary(row => row.RowId);
        void AppendRow(BalanceSheetRow row, string prefix = "- ")
        {
            if (baselineRows is null || comparison is null)
            {
                builder.AppendLine($"{prefix}{row.Label}: {BalanceSheetFormatting.Amount(row.Cell.Value)}");
                return;
            }

            var previous = baselineRows.GetValueOrDefault(row.RowId);
            var change = comparison.RowChanges.GetValueOrDefault(row.RowId);
            builder.AppendLine($"{prefix}{row.Label}: مانده در {anchorDate} {BalanceSheetFormatting.Amount(row.Cell.Value)} | " +
                               $"مانده در {baselineDate} {BalanceSheetFormatting.Amount(previous?.Cell.Value)} | " +
                               $"درصد تغییر {BalanceSheetFormatting.Percent(change?.Percent, signed: true)}");
        }

        foreach (var section in anchor.Sections)
        {
            builder.AppendLine();
            builder.AppendLine($"**{section.Label}** ({section.Caption})");
            foreach (var group in section.Groups)
            {
                if (group.Label is not null && includeDetails && group.Rows.Count > 0)
                    builder.AppendLine($"{group.Label}:");
                if (includeDetails)
                    foreach (var row in group.Rows) AppendRow(row);
                if (group.Subtotal is not null) AppendRow(group.Subtotal);
            }

            if (section.SectionId == "assets") AppendRow(anchor.FinalTotals.TotalAssets);
            else if (section.Total is not null) AppendRow(section.Total);
        }

        AppendRow(anchor.FinalTotals.TotalLiabilitiesAndEquity);
        builder.AppendLine();
        var ratioLine = $"**نسبت حقوق صاحبان سهام به کل دارایی‌ها: {BalanceSheetFormatting.Percent(anchor.Ratio.Value)}**";
        if (comparison?.RatioChange is { } ratioChange && baseline is not null)
            ratioLine = $"**نسبت حقوق صاحبان سهام به کل دارایی‌ها: {anchorDate} {BalanceSheetFormatting.Percent(ratioChange.Anchor)} | " +
                        $"{baselineDate} {BalanceSheetFormatting.Percent(ratioChange.Baseline)} | تغییر {BalanceSheetFormatting.PercentagePoints(ratioChange.PpDifference)}**";
        builder.AppendLine(ratioLine);

        foreach (var notice in Notices(anchor).Concat(baseline is null ? [] : Notices(baseline)))
            builder.AppendLine($"⚠️ {notice}");
        if (anchor.AnalyticalItems.Count > 0)
            builder.AppendLine("اقلام تحلیلی: " + string.Join(" | ", anchor.AnalyticalItems.Select(row =>
                $"{row.Label}: {BalanceSheetFormatting.Amount(row.Cell.Value)}")));
        return builder.ToString().TrimEnd();
    }

    private static string Metadata(BalanceSheetStatementInfo statement)
    {
        var parts = new List<string>
        {
            $"تاریخ: {BalanceSheetFormatting.JalaliDate(statement.PeriodEnd)}",
            BalanceSheetFormatting.PeriodLength(statement.PeriodMonths),
            // Owner instruction 2026-11-05: the standalone basis is the default and is not labelled; only consolidated is.
            statement.IsComposing ? "تلفیقی" : string.Empty,
            statement.IsAudited ? "حسابرسی‌شده" : "حسابرسی‌نشده"
        };
        if (statement.IsRepresented) parts.Add("تجدید ارائه‌شده");
        if (statement.JalaliPublishedAt is { } published) parts.Add($"تاریخ انتشار: {BalanceSheetFormatting.PersianDigits(published)}");
        parts.Add("میلیون ریال");
        return string.Join(" | ", parts.Where(part => part.Length > 0));
    }

    internal static IEnumerable<string> Notices(BalanceSheetSnapshot snapshot)
    {
        var date = BalanceSheetFormatting.JalaliDate(snapshot.Statement.PeriodEnd);
        if (snapshot.Reconciliation.R1.State == BalanceSheetCheckStates.Discrepancy)
            yield return $"در {date} جمع کل دارایی‌ها با جمع بدهی‌ها و حقوق صاحبان سهام گزارش‌شده برابر نیست (اختلاف {BalanceSheetFormatting.Amount(snapshot.Reconciliation.R1.Difference)}).";
        if (snapshot.Reconciliation.R2.State == BalanceSheetCheckStates.Discrepancy)
            yield return $"در {date} جمع کل دارایی‌ها با مجموع جمع کل بدهی‌ها و جمع حقوق صاحبان سهام برابر نیست (اختلاف {BalanceSheetFormatting.Amount(snapshot.Reconciliation.R2.Difference)}).";
        foreach (var row in BalanceSheetComparisonBuilder.AllRows(snapshot).Where(row => row.Cell.AliasDiscrepancy is not null))
            yield return $"{row.Label}: مقدار منبع جایگزین {row.Cell.AliasDiscrepancy!.OtherSourceItemId} برابر {BalanceSheetFormatting.Amount(row.Cell.AliasDiscrepancy.OtherValue)} است (اختلاف {BalanceSheetFormatting.Amount(row.Cell.AliasDiscrepancy.Difference)}).";
    }

    private static BalanceSheetBaselineCandidate? SelectedCandidate(BalanceSheetReportResult result) =>
        result.Comparison?.Candidates.FirstOrDefault(item => item.Kind == result.Comparison.SelectedBaselineKind);

    private static string Alternatives(BalanceSheetReportResult result, BalanceSheetBaselineCandidate candidate)
    {
        var options = candidate.Alternatives
            .Select(kind => result.Comparison!.Candidates.FirstOrDefault(item => item.Kind == kind))
            .Where(item => item is not null)
            .Select(item => BalanceSheetFormatting.BaselineLabel(item!))
            .ToArray();
        return options.Length == 0 ? string.Empty : $" مبناهای در دسترس: {string.Join("، ", options)}.";
    }

    internal static string ReasonText(string? reason) => reason switch
    {
        BalanceSheetBaselineResolver.MissingFiscalMetadata => "اطلاعات سال مالی ناشر موجود نیست",
        BalanceSheetBaselineResolver.FiscalCalendarChanged => "سال مالی ناشر تغییر کرده است",
        BalanceSheetBaselineResolver.AnchorNotOnFiscalGrid => "تاریخ گزارش بر پایان فصل مالی منطبق نیست",
        BalanceSheetBaselineResolver.NoEarlierReport => "گزارش قبلی موجود نیست",
        BalanceSheetBaselineResolver.NoMatchingStage => "گزارش هم‌دوره سال قبل موجود نیست",
        BalanceSheetOutcomes.NoSnapshotAtDate => "در این تاریخ ترازنامه جداگانه موجود نیست",
        BalanceSheetOutcomes.ConflictingReportRevision => "نسخه‌های هم‌رتبه متناقض‌اند",
        BalanceSheetOutcomes.TooManyReportVersions => "نسخه‌های هم‌رتبه بیش از حد مجاز خواندن است",
        _ => reason ?? "نامشخص"
    };
}

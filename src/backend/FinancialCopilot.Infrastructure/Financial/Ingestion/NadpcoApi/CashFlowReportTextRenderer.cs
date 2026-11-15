using System.Globalization;
using System.Text;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal static class CashFlowReportTextRenderer
{
    private const int MaxCharacters = 3899;

    public static string Render(CashFlowReportResult report)
    {
        if (report.Outcome != "available") return report.Message ?? report.Outcome;
        var text = new StringBuilder();
        var company = report.Company.Symbol ?? report.Company.Name ?? report.Request.CompanyQuery;
        text.AppendLine(company);
        if (report.Metadata is { } metadata)
        {
            text.Append(metadata.ViewLabel).Append(" · ").Append(metadata.BasisLabel).Append(" · ")
                .Append("واحد مبالغ: ").AppendLine(metadata.Unit);
            var facts = new List<string>();
            if (metadata.JalaliPeriodEnd is { Length: > 0 } periodEnd) facts.Add($"پایان دوره: {periodEnd}");
            if (metadata.FiscalYear is { } year) facts.Add($"سال مالی: {BalanceSheetFormatting.PersianDigits(year.ToString(CultureInfo.InvariantCulture))}");
            if (metadata.IsAudited is { } audited) facts.Add(audited ? "حسابرسی‌شده" : "حسابرسی‌نشده");
            if (metadata.IsRepresented is { } represented && represented) facts.Add("ارائهٔ مجدد");
            if (metadata.JalaliPublishedAt is { Length: > 0 } published) facts.Add($"تاریخ انتشار: {published}");
            if (facts.Count > 0) text.AppendLine(string.Join(" · ", facts));
            text.AppendLine($"منبع داده: {metadata.ProviderName}");
        }
        text.AppendLine(string.Join(" | ", report.Columns.Select(column => column.Label)));

        if (report.Request.Focus == "analysis" && report.Analysis is { } analysis)
        {
            foreach (var fact in analysis.Facts) text.AppendLine(fact.Text);
        }
        else if (report.Request.Focus == "metric")
        {
            var metrics = report.Kpis.Where(kpi => report.Request.MetricId is null || kpi.MetricId == report.Request.MetricId);
            foreach (var metric in metrics)
                text.Append(metric.Label).Append(": ").AppendLine(Format(metric.Value, metric.State, metric.Reason));
        }
        else
        {
            foreach (var section in report.Sections)
            {
                text.AppendLine().Append(section.Label).AppendLine();
                foreach (var row in section.Rows)
                {
                    var values = report.Columns.Take(2).Select(column =>
                    {
                        var cell = row.Cells.GetValueOrDefault(column.ColumnId);
                        return cell is null ? "—" : Format(cell.Value, cell.State, cell.Reason);
                    }).ToArray();
                    if (values.All(value => value == "—")) continue;
                    text.Append(row.Label).Append(": ").AppendLine(string.Join(" | ", values));
                }
            }
        }

        if (report.Verification.MainTotals is { } mainTotals)
        {
            text.Append("تطبیق جمع جریان‌ها: ").AppendLine(State(mainTotals.Status));
            AppendDimension(text, "تطبیق مانده وجه نقد", report.Verification.CashBalance);
        }
        var anchorColumnId = report.Columns.FirstOrDefault(column => report.AnchorStatementId is { } anchor && column.StatementIds.Contains(anchor))?.ColumnId;
        foreach (var coverage in (report.Verification.DetailCoverage ?? []).Where(item => item.ColumnId == anchorColumnId))
            text.Append("پوشش ریزاقلامِ ").Append(SectionName(coverage.SectionId)).Append(": ").AppendLine(State(coverage.Status));
        foreach (var check in report.Verification.Checks.Take(12))
            text.Append(CheckName(check.CheckId)).Append(": ").AppendLine(State(check.State));
        foreach (var row in report.Sections.SelectMany(section => section.Rows).Where(row => row.Kind == "residual" && row.Note is not null))
            text.AppendLine(row.Note);
        foreach (var warning in report.Warnings.Take(8)) text.AppendLine(warning.Message);
        foreach (var notice in report.Notices.Take(2)) text.AppendLine(notice);
        if (text.Length <= MaxCharacters) return text.ToString().TrimEnd();
        return string.Concat(text.ToString().AsSpan(0, MaxCharacters - 1), "…");
    }

    private static string Format(decimal? value, string state, string? reason)
    {
        if (value is { } amount) return BalanceSheetFormatting.PersianDigits(amount.ToString("#,##0.###", CultureInfo.InvariantCulture));
        return state switch
        {
            "ambiguous" => "مبهم",
            "unverified" => "تأییدنشده",
            _ => reason is null ? "گزارش نشده" : "در دسترس نیست"
        };
    }

    private static void AppendDimension(StringBuilder text, string label, CashFlowReportValidationDimension? dimension)
    {
        if (dimension is null) return;
        text.Append(label).Append(": ").Append(State(dimension.Status));
        if (dimension.Expected is { } expected && dimension.Actual is { } actual)
            text.Append("؛ مقدار مورد انتظار ").Append(Format(expected, "available", null))
                .Append("، مقدار گزارش‌شده ").Append(Format(actual, "available", null));
        if (dimension.Difference is { } difference) text.Append("، اختلاف ").Append(Format(difference, "available", null));
        text.AppendLine();
    }

    private static string State(string state) => state switch
    {
        "verified" or "passed" or "complete" => "تطبیق‌شده",
        "failed" or "inconsistent_source_data" => "مغایرت منبع",
        "documented_difference" => "اختلاف تخصیص‌نیافته",
        "insufficient_detail" => "اطلاعات تفصیلی ناکافی",
        _ => "تأییدنشده"
    };

    private static string SectionName(string section) => section switch
    {
        "operating" => "عملیاتی", "investing" => "سرمایه‌گذاری", "financing" => "تأمین مالی", _ => "گزارش"
    };

    private static string CheckName(string checkId) => checkId switch
    {
        "R1" => "تطبیق جمع جریان‌های نقدی", "R2" => "تطبیق مانده وجه نقد", "V3" => "کنترل متقابل جریان عملیاتی",
        "C1" => "پیوستگی ماندهٔ آغاز سال مالی", "C2" => "پیوستگی ماندهٔ ابتدای فصل", _ => "کنترل حسابداری"
    };
}

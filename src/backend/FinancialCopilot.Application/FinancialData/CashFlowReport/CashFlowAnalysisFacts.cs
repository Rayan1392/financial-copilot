using System.Globalization;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;

namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public sealed record CashFlowAnalysisFact(string FactId, string Text, IReadOnlyList<string> MetricIds, string ColumnId,
    IReadOnlyList<string>? RelatedColumnIds = null);
public sealed record CashFlowAnalysis(string Topic, IReadOnlyList<CashFlowAnalysisFact> Facts);
public sealed record CashFlowAnalysisComparison(IReadOnlyList<CashFlowMetric> Metrics, string ColumnId, string PeriodLabel);

/// <summary>Fixed fact templates over calculated metrics; no qualitative investment verdicts.</summary>
public static class CashFlowAnalysisFacts
{
    public static CashFlowAnalysis Build(string topic, string columnId, string template, IReadOnlyList<CashFlowMetric> metrics,
        IReadOnlyList<CashFlowMetric>? previousMetrics = null, IReadOnlyList<CashFlowMetric>? quarterlyOperating = null,
        CashFlowAnalysisComparison? previousQuarter = null, CashFlowAnalysisComparison? sameQuarterPreviousYear = null)
    {
        var facts = new List<CashFlowAnalysisFact>();
        CashFlowMetric? Find(string id) => metrics.FirstOrDefault(metric => metric.MetricId == id);
        void Add(string id, string label)
        {
            var metric = Find(id);
            var suffix = metric?.State == "unverified" ? " (تطبیق کامل ممکن نیست)" : string.Empty;
            var text = metric?.Value is { } value
                ? $"{label}: {Format(value, metric.Unit)}{suffix}."
                : $"{label}: {Unavailable(metric?.Reason)}.";
            facts.Add(new(id, text, [id], columnId));
        }
        void Compare(string metricId, string label, CashFlowAnalysisComparison? comparison, string comparisonId)
        {
            var current = Find(metricId);
            var prior = comparison?.Metrics.FirstOrDefault(metric => metric.MetricId == metricId);
            if (comparison is null || current?.Value is not { } currentValue || prior?.Value is not { } priorValue ||
                current.State != "available" || prior.State != "available") return;
            var change = currentValue - priorValue;
            var direction = priorValue < 0m && currentValue >= 0m ? "از مقدار منفی به مثبت رسیده است"
                : priorValue >= 0m && currentValue < 0m ? "از مقدار مثبت به منفی رسیده است"
                : change > 0m ? "افزایش داشته است" : change < 0m ? "کاهش داشته است" : "بدون تغییر بوده است";
            var text = $"{label} نسبت به {comparison.PeriodLabel} {direction}: از {Format(priorValue, current.Unit)} به {Format(currentValue, current.Unit)}؛ تغییر {Format(Math.Abs(change), current.Unit)}.";
            facts.Add(new(comparisonId, text, [metricId], columnId, [comparison.ColumnId]));
        }
        switch (topic)
        {
            case "earnings_quality":
                Add("operating_cash_flow", "جریان خالص نقد عملیاتی");
                var profit = Find("cash_conversion")?.Components.FirstOrDefault(item => item.SourceType == "income_statement");
                facts.Add(new("net_profit", profit?.Value is { } netProfit ? $"سود خالص: {Format(netProfit, "million_irr")}." : "سود خالص هم‌دوره در دسترس نیست.", ["cash_conversion"], columnId));
                Add("cash_conversion", "نسبت تبدیل سود به نقد");
                break;
            case "operating_generation":
                Add("operating_cash_flow", "جریان خالص نقد عملیاتی");
                var quarters = quarterlyOperating?.Where(metric => metric.MetricId == "operating_cash_flow" && metric.Value.HasValue).ToArray() ?? [];
                if (quarters.Length > 0)
                    facts.Add(new("quarter_counts", $"در {BalanceSheetFormatting.PersianDigits(quarters.Length.ToString(CultureInfo.InvariantCulture))} فصل دارای داده، جریان عملیاتی {BalanceSheetFormatting.PersianDigits(quarters.Count(metric => metric.Value > 0m).ToString(CultureInfo.InvariantCulture))} فصل مثبت و {BalanceSheetFormatting.PersianDigits(quarters.Count(metric => metric.Value < 0m).ToString(CultureInfo.InvariantCulture))} فصل منفی بود.", ["operating_cash_flow"], columnId));
                break;
            case "cash_change_drivers":
                Add("total_change_in_cash", "تغییر کل موجودی نقد");
                Add("operating_cash_flow", "فعالیت‌های عملیاتی");
                Add("investing_cash_flow", "فعالیت‌های سرمایه‌گذاری");
                Add("financing_cash_flow", "فعالیت‌های تأمین مالی");
                Add("fx_effect", "اثر تغییرات نرخ ارز");
                if (Find("fx_share_of_cash_change") is not null) Add("fx_share_of_cash_change", "سهم اثر نرخ ارز از تغییر موجودی نقد");
                break;
            case "financing_dependence":
                if (template == CashFlowLineRegistry.BankingTemplate)
                    facts.Add(new("bank_not_applicable", "شاخص‌های جریان نقد آزاد و خالص استقراض برای بانک‌ها محاسبه نمی‌شود.", [], columnId));
                else
                {
                    Add("capex", "مخارج سرمایه‌ای نقدی"); Add("operating_cash_flow", "جریان خالص نقد عملیاتی");
                    Add("free_cash_flow", "جریان نقد آزاد"); Add("net_debt_cash_flow", "خالص جریان نقد استقراض");
                }
                break;
            case "financing_structure":
                Add("financing_cash_flow", "جریان نقد تأمین مالی"); Add("facilities_received", "تسهیلات دریافتی");
                Add("facilities_repaid", "بازپرداخت اصل تسهیلات"); Add("interest_paid", "سود پرداختی بابت تأمین مالی");
                Add("dividends_paid", "سود سهام پرداختی");
                break;
            default:
                Add("operating_cash_flow", "جریان خالص نقد عملیاتی"); Add("investing_cash_flow", "جریان نقد سرمایه‌گذاری");
                Add("financing_cash_flow", "جریان نقد تأمین مالی"); Add("closing_cash", "مانده وجه نقد پایان دوره");
                break;
        }
        var comparisonMetrics = topic switch
        {
            "earnings_quality" => new[] { "cash_conversion" },
            "operating_generation" => new[] { "operating_cash_flow" },
            "cash_change_drivers" => new[] { "total_change_in_cash", "operating_cash_flow", "investing_cash_flow", "financing_cash_flow", "fx_effect" },
            "financing_dependence" => new[] { "ppe_capex", "simplified_free_cash_flow", "net_debt_cash_flow" },
            "financing_structure" => new[] { "financing_cash_flow", "facilities_received", "facilities_repaid", "interest_paid", "dividends_paid" },
            _ => new[] { "operating_cash_flow", "total_change_in_cash", "closing_cash" }
        };
        foreach (var id in comparisonMetrics)
        {
            var label = MetricLabel(id);
            Compare(id, label, previousQuarter, $"{id}_previous_quarter");
            Compare(id, label, sameQuarterPreviousYear, $"{id}_same_quarter_previous_year");
        }
        return new(topic, facts);
    }

    private static string Format(decimal value, string unit) => unit switch
    {
        "percent" => BalanceSheetFormatting.Percent(value),
        "multiple" => $"{BalanceSheetFormatting.PersianDigits(Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)).Replace('.', '٫')} برابر",
        _ => BalanceSheetFormatting.Amount(value)
    };

    private static string MetricLabel(string id) => id switch
    {
        "operating_cash_flow" => "جریان نقد عملیاتی",
        "investing_cash_flow" => "جریان نقد سرمایه‌گذاری",
        "financing_cash_flow" => "جریان نقد تأمین مالی",
        "total_change_in_cash" => "تغییر کل وجه نقد",
        "closing_cash" => "مانده وجه نقد پایان دوره",
        "fx_effect" => "اثر تغییر نرخ ارز",
        "cash_conversion" => "نسبت تبدیل سود به وجه نقد",
        "ppe_capex" => "مخارج سرمایه‌ای تأییدشدهٔ دارایی ثابت مشهود",
        "simplified_free_cash_flow" => "جریان نقد آزاد ساده‌شده",
        "ppe_capex_coverage" => "پوشش مخارج دارایی ثابت مشهود",
        "net_debt_cash_flow" => "خالص جریان نقد استقراض",
        "facilities_received" => "تسهیلات دریافتی",
        "facilities_repaid" => "بازپرداخت اصل تسهیلات",
        "interest_paid" => "سود پرداختی بابت تأمین مالی",
        "dividends_paid" => "سود سهام پرداختی",
        _ => id
    };

    private static string Unavailable(string? reason) => reason switch
    {
        "non_positive_net_profit" => "به علت سود خالص غیرمثبت محاسبه نمی‌شود",
        "non_positive_revenue" => "به علت فروش غیرمثبت محاسبه نمی‌شود",
        "non_positive_operating_cash_flow" => "به علت جریان عملیاتی غیرمثبت محاسبه نمی‌شود",
        "zero_capex" => "به علت مخارج سرمایه‌ای صفر یا منفی محاسبه نمی‌شود",
        "capex_mapping_unverified" => "به‌دلیل تأیید نشدن طبقه‌بندی بعضی اقلام، محاسبهٔ جامع ارائه نمی‌شود",
        "capex_component_missing" => "مبلغ لازم برای محاسبه گزارش نشده است",
        "unexpected_capex_sign" => "علامت مبلغ خرید دارایی ثابت مشهود با ماهیت پرداخت سازگار نیست",
        "mixed_sign_change" => "به‌دلیل ناهم‌جهت بودن اثر ارز و تغییر خالص وجه نقد، نسبت معنادار نیست",
        "income_statement_unavailable" => "صورت سود و زیان هم‌دوره در دسترس نیست",
        _ => "داده کافی در دسترس نیست"
    };
}

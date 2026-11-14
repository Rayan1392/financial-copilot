using System.Globalization;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;

namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public sealed record CashFlowAnalysisFact(string FactId, string Text, IReadOnlyList<string> MetricIds, string ColumnId);
public sealed record CashFlowAnalysis(string Topic, IReadOnlyList<CashFlowAnalysisFact> Facts);

/// <summary>Fixed fact templates over calculated metrics; no qualitative investment verdicts.</summary>
public static class CashFlowAnalysisFacts
{
    public static CashFlowAnalysis Build(string topic, string columnId, string template, IReadOnlyList<CashFlowMetric> metrics,
        IReadOnlyList<CashFlowMetric>? previousMetrics = null, IReadOnlyList<CashFlowMetric>? quarterlyOperating = null)
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
                var previous = previousMetrics?.FirstOrDefault(metric => metric.MetricId == "operating_cash_flow");
                if (previous?.Value is { } old) facts.Add(new("previous_operating", $"جریان نقد عملیاتی دوره مشابه سال قبل: {Format(old, "million_irr")}.", ["operating_cash_flow"], columnId));
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
        return new(topic, facts);
    }

    private static string Format(decimal value, string unit) => unit switch
    {
        "percent" => BalanceSheetFormatting.Percent(value),
        "multiple" => $"{BalanceSheetFormatting.PersianDigits(Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)).Replace('.', '٫')} برابر",
        _ => $"{BalanceSheetFormatting.Amount(value)} میلیون ریال"
    };

    private static string Unavailable(string? reason) => reason switch
    {
        "non_positive_net_profit" => "به علت سود خالص غیرمثبت محاسبه نمی‌شود",
        "non_positive_revenue" => "به علت فروش غیرمثبت محاسبه نمی‌شود",
        "non_positive_operating_cash_flow" => "به علت جریان عملیاتی غیرمثبت محاسبه نمی‌شود",
        "zero_capex" => "به علت مخارج سرمایه‌ای صفر یا منفی محاسبه نمی‌شود",
        "income_statement_unavailable" => "صورت سود و زیان هم‌دوره در دسترس نیست",
        _ => "داده کافی در دسترس نیست"
    };
}

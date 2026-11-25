using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

internal static class SalesReportIntentRules
{
    public static bool IsExplicitDataRequest(AiQueryRequest request)
    {
        if (request.Context?.InsightEventId is not null || request.Context?.AlertId is not null)
            return false;

        var message = request.Message;
        if (MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery(message))
            return false;

        var words = QueryNormalization.Normalize(message).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(word => word is "تفسیر" or "تحلیل" or "چرایی" or "چرا" or
                "interpret" or "analyze" or "analyse" or "why"))
            return false;

        if (ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery(message))
            return ProductRevenueMixIntentRules.ExtractCompanySymbol(message) is not null;

        if (MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(message))
        {
            var trend = MonthlyProductTrendIntentRules.BuildQuery(message);
            return trend.CompanyText.Length > 0 && trend.Focus == MonthlyProductComparisonFocus.Rate;
        }

        return MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(message) &&
               MonthlyActivityTrendIntentRules.ExtractCompanySymbol(message) is not null;
    }
}

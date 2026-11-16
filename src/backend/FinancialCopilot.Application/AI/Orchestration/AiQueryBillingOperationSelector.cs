namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Maps governed V2 financial-query capabilities to their operation-based tariffs.</summary>
public static class AiQueryBillingOperationSelector
{
    public const string DefaultOperationCode = "AiQuery.Scanner";
    public const string FinancialStatementOperationCode = "AiQuery.FinancialStatement";
    public const string MonthlySalesTrendOperationCode = "AiQuery.MonthlySalesTrend";

    public static string Select(string? validatedCapabilityCode, string? originalMessage)
    {
        if (!string.IsNullOrWhiteSpace(validatedCapabilityCode))
        {
            return validatedCapabilityCode switch
            {
                "financial_statement_table" or "financial_statement_period_analysis" => FinancialStatementOperationCode,
                "monthly_activity_trend" or "monthly_product_trend" => MonthlySalesTrendOperationCode,
                _ => DefaultOperationCode
            };
        }

        if (FinancialStatementTableIntentRules.LooksLikeFinancialStatementTableQuery(originalMessage) ||
            FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(originalMessage) ||
            FinancialStatementAnalysisIntentRules.LooksLikeFinancialStatementAnalysisQuery(originalMessage))
        {
            return FinancialStatementOperationCode;
        }

        if (MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(originalMessage) ||
            MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(originalMessage))
        {
            return MonthlySalesTrendOperationCode;
        }

        return DefaultOperationCode;
    }
}

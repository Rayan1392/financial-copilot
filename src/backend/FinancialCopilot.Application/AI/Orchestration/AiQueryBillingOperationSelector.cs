namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Maps governed V2 financial-query capabilities to their operation-based tariffs.</summary>
public static class AiQueryBillingOperationSelector
{
    public const string DefaultOperationCode = "AiQuery.Scanner";
    public const string FinancialStatementOperationCode = "AiQuery.FinancialStatement";
    public const string MonthlySalesTrendOperationCode = "AiQuery.MonthlySalesTrend";

    /// <param name="periodAnalysisEnabled">
    /// Feature 081 change «period-analysis-feature-flag»: when 081 is off its turns end unsupported, so they reserve the
    /// default operation instead of the statement tariff.
    /// </param>
    public static string Select(string? validatedCapabilityCode, string? originalMessage, bool periodAnalysisEnabled = true)
    {
        if (!string.IsNullOrWhiteSpace(validatedCapabilityCode))
        {
            return validatedCapabilityCode switch
            {
                "financial_statement_table" => FinancialStatementOperationCode,
                "financial_statement_period_analysis" when periodAnalysisEnabled => FinancialStatementOperationCode,
                "monthly_activity_trend" or "monthly_product_trend" => MonthlySalesTrendOperationCode,
                // Standalone valuation gauge cards: one minimum credit per usable answer (OQ-131), however many cards.
                ValuationGaugeCapability.Code => DefaultOperationCode,
                // Option comparison: one credit per answer with rows (OQ-173).
                OptionComparisonCapability.Code => DefaultOperationCode,
                _ => DefaultOperationCode
            };
        }

        if (ValuationGaugeIntentRules.TryMatch(originalMessage) is not null)
            return DefaultOperationCode;

        if (FinancialStatementTableIntentRules.LooksLikeFinancialStatementTableQuery(originalMessage) ||
            FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(originalMessage) ||
            periodAnalysisEnabled && FinancialStatementAnalysisIntentRules.LooksLikeFinancialStatementAnalysisQuery(originalMessage))
        {
            return FinancialStatementOperationCode;
        }

        if (ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery(originalMessage, isV2: true))
            return DefaultOperationCode;

        if (MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(originalMessage) ||
            MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(originalMessage))
        {
            return MonthlySalesTrendOperationCode;
        }

        return DefaultOperationCode;
    }
}

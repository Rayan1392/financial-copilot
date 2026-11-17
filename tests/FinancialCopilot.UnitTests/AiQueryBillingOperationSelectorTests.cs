using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class AiQueryBillingOperationSelectorTests
{
    [Theory]
    [InlineData("financial_statement_table", "P/E فولاد", "AiQuery.FinancialStatement")]
    [InlineData("financial_statement_period_analysis", "P/E فولاد", "AiQuery.FinancialStatement")]
    [InlineData("monthly_activity_trend", "P/E فولاد", "AiQuery.MonthlySalesTrend")]
    [InlineData("monthly_product_trend", "P/E فولاد", "AiQuery.MonthlySalesTrend")]
    [InlineData("symbol_lookup", "روند فروش ماهانه کهمدا", "AiQuery.Scanner")]
    public void Select_UsesValidatedCapabilityAsTheAuthoritativeBillingRoute(
        string capabilityCode,
        string message,
        string expectedOperationCode)
    {
        Assert.Equal(expectedOperationCode, AiQueryBillingOperationSelector.Select(capabilityCode, message));
    }

    [Theory]
    [InlineData("صورت جریان وجوه نقد فولاد", "AiQuery.FinancialStatement")]
    [InlineData("جدول صورت سود و زیان غالبر", "AiQuery.FinancialStatement")]
    [InlineData("جدول ترازنامه کگل", "AiQuery.FinancialStatement")]
    [InlineData("گزارش فصلی وبملت", "AiQuery.FinancialStatement")]
    [InlineData("تحلیل صورت سود و زیان فملی", "AiQuery.FinancialStatement")]
    [InlineData("روند فروش ماهانه کهمدا", "AiQuery.MonthlySalesTrend")]
    [InlineData("روند فروش آهن اسفنجی کهمدا", "AiQuery.MonthlySalesTrend")]
    [InlineData("P/E فولاد", "AiQuery.Scanner")]
    public void Select_UsesExistingDeterministicRulesWhenNoCapabilityFrameExists(
        string message,
        string expectedOperationCode)
    {
        Assert.Equal(expectedOperationCode, AiQueryBillingOperationSelector.Select(null, message));
    }
}

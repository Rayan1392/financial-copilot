using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialStatementAnalysisIntentRulesTests
{
    [Fact]
    public void LooksLikeFinancialStatementAnalysisQuery_MonthlyTrendPhrase_ReturnsFalse()
    {
        Assert.False(FinancialStatementAnalysisIntentRules.LooksLikeFinancialStatementAnalysisQuery(
            "روند فروش ماهانه غالبر را نشان بده"));
    }

    [Fact]
    public void BuildQuery_DefaultVariant_RemainsNonConsolidated()
    {
        var query = FinancialStatementAnalysisIntentRules.BuildQuery(
            "صورت مالی غالبر را تحلیل کن");

        Assert.Equal(FinancialStatementVariantPreference.DefaultNonConsolidated, query.VariantPreference);
        Assert.Equal("غالبر", query.SymbolOrCompanyName);
    }

    [Fact]
    public void BuildQuery_ExplicitConsolidated_SelectsConsolidatedOnly()
    {
        var query = FinancialStatementAnalysisIntentRules.BuildQuery(
            "صورت مالی تلفیقی غالبر را تحلیل کن");

        Assert.Equal(FinancialStatementVariantPreference.ConsolidatedOnly, query.VariantPreference);
    }

    [Fact]
    public void BuildQuery_BalanceSheetMetric_ExtractsFocus()
    {
        var query = FinancialStatementAnalysisIntentRules.BuildQuery(
            "نسبت جاری غالبر چقدر است؟");

        Assert.Equal(FinancialStatementType.BalanceSheet, query.StatementTypeFocus);
        Assert.Contains("CURRENT_RATIO", query.MetricFocusCodes!);
        Assert.True(query.IncludeBalanceSheetSummary);
    }
}

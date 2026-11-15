using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Domain.Financial.Entities;
using Xunit;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161RoutingTests
{
    private const string CashFlow = "\u0635\u0648\u0631\u062a \u062c\u0631\u06cc\u0627\u0646 \u0648\u062c\u0648\u0647 \u0646\u0642\u062f";
    private const string Steel = "\u0641\u0648\u0644\u0627\u062f";

    [Theory]
    [InlineData(CashFlow + " " + Steel)]
    [InlineData("\u0635\u0648\u0631\u062a \u062c\u0631\u06cc\u0627\u0646\u200c\u0647\u0627\u06cc \u0646\u0642\u062f\u06cc " + Steel)]
    [InlineData("\u06af\u0632\u0627\u0631\u0634 \u062c\u0631\u06cc\u0627\u0646 \u0646\u0642\u062f\u06cc \u0648\u0628\u0645\u0644\u062a")]
    [InlineData("\u06af\u0631\u062f\u0634 \u0648\u062c\u0647 \u0646\u0642\u062f " + Steel)]
    [InlineData("Cash Flow Statement " + Steel)]
    public void V2CashFlowAliases_AreOptInAndResolveCompany(string message)
    {
        var off = FinancialStatementTableIntentRules.BuildV2Query(message);
        var on = FinancialStatementTableIntentRules.BuildV2Query(message, cashFlowEnabled: true);

        Assert.Null(off.CashFlowReportRequest);
        Assert.Equal(FinancialStatementType.CashFlow, on.StatementType);
        Assert.NotNull(on.CashFlowReportRequest);
        Assert.Contains(on.CompanyQuery!, on.CashFlowReportRequest!.CompanyQuery, StringComparison.Ordinal);
        Assert.True(FinancialStatementTableIntentRules.LooksLikeV2FinancialStatementTableQuery(message, cashFlowEnabled: true));
    }

    [Fact]
    public void FullTableAndConsolidatedVariantRemainOnFeature083ExceptDatedVariant()
    {
        Assert.False(FinancialStatementTableIntentRules.ResolveCashFlowRequest("\u062c\u062f\u0648\u0644 \u06a9\u0627\u0645\u0644 " + CashFlow + " " + Steel).Use);
        Assert.False(FinancialStatementTableIntentRules.ResolveCashFlowRequest(CashFlow + " \u062a\u0644\u0641\u06cc\u0642\u06cc " + Steel).Use);

        var dated = FinancialStatementTableIntentRules.ResolveCashFlowRequest(CashFlow + " " + Steel + " 1405/06/31 \u062a\u0644\u0641\u06cc\u0642\u06cc");
        Assert.True(dated.Use);
        Assert.Equal("unsupported_consolidated_variant", dated.UnsupportedVariantFilter);
        Assert.Equal(new DateOnly(2026, 9, 22), dated.Request!.RequestedDate);
    }

    [Fact]
    public void MetricsAnalysisAndPeriodCuesProduceGovernedRequestShape()
    {
        var metric = FinancialStatementTableIntentRules.ResolveCashFlowRequest("\u062c\u0631\u06cc\u0627\u0646 \u0646\u0642\u062f \u0639\u0645\u0644\u06cc\u0627\u062a\u06cc " + Steel + " \u0686\u0642\u062f\u0631 \u0627\u0633\u062a");
        Assert.Equal(("metric", "operating_cash_flow"), (metric.Request!.Focus, metric.Request.MetricId));
        Assert.Equal(Steel, metric.Request.CompanyQuery);

        var analysis = FinancialStatementTableIntentRules.ResolveCashFlowRequest("\u062a\u062d\u0644\u06cc\u0644 " + CashFlow + " " + Steel);
        Assert.Equal(("analysis", "overview"), (analysis.Request!.Focus, analysis.Request.AnalysisTopic));

        var quarterly = FinancialStatementTableIntentRules.ResolveCashFlowRequest(CashFlow + " " + Steel + " \u0631\u0648\u0646\u062f \u062f\u0631 3 \u0633\u0627\u0644 \u0627\u062e\u06cc\u0631");
        Assert.Equal("quarterly", quarterly.Request!.View);
        Assert.Equal(8, quarterly.Request.PeriodCount);

        var annual = FinancialStatementTableIntentRules.ResolveCashFlowRequest(CashFlow + " " + Steel + " \u0633\u0627\u0644\u0627\u0646\u0647 \u062f\u0631 3 \u0633\u0627\u0644 \u0627\u062e\u06cc\u0631");
        Assert.Equal("annual", annual.Request!.View);
        Assert.Null(annual.Request.PeriodCount);
    }

    [Fact]
    public void DeterministicV2Path_AddsGovernedEvidenceOnlyInsideTheEnabledScope()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var message = CashFlow + " " + Steel;
        var enabled = new DeterministicCapabilityInterpreter(registry, feature161:
            new Feature161CashFlowRoutingScope(true, Feature154RoutingScope.V2)).Interpret(message);
        Assert.Equal("financial_statement_table", enabled.CapabilityCandidates.First().CapabilityCode);
        Assert.Contains(enabled.Evidence, item => item.Value == DeterministicCapabilityInterpreter.Feature161GovernedReportEvidence);

        var disabled = new DeterministicCapabilityInterpreter(registry, feature161:
            new Feature161CashFlowRoutingScope(false, Feature154RoutingScope.V2)).Interpret(message);
        Assert.DoesNotContain(disabled.Evidence, item => item.Value == DeterministicCapabilityInterpreter.Feature161GovernedReportEvidence);

        var frozen = new DeterministicCapabilityInterpreter(registry, feature161:
            new Feature161CashFlowRoutingScope(true, Feature154RoutingScope.V1Frozen)).Interpret(message);
        Assert.DoesNotContain(frozen.Evidence, item => item.Value == DeterministicCapabilityInterpreter.Feature161GovernedReportEvidence);
    }

    [Fact]
    public void LiquidityAndRealMoneyFlowAreNotCashFlowReportAliases()
    {
        Assert.False(FinancialStatementTableIntentRules.ResolveCashFlowRequest("\u0648\u0636\u0639\u06cc\u062a \u0646\u0642\u062f\u06cc\u0646\u06af\u06cc \u0648\u0628\u0645\u0644\u062a").Use);
        Assert.False(FinancialStatementTableIntentRules.ResolveCashFlowRequest("\u062c\u0631\u06cc\u0627\u0646 \u0646\u0642\u062f\u06cc\u0646\u06af\u06cc \u062d\u0642\u06cc\u0642\u06cc " + Steel).Use);
        Assert.False(FinancialStatementTableIntentRules.BuildV2Query("\u0645\u0648\u062c\u0648\u062f\u06cc \u0646\u0642\u062f " + Steel, true).CashFlowReportRequest is not null);
    }

    [Theory]
    [InlineData("\u0647\u0645\u06cc\u0646 \u06af\u0632\u0627\u0631\u0634 \u0631\u0627 \u0628\u0631\u0627\u06cc " + Steel + " \u0628\u062f\u0647", false, "report", null)]
    [InlineData("\u062f\u0648\u0631\u0647 \u0642\u0628\u0644\u0634 \u0686\u0637\u0648\u0631 \u0628\u0648\u062f", true, "report", null)]
    [InlineData("\u0628\u0627 \u0633\u0648\u062f \u062e\u0627\u0644\u0635\u0634 \u0645\u0642\u0627\u06cc\u0633\u0647 \u06a9\u0646", false, "analysis", "earnings_quality")]
    [InlineData("\u0648\u0636\u0639\u06cc\u062a \u062a\u0623\u0645\u06cc\u0646 \u0645\u0627\u0644\u06cc \u0622\u0646 \u0686\u0637\u0648\u0631 \u0627\u0633\u062a", false, "analysis", "financing_structure")]
    public void ActiveTaskFollowUpsResolveToReportFamily(string message, bool previous, string focus, string? topic)
    {
        var result = FinancialStatementTableIntentRules.ResolveCashFlowFollowUp(message);
        Assert.True(result.Use);
        Assert.Equal(previous, result.Request!.PreviousReport);
        Assert.Equal(focus, result.Request.Focus);
        Assert.Equal(topic, result.Request.AnalysisTopic);
    }
}

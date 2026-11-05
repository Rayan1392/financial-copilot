using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// F154-R-05: the methods the frozen V1 path calls (LlmAiIntentDetector lines 81/89, AiQueryOrchestrationService
/// BuildQuery) return exactly their HEAD 573cfd8 values; only the V2 entry points recognize the new aliases.
/// </summary>
public sealed class BalanceSheetReport154V1FreezeTests
{
    [Theory] // message, HEAD LooksLikeFinancialStatementTableQuery, HEAD LooksLikeFinancialStatementAnalysisQuery, HEAD BuildQuery statement type
    [InlineData("وضعیت مالی وبملت", false, false, null)]
    [InlineData("بیلان فاذر", false, false, null)]
    [InlineData("balance sheet وبملت", false, false, null)]
    [InlineData("عملکرد مالی وبملت", false, false, null)]
    [InlineData("P&L وبملت", false, false, null)]
    [InlineData("تحلیل عملکرد مالی وبملت", false, false, null)]
    [InlineData("ترازنامه وبملت", true, true, "BalanceSheet")]
    [InlineData("صورت سود و زیان وبملت", true, true, "IncomeStatement")]
    [InlineData("آخرین ترازنامه کگل", true, true, "BalanceSheet")]
    [InlineData("تحلیل ترازنامه وبملت", false, true, "BalanceSheet")]
    public void V1ReachableMethods_ReturnHeadValues(string message, bool table, bool analysis, string? statementType)
    {
        Assert.Equal(table, FinancialStatementTableIntentRules.LooksLikeFinancialStatementTableQuery(message));
        Assert.Equal(analysis, FinancialStatementAnalysisIntentRules.LooksLikeFinancialStatementAnalysisQuery(message));
        var query = FinancialStatementTableIntentRules.BuildQuery(message);
        Assert.Equal(statementType, query.StatementType?.ToString());
        Assert.False(query.UseBalanceSheetReport);
        Assert.Equal("ترازنامه وبملت", FinancialStatementTableIntentRules.NormalizeText("ترازنامه وبملت"));
        Assert.Equal("ترازنامۀ", FinancialStatementTableIntentRules.NormalizeText("ترازنامۀ"));
    }

    [Fact]
    public void InterpreterWithFeature154Disabled_MatchesHeadForNewAliases()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var v1 = new DeterministicCapabilityInterpreter(registry, null, Feature154RoutingScope.V1Frozen);
        // HEAD values from the T02 baseline (TestData/Feature154/routing-baseline.json).
        Assert.Equal("comprehensive_analysis", v1.Interpret("وضعیت مالی وبملت").CapabilityCandidates.FirstOrDefault()?.CapabilityCode);
        Assert.Empty(v1.Interpret("بیلان فاذر").CapabilityCandidates);
        Assert.DoesNotContain(v1.Interpret("ترازنامه وبملت").Evidence, item => item.Value == "balance-sheet-report");
    }

    [Theory] // PD-27 is V2-only: the frozen V1 scope never sees the new alias, the review rule or the report marker.
    [InlineData("صورت وضعیت مالی فاذر را بررسی کن")]
    [InlineData("صورت وضعیت فاذر")]
    [InlineData("عملکرد مالی فصلی فولاد را بررسی کن")]
    [InlineData("عملکرد فصلی فملی را بررسی کن")]
    [InlineData("بررسی وضعیت مالی وبملت")]
    public void Pd27_IsInvisibleToTheFrozenV1Scope(string message)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var v1 = new DeterministicCapabilityInterpreter(registry, null, Feature154RoutingScope.V1Frozen).Interpret(message);
        Assert.DoesNotContain(v1.Evidence, item => item.Value is DeterministicCapabilityInterpreter.Feature154GovernedReportEvidence or "balance-sheet-report");
        Assert.NotEqual("financial_statement_table", v1.CapabilityCandidates.FirstOrDefault()?.CapabilityCode);
        Assert.False(FinancialStatementTableIntentRules.BuildQuery(message).UseBalanceSheetReport);
        Assert.False(FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(message));
        Assert.False(FinancialStatementTableIntentRules.LooksLikeFinancialStatementTableQuery(message));
    }
}

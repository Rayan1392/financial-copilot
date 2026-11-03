using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.UnitTests;

public sealed class Feature150SharedChatRoutingTests
{
    private static readonly ConversationalCapabilityRegistry Registry =
        new(InitialConversationalCapabilityCatalog.Create());

    [Fact]
    public async Task ProposalParser_AcceptsNullForEveryNullableSchemaField()
    {
        const string json = """
            {
              "capabilityCodes": ["symbol_metric_lookup"],
              "missingSlots": [],
              "presentation": null,
              "confidence": 0.9,
              "evidence": [],
              "intent": null,
              "entities": [{"text":"\u0641\u0648\u0644\u0627\u062f","entityType":null,"scope":null,"confidence":0.9}],
              "metricHints": [],
              "period": null,
              "comparison": null,
              "periodCount": null,
              "operations": [],
              "unsupportedParts": []
            }
            """;
        var provider = new LlmQueryInterpretationProposalProvider(new StubExecutionService(json), Registry);

        var proposal = await provider.ProposeAsync("P/E", Guid.NewGuid(), "feature150-null-test", CancellationToken.None);

        Assert.NotNull(proposal);
        Assert.Null(proposal.PeriodCount);
        Assert.Null(proposal.Presentation);
        Assert.Null(proposal.Intent);
        Assert.Null(proposal.Period);
        Assert.Null(proposal.Comparison);
        var entity = Assert.Single(proposal.Entities!);
        Assert.Null(entity.EntityType);
        Assert.Null(entity.Scope);
    }

    [Fact]
    public void NetProfitMetricWords_AreNotExtractedAsCompanyEntities()
    {
        var query = "\u0633\u0648\u062f \u062e\u0627\u0644\u0635 \u0622\u062e\u0631\u06cc\u0646 \u0641\u0635\u0644 \u0641\u0648\u0644\u0627\u062f \u0686\u0642\u062f\u0631 \u0628\u0648\u062f\u0647\u061f";

        var interpretation = Interpret(query);

        Assert.Equal(["\u0641\u0648\u0644\u0627\u062f"], interpretation.EntityMentions.Select(entity => entity.Text));
        Assert.Equal("financial_statement_table", interpretation.CapabilityCandidates[0].CapabilityCode);
        Assert.True(FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(query));
    }

    [Theory]
    [InlineData("\u0633\u0647\u0627\u0645\u200c\u0647\u0627\u06cc\u06cc \u0628\u0627 P/E \u06a9\u0645\u062a\u0631 \u0627\u0632 \u06f5 \u0631\u0627 \u067e\u06cc\u062f\u0627 \u06a9\u0646")]
    [InlineData("\u0634\u0631\u06a9\u062a\u200c\u0647\u0627\u06cc\u06cc \u0628\u0627 P/S \u06a9\u0645\u062a\u0631 \u0627\u0632 \u06f1")]
    public void LessThanThresholdPhrasing_PrefersStockScreening(string query)
    {
        var interpretation = Interpret(query);

        Assert.Equal("stock_screening", interpretation.CapabilityCandidates[0].CapabilityCode);
        Assert.DoesNotContain("symbol_metric_lookup", interpretation.CapabilityCandidates.Take(1).Select(candidate => candidate.CapabilityCode));
    }

    [Fact]
    public void MonthlySalesGrowthThreshold_IsNotParsedAsProductTrend()
    {
        var query = "\u0646\u0645\u0627\u062f\u0647\u0627\u06cc\u06cc \u0628\u0627 \u0631\u0634\u062f \u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 \u0628\u06cc\u0634\u062a\u0631 \u0627\u0632 \u06f2\u06f0 \u062f\u0631\u0635\u062f";

        var interpretation = Interpret(query);

        Assert.Equal("stock_screening", interpretation.CapabilityCandidates[0].CapabilityCode);
        Assert.DoesNotContain("product_sales_trend", interpretation.CapabilityCandidates.Take(1).Select(candidate => candidate.CapabilityCode));
    }

    [Fact]
    public void QuarterlyNetProfitTrend_InfersIncomeStatementAndFourQuarterWindow()
    {
        var query = "\u0631\u0648\u0646\u062f \u0633\u0648\u062f \u062e\u0627\u0644\u0635 \u0686\u0647\u0627\u0631 \u0641\u0635\u0644 \u0627\u062e\u06cc\u0631 \u0641\u0648\u0644\u0627\u062f \u0686\u0637\u0648\u0631 \u0628\u0648\u062f\u0647\u061f";

        var interpretation = Interpret(query);
        var tableQuery = FinancialStatementTableIntentRules.BuildQuarterlyQuery(query);

        Assert.Equal("financial_statement_table", interpretation.CapabilityCandidates[0].CapabilityCode);
        Assert.True(FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(query));
        Assert.Equal("\u0641\u0648\u0644\u0627\u062f", tableQuery.CompanyQuery);
        Assert.Equal(FinancialStatementType.IncomeStatement, tableQuery.StatementType);
        Assert.Equal(4, tableQuery.QuarterCount);
    }

    private static QueryInterpretation Interpret(string query) =>
        new DeterministicCapabilityInterpreter(Registry).Interpret(query);

    private sealed class StubExecutionService(string json) : IAiModelExecutionService
    {
        public Task<AiModelResult> ExecuteAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiModelResult(null, json, [],
                new AiExecutionUsageFacts(request.CorrelationId, "test", "test-model", AiExecutionStatus.Completed,
                    TimeSpan.Zero, 1)));

        public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthAiRegressionDatasetTests
{
    private static readonly Guid TenantId = Guid.Parse("9a1b2c3d-4e5f-6789-abcd-ef0123456789");

    public static IEnumerable<object[]> Cases =>
        GoldenDatasets.Feature116SalesGrowthEvaluation.Questions.Select(question => new object[] { question });

    [Fact]
    public void Feature116Dataset_IsVersionedAndCoversGoldenAndAdversarialShapes()
    {
        var dataset = GoldenDatasets.Feature116SalesGrowthEvaluation;

        Assert.Equal("feature-116-sales-growth-regression", dataset.Name);
        Assert.Equal("1.0.0", dataset.Version);
        Assert.True(dataset.Questions.Count >= 10);
        Assert.Contains(dataset.Questions, question => question.Query.Contains("SQL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dataset.Questions, question => question.ExpectedIntent == DetectedIntent.SymbolLookup);
        Assert.Contains(dataset.Questions, question => question.ExpectedClarification);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GoldenUtterance_AssertIntentStructuredExpectationAndRouting(GoldenQuestion question)
    {
        var fake = new RegressionFakeExecutionService();
        var detector = new LlmAiIntentDetector(fake);

        var result = await detector.DetectAsync(
            new IntentDetectionInput(question.Query, question.Language, "feature-116", TenantId),
            CancellationToken.None);

        Assert.Equal(question.ExpectedIntent, result.Intent);
        Assert.Equal(question.ExpectedClarification, result.Intent == DetectedIntent.Clarification);
        Assert.False(string.IsNullOrWhiteSpace(question.ExpectedRoutingTarget));

        if (question.ExpectedSalesGrowth is not null)
        {
            Assert.Equal("screen_stocks", question.ExpectedRoutingTarget);
            if (question.ExpectedSalesGrowth.ThresholdValue is not null)
            {
                Assert.Equal("MONTHLY_SALES_GROWTH", question.ExpectedConditions.Single().MetricCode);
                Assert.Equal(question.ExpectedSalesGrowth.ThresholdValue, question.ExpectedConditions.Single().Threshold);
                Assert.Equal(
                    question.ExpectedSalesGrowth.ComparisonOperator,
                    question.ExpectedConditions.Single().Operator);
            }
            Assert.True(SalesGrowthSymbolScannerIntentRules.LooksLikeSalesGrowthScannerQuery(question.Query));
        }
        else if (question.ExpectedIntent == DetectedIntent.SymbolLookup)
        {
            Assert.Equal("lookup_symbol_metrics", question.ExpectedRoutingTarget);
            Assert.False(SalesGrowthSymbolScannerIntentRules.LooksLikeSalesGrowthScannerQuery(question.Query));
        }
        else
        {
            Assert.NotEqual("screen_stocks", question.ExpectedRoutingTarget);
            Assert.False(SalesGrowthSymbolScannerIntentRules.LooksLikeSalesGrowthScannerQuery(question.Query));
        }
    }

    [Fact]
    public async Task AmbiguousSalesGrowthPhrase_UsesFakeProviderAndPreservesClarification()
    {
        var fake = new RegressionFakeExecutionService();
        var detector = new LlmAiIntentDetector(fake);

        var result = await detector.DetectAsync(
            new IntentDetectionInput("رشد فروش ماه قبل", "fa", "feature-116-ambiguous", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.Clarification, result.Intent);
        Assert.Equal(1, fake.CallCount);
        Assert.False(SalesGrowthSymbolScannerIntentRules.LooksLikeSalesGrowthScannerQuery("رشد فروش ماه قبل"));
    }

    private sealed class RegressionFakeExecutionService : IAiModelExecutionService
    {
        public int CallCount { get; private set; }

        public Task<AiModelResult> ExecuteAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: "{\"intent\":\"Clarification\",\"confidence\":0.91}",
                ToolCalls: [],
                Usage: new AiExecutionUsageFacts(
                    request.CorrelationId, "feature-116-fake", "feature-116-regression-model",
                    AiExecutionStatus.Completed, TimeSpan.Zero, 0)));
        }

        public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

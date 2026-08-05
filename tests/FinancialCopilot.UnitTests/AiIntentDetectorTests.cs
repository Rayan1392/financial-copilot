using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class AiIntentDetectorTests
{
    private static readonly Guid TenantId = Guid.Parse("9a1b2c3d-4e5f-6789-abcd-ef0123456789");

    [Theory]
    [InlineData("pe کگل چقدر است؟")]
    [InlineData("P/E کگل")]
    [InlineData("پی به ای کگل")]
    [InlineData("نسبت قیمت به سود کگل")]
    [InlineData("price-to-earnings FMLCO")]
    public async Task Detect_PePointLookup_UsesDeterministicSymbolLookupRoute(string query)
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput(query, "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.SymbolLookup, result.Intent);
        Assert.True(result.Confidence >= 0.95);
    }

    [Theory]
    [InlineData("پرفروش‌ترین محصول کچاد؟", "کچاد")]
    [InlineData("پرفروش ترین محصول کچاد؟", "کچاد")]
    [InlineData("پرفروشترین محصول کچاد؟", "کچاد")]
    [InlineData("پرفروش‌ترین محصولات کچاد؟", "کچاد")]
    [InlineData("مهم‌ترین محصول کگل چیست؟", "کگل")]
    [InlineData("مهم‌ترین محصول کچاد چیست؟", "کچاد")]
    [InlineData("کگل بیشتر از چه محصولی درآمد دارد؟", "کگل")]
    [InlineData("ترکیب فروش محصولات فملی را نشان بده", "فملی")]
    public async Task Detect_ProductRevenueMix_UsesDeterministicRouteAndExtractsSymbol(string query, string expectedSymbol)
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput(query, "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.ProductRevenueMix, result.Intent);
        Assert.True(result.Confidence >= 0.95);
        Assert.Equal(expectedSymbol, ProductRevenueMixIntentRules.ExtractCompanySymbol(query));
    }

    [Theory]
    [InlineData("P/E کمتر از ۴")]
    [InlineData("P/E below 6")]
    public async Task Detect_PeFilterQuery_StillUsesModelClassification(string query)
    {
        var detector = new LlmAiIntentDetector(new ScannerIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput(query, "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.Scanner, result.Intent);
    }

    [Theory]
    [InlineData("صورت مالی غالبر را تحلیل کن")]
    [InlineData("سود خالص غالبر چقدر شده؟")]
    [InlineData("ROE غالبر چقدر است؟")]
    public async Task Detect_FinancialStatementAnalysis_UsesDeterministicRoute(string query)
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput(query, "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.FinancialStatementPeriodAnalysis, result.Intent);
        Assert.True(result.Confidence >= 0.95);
    }

    [Fact]
    public async Task Detect_DisclosureListing_UsesDeterministicRouteBeforeTheModel()
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput("فهرست آخرین تولید و فروش منتشر شده را بده", "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.DisclosureListing, result.Intent);
        Assert.Equal(0.99, result.Confidence);
    }

    [Theory]
    [InlineData("list stocks with sales growth above 30% versus same month last year")]
    [InlineData("نمادها با رشد فروش حداقل ۲ برابر نسبت به میانگین ۱۲ ماهه")]
    public async Task Detect_SalesGrowthDiscovery_UsesDeterministicScannerRoute(string query)
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput(query, "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.Scanner, result.Intent);
        Assert.Equal(0.99, result.Confidence);
    }

    [Theory]
    [InlineData("sales growth شغدیر")]
    [InlineData("رشد فروش فولاد")]
    public async Task Detect_SingleSymbolSalesGrowth_RemainsSymbolLookup(string query)
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput(query, "fa", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.SymbolLookup, result.Intent);
        Assert.Equal(0.98, result.Confidence);
    }

    [Fact]
    public async Task Detect_SalesGrowthDiscovery_ComposesWithOtherScannerFilters()
    {
        var detector = new LlmAiIntentDetector(new UnknownIntentExecutionService());

        var result = await detector.DetectAsync(
            new IntentDetectionInput("list stocks with sales growth above 30% and P/E below 5", "en", "corr", TenantId),
            CancellationToken.None);

        Assert.Equal(DetectedIntent.Scanner, result.Intent);
    }

    private sealed class UnknownIntentExecutionService : IAiModelExecutionService
    {
        public Task<AiModelResult> ExecuteAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result("""{"intent":"Unknown","confidence":0.1}""", request.CorrelationId));

        public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ScannerIntentExecutionService : IAiModelExecutionService
    {
        public Task<AiModelResult> ExecuteAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result("""{"intent":"Scanner","confidence":0.97}""", request.CorrelationId));

        public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
            AiModelSelectionRequest selection,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static AiModelResult Result(string json, string correlationId) =>
        new(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                correlationId,
                "StubProvider",
                "stub-model",
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0));
}

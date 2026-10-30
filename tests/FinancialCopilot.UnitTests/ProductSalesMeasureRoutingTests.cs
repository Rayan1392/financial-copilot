using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// The product sales value specialist answers SalesAmount only. A sales-quantity or production-quantity question must
/// never be answered with an amount: it is not routed there, and if it arrives anyway the specialist reports Unsupported
/// so the chat fallback can answer the requested measure.
/// </summary>
public sealed class ProductSalesMeasureRoutingTests
{
    [Theory]
    [InlineData("مقدار فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟")]
    [InlineData("میزان فروش گندله کگل چقدر بوده؟")]
    [InlineData("حجم فروش گندله کگل در شهریور ۱۴۰۵")]
    [InlineData("فروش گندله کگل در شهریور چند تن بوده؟")]
    [InlineData("مقدار تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟")]
    public void PhysicalQuantityQuestions_AreNotProductSalesValue(string question)
    {
        Assert.True(ProductSemanticIntentRules.RequestsPhysicalQuantity(question));
        Assert.False(ProductSemanticIntentRules.LooksLikeProductSalesValue(question));
    }

    [Theory]
    [InlineData("مبلغ فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟")]
    [InlineData("فولاد محصولات گرمش چقدر فروخته؟")]
    public void MonetarySalesQuestions_AreNotPhysicalQuantity(string question) =>
        Assert.False(ProductSemanticIntentRules.RequestsPhysicalQuantity(question));

    [Fact]
    public async Task SalesValueSpecialist_RefusesAQuantityQuestionWithoutAnsweringAnAmount()
    {
        var useCase = new NeverCalledTrendUseCase();
        var executor = new ProductSalesValueCapabilityExecutor(useCase);

        var result = await executor.ExecuteAsync(Frame("مقدار فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟"),
            new QueryExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "fa"), CancellationToken.None);

        Assert.Equal(CapabilityExecutionStatus.Unsupported, result.Status);
        Assert.Equal("requested_measure_is_physical_quantity", result.ReasonCode);
        Assert.Null(result.Payload);
        Assert.False(useCase.Called);
    }

    [Theory]
    [InlineData("مبلغ فروش گندله کگهر در تیر ۱۴۰۵ چقدر بود؟")]
    [InlineData("مبلغ فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟")]
    [InlineData("مبلغ فروش گندله کگل در تیرماه چقدر بود؟")]
    [InlineData("مبلغ فروش گندله کگل در 1405/04")]
    [InlineData("فروش گندله کگل در سال ۱۴۰۴ چقدر بود؟")]
    public async Task SalesValueSpecialist_RefusesAnExplicitPeriodWithoutAnsweringTheLatestMonth(string question)
    {
        var useCase = new NeverCalledTrendUseCase();
        var executor = new ProductSalesValueCapabilityExecutor(useCase);

        var result = await executor.ExecuteAsync(Frame(question),
            new QueryExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "fa"), CancellationToken.None);

        Assert.Equal(CapabilityExecutionStatus.Unsupported, result.Status);
        Assert.Equal("requested_period_is_not_latest_month", result.ReasonCode);
        Assert.Null(result.Payload);
        Assert.False(useCase.Called);
    }

    [Theory]
    [InlineData("مبلغ فروش گندله کگهر چقدر است؟", "گندله")]
    [InlineData("مبلغ فروش محصول گندله کگهر چقدر است؟", "محصول گندله")]
    [InlineData("مبلغ فروش گندله کگهر در تیر ۱۴۰۵ چقدر بود؟", "گندله")]
    [InlineData("مبلغ فروش محصول گندله کگهر در تیر ۱۴۰۵ چقدر بود؟", "محصول گندله")]
    [InlineData("مبلغ فروش گندله بورس کالا کگهر در تیرماه ۱۴۰۵ چقدر بوده؟", "گندله بورس کالا")]
    public void ProductSalesValueQuestions_RouteToTheSalesValueSpecialistWithOrWithoutTheProductMarker(string question, string product)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());

        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(question);

        Assert.True(ProductSemanticIntentRules.LooksLikeProductSalesValue(question));
        Assert.Equal("product_sales_value", interpretation.CapabilityCandidates.First().CapabilityCode);
        Assert.Equal(product, Assert.Single(interpretation.EntityMentions, entity => entity.EntityType == "product").Text);
    }

    [Theory]
    [InlineData("مبلغ فروش کگهر چقدر است؟")]
    [InlineData("مبلغ فروش کگهر در تیر ۱۴۰۵ چقدر بود؟")]
    [InlineData("Which company has revenue 3300508?")]
    public void CompanyLevelSalesQuestions_HaveNoUnmarkedProduct(string question)
    {
        Assert.Null(ProductSemanticIntentRules.ExtractUnmarkedProductMention(question));
        Assert.False(ProductSemanticIntentRules.LooksLikeProductSalesValue(question));
    }

    [Theory]
    [InlineData("شرکت‌ها را بر اساس مبلغ فروش گندله در مرداد ۱۴۰۵ رتبه‌بندی کن.")]
    [InlineData("کدام شرکت‌ها بیشترین فروش گندله را داشته‌اند؟")]
    public void RankingQuestions_AreNotProductSalesValue(string question) =>
        Assert.False(ProductSemanticIntentRules.LooksLikeProductSalesValue(question));

    [Fact]
    public void NonSalesQuestions_AreNotProductSalesValue() =>
        Assert.False(ProductSemanticIntentRules.LooksLikeProductSalesValue("سود خالص کگل در آخرین دوره چقدر است؟"));

    [Theory]
    [InlineData("روند فروش گندله کگل در ۱۲ ماه اخیر چطور بوده؟", "product_sales_trend")]
    public void ProductSalesTrend_KeepsItsSpecialist(string question, string capability)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());

        Assert.False(ProductSemanticIntentRules.LooksLikeProductSalesValue(question));
        Assert.Equal(capability, new DeterministicCapabilityInterpreter(registry).Interpret(question).CapabilityCandidates.First().CapabilityCode);
    }

    [Theory]
    [InlineData("محصول گندله", "گندله")]
    [InlineData("محصولات گرم", "گرم")]
    [InlineData("گندله", null)]
    public void StripProductMarker_RemovesOnlyTheGenericMarker(string mention, string? expected) =>
        Assert.Equal(expected, ProductSemanticIntentRules.StripProductMarker(mention));

    [Fact]
    public async Task SalesValueSpecialist_WithoutAPeriod_StillAnswersTheLatestReportedMonth()
    {
        JalaliPeriod.TryCreate(1405, 4, out var tir);
        JalaliPeriod.TryCreate(1405, 5, out var mordad);
        var executor = new ProductSalesValueCapabilityExecutor(new FixedTrendUseCase(
            Point(tir, 91_302_513m), Point(mordad, 39_580_764m)));

        var result = await executor.ExecuteAsync(Frame("مبلغ فروش محصول گندله کگهر چقدر است؟"),
            new QueryExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "fa"), CancellationToken.None);

        var payload = Assert.IsType<ProductSalesValuePayload>(result.Payload);
        Assert.Equal(mordad, payload.Period);
        Assert.Equal(39_580_764m, payload.SalesAmountMillionRial);
    }

    [Theory]
    [InlineData("مبلغ فروش گندله کگهر چقدر است؟")]
    [InlineData("فولاد محصولات گرمش چقدر فروخته؟")]
    [InlineData("مبلغ فروش گندله کگل در آخرین ماه چقدر بود؟")]
    public void QuestionsWithoutAPeriod_KeepTheLatestMonthSpecialist(string question) =>
        Assert.False(PersianReportingPeriodNormalizer.MentionsReportingPeriod(question));

    private static ValidatedQueryFrame Frame(string question) => new("product_sales_value", 1,
        [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, "کگل", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid),
         new ResolvedQuerySlot(QuerySlotType.Product, "گندله", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)],
        new QueryInterpretation(question, question, "fa", [new CapabilityCandidate("product_sales_value", 1, 0.9m, [])],
            [], [], null, null, null, [], [], 0.9m, [], 1, InterpretationConfidenceBand.High));

    private static MonthlyProductTrendPoint Point(JalaliPeriod period, decimal salesMillionRial) =>
        new(period, period.ToString(), "key", "گندله", "تن", null, null, salesMillionRial, null, null,
            MonthlyProductTrendRateStatus.UnavailableRate, []);

    private sealed class FixedTrendUseCase(params MonthlyProductTrendPoint[] points) : IMonthlyProductTrendQueryUseCase
    {
        public Task<MonthlyProductTrendResult> ExecuteAsync(MonthlyProductTrendQuery query, CancellationToken ct = default) =>
            Task.FromResult(new MonthlyProductTrendResult(MonthlyProductTrendResult.Discriminator, MonthlyProductTrendResult.CurrentVersion,
                MonthlyProductTrendResolutionState.Resolved, "کگهر", "1", "گهر زمین", "کگهر", "key", null, null, "گندله", "تن",
                points, [], []));
    }

    /// <summary>A question about a set of companies is never narrowed to a company remembered from an earlier turn.</summary>
    [Theory]
    [InlineData("کدام شرکت‌ها در مرداد ۱۴۰۵ گندله تولید کرده‌اند؟", true)]
    [InlineData("شرکت‌ها را بر اساس مبلغ فروش گندله در مرداد ۱۴۰۵ رتبه‌بندی کن.", true)]
    [InlineData("رتبه‌بندی مبلغ فروش گندله در مرداد ۱۴۰۵", true)]
    [InlineData("روند فروش گندله کگل در ۱۲ ماه اخیر چطور بوده؟", false)]
    [InlineData("مبلغ فروش گندله کگهر در تیر ۱۴۰۵", false)]
    [InlineData("روند فروش ماهانه را هم نشان بده", false)]
    public void CompanyPopulationQuestionsAreRecognized(string question, bool population) =>
        Assert.Equal(population, ProductSemanticIntentRules.AsksForCompanyPopulation(question));

    private sealed class NeverCalledTrendUseCase : IMonthlyProductTrendQueryUseCase
    {
        public bool Called { get; private set; }

        public Task<MonthlyProductTrendResult> ExecuteAsync(MonthlyProductTrendQuery query, CancellationToken ct = default)
        {
            Called = true;
            throw new InvalidOperationException("A refused question must not read the latest sales amount.");
        }
    }
}

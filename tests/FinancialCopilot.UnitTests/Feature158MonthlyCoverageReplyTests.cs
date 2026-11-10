using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 158 D4: a monthly-sales turn with no data gets the true reason. Symbols are invented.</summary>
public sealed class Feature158MonthlyCoverageReplyTests
{
    private const string Symbol = "نمونه";
    private const string OutsideScopeFa = "این نماد در فهرست شرکت‌های تحت پوشش گزارش ماهانه نیست.";
    private const string OutsideScopeEn = "This symbol is not on the list of companies covered by monthly reports.";

    [Theory]
    [InlineData("ساختمانی", "construction")]
    [InlineData("سرمایه‌گذاری", "investment")]
    [InlineData("بانکی", "banking")]
    [InlineData("لیزینگی", "leasing")]
    [InlineData("بیمه‌ای", "insurance")]
    [InlineData("حمل و نقل دریایی", "maritime transportation")]
    [InlineData("تأمین سرمایه", "investment banking")]
    public async Task NoRouteCode_ReturnsNotCoveredReasonAndExactReply(string categoryFa, string categoryEn)
    {
        var result = await Execute(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, categoryFa, categoryEn), "fa");

        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.MonthlyReportNotCovered, result.ReasonCode);
        Assert.Equal(
            $"پوشش گزارش ماهانهٔ فروش برای شرکت‌های {categoryFa} در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «{Symbol}» نمودار فروش ماهانه نداریم.",
            result.Payload);
    }

    [Fact]
    public async Task EnglishReply_UsesCategoryEnAndOmitsSymbol()
    {
        var result = await Execute(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, "بانکی", "banking"), "en");

        Assert.Equal(
            "Monthly sales report coverage for banking companies is not available from the current data source, so there is no monthly sales chart for this symbol.",
            result.Payload);
        Assert.True(AiDialogueOutcomePolicy.IsCompatibleWithLanguage((string)result.Payload!, "en"));
    }

    [Theory]
    [InlineData("fa", OutsideScopeFa)]
    [InlineData("en", OutsideScopeEn)]
    public async Task OutsideScope_ReturnsIneligibleReasonAndOwnSentence(string language, string expected)
    {
        var result = await Execute(new MonthlyReportCoverage(MonthlyReportCoverageKind.OutsideScope), language);

        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.DataStaleOrIneligible, result.ReasonCode);
        Assert.Equal(expected, result.Payload);
    }

    [Fact]
    public async Task CompanyAmbiguous_ReturnsDisambiguation()
    {
        var result = await Execute(new MonthlyReportCoverage(MonthlyReportCoverageKind.CompanyAmbiguous), "fa");

        Assert.Equal(CapabilityExecutionStatus.DisambiguationRequired, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.EntityAmbiguous, result.ReasonCode);
    }

    [Theory]
    [InlineData(MonthlyReportCoverageKind.HasRoute)]
    [InlineData(MonthlyReportCoverageKind.Unknown)]
    public async Task SupportedRouteAndUnknown_KeepTodaysReason(MonthlyReportCoverageKind kind)
    {
        var result = await Execute(new MonthlyReportCoverage(kind), "fa");

        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.SupportedButNoRows, result.ReasonCode);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ResultPresent_DoesNotCallPort()
    {
        var reader = new FakeReader(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, "بانکی", "banking"));
        var executor = new MonthlyActivityTrendCapabilityExecutor(new FakeTrend(HasData: true), reader);

        var result = await executor.ExecuteAsync(Frame("fa"), Context(), default);

        Assert.Equal(CapabilityExecutionStatus.Executed, result.Status);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task FrozenV1_DoesNotCallPort()
    {
        var reader = new FakeReader(new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, "بانکی", "banking"));
        var executor = new MonthlyActivityTrendCapabilityExecutor(new FakeTrend(HasData: false), reader);

        using (Feature154RoutingScope.EnterFrozenV1())
        {
            var result = await executor.ExecuteAsync(Frame("fa"), Context(), default);

            Assert.Equal(DialogueOutcomeReasonCodes.SupportedButNoRows, result.ReasonCode);
            Assert.Equal(0, reader.Calls);
        }
    }

    [Theory]
    [InlineData("fa", "پوشش گزارش ماهانهٔ فروش برای شرکت‌های بانکی در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم.")]
    [InlineData("en", "Monthly sales report coverage for banking companies is not available from the current data source, so there is no monthly sales chart for this symbol.")]
    [InlineData("fa", OutsideScopeFa)]
    [InlineData("en", OutsideScopeEn)]
    public void Reply_RendersThroughOutcomePolicy(string language, string text)
    {
        var outcome = new DialogueOutcomeResult(DialogueOutcome.NoData, DialogueOutcomeReasonCodes.MonthlyReportNotCovered, language, null, false);

        Assert.Equal(text, AiDialogueOutcomePolicy.ComposeSystemMessage(outcome, text));
    }

    private static Task<CapabilityExecutionResult> Execute(MonthlyReportCoverage coverage, string language) =>
        new MonthlyActivityTrendCapabilityExecutor(new FakeTrend(HasData: false), new FakeReader(coverage))
            .ExecuteAsync(Frame(language), Context(), default);

    private static ValidatedQueryFrame Frame(string language) => new(
        "monthly_activity_trend", 1,
        [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, Symbol, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, "monthly_activity_trend")],
        new("q", "q", language, [], [], [], null, null, null, [], [], 1m, [], 1));

    private static QueryExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "fa");

    private sealed class FakeReader(MonthlyReportCoverage coverage) : IMonthlyReportCoverageReader
    {
        public int Calls { get; private set; }

        public Task<MonthlyReportCoverage> ReadAsync(string symbol, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(coverage);
        }
    }

    private sealed record FakeTrend(bool HasData) : IMonthlyActivityTrendQueryUseCase
    {
        public Task<MonthlyActivityTrendResponse?> ExecuteAsync(MonthlyActivityTrendQuery query, CancellationToken ct = default) =>
            Task.FromResult(HasData
                ? new MonthlyActivityTrendResponse(Symbol, "شرکت", 1405, 4, "میلیارد تومان", 1m, null, null, null, null, null, null, [], [], [], "NoavaranCurrentApi", DateTimeOffset.UtcNow)
                : null);
    }
}

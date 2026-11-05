using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Domain.Financial.Entities;
using static FinancialCopilot.UnitTests.BalanceSheet154Fixtures;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 154 T12 routing on both V2 paths: the deterministic branch (FinancialStatementTableIntentRules.BuildV2Query,
/// as used by FinancialCopilotWorkflowDefinition) and the semantic route (HybridCapabilityInterpreter, which recognizes
/// balance-sheet reports deterministically without asking the model, then the same executor recognition).
/// </summary>
public sealed class BalanceSheetReport154RoutingTests
{
    private static readonly ConversationalCapabilityRegistry Registry = new(InitialConversationalCapabilityCatalog.Create());

    private sealed class ModelMustNotBeCalled : IQueryInterpretationProposalProvider
    {
        public int Calls { get; private set; }

        public Task<QueryInterpretationProposal?> ProposeAsync(string originalText, Guid tenantId, string correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<QueryInterpretationProposal?>(new QueryInterpretationProposal(
                ["comprehensive_analysis"], [], null, 0.99m, ["scripted-model"]));
        }
    }

    private static async Task<(string? Capability, bool ModelUsed, int ModelCalls)> Semantic(string message)
    {
        var model = new ModelMustNotBeCalled();
        var hybrid = new HybridCapabilityInterpreter(new DeterministicCapabilityInterpreter(Registry), Registry,
            new QueryInterpretationValidator(Registry), model);
        var result = await hybrid.InterpretAsync(message, Guid.NewGuid(), "feature154-routing", CancellationToken.None);
        return (result.Interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode, result.ModelProposalUsed, model.Calls);
    }

    private static void AssertFeature154(string message, string expectedCompany, string view, string? baseline = null)
    {
        var query = FinancialStatementTableIntentRules.BuildV2Query(message);
        Assert.True(query.UseBalanceSheetReport, message);
        Assert.Equal(FinancialStatementType.BalanceSheet, query.StatementType);
        Assert.Equal(expectedCompany, query.CompanyQuery);
        Assert.Equal(view, query.BalanceSheetView);
        if (baseline is not null) Assert.Equal(baseline, query.BaselineKind);
        Assert.False(FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery(message), message);
        Assert.True(FinancialStatementTableIntentRules.LooksLikeV2FinancialStatementTableQuery(message), message);
        var deterministic = new DeterministicCapabilityInterpreter(Registry).Interpret(message);
        Assert.Equal("financial_statement_table", deterministic.CapabilityCandidates[0].CapabilityCode);
        Assert.Equal([expectedCompany], deterministic.EntityMentions.Select(entity => entity.Text));
        var executor = FinancialStatementTableIntentRules.ResolveBalanceSheetRequest(message);
        Assert.Equal((true, query.BalanceSheetView, query.BaselineKind), (executor.Use, executor.View, executor.BaselineKind));
    }

    [Theory] // AC-R01, AC-R02, AC-R06, AC-R07
    [InlineData("ترازنامه وبملت", "وبملت")]
    [InlineData("گزارش ترازنامه بانک ملت", "بانک ملت")]
    [InlineData("صورت وضعیت مالی شگل", "شگل")]
    [InlineData("وضعیت مالی وبملت", "وبملت")]
    [InlineData("وضعیت مالی شگل", "شگل")]
    [InlineData("بیلان فاذر", "فاذر")]
    [InlineData("صورت دارایی‌ها و بدهی‌ها شگل", "شگل")]
    [InlineData("دارایی‌ها و بدهی‌های فولاد", "فولاد")]
    [InlineData("آخرین ترازنامه فاذر", "فاذر")]
    [InlineData("balance sheet وبملت", "وبملت")]
    [InlineData("Balance Sheet وبملت", "وبملت")]
    [InlineData("statement of financial position فاذر", "فاذر")]
    [InlineData("وبملت ترازنامه", "وبملت")]
    [InlineData("ترازنامه بانک ملت رو نشون بده", "بانک ملت")]
    [InlineData("ترازنامه‌ی وبملت", "وبملت")]
    [InlineData("ترازنامۀ وبملت", "وبملت")]
    [InlineData("صورت وضعيت مالي شگل", "شگل")]
    [InlineData("گزارش ترازنامه فصلی فولاد", "فولاد")]
    [InlineData("آخرین ترازنامه کگل", "کگل")]
    // PD-27 (owner instruction 2026-11-05).
    [InlineData("صورت وضعیت مالی فاذر را بررسی کن", "فاذر")]
    [InlineData("صورت وضعیت فاذر", "فاذر")]
    [InlineData("ترازنامه فاذر را بده", "فاذر")]
    [InlineData("ترازنامه فاذر؟", "فاذر")]
    [InlineData("بررسی وضعیت مالی وبملت", "وبملت")]
    [InlineData("ترازنامه وبملت را بررسی کن", "وبملت")]
    [InlineData("بررسی بیلان فاذر", "فاذر")]
    public async Task OrdinaryRequests_OpenTheDefaultReport_OnBothV2Paths(string message, string company)
    {
        AssertFeature154(message, company, BalanceSheetViews.Default);
        var semantic = await Semantic(message);
        Assert.Equal(("financial_statement_table", false, 0), semantic);
    }

    [Theory] // AC-R08
    [InlineData("ترازنامه وبملت را با پایان سال مالی قبل مقایسه کن", BalanceSheetBaselineKinds.FiscalYearEnd)]
    [InlineData("مقایسه ترازنامه وبملت", BalanceSheetBaselineKinds.FiscalYearEnd)]
    [InlineData("تغییرات ترازنامه وبملت", BalanceSheetBaselineKinds.FiscalYearEnd)]
    [InlineData("ترازنامه وبملت نسبت به گزارش قبلی", BalanceSheetBaselineKinds.PreviousReport)]
    [InlineData("ترازنامه وبملت در مقایسه با مدت مشابه سال قبل", BalanceSheetBaselineKinds.SameStagePreviousYear)]
    [InlineData("ترازنامه وبملت را با فصل قبل مقایسه کن", BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd)]
    [InlineData("ترازنامه وبملت را با پایان فصل قبل مقایسه کن", BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd)]
    [InlineData("مقایسه ترازنامه فصلی وبملت", BalanceSheetBaselineKinds.FiscalYearEnd)]
    [InlineData("ترازنامه وبملت ۱۴۰۵/۰۶/۳۱ و ۱۴۰۵/۰۳/۳۱ را مقایسه کن", BalanceSheetBaselineKinds.Explicit)]
    public async Task ComparisonRequests_MapToTheirBaseline_NeverFeature083Or144(string message, string baseline)
    {
        AssertFeature154(message, "وبملت", BalanceSheetViews.Comparison, baseline);
        Assert.Null(FinancialStatementTableIntentRules.BuildV2Query(message).UnsupportedVariantFilter);
        Assert.Equal(("financial_statement_table", false, 0), await Semantic(message));
    }

    [Fact]
    public void ExplicitDates_KeepBothOperands()
    {
        var query = FinancialStatementTableIntentRules.BuildV2Query("ترازنامه وبملت ۱۴۰۵/۰۶/۳۱ و ۱۴۰۵/۰۳/۳۱ را مقایسه کن");
        Assert.Equal([J(1405, 6, 31), J(1405, 3, 31)], query.ComparisonDates);
    }

    [Fact] // AC-R10, PD-20
    public void SingleDate_AndDateWithFeature083OnlyFilter()
    {
        AssertFeature154("ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱", "وبملت", BalanceSheetViews.SingleDate);
        Assert.Equal([J(1405, 3, 31)], FinancialStatementTableIntentRules.BuildV2Query("ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱").ComparisonDates);
        AssertFeature154("ترازنامه ۱۴۰۴/۱۲/۲۹ فاذر", "فاذر", BalanceSheetViews.SingleDate);

        var consolidated = FinancialStatementTableIntentRules.BuildV2Query("ترازنامه تلفیقی وبملت ۱۴۰۵/۰۳/۳۱");
        Assert.True(consolidated.UseBalanceSheetReport);
        Assert.Equal("تلفیقی", consolidated.UnsupportedVariantFilter);
        Assert.Equal([J(1405, 3, 31)], consolidated.ComparisonDates);
        Assert.Equal("وبملت", consolidated.CompanyQuery);
        var fullTable = FinancialStatementTableIntentRules.BuildV2Query("همه اقلام ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱");
        Assert.Equal("همه اقلام", fullTable.UnsupportedVariantFilter);
    }

    [Theory] // AC-R09: full-table and variant requests keep Feature 083.
    [InlineData("همه اقلام ترازنامه وبملت")]
    [InlineData("جدول کامل ترازنامه شگل")]
    [InlineData("ترازنامه تلفیقی وبملت")]
    [InlineData("ترازنامه حسابرسی‌شده فاذر")]
    [InlineData("ترازنامه تجدید ارائه‌شده شگل")]
    [InlineData("ترازنامه ۱۲ ماهه شگل")]
    public void FullTableAndVariantRequests_StayOnFeature083(string message)
    {
        var query = FinancialStatementTableIntentRules.BuildV2Query(message);
        Assert.False(query.UseBalanceSheetReport);
        Assert.Equal(FinancialStatementType.BalanceSheet, query.StatementType);
        Assert.Equal(FinancialStatementTableIntentRules.BuildQuery(message), query);
    }

    [Theory] // AC-R03: «عملکرد مالی» follows exactly the «صورت سود و زیان» route (Feature 144).
    [InlineData("عملکرد مالی وبملت")]
    [InlineData("صورت عملکرد مالی وبملت")]
    public async Task IncomeAliases_FollowTheProfitAndLossRoute(string message)
    {
        var reference = Feature154RoutingBaselineTests.Capture("صورت سود و زیان وبملت", true);
        var actual = Feature154RoutingBaselineTests.Capture(message, true);
        Assert.Equal(reference with { Phrase = message }, actual);
        Assert.Equal("وبملت", FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(message).CompanyQuery);
        Assert.Equal(FinancialStatementType.IncomeStatement, FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(message).StatementType);
        Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetReportQuery(message));
        Assert.Equal(await Semantic("صورت سود و زیان وبملت"), await Semantic(message));
    }

    [Fact]
    public void AnalysisOfFinancialPerformance_BehavesLikeAnalysisOfProfitAndLoss()
    {
        var reference = Feature154RoutingBaselineTests.Capture("تحلیل صورت سود و زیان وبملت", true);
        Assert.Equal(reference with { Phrase = "تحلیل عملکرد مالی وبملت" }, Feature154RoutingBaselineTests.Capture("تحلیل عملکرد مالی وبملت", true));
        Assert.Equal(FinancialStatementType.IncomeStatement,
            FinancialStatementAnalysisIntentRules.BuildV2Query("تحلیل عملکرد مالی وبملت").StatementTypeFocus);
    }

    [Theory]
    [InlineData("P&L وبملت", "وبملت")]
    [InlineData("statement of profit or loss شگل", "شگل")]
    [InlineData("statement of operations فاذر", "فاذر")]
    [InlineData("گزارش سود و زیان شگل", null)]
    [InlineData("صورت درآمد و هزینه فاذر", null)]
    public void EnglishAndPersianIncomeAliases_ResolveToIncomeStatement(string message, string? company)
    {
        var query = FinancialStatementTableIntentRules.BuildV2Query(message);
        Assert.Equal(FinancialStatementType.IncomeStatement, query.StatementType);
        Assert.False(query.UseBalanceSheetReport);
        if (company is not null) Assert.Equal(company, query.CompanyQuery);
    }

    [Theory] // AC-R04: aliases and possessive forms are never companies; the company comes from conversation state.
    [InlineData("عملکرد مالی‌اش")]
    [InlineData("وضعیت مالی‌اش را نشان بده")]
    [InlineData("ترازنامه‌اش")]
    public void Possessives_AreNeverCompanyHints(string message)
    {
        var balance = FinancialStatementTableIntentRules.BuildV2Query(message);
        var quarterly = FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(message);
        Assert.Null(FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery(message) ? quarterly.CompanyQuery : balance.CompanyQuery);
        Assert.Null(balance.CompanyQuery);
        Assert.Empty(new DeterministicCapabilityInterpreter(Registry).Interpret(message).EntityMentions);
    }

    [Theory] // Analysis and single-value requests keep their routes («بررسی» alone opens the report since PD-27).
    [InlineData("تحلیل ترازنامه وبملت")]
    [InlineData("تحلیل ترازنامه فاذر")]
    [InlineData("ترازنامه وبملت را بررسی و تحلیل کن")]
    [InlineData("بررسی ترازنامه و سود و زیان وبملت")]
    [InlineData("جمع دارایی‌های ترازنامه شگل چقدر است؟")]
    public void AnalysisAndSingleValue_AreNotFeature154(string message) =>
        Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetReportQuery(message));

    [Fact] // AC-R05: bare «وضعیت» and «عملکرد» keep their behavior (also asserted by the T02 baseline test).
    public void BareWords_AreUnchanged()
    {
        Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetReportQuery("وضعیت وبملت"));
        Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetReportQuery("عملکرد وبملت"));
        Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetReportQuery("وضعیت فاذر"));
        Assert.Equal("comprehensive_analysis", new DeterministicCapabilityInterpreter(Registry).Interpret("وضعیت فاذر").CapabilityCandidates[0].CapabilityCode);
        Assert.Equal("comprehensive_analysis", new DeterministicCapabilityInterpreter(Registry).Interpret("وضعیت وبملت").CapabilityCandidates[0].CapabilityCode);
    }

    [Fact] // V1 freeze: the shared BuildQuery never selects Feature 154.
    public void V1SharedBuilder_NeverSelectsFeature154()
    {
        foreach (var message in new[] { "ترازنامه وبملت", "وضعیت مالی وبملت", "مقایسه ترازنامه وبملت", "ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱" })
        {
            var query = FinancialStatementTableIntentRules.BuildQuery(message);
            Assert.False(query.UseBalanceSheetReport);
            Assert.Null(query.ComparisonDates);
        }
    }

    [Theory] // F154-R-14: only bare cues continue an active Feature 154 task.
    [InlineData("با گزارش قبلی مقایسه کن", true)]
    [InlineData("با پایان سال مالی قبل مقایسه کن", true)]
    [InlineData("با فصل قبل مقایسه کن", true)]
    [InlineData("با مدت مشابه سال قبل مقایسه کن", true)]
    [InlineData("با ۱۴۰۴/۱۲/۲۹ مقایسه کن", true)]
    [InlineData("نسبت به گزارش قبلی", true)]
    [InlineData("فروش ماهانه وبملت را با سال قبل مقایسه کن", false)]
    [InlineData("روند فروش وبملت در مقایسه با دوره قبل", false)]
    [InlineData("فروش وبملت در ۱۴۰۴/۱۲/۲۹ چقدر بود", false)]
    [InlineData("قیمت وبملت نسبت به هفته قبل", false)]
    [InlineData("با گزارش قبلی مقایسه و تحلیل کن", false)]
    public void OnlyBareCuesContinueTheActiveReport(string message, bool continues) =>
        Assert.Equal(continues, FinancialStatementTableIntentRules.LooksLikeBalanceSheetFollowUpCue(message));

    [Fact] // F154-R-14: no T02 phrase triggers the follow-up override.
    public void T02Phrases_NeverTriggerTheFollowUpOverride()
    {
        foreach (var (phrase, _) in Feature154RoutingBaselineTests.Phrases)
        {
            Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetFollowUpCue(phrase), phrase);
            Assert.False(FinancialStatementTableIntentRules.LooksLikeRepeatForCompany(phrase), phrase);
        }
    }

    [Fact] // PD-27: the renamed starter card behaves exactly like the old one on both V2 paths.
    public async Task RenamedIncomeCard_FollowsTheOldCardRoute()
    {
        const string oldCard = "گزارش سود و زیان فصلی فولاد را بررسی کن";
        const string newCard = "عملکرد مالی فصلی فولاد را بررسی کن";
        Assert.Equal(Feature154RoutingBaselineTests.Capture(oldCard, true) with { Phrase = newCard },
            Feature154RoutingBaselineTests.Capture(newCard, true));
        Assert.True(FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery(newCard));
        Assert.Equal("فولاد", FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(newCard).CompanyQuery);
        Assert.Equal(FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(oldCard) with { UserMessage = newCard },
            FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(newCard));
        Assert.Equal(await Semantic(oldCard), await Semantic(newCard));
        Assert.Equal(("financial_statement_table", false, 0), await Semantic(newCard));
        // The V2 workflow branches checked before the statement-table branch treat both texts alike.
        foreach (var card in new[] { oldCard, newCard })
        {
            Assert.False(MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery(card), card);
            Assert.False(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(card), card);
            Assert.False(MonthlySalesQualityRankingIntentRules.LooksLikeMonthlySalesQualityRankingQuery(card), card);
            Assert.False(DisclosureListingIntentRules.LooksLikeDisclosureListingQuery(card), card);
            Assert.False(MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(card), card);
            Assert.False(ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery(card), card);
            Assert.False(ProductSemanticIntentRules.LooksLikeProductRevenueComposition(card), card);
        }
    }

    [Theory] // PD-27 (owner 2026-11-05): «عملکرد فصلی X را بررسی کن» is the same request as «عملکرد مالی فصلی X را بررسی کن».
    [InlineData("عملکرد فصلی فملی را بررسی کن", "عملکرد مالی فصلی فملی را بررسی کن", "فملی")]
    [InlineData("عملکرد فصلی فولاد را بررسی کن", "عملکرد مالی فصلی فولاد را بررسی کن", "فولاد")]
    public async Task QuarterlyPerformanceShorthand_FollowsTheFinancialPerformanceRoute(string shorthand, string full, string company)
    {
        Assert.Equal(Feature154RoutingBaselineTests.Capture(full, true) with { Phrase = shorthand },
            Feature154RoutingBaselineTests.Capture(shorthand, true));
        Assert.True(FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery(shorthand));
        Assert.Equal(company, FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(shorthand).CompanyQuery);
        Assert.Equal(FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(full) with { UserMessage = shorthand },
            FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(shorthand));
        Assert.Equal(await Semantic(full), await Semantic(shorthand));
        Assert.Equal(("financial_statement_table", false, 0), await Semantic(shorthand));
        Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetReportQuery(shorthand));
        // The shorthand rewrite is quarterly-only: it fixes no statement type for other routes.
        Assert.Null(FinancialStatementTableIntentRules.ResolveV2StatementType(shorthand));
    }

    [Fact] // PD-27 shorthand boundaries: the bare shorthand keeps its report; «تحلیل» keeps analysis.
    public void QuarterlyPerformanceShorthand_KeepsExistingBoundaries()
    {
        Assert.True(FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery("عملکرد فصلی فملی"));
        Assert.Equal("فملی", FinancialStatementTableIntentRules.BuildV2QuarterlyQuery("عملکرد فصلی فملی").CompanyQuery);
        Assert.False(FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery("تحلیل عملکرد فصلی فملی"));
        Assert.False(FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery("عملکرد فصلی و ترازنامه فملی را بررسی کن"));
    }

    [Theory] // PD-27: «تحلیل» keeps analysis; «بررسی» without the quarter cue keeps its route.
    [InlineData("تحلیل صورت سود و زیان فولاد")]
    [InlineData("تحلیل عملکرد مالی فصلی فولاد")]
    [InlineData("صورت سود و زیان فولاد را بررسی کن")]
    public void IncomeAnalysisAndUnscopedReview_AreNotTheQuarterlyReport(string message)
    {
        Assert.False(FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery(message));
        Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetReportQuery(message));
    }

    [Fact] // PD-27: «صورت وضعیت» is the balance sheet only when it does not head another statement name.
    public void StatementOfPositionAlias_DoesNotCaptureOtherStatements()
    {
        Assert.Equal(FinancialStatementType.BalanceSheet, FinancialStatementTableIntentRules.ResolveV2StatementType("صورت وضعیت فاذر"));
        Assert.Equal(FinancialStatementType.CashFlow, FinancialStatementTableIntentRules.ResolveV2StatementType("صورت وضعیت جریان وجوه نقد فولاد"));
        Assert.False(FinancialStatementTableIntentRules.LooksLikeBalanceSheetReportQuery("صورت وضعیت جریان وجوه نقد فولاد"));
        Assert.Null(FinancialStatementTableIntentRules.ResolveV2StatementType("وضعیت فاذر"));
    }
}

using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.AI.SymbolGroundedFallback;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.CashFlowReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using static FinancialCopilot.UnitTests.ReportDiscussion162Tests;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «local-company-data-context» (162 and 163): section projections, the data pack loader, the prompt budget that
/// replaces the "too large" refusal (BUG-042), grounding over all sections and the 162 analysis handler with the pack.
/// No model is called (deterministic fakes).
/// </summary>
public sealed class CompanyDataPack162Tests
{
    private const string Symbol = "فولاد";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    // ── Fixtures ───────────────────────────────────────────────────────────────────────

    private static BalanceSheetReportResult BalanceOf(string symbol = Symbol)
    {
        var balance = Balance();
        return balance with { Company = balance.Company with { Symbol = symbol } };
    }

    private static MonthlyActivityTrendResponse MonthlyOf(string symbol = Symbol) => Monthly() with { CompanySymbol = symbol };

    private static CashFlowCell CashCell(decimal? value, string state = "available") =>
        new(value, state, null, new CashFlowCellProvenance([], [], []));

    internal static CashFlowReportResult CashFlow(string symbol = Symbol, string outcome = "available")
    {
        CashFlowReportColumn Column(string id, string label, DateOnly end, string state = "available") =>
            new(id, "quarter", label, 1404, 2, 3, end, "1404/06/31", "differenced", [], state, null);
        var columns = new[]
        {
            Column("c1", "فصل ۱ سال مالی ۱۴۰۴", new DateOnly(2025, 6, 21)),
            Column("c0", "فصل ۲ سال مالی ۱۴۰۴", new DateOnly(2025, 9, 22)),
            Column("cx", "فصل ۴ سال مالی ۱۴۰۳", new DateOnly(2025, 3, 20), state: "unavailable")
        };
        var rows = new[]
        {
            new CashFlowReportRow("operating_cash_flow", "جریان خالص نقد عملیاتی", "total", [122], null,
                new Dictionary<string, CashFlowCell> { ["c0"] = CashCell(2000m), ["c1"] = CashCell(1500m, "unverified"), ["cx"] = CashCell(900m) }),
            new CashFlowReportRow("investing_cash_flow", "جریان خالص نقد سرمایه‌گذاری", "total", [123], null,
                new Dictionary<string, CashFlowCell> { ["c0"] = CashCell(-500m), ["c1"] = CashCell(null, "not_reported") })
        };
        var kpis = new[]
        {
            new CashFlowReportKpi("cash_conversion", "نسبت تبدیل سود به نقد", "c0", 1.67m, "times", "available", null,
                "operating_cash_flow / net_profit", []),
            new CashFlowReportKpi("capex", "مخارج سرمایه‌ای", "c0", null, "million_irr", "unavailable", "missing", "x", [])
        };
        return new CashFlowReportResult(CashFlowReportContract.Version, Now, outcome, null, null, [], false, null,
            new CashFlowReportCompany(symbol, "فولاد مبارکه", "NoavaranCurrentApi", "ext-1"), new CashFlowReportRequest(symbol),
            new Dictionary<Guid, CashFlowReportStatement>(), null, columns,
            [new CashFlowReportSection("operating", "فعالیت‌های عملیاتی", rows)], null, kpis, null,
            new CashFlowReportVerification("verified", []), [], [], null,
            new CashFlowReportMetadata("پیش‌فرض", "جداگانه", "1404/06/31", 1404, false, false, null, "NoavaranCurrentApi", "million_irr"));
    }

    private static readonly (string Id, string Label)[] IncomeRows =
    [
        ("source_15", "فروش"), ("source_1", "بهای تمام‌شده کالای فروش‌رفته"), ("source_139", "سود (زیان) ناخالص"),
        ("gross_margin", "مارجین ناخالص"), ("source_5", "هزینه‌های عمومی، اداری و تشکیلاتی"),
        ("source_39", "خالص سایر درآمدها (هزینه‌ها)ی عملیاتی"), ("source_140", "سود (زیان) عملیاتی"),
        ("source_12", "هزینه‌های مالی"), ("source_40", "خالص سایر درآمدها و هزینه‌های غیرعملیاتی"),
        ("source_142", "سود (زیان) عملیات در حال تداوم قبل از مالیات"), ("source_334", "مالیات"),
        ("source_278", "سود (زیان) عملیات متوقف‌شده پس از اثر مالیاتی"), ("source_143", "سود (زیان) خالص"),
        ("source_160", "سود هر سهم پس از کسر مالیات"), ("source_153", "تعداد سهام شرکت (هزار سهم)")
    ];

    /// <summary>A فاراک-sized statement: the 15 Feature 144 rows, every quarter, with the report's QoQ comparisons.</summary>
    internal static QuarterlyFinancialReportTableResult LargeQuarterly(int quarters, string symbol = Symbol)
    {
        var fiscal = Enumerable.Range(0, quarters).Select(index =>
        {
            var quarter = 2 - index;
            var year = 1405;
            while (quarter <= 0) { quarter += 4; year--; }
            return new QuarterlyFiscalPeriod(year, quarter, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        }).ToArray();
        var order = 0;
        var rows = IncomeRows.Select(row => new QuarterlyFinancialRow(row.Id, ++order, row.Label, null, null,
            QuarterlyRowPolicy.CumulativeFlow, row.Id == "gross_margin" ? "percent" : "million_rial",
            fiscal.Select((period, index) => (period.Label, new QuarterlyFinancialCell(12_345_678.9m + index * 1000,
                    QuarterlyCellState.Available, null, "million_rial", false, [], [], "test", [])))
                .ToDictionary(item => item.Label, item => item.Item2))).ToArray();
        var comparisons = IncomeRows.SelectMany(row => Enumerable.Range(0, quarters - 1).Select(index =>
            new QuarterlyQoQComparison(row.Id, fiscal[index], fiscal[index + 1], 10m, 1.5m,
                row.Id == "gross_margin" ? 1m : null, QuarterlyComparisonState.Available))).ToList();
        return new QuarterlyFinancialReportTableResult(symbol, "سیمان فارس", "ext", "NoavaranCurrentApi", "IncomeStatement",
            false, quarters, fiscal, rows, comparisons, Now, "Available", null, Now);
    }

    private static ReportDataSet QuarterlyData(QuarterlyFinancialReportTableResult? table = null) =>
        ReportDataSetBuilder.BuildQuarterly(Table(table ?? Quarterly()))!;

    private static GroundedAnalysisOptions Options(bool enabled = true, int sectionSeconds = 3, int totalSeconds = 8) => new()
    {
        Enabled = true,
        DataPack = new GroundedAnalysisDataPackOptions
        {
            Enabled = enabled, SectionTimeoutSeconds = sectionSeconds, TotalTimeoutSeconds = totalSeconds
        }
    };

    /// <summary>Answers each statement query the way the chat path asks it (refinement R1).</summary>
    private sealed class FakeStatements(
        QuarterlyFinancialReportTableResult? quarterly = null,
        BalanceSheetReportResult? balance = null,
        CashFlowReportResult? cashFlow = null,
        Func<FinancialStatementTableQuery, CancellationToken, Task>? before = null) : IFinancialStatementTableQueryUseCase
    {
        public List<string> Asked { get; } = [];

        public async Task<FinancialStatementTableResult?> ExecuteAsync(FinancialStatementTableQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var kind = query.CashFlowReportRequest is not null ? "CashFlow"
                : query.UseBalanceSheetReport ? "BalanceSheet"
                : "IncomeStatement";
            Asked.Add(kind);
            if (before is not null) await before(query, ct);
            return kind switch
            {
                "CashFlow" => cashFlow is null ? null : new FinancialStatementTableResult(null, [], [], [], "r", Now, CashFlowReport: cashFlow),
                "BalanceSheet" => balance is null ? null : Table(balance: balance),
                _ => quarterly is null ? null : Table(quarterly)
            };
        }
    }

    private sealed class FakeMonthly(MonthlyActivityTrendResponse? trend, Exception? failure = null) : IMonthlyActivityTrendQueryUseCase
    {
        public int Calls { get; private set; }

        public Task<MonthlyActivityTrendResponse?> ExecuteAsync(MonthlyActivityTrendQuery query, CancellationToken ct = default)
        {
            Calls++;
            ct.ThrowIfCancellationRequested();
            return failure is not null ? Task.FromException<MonthlyActivityTrendResponse?>(failure) : Task.FromResult(trend);
        }
    }

    private static CompanyAnalysisDataPackLoader Loader(
        FakeStatements? statements = null,
        FakeMonthly? monthly = null,
        bool cashFlowEnabled = true,
        GroundedAnalysisOptions? options = null) =>
        new(statements ?? new FakeStatements(Quarterly(), BalanceOf(), CashFlow()),
            monthly ?? new FakeMonthly(MonthlyOf()), options ?? Options(), cashFlowEnabled);

    // ── Projections (T05–T07) ──────────────────────────────────────────────────────────

    [Fact]
    public void CashFlowProjection_CopiesAvailableAndUnverifiedCells_AndAvailableMetrics()
    {
        var data = ReportDataSetBuilder.BuildCashFlow(CashFlow())!;

        Assert.Equal(ReportFamily.CashFlowStatement, data.Family);
        Assert.Equal(["فصل ۲ سال مالی ۱۴۰۴", "فصل ۱ سال مالی ۱۴۰۴"], data.PeriodsNewestFirst);
        Assert.Equal(3, data.Facts.Count); // the unavailable column and the not-reported cell are skipped
        Assert.Contains(data.Facts, fact => fact.Value == 2000m && fact.PeriodRank == 0 && fact.Unit == "million_irr");
        Assert.Contains(data.Facts, fact => fact.Value == 1500m && fact.Label.EndsWith("(تطبیق کامل ممکن نیست)", StringComparison.Ordinal));
        Assert.DoesNotContain(data.Facts, fact => fact.Value == 900m);
        var metric = Assert.Single(data.Calculations);
        Assert.Equal((1.67m, "times", 0), (metric.Value, metric.Unit, metric.PeriodRank));
    }

    [Fact]
    public void CashFlowProjection_IsNull_WhenTheReportIsNotAvailable()
    {
        Assert.Null(ReportDataSetBuilder.BuildCashFlow(CashFlow(outcome: "no_data")));
        Assert.Null(ReportDataSetBuilder.BuildCashFlow(null));
    }

    [Fact]
    public void BalanceSheetAndMonthlyProjections_FromPlainResults_MatchTheActiveReportProjections()
    {
        var balance = ReportDataSetBuilder.BuildBalanceSheet(BalanceOf())!;
        var fromPayload = ReportDataSetBuilder.Build(ActiveReportResolver.TryDescribe(Guid.NewGuid(), Payload(Table(balance: BalanceOf())))!)!;
        Assert.Equal(fromPayload.Facts.Select(fact => (fact.Label, fact.Value)), balance.Facts.Select(fact => (fact.Label, fact.Value)));

        var monthly = ReportDataSetBuilder.BuildMonthly(MonthlyOf())!;
        Assert.Equal(ReportFamily.MonthlySales, monthly.Family);
        Assert.Contains(monthly.Facts, fact => fact.Label == "فروش ماهانه" && fact.Value == 5000m);
        Assert.Null(ReportDataSetBuilder.BuildMonthly(Monthly() with { LatestMonthlySalesAmount = null, ChartPoints = [] }));
    }

    // ── Loader (T08–T10) ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Loader_SendsEveryAvailableSection_PrimaryFirst_WithoutReadingThePrimaryAgain()
    {
        var statements = new FakeStatements(Quarterly(), BalanceOf(), CashFlow());
        var pack = await Loader(statements).LoadAsync(QuarterlyData(), CancellationToken.None);

        Assert.Equal(["IncomeStatement", "BalanceSheet", "CashFlow", "MonthlyActivity"], pack.SectionNames);
        Assert.True(pack.Primary.Primary);
        Assert.Empty(pack.NotAvailable);
        Assert.Equal(["BalanceSheet", "CashFlow"], statements.Asked); // FR-4: the shown income statement is not read
        Assert.All(pack.Reads, read => Assert.Equal("sent", read.Outcome));
    }

    [Fact]
    public async Task Loader_ListsCashFlowAsDisabled_WhenTheFeatureIsOff()
    {
        var statements = new FakeStatements(Quarterly(), BalanceOf(), CashFlow());
        var pack = await Loader(statements, cashFlowEnabled: false).LoadAsync(QuarterlyData(), CancellationToken.None);

        Assert.DoesNotContain("CashFlow", pack.SectionNames);
        Assert.Contains(new CompanyDataGap(CompanyDataSectionKind.CashFlow, CompanyDataGapReasons.Disabled), pack.NotAvailable);
        Assert.DoesNotContain("CashFlow", statements.Asked);
    }

    [Fact]
    public async Task Loader_ListsMonthlyAsNoData_ForACompanyWithoutMonthlyReports()
    {
        var pack = await Loader(monthly: new FakeMonthly(null)).LoadAsync(QuarterlyData(), CancellationToken.None);

        Assert.DoesNotContain("MonthlyActivity", pack.SectionNames);
        Assert.Contains(new CompanyDataGap(CompanyDataSectionKind.MonthlyActivity, CompanyDataGapReasons.NoData), pack.NotAvailable);
    }

    [Fact]
    public async Task Loader_LeavesOutAFailingSection_AndKeepsTheOthers()
    {
        var pack = await Loader(monthly: new FakeMonthly(null, new InvalidOperationException("db down")))
            .LoadAsync(QuarterlyData(), CancellationToken.None);

        Assert.Contains(new CompanyDataGap(CompanyDataSectionKind.MonthlyActivity, CompanyDataGapReasons.Error), pack.NotAvailable);
        Assert.Contains("BalanceSheet", pack.SectionNames);
    }

    [Fact]
    public async Task Loader_LeavesOutASlowSection_AsTimeout()
    {
        var statements = new FakeStatements(Quarterly(), BalanceOf(), CashFlow(), before: async (query, ct) =>
        {
            if (query.UseBalanceSheetReport) await Task.Delay(Timeout.Infinite, ct);
        });
        var pack = await Loader(statements, options: Options(sectionSeconds: 1, totalSeconds: 5))
            .LoadAsync(QuarterlyData(), CancellationToken.None);

        Assert.Contains(new CompanyDataGap(CompanyDataSectionKind.BalanceSheet, CompanyDataGapReasons.Timeout), pack.NotAvailable);
        Assert.Contains("CashFlow", pack.SectionNames);
    }

    [Fact]
    public async Task Loader_RejectsAnotherCompanysSection()
    {
        var pack = await Loader(new FakeStatements(Quarterly(), BalanceOf("فملی"), CashFlow()))
            .LoadAsync(QuarterlyData(), CancellationToken.None);

        Assert.Contains(new CompanyDataGap(CompanyDataSectionKind.BalanceSheet, CompanyDataGapReasons.OtherCompany), pack.NotAvailable);
        Assert.DoesNotContain("BalanceSheet", pack.SectionNames);
    }

    [Fact]
    public async Task Loader_PropagatesTheRequestsCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Loader().LoadAsync(QuarterlyData(), cancelled.Token));
    }

    [Fact]
    public async Task Loader_Disabled_ReturnsOnlyThePrimaryReport()
    {
        var statements = new FakeStatements(Quarterly(), BalanceOf(), CashFlow());
        var pack = await Loader(statements, options: Options(enabled: false)).LoadAsync(QuarterlyData(), CancellationToken.None);

        Assert.Equal(["IncomeStatement"], pack.SectionNames);
        Assert.Empty(statements.Asked);
    }

    [Fact]
    public async Task Loader_ForAShownBalanceSheet_ReadsTheIncomeStatement()
    {
        var statements = new FakeStatements(Quarterly() with { CompanySymbol = Symbol }, BalanceOf(), CashFlow());
        var pack = await Loader(statements).LoadAsync(ReportDataSetBuilder.BuildBalanceSheet(BalanceOf())!, CancellationToken.None);

        Assert.Equal(["BalanceSheet", "IncomeStatement", "CashFlow", "MonthlyActivity"], pack.SectionNames);
    }

    // ── Evidence and grounding (T09, T15) ──────────────────────────────────────────────

    [Fact]
    public async Task Renumber_GivesUniqueIdsAcrossSections_AndRemapsOperands()
    {
        var pack = await Loader().LoadAsync(QuarterlyData(), CancellationToken.None);
        var sections = CompanyDataPackEvidence.Renumber(pack.Sections);
        var facts = sections.SelectMany(section => section.Data.Facts).ToArray();
        var calculations = sections.SelectMany(section => section.Data.Calculations).ToArray();

        Assert.Equal(facts.Length, facts.Select(fact => fact.EvidenceId).Distinct().Count());
        Assert.Equal(calculations.Length, calculations.Select(calculation => calculation.EvidenceId).Distinct().Count());
        var ids = facts.Select(fact => fact.EvidenceId).ToHashSet();
        Assert.All(calculations.SelectMany(calculation => calculation.OperandEvidenceIds), operand => Assert.Contains(operand, ids));
    }

    [Fact]
    public async Task Grounding_AcceptsAFigureOfASupportingSection_AndRejectsAnInventedOne()
    {
        var pack = await Loader().LoadAsync(QuarterlyData(), CancellationToken.None);
        var prompt = new ReportDiscussionContextBuilder(new ReportDiscussionOptions())
            .BuildAnalysis(pack, [], "تحلیل کن", new AnalysisChoice(AnalysisDepth.Deep, true), 40000, 120000)!;

        // Total assets of the balance sheet (1,000 million rial = 0.1 billion toman) and the cash-flow ratio 1.67.
        Assert.True(NumericGroundingValidator.Validate("جمع کل دارایی‌ها ۰٫۱ میلیارد تومان و نسبت تبدیل ۱٫۶۷ برابر است.",
            prompt.Data, "تحلیل کن").Passed);
        Assert.False(NumericGroundingValidator.Validate("فروش ۷۷۷ میلیارد تومان بود.", prompt.Data, "تحلیل کن").Passed);
        // Period keys are references, not numbers.
        Assert.True(NumericGroundingValidator.Validate("در P0 و P1 جمع کل دارایی‌ها ۰٫۱ میلیارد تومان است.", prompt.Data, "تحلیل کن").Passed);
    }

    // ── Prompt block and budget (T11, T13, T14; BUG-042) ───────────────────────────────

    [Fact]
    public async Task Render_HasOneSectionPerReport_PeriodKeys_AndTheNotAvailableList()
    {
        var pack = await Loader(cashFlowEnabled: false).LoadAsync(QuarterlyData(), CancellationToken.None);
        var block = CompanyDataPrompt.Render(CompanyDataPackEvidence.Renumber(pack.Sections), pack.NotAvailable);

        Assert.StartsWith("<company_data>\n", block, StringComparison.Ordinal);
        Assert.Contains("<section kind=\"IncomeStatement\" title=\"صورت سود و زیان فصلی\" primary=\"true\">", block);
        Assert.Contains("<section kind=\"BalanceSheet\"", block);
        Assert.Contains("<section kind=\"MonthlyActivity\"", block);
        Assert.Contains("P0 = فصل 2 سال مالی 1404; P1 = فصل 1 سال مالی 1404", block);
        Assert.Contains("<not_available>\nCashFlow: disabled\n</not_available>", block);
        Assert.DoesNotContain("| فصل 2 سال مالی 1404 |", block); // fact lines carry the key, not the label
    }

    [Theory]
    [InlineData(AnalysisDepth.Simple, 24000)]
    [InlineData(AnalysisDepth.Deep, 40000)]
    public async Task Budget_AnswersALargeStatement_AtBothDepths(AnalysisDepth depth, int target)
    {
        // 12 quarters of 15 rows: about 27,000 estimated tokens alone, refused at Simple before this change (BUG-042).
        var statements = new FakeStatements(LargeQuarterly(12), BalanceOf(), CashFlow());
        var pack = await Loader(statements).LoadAsync(QuarterlyData(LargeQuarterly(12)), CancellationToken.None);
        var prompt = new ReportDiscussionContextBuilder(new ReportDiscussionOptions())
            .BuildAnalysis(pack, [], "صورت سود و زیان را تفسیر کن", new AnalysisChoice(depth, true), target, 120000);

        Assert.NotNull(prompt);
        Assert.True(prompt!.EstimatedInputTokens <= target);
        Assert.Equal("IncomeStatement", prompt.DataFacts!.SectionsSent[0]);
    }

    [Fact]
    public async Task Budget_FitsTheWholePackOfAFiveQuarterStatement_WithoutTrimming()
    {
        var pack = await Loader(new FakeStatements(LargeQuarterly(5), BalanceOf(), CashFlow()))
            .LoadAsync(QuarterlyData(LargeQuarterly(5)), CancellationToken.None);
        var prompt = new ReportDiscussionContextBuilder(new ReportDiscussionOptions())
            .BuildAnalysis(pack, [], "تحلیل کن", new AnalysisChoice(AnalysisDepth.Simple, true), 24000, 120000)!;

        Assert.Equal(1, prompt.DataFacts!.DegradationStep);
        Assert.Equal(5, prompt.Data.Facts.Where(fact => fact.Label == "فروش").Count());
        Assert.Equal(["IncomeStatement", "BalanceSheet", "CashFlow", "MonthlyActivity"], prompt.DataFacts.SectionsSent);
    }

    [Fact]
    public async Task Budget_DegradesStepByStep_AndNeverRefusesBelowTheCeiling()
    {
        var pack = await Loader(new FakeStatements(LargeQuarterly(8), BalanceOf(), CashFlow()))
            .LoadAsync(QuarterlyData(LargeQuarterly(8)), CancellationToken.None);
        var builder = new ReportDiscussionContextBuilder(new ReportDiscussionOptions());
        var choice = new AnalysisChoice(AnalysisDepth.Simple, true);

        // A target below the system text: nothing fits, so the smallest state is sent (primary newest period only).
        var smallest = builder.BuildAnalysis(pack, [], "تحلیل کن", choice, 4000, 120000)!;
        Assert.Equal(5, smallest.DataFacts!.DegradationStep);
        Assert.Equal(["IncomeStatement"], smallest.DataFacts.SectionsSent);
        Assert.Contains(smallest.DataFacts.NotAvailable, gap => gap.Reason == CompanyDataGapReasons.Trimmed);
        Assert.All(smallest.Data.Facts, fact => Assert.Equal(0, fact.PeriodRank));

        // Only the hard ceiling refuses.
        Assert.Null(builder.BuildAnalysis(pack, [], "تحلیل کن", choice, 4000, 4000));
    }

    [Fact]
    public async Task Budget_TrimsSupportingSectionsBeforeThePrimary()
    {
        var pack = await Loader(new FakeStatements(LargeQuarterly(12), BalanceOf(), CashFlow()))
            .LoadAsync(QuarterlyData(LargeQuarterly(5)), CancellationToken.None);
        var builder = new ReportDiscussionContextBuilder(new ReportDiscussionOptions());
        var full = builder.BuildAnalysis(pack, [], "تحلیل کن", new AnalysisChoice(AnalysisDepth.Deep, true), 120000, 120000)!;

        var tighter = builder.BuildAnalysis(pack, [], "تحلیل کن", new AnalysisChoice(AnalysisDepth.Deep, true),
            full.EstimatedInputTokens - 1, 120000)!;
        Assert.True(tighter.DataFacts!.DegradationStep >= 2);
        Assert.Equal(5, tighter.Data.Facts.Count(fact => fact.Label == "فروش" && fact.Period.StartsWith("فصل", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task FallbackBuilder_SendsThePack_ForAConfirmedRun()
    {
        var pack = await Loader().LoadAsync(QuarterlyData(), CancellationToken.None);
        var builder = new SymbolGroundedFallbackPromptBuilder(new SymbolGroundedFallbackOptions(), new SymbolGroundedFallbackOffers(true));

        var prompt = builder.BuildAnalysis(pack, "وضعیت فولاد چطور است؟", new AnalysisChoice(AnalysisDepth.Simple, false), 24000, 120000)!;

        Assert.Contains("<company_data>", prompt.UserContent);
        Assert.Contains("<offers>", prompt.UserContent);
        Assert.Equal(["IncomeStatement", "BalanceSheet", "CashFlow", "MonthlyActivity"], prompt.DataFacts!.SectionsSent);
        Assert.DoesNotContain("<report>", prompt.SystemPrompt);
    }

    [Theory] // AC-8 for 163: a فاراک-sized statement is answered at both depths, never over_input_budget below the ceiling.
    [InlineData(AnalysisDepth.Simple, 24000)]
    [InlineData(AnalysisDepth.Deep, 40000)]
    public async Task FallbackBuilder_AnswersALargeStatement_AtBothDepths(AnalysisDepth depth, int target)
    {
        var pack = await Loader(new FakeStatements(LargeQuarterly(12), BalanceOf(), CashFlow()))
            .LoadAsync(QuarterlyData(LargeQuarterly(12)), CancellationToken.None);
        var builder = new SymbolGroundedFallbackPromptBuilder(new SymbolGroundedFallbackOptions(), new SymbolGroundedFallbackOffers(true));

        var prompt = builder.BuildAnalysis(pack, "وضعیت فولاد چطور است؟", new AnalysisChoice(depth, true), target, 120000);

        Assert.NotNull(prompt);
        Assert.True(prompt!.EstimatedInputTokens <= target);
        Assert.Equal("IncomeStatement", prompt.DataFacts!.SectionsSent[0]);
    }

    // ── Telemetry (T20, AC-11) ─────────────────────────────────────────────────────────

    private sealed class LogCapture<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public async Task Telemetry_LogsSectionsGapsStepAndCodalCount_WithoutValues()
    {
        var pack = await Loader(new FakeStatements(LargeQuarterly(5), BalanceOf(), CashFlow()), cashFlowEnabled: false)
            .LoadAsync(QuarterlyData(LargeQuarterly(5)), CancellationToken.None);
        var prompt = new ReportDiscussionContextBuilder(new ReportDiscussionOptions())
            .BuildAnalysis(pack, [], "تحلیل کن", new AnalysisChoice(AnalysisDepth.Deep, true), 40000, 120000)!;
        var logger = new LogCapture<FinancialCopilot.Infrastructure.AI.GroundedAnalysis.GroundedAnalysisTelemetry>();
        var telemetry = new FinancialCopilot.Infrastructure.AI.GroundedAnalysis.GroundedAnalysisTelemetry(
            Microsoft.Extensions.Options.Options.Create(new FinancialCopilot.Infrastructure.Billing.UsageBasedCreditBillingOptions()),
            new GroundedAnalysisOptions(), new FinancialCopilot.Billing.Services.UsageBasedCreditCalculator(), logger);
        ExternalSource[] sources =
        [
            new("W1", "اطلاعیه", "https://codal.ir/Reports/Decision.aspx?LetterSerial=1", "1404/08/10"),
            new("W2", "اطلاعیه", "https://www.codal.ir/x", "1404/08/10"),
            new("W3", "خبر", "https://codal.ir.example.com/x", "1404/08/10"),
            new("W4", "خبر", "https://example.com/codal.ir", "1404/08/10")
        ];

        telemetry.RecordDataPack("162", prompt.DataFacts, sources, "corr-1");

        var line = Assert.Single(logger.Messages);
        Assert.Contains("Feature=162", line);
        Assert.Contains("SectionsSent=IncomeStatement,BalanceSheet,MonthlyActivity", line);
        Assert.Contains("NotAvailable=CashFlow:disabled", line);
        Assert.Contains($"DegradationStep={prompt.DataFacts!.DegradationStep}", line);
        Assert.Contains("CodalCitations=2", line);
        Assert.Contains("CorrelationId=corr-1", line);
        // Kinds, reasons, counts and times only: no label or value of any section reaches the log.
        Assert.DoesNotContain("فروش", line);
        Assert.All(prompt.Data.Facts.Where(fact => Math.Abs(fact.Value) >= 1000),
            fact => Assert.DoesNotContain(fact.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), line));
    }

    // ── 162 handler with the pack (T16, T21) ───────────────────────────────────────────

    private sealed class FakeAnalysisModel(ReportDiscussionDisposition decision, string answer) : IReportDiscussionAnalysisModel
    {
        public ReportDiscussionPrompt? LastAnalysisPrompt { get; private set; }

        public Task<ReportDiscussionDisposition> DecideAsync(ReportDiscussionPrompt prompt, Guid tenantId, string correlationId,
            CancellationToken cancellationToken) => Task.FromResult(decision);

        public Task<ReportDiscussionAnalysisReply> AnalyzeAsync(ReportDiscussionPrompt prompt, AnalysisChoice choice, Guid tenantId,
            string correlationId, CancellationToken cancellationToken)
        {
            LastAnalysisPrompt = prompt;
            return Task.FromResult(new ReportDiscussionAnalysisReply(ReportDiscussionDisposition.Answered, answer, ["F1"], null, [], []));
        }
    }

    private sealed class UnusedModel : IReportDiscussionModel
    {
        public Task<ReportDiscussionModelReply> AnswerAsync(ReportDiscussionPrompt prompt, Guid tenantId, string correlationId,
            CancellationToken cancellationToken) => throw new InvalidOperationException("the legacy path must not run");
    }

    private static readonly GroundedAnalysisPricing Pricing = new(new GroundedAnalysisPricingOptions
    {
        Credits = new GroundedAnalysisCreditsOptions { SimpleNoWeb = 8, SimpleWeb = 12, DeepNoWeb = 16, DeepWeb = 25 }
    });

    private static (ReportDiscussionHandler Handler, FakeAnalysisModel Model, FakeStatements Statements, FakeMonthly Monthly) AnalysisHandler(
        ReportDiscussionDisposition decision, string answer, bool dataPackEnabled = true)
    {
        var quarterly = Quarterly();
        var statements = new FakeStatements(quarterly, BalanceOf(), CashFlow());
        var monthly = new FakeMonthly(MonthlyOf());
        var options = Options(enabled: dataPackEnabled);
        var model = new FakeAnalysisModel(decision, answer);
        var handler = new ReportDiscussionHandler(
            new FakeReader(History(Payload(Table(quarterly)))), new FakeTaskState(null), new UnusedModel(),
            new ReportDiscussionOptions { Enabled = true }, null, model, options, Pricing,
            new CompanyAnalysisDataPackLoader(statements, monthly, options, cashFlowEnabled: true));
        return (handler, model, statements, monthly);
    }

    private static AiQueryRequest AnalysisRequest() =>
        new("صورت سود و زیان فولاد را تفسیر کن", Guid.NewGuid(), Guid.NewGuid(), "corr-pack", Guid.NewGuid());

    [Fact]
    public async Task Handler_SendsThePack_AndAnswersFromASupportingSection_AtTheFixedTier()
    {
        var (handler, model, _, _) = AnalysisHandler(ReportDiscussionDisposition.Answered,
            "**جمع کل دارایی‌ها ۰٫۱ میلیارد تومان است.**\n\n- **نقد:** نسبت تبدیل سود به نقد ۱٫۶۷ برابر است.");
        decimal? reserved = null;

        var turn = await handler.TryHandleAsync(AnalysisRequest(), Guid.NewGuid(), false, CancellationToken.None,
            (credits, _) => { reserved = credits; return Task.CompletedTask; });

        Assert.Equal(ReportDiscussionTurnKind.Reply, turn.Kind);
        Assert.Equal(DialogueOutcome.Answered, turn.Outcome);
        Assert.Equal(25m, reserved); // defaults Deep + web: the fixed tier is unchanged
        Assert.Equal(GroundedAnalysisBillingPolicy.AnsweredStatus(new AnalysisChoice(AnalysisDepth.Deep, true)), turn.BillingCompletionStatus);
        Assert.Contains("<section kind=\"BalanceSheet\"", model.LastAnalysisPrompt!.UserContent);
        Assert.Contains("<section kind=\"CashFlow\"", model.LastAnalysisPrompt.UserContent);
        Assert.Contains("<section kind=\"MonthlyActivity\"", model.LastAnalysisPrompt.UserContent);
        Assert.Equal(["IncomeStatement", "BalanceSheet", "CashFlow", "MonthlyActivity"], turn.AnalysisRun!.SectionsUsed);
        Assert.Equal(1, turn.Diagnostics!.DataFacts!.DegradationStep);
    }

    [Theory]
    [InlineData(ReportDiscussionDisposition.NeedsDifferentData)]
    [InlineData(ReportDiscussionDisposition.OutsideReport)]
    public async Task Handler_HandBack_ReadsNoSection(ReportDiscussionDisposition decision)
    {
        var (handler, model, statements, monthly) = AnalysisHandler(decision, "");

        var turn = await handler.TryHandleAsync(AnalysisRequest(), Guid.NewGuid(), false, CancellationToken.None);

        Assert.Equal(ReportDiscussionTurnKind.HandBack, turn.Kind);
        Assert.Empty(statements.Asked);
        Assert.Equal(0, monthly.Calls);
        Assert.Null(model.LastAnalysisPrompt);
    }

    [Fact]
    public async Task Handler_WithTheDataPackOff_SendsOnlyTheShownReport()
    {
        var (handler, model, statements, _) = AnalysisHandler(ReportDiscussionDisposition.Answered,
            "**فروش ۰٫۱ میلیارد تومان بوده است.**", dataPackEnabled: false);

        await handler.TryHandleAsync(AnalysisRequest(), Guid.NewGuid(), false, CancellationToken.None);

        Assert.Empty(statements.Asked);
        Assert.Contains("<company_data>", model.LastAnalysisPrompt!.UserContent);
        Assert.DoesNotContain("<section kind=\"BalanceSheet\"", model.LastAnalysisPrompt.UserContent);
    }
}

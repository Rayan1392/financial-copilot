using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace FinancialCopilot.UnitTests;

public sealed class SymbolSnapshot156Tests
{
    private const string Symbol = "اخابر";

    private static readonly ConversationalCapabilityRegistry Registry = new(InitialConversationalCapabilityCatalog.Create());
    private static readonly EligibleSymbol Eligible = new(Symbol, "مخابرات ایران", "بورس", "مخابرات");

    // ---- Detection (AC-01, AC-02, AC-03) ----

    [Theory]
    [InlineData("اخابر", "اخابر")]
    [InlineData("نماد اخابر", "اخابر")]
    [InlineData("سهم اخابر", "اخابر")]
    [InlineData("سهام اخابر", "اخابر")]
    [InlineData("اخابر؟", "اخابر")]
    [InlineData("  اخابر!  ", "اخابر")]
    [InlineData("كگل", "کگل")]
    [InlineData("وب‌ملت", "وبملت")]
    [InlineData("وب ملت", "وبملت")]
    [InlineData("نماد", "نماد")]
    public void ExtractCompactKey_ReturnsTheTickerKey(string message, string expected) =>
        Assert.Equal(expected, SymbolSnapshotIntentRules.ExtractCompactKey(message));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("؟؟")]
    public void ExtractCompactKey_RejectsEmptyMessages(string? message) =>
        Assert.Null(SymbolSnapshotIntentRules.ExtractCompactKey(message));

    [Fact]
    public void ExtractCompactKey_RejectsLongMessages() =>
        Assert.Null(SymbolSnapshotIntentRules.ExtractCompactKey(new string('ا', SymbolSnapshotIntentRules.MaxMessageLength + 1)));

    [Theory]
    [InlineData("اخابر")]
    [InlineData("نماد اخابر")]
    [InlineData("اخابر؟")]
    public async Task Gate_RoutesABareTickerToTheSnapshot(string message)
    {
        var (result, _, _) = await PrepareAsync(message);

        Assert.Equal(SymbolSnapshotCapability.Code, result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal("SemanticPrimary", result.Request.SemanticRoutingDisposition);
        Assert.Equal(Symbol, result.Request.SemanticFrame!.Value(QuerySlotType.CompanyOrSymbol));
    }

    [Fact]
    public async Task Gate_LeavesTheCompanyInTheConversationState()
    {
        var (result, _, _) = await PrepareAsync(Symbol);

        Assert.Equal(SymbolSnapshotCapability.Code, result.Transition?.Current?.ActiveCapability);
        Assert.Contains(result.Slots, slot => slot.Type == QuerySlotType.CompanyOrSymbol && slot.Value == Symbol);
    }

    [Theory]
    [InlineData("سلام")]
    [InlineData("اخابر سود")]
    [InlineData("اخابر کگل")]
    [InlineData("فولاد")]
    public async Task Gate_LeavesOtherMessagesOnTheirRoute(string message)
    {
        var (result, _, _) = await PrepareAsync(message);

        Assert.NotEqual(SymbolSnapshotCapability.Code, result.Request.SemanticFrame?.CapabilityCode);
    }

    // ---- Guards (AC-04, AC-05, AC-06) ----

    [Fact]
    public async Task Gate_DoesNotInterceptATickerThatAnswersAPendingQuestion()
    {
        var (result, _, _) = await PrepareAsync(Symbol, pendingCapability: "monthly_activity_trend");

        Assert.Equal("monthly_activity_trend", result.Request.SemanticFrame?.CapabilityCode);
    }

    [Fact]
    public async Task Gate_WithTheFlagOff_LeavesABareTickerOnItsRoute()
    {
        var (result, _, calls) = await PrepareAsync(Symbol, enabled: false);

        Assert.NotEqual(SymbolSnapshotCapability.Code, result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal(1, calls.Calls);
    }

    [Fact]
    public async Task Gate_DoesNotCallTheModelForADetectedTicker()
    {
        var (_, _, calls) = await PrepareAsync(Symbol);

        Assert.Equal(0, calls.Calls);
    }

    [Fact]
    public void Registry_HidesTheCapabilityFromInterpretersAndGuidance()
    {
        Assert.DoesNotContain(Registry.GetEnabled(), definition => definition.Code == SymbolSnapshotCapability.Code);
        Assert.NotNull(Registry.Find(SymbolSnapshotCapability.Code));
    }

    [Fact]
    public void DetectedIntent_KeepsThePersistedOrdinals()
    {
        Assert.Equal(15, (int)DetectedIntent.Unknown);
        Assert.Equal(16, (int)DetectedIntent.SymbolSnapshot);
    }

    // ---- Use case (AC-07 to AC-11) ----

    [Fact]
    public async Task UseCase_ReturnsTheSourceValues()
    {
        var result = await RunUseCaseAsync();

        Assert.True(result.IsComplete());
        Assert.Equal(3450m, result.Price.LatestPrice);
        Assert.Equal(2.1m, result.Price.DailyChangePercent);
        Assert.Equal("1405/08/18", result.Price.TradingDateJalali);
        Assert.Equal("مهر 1405", result.MonthlySales.PeriodLabelFa);
        Assert.Equal(1234m, result.MonthlySales.SalesAmount);
        Assert.Equal(4m, result.MonthlySales.MonthOverMonthPercent);
        Assert.Equal(31m, result.MonthlySales.YearOverYearPercent);
        Assert.Equal("فصل دوم 1405", result.QuarterlyReport.PeriodLabelFa);
        Assert.False(result.QuarterlyReport.IsAudited);
        Assert.Equal([15m * 10, 139m * 10, 143m * 10, 160m * 10], result.QuarterlyReport.Rows!.Select(row => row.Value!.Value));
    }

    [Fact]
    public async Task UseCase_ComputesYearOverYearAgainstTheSameFiscalQuarter()
    {
        var result = await RunUseCaseAsync();

        Assert.Equal(100m, result.QuarterlyReport.Rows![0].YearOverYearPercent);
    }

    [Fact]
    public async Task UseCase_ShowsNoPercentWhenTheSignChangedOrTheBaseIsNotPositive()
    {
        var signChanged = await RunUseCaseAsync(quarterly: Quarterly(baseValue: -5m));
        var zeroBase = await RunUseCaseAsync(quarterly: Quarterly(baseValue: 0m));

        Assert.Null(signChanged.QuarterlyReport.Rows![0].YearOverYearPercent);
        Assert.Null(zeroBase.QuarterlyReport.Rows![0].YearOverYearPercent);
    }

    [Fact]
    public async Task UseCase_UsesTheBankingRowsForABank()
    {
        var result = await RunUseCaseAsync(quarterly: Quarterly(banking: true));

        Assert.Equal(["ردیف 306", "ردیف 307", "ردیف 143", "ردیف 160"], result.QuarterlyReport.Rows!.Select(row => row.LabelFa));
    }

    [Theory]
    [InlineData("price")]
    [InlineData("monthly")]
    [InlineData("quarterly")]
    public async Task UseCase_KeepsTheOtherSectionsWhenOneFails(string failing)
    {
        var result = await RunUseCaseAsync(failing: failing);

        Assert.Equal(failing == "price" ? SymbolSnapshotSectionStatus.Unavailable : SymbolSnapshotSectionStatus.Available, result.Price.Status);
        Assert.Equal(failing == "monthly" ? SymbolSnapshotSectionStatus.Unavailable : SymbolSnapshotSectionStatus.Available, result.MonthlySales.Status);
        Assert.Equal(failing == "quarterly" ? SymbolSnapshotSectionStatus.Unavailable : SymbolSnapshotSectionStatus.Available, result.QuarterlyReport.Status);
        Assert.True(result.HasData());
        Assert.False(result.IsComplete());
    }

    [Fact]
    public async Task UseCase_MarksATimedOutSectionUnavailable()
    {
        var result = await RunUseCaseAsync(hanging: "monthly", options: new SymbolSnapshotOptions
        {
            SectionTimeoutMilliseconds = 20,
            TurnBudgetMilliseconds = 2000
        });

        Assert.Equal(SymbolSnapshotSectionStatus.Unavailable, result.MonthlySales.Status);
        Assert.Equal(SymbolSnapshotSectionStatus.Available, result.QuarterlyReport.Status);
    }

    [Fact]
    public async Task UseCase_StopsWhenTheCallerCancels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildUseCase(hanging: "price")
            .ExecuteAsync(Symbol, "correlation", cancelled.Token));
    }

    [Fact]
    public async Task Executor_ReturnsNoDataWithTheResultWhenAllSectionsAreEmpty()
    {
        var execution = await ExecuteAsync(new FakeSources { Empty = true });

        Assert.Equal(CapabilityExecutionStatus.NoData, execution.Status);
        Assert.Equal("NoData", SemanticBillingCompletionStatus.For(execution.Status));
        Assert.IsType<SymbolSnapshotResult>(execution.Payload);
    }

    [Fact]
    public async Task Executor_ChargesForAPartialCard()
    {
        var execution = await ExecuteAsync(new FakeSources { Failing = "monthly" });

        Assert.Equal(CapabilityExecutionStatus.Partial, execution.Status);
        Assert.Equal("Completed", SemanticBillingCompletionStatus.For(execution.Status));
    }

    // ---- Provider mode (AC-14) ----

    [Fact]
    public async Task UseCase_InCyclicalWavesMode_MarksMonthlyUnavailableAndStaysComplete()
    {
        var availability = new ChatCapabilityAvailability(FinancialDataChatProvider.CyclicalWaves);
        var sources = new FakeSources();
        var result = await new SymbolSnapshotUseCase(new FakeEligible(), sources, sources, sources, new SymbolSnapshotOptions(), availability)
            .ExecuteAsync(Symbol, "correlation", CancellationToken.None);

        Assert.Equal(SymbolSnapshotSectionStatus.UnavailableForProvider, result!.MonthlySales.Status);
        Assert.True(result.IsComplete());
        Assert.DoesNotContain(
            new SymbolSnapshotFollowUpSuggestionService(Registry, new DeterministicCapabilityInterpreter(Registry), availability).Build(result),
            action => action.CapabilityCode == "monthly_activity_trend");
    }

    // ---- Buttons (AC-13) ----

    [Fact]
    public async Task Suggestions_AreOrderedLimitedAndRouteToTheirCapabilities()
    {
        var actions = BuildSuggestions(await RunUseCaseAsync());

        Assert.Equal(["روند فروش ماهانه", "گزارش فصلی کامل", "ترازنامه", "ترکیب فروش محصولات"], actions.Select(action => action.LocalizedLabel));
    }

    [Fact]
    public async Task Suggestions_DropButtonsOfMissingSections()
    {
        var actions = BuildSuggestions(await RunUseCaseAsync(failing: "monthly"));

        Assert.Equal(["گزارش فصلی کامل", "ترازنامه"], actions.Select(action => action.LocalizedLabel));
    }

    // ---- Telegram (AC-15) ----

    [Fact]
    public async Task Telegram_ShowsTheThreeSectionsAndSuggestions()
    {
        var result = await RunUseCaseAsync();
        var response = new AiQueryResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.SymbolSnapshot,
            null, null, null, null, null, SymbolSnapshotTextComposer.Compose(result), false, null, null,
            SuggestedActions: BuildSuggestions(result), SymbolSnapshotResult: result);

        var messages = new TelegramAssistantResponseRenderer(new UnusedChartRenderer(), NullLogger<TelegramAssistantResponseRenderer>.Instance)
            .Render(response, "fa-IR");
        var text = string.Join("\n", messages.Select(message => message.Text));

        Assert.Contains("آخرین قیمت", text);
        Assert.Contains("فروش ماهانه", text);
        Assert.Contains("گزارش فصلی", text);
        Assert.Contains("روند فروش ماهانه", text);
    }

    [Fact]
    public async Task TextComposer_ShowsOnlyTheIntroWhenThereIsNoData()
    {
        var result = await RunUseCaseAsync(new FakeSources { Empty = true });

        Assert.Equal(result.IntroFa, SymbolSnapshotTextComposer.Compose(result));
    }

    // ---- Eligible symbols ----

    [Fact]
    public async Task Resolver_MatchesATickerAndReadsItsMarketAndGroup()
    {
        await using var db = NewDb();
        var market = new NormalizedMarketRow { Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalId = "m", Name = "بورس" };
        var group = new NormalizedIndustryGroupRow { Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalId = "g", Name = "مخابرات" };
        db.Markets.Add(market);
        db.IndustryGroups.Add(group);
        db.NoavaranEligibleCompanies.Add(Company(Symbol, "مخابرات ایران", market.Id, group.Id));
        await db.SaveChangesAsync();

        var result = await NewResolver(db).ResolveAsync(Symbol, CancellationToken.None);

        Assert.Equal(new EligibleSymbol(Symbol, "مخابرات ایران", "بورس", "مخابرات"), result);
    }

    [Fact]
    public async Task Resolver_NormalizesStoredSymbols()
    {
        await using var db = NewDb();
        db.NoavaranEligibleCompanies.Add(Company("كگل", "گل گهر"));
        await db.SaveChangesAsync();

        Assert.NotNull(await NewResolver(db).ResolveAsync("کگل", CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_NeverMatchesAKeySharedByTwoCompanies()
    {
        await using var db = NewDb();
        db.NoavaranEligibleCompanies.AddRange(Company("تست", "الف"), Company("تست", "ب"));
        await db.SaveChangesAsync();

        Assert.Null(await NewResolver(db).ResolveAsync("تست", CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_ReturnsNullWhenTheLookupFails()
    {
        var db = NewDb();
        await db.DisposeAsync();

        Assert.Null(await NewResolver(db).ResolveAsync(Symbol, CancellationToken.None));
    }

    // ---- Conversation history (AC-16) ----

    [Fact]
    public async Task AssistantPayload_KeepsTheCardWhenStoredAndWritesStatusesByName()
    {
        var result = await RunUseCaseAsync();
        var payload = new AssistantMessagePayload(2, DetectedIntent.SymbolSnapshot, false, null, "متن", null, null, null, null, null, null, null, SymbolSnapshotResult: result);

        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<AssistantMessagePayload>(json)!.SymbolSnapshotResult!;

        Assert.Contains("\"Status\":\"Available\"", json);
        Assert.Equal(result.Price, restored.Price);
        Assert.Equal(result.MonthlySales, restored.MonthlySales);
        Assert.Equal(result.QuarterlyReport.Rows, restored.QuarterlyReport.Rows);
    }

    // ---- Helpers ----

    private static FinancialIngestionDbContext NewDb() => new(
        new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static NoavaranEligibleSymbolResolver NewResolver(FinancialIngestionDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<NoavaranEligibleSymbolResolver>.Instance);

    private static NoavaranEligibleCompanyRow Company(string symbol, string name, Guid? marketId = null, Guid? groupId = null) => new()
    {
        Id = Guid.NewGuid(),
        ProviderName = "NoavaranCurrentApi",
        ExternalCompanyId = Guid.NewGuid().ToString("N"),
        Name = name,
        CompanySymbol = symbol,
        TseSymbol = symbol,
        MarketId = marketId,
        GroupId = groupId
    };

    private static IReadOnlyCollection<SuggestedAction> BuildSuggestions(SymbolSnapshotResult result) =>
        new SymbolSnapshotFollowUpSuggestionService(Registry, new DeterministicCapabilityInterpreter(Registry)).Build(result);

    private static Task<SymbolSnapshotResult> RunUseCaseAsync(
        string? failing = null,
        string? hanging = null,
        SymbolSnapshotOptions? options = null,
        QuarterlyFinancialReportTableResult? quarterly = null) =>
        RunUseCaseAsync(new FakeSources { Failing = failing, Hanging = hanging, QuarterlyOverride = quarterly }, options);

    private static async Task<SymbolSnapshotResult> RunUseCaseAsync(FakeSources sources, SymbolSnapshotOptions? options = null) =>
        (await new SymbolSnapshotUseCase(new FakeEligible(), sources, sources, sources, options ?? new SymbolSnapshotOptions())
            .ExecuteAsync(Symbol, "correlation", CancellationToken.None))!;

    private static SymbolSnapshotUseCase BuildUseCase(string? hanging)
    {
        var sources = new FakeSources { Hanging = hanging };
        return new SymbolSnapshotUseCase(new FakeEligible(), sources, sources, sources, new SymbolSnapshotOptions());
    }

    private static Task<CapabilityExecutionResult> ExecuteAsync(FakeSources sources)
    {
        var frame = new ValidatedQueryFrame(
            SymbolSnapshotCapability.Code,
            Registry.Version,
            [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, Symbol, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, SymbolSnapshotCapability.Code)],
            SymbolSnapshotIntentRules.BuildInterpretation(Symbol, Symbol, Registry.Version));
        var executor = new SymbolSnapshotCapabilityExecutor(
            new SymbolSnapshotUseCase(new FakeEligible(), sources, sources, sources, new SymbolSnapshotOptions()));
        return executor.ExecuteAsync(frame, new QueryExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "correlation", "fa"), CancellationToken.None);
    }

    private static async Task<(ConversationDialogueGateResult Result, ConversationDialogueGate Gate, CountingProposalProvider Calls)> PrepareAsync(
        string message,
        bool enabled = true,
        string? pendingCapability = null)
    {
        var repository = new InMemoryTaskStates();
        var state = new ConversationTaskStateService(repository, TimeProvider.System, new ConversationTaskStateOptions());
        var conversationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        if (pendingCapability is not null)
        {
            await state.RecordPendingAsync(
                new ConversationTaskStateScope(conversationId, tenantId, actorId),
                pendingCapability,
                [],
                new PendingDialogueAction(PendingDialogueActionKind.Clarification, QuerySlotType.CompanyOrSymbol, [], "required_input_missing", Guid.NewGuid(), 0),
                "correlation",
                CancellationToken.None);
        }

        var calls = new CountingProposalProvider();
        var interpreter = new DeterministicCapabilityInterpreter(Registry);
        var gate = new ConversationDialogueGate(
            state,
            interpreter,
            new FixedEntityResolver(),
            new CapabilitySlotValidator(Registry),
            new NoDirectMetrics(),
            new SemanticRoutingRolloutCoordinator(new SemanticRoutingOptions(DefaultMode: SemanticRoutingMode.SemanticPrimary), new NullSemanticRoutingTelemetrySink()),
            new NoMessages(),
            TimeProvider.System,
            hybridInterpreter: new HybridCapabilityInterpreter(interpreter, Registry, new QueryInterpretationValidator(Registry), calls),
            eligibleSymbolResolver: new FakeEligible(),
            symbolSnapshotOptions: new SymbolSnapshotOptions { Enabled = enabled },
            capabilityRegistry: Registry);
        var request = new AiQueryRequest(message, tenantId, actorId, Guid.NewGuid().ToString("N"));
        return (await gate.PrepareAsync(request, conversationId, CancellationToken.None), gate, calls);
    }

    private static QuarterlyFinancialReportTableResult Quarterly(bool banking = false, decimal? baseValue = null)
    {
        var periods = new[]
        {
            new QuarterlyFiscalPeriod(1405, 2, new DateOnly(2026, 6, 22), new DateOnly(2026, 9, 22)),
            new QuarterlyFiscalPeriod(1405, 1, new DateOnly(2026, 3, 21), new DateOnly(2026, 6, 21)),
            new QuarterlyFiscalPeriod(1404, 4, new DateOnly(2025, 12, 22), new DateOnly(2026, 3, 20)),
            new QuarterlyFiscalPeriod(1404, 3, new DateOnly(2025, 9, 23), new DateOnly(2025, 12, 21)),
            new QuarterlyFiscalPeriod(1404, 2, new DateOnly(2025, 6, 22), new DateOnly(2025, 9, 22))
        };
        var rows = (banking ? new[] { 306, 307, 143, 160 } : new[] { 15, 139, 143, 160 })
            .Select((item, index) => new QuarterlyFinancialRow(
                $"source_{item}",
                index,
                $"ردیف {item}",
                item,
                null,
                QuarterlyRowPolicy.CumulativeFlow,
                item == 160 ? "rial_per_share" : "million_rial",
                periods.ToDictionary(
                    period => period.Label,
                    period => Cell(period.FiscalYear == 1404 && period.Quarter == 2 ? baseValue ?? item * 5m : item * 10m))))
            .ToArray();
        return new QuarterlyFinancialReportTableResult(
            Symbol, "مخابرات ایران", "464", "NoavaranCurrentApi", "IncomeStatement", false, 5, periods, rows, [], null, "Available", null, DateTimeOffset.UtcNow,
            [new QuarterlySelectedReportReference(Guid.NewGuid(), "r", "NoavaranCurrentApi", "IncomeStatement", periods[0].Start, periods[0].End, 6, false, false, false, "c", DateTimeOffset.UtcNow, null, null)],
            TemplateId: banking ? "BankingIncomeStatement" : "GeneralIncomeStatement");
    }

    private static QuarterlyFinancialCell Cell(decimal value) =>
        new(value, QuarterlyCellState.Available, null, "million_rial", false, [], [], "test", []);

    private sealed class FakeSources : IMarketQuoteResolver, IMonthlyActivityTrendQueryUseCase, IQuarterlyFinancialReportsQueryUseCase
    {
        public string? Failing { get; init; }
        public string? Hanging { get; init; }
        public bool Empty { get; init; }
        public QuarterlyFinancialReportTableResult? QuarterlyOverride { get; init; }

        public async Task<BatchMarketQuoteResult> ResolveAsync(IReadOnlyCollection<SymbolCode> symbols, CancellationToken cancellationToken)
        {
            await Gate("price", cancellationToken);
            return Empty
                ? new BatchMarketQuoteResult([], symbols)
                : new BatchMarketQuoteResult(
                    [new MarketQuoteObservation(symbols.Single(), 3450m, 2.1m, DateTimeOffset.UtcNow, MarketQuoteSource.LiveQuote, null!, new DateOnly(2026, 11, 9))],
                    []);
        }

        public async Task<MonthlyActivityTrendResponse?> ExecuteAsync(MonthlyActivityTrendQuery query, CancellationToken ct = default)
        {
            await Gate("monthly", ct);
            Assert.False(query.IncludeChartPayload);
            return Empty
                ? null
                : new MonthlyActivityTrendResponse(Symbol, "مخابرات ایران", 1405, 7, "میلیارد ریال", 1234m, null, null, 31m, null, null, null, [], [], [], "test", DateTimeOffset.UtcNow)
                {
                    SalesAmountMomGrowthPercent = 4m
                };
        }

        public async Task<QuarterlyFinancialReportTableResult> ExecuteAsync(QuarterlyFinancialReportTableQuery query, CancellationToken cancellationToken = default)
        {
            await Gate("quarterly", cancellationToken);
            Assert.Equal(5, query.QuarterCount);
            return Empty
                ? Quarterly() with { PeriodsNewestFirst = [], Outcome = QuarterlyFinancialReportOutcome.CompanyFoundNoPersistedReports }
                : QuarterlyOverride ?? Quarterly();
        }

        private async Task Gate(string section, CancellationToken cancellationToken)
        {
            if (Failing == section)
                throw new InvalidOperationException("source failed");
            if (Hanging == section)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FakeEligible : IEligibleSymbolResolver
    {
        public Task<EligibleSymbol?> ResolveAsync(string compactKey, CancellationToken cancellationToken) =>
            Task.FromResult(compactKey == Symbol ? Eligible : null);
    }

    private sealed class FixedEntityResolver : ICanonicalQueryEntityResolver
    {
        private static EntityResolutionResult Resolved() => new EntityResolutionResult.Resolved(
            new CanonicalQueryEntity(Guid.NewGuid(), Symbol, "مخابرات ایران", "company", "exact_ticker", "464"),
            new EntityResolutionEvidence("exact_ticker", 1m));

        public Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default) => Task.FromResult(Resolved());
        public Task<EntityResolutionResult> ResolveExactTickerFromTextAsync(string? text, CancellationToken cancellationToken = default) => Task.FromResult(Resolved());
        public Task<EntityResolutionResult> ResolveFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) => Task.FromResult(Resolved());
        public Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityResolutionResult.Resolved>>([(EntityResolutionResult.Resolved)Resolved()]);
    }

    private sealed class CountingProposalProvider : IQueryInterpretationProposalProvider
    {
        public int Calls { get; private set; }

        public Task<QueryInterpretationProposal?> ProposeAsync(string originalText, Guid tenantId, string correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<QueryInterpretationProposal?>(null);
        }
    }

    private sealed class InMemoryTaskStates : IConversationTaskStateRepository
    {
        private readonly Dictionary<ConversationTaskStateScope, ConversationTaskState> states = [];

        public Task<ConversationTaskState?> FindAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(states.GetValueOrDefault(scope));

        public Task<ConversationTaskStateWriteResult> TryWriteAsync(ConversationTaskState state, long? expectedVersion, CancellationToken cancellationToken)
        {
            states[new(state.ConversationId, state.TenantId, state.ActorId)] = state;
            return Task.FromResult(new ConversationTaskStateWriteResult(true, state));
        }

        public Task DeleteAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken)
        {
            states.Remove(scope);
            return Task.CompletedTask;
        }
    }

    private sealed class NoMessages : IMessageRepository
    {
        public Task<Guid> AppendAsync(Guid conversationId, MessageRole role, string content, string? scannerQueryPlanJson, DateTimeOffset createdAt, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task<IReadOnlyCollection<MessageRecord>> ListByConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<MessageRecord>>([]);
    }

    private sealed class NoDirectMetrics : IDirectMetricRoutingRegistry
    {
        public DirectMetricRoutingMatch? TryResolve(string userMessage, DateOnly asOf) => null;
        public IReadOnlyList<DirectMetricRoutingMatch> ResolveAll(string userMessage, DateOnly asOf) => [];
        public bool ContainsDirectMetricTerm(string userMessage, DateOnly asOf) => false;
        public SymbolLookupPeriodSelector? ResolvePeriodSelector(string userMessage, MetricCode metricCode) => null;
        public string ResolveDisplayLabel(MetricCode metricCode, SymbolLookupPeriodSelector? selector) => metricCode.Value;
        public string StripResolvedPhrase(string userMessage, DirectMetricRoutingMatch match) => userMessage;
    }

    private sealed class UnusedChartRenderer : ITelegramMonthlyTrendChartRenderer
    {
        public TelegramAssistantMediaAttachment Render(MonthlyActivityTrendResponse trend) =>
            throw new InvalidOperationException("must not be called");
    }
}

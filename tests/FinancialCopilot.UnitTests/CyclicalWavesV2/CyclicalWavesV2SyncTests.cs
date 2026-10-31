using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using FinancialCopilot.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

public sealed class CyclicalWavesV2SyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Full_RunsPhasesInOrderAndOneTickerFailureDoesNotStopTheRest()
    {
        var client = new FakeClient { FailScreenerFor = { "بد" } };
        var store = new FakeStore();
        var runs = new FakeRunStore();
        var coordinator = Create(client, store, new FakeAnalysisStore(), runs);

        var outcome = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Full), "test", CancellationToken.None);

        Assert.Equal(CyclicalWavesV2SyncRunStatus.CompletedWithErrors, outcome.Status);
        Assert.Equal(["tickers", "lines", "screener:الف", "statements:الف", "screener:بد", "statements:بد", "screener:ج", "statements:ج", "analysis:1"],
            client.Calls);
        Assert.Equal(3, outcome.Counts.TotalItems - 1);
        Assert.Equal(1, outcome.Counts.FailedItems);
        Assert.Contains("بد: Screener:ProviderRemoteUnavailable", outcome.ErrorSummary);
        Assert.Contains("Catalog tickers=3", outcome.ErrorSummary);
        Assert.Equal(CyclicalWavesV2SyncRunStatus.CompletedWithErrors, runs.Completed[outcome.RunId!.Value].Status);
    }

    [Fact]
    public async Task Screener_SecondUnchangedRunUpdatesNothing()
    {
        var client = new FakeClient();
        var store = new FakeStore();
        var coordinator = Create(client, store, new FakeAnalysisStore(), new FakeRunStore());
        await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Catalog), "test", CancellationToken.None);

        var first = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Screener), "test", CancellationToken.None);
        var second = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Screener), "test", CancellationToken.None);

        Assert.Equal(3, first.Counts.UpdatedItems);
        Assert.Equal(0, second.Counts.UpdatedItems);
        Assert.Equal(3, second.Counts.UnchangedItems);
        Assert.Equal(CyclicalWavesV2SyncRunStatus.Succeeded, second.Status);
    }

    [Fact]
    public async Task Screener404CountsAsFailureButStatements404IsUnchanged()
    {
        var client = new FakeClient { ScreenerNotFound = { "الف" }, StatementsNotFound = { "ج" } };
        var store = new FakeStore();
        var coordinator = Create(client, store, new FakeAnalysisStore(), new FakeRunStore());
        await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Catalog), "test", CancellationToken.None);

        var screener = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Screener), "test", CancellationToken.None);
        var statements = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.IncomeStatements), "test", CancellationToken.None);

        Assert.Equal(1, screener.Counts.FailedItems);
        Assert.Contains("الف: ScreenerNotFound", screener.ErrorSummary);
        Assert.Equal(0, statements.Counts.FailedItems);
        Assert.Equal(3, statements.Counts.UnchangedItems);
    }

    [Fact]
    public async Task Ticker_RequiresACatalogRow()
    {
        var coordinator = Create(new FakeClient(), new FakeStore(), new FakeAnalysisStore(), new FakeRunStore());

        var outcome = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Ticker, "ناشناس"), "test", CancellationToken.None);

        Assert.Equal(CyclicalWavesV2SyncRunStatus.Failed, outcome.Status);
        Assert.Contains("TickerNotInCatalog", outcome.ErrorSummary);
    }

    [Fact]
    public async Task AuthenticationFailureEndsTheRunAsFailedWithoutSecrets()
    {
        var client = new FakeClient { FailAll = new FinancialProviderException(FinancialProviderErrorCode.Unauthorized, "CyclicalWaves v2 login failed with HTTP 401.") };
        var store = new FakeStore();
        store.Seed("الف");
        var coordinator = Create(client, store, new FakeAnalysisStore(), new FakeRunStore());

        var outcome = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Screener), "test", CancellationToken.None);

        Assert.Equal(CyclicalWavesV2SyncRunStatus.Failed, outcome.Status);
        Assert.StartsWith("RunFailed: Authentication:Unauthorized", outcome.ErrorSummary);
        Assert.DoesNotContain("secret", outcome.ErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", outcome.ErrorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyCatalogIsRejectedInsteadOfDeactivatingEveryTicker()
    {
        var client = new FakeClient { Tickers = [] };
        var store = new FakeStore();
        store.Seed("الف");
        var coordinator = Create(client, store, new FakeAnalysisStore(), new FakeRunStore());

        var outcome = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Catalog), "test", CancellationToken.None);

        Assert.Equal(CyclicalWavesV2SyncRunStatus.Failed, outcome.Status);
        Assert.Equal(["الف"], await store.GetActiveTickersAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenAnotherRunHoldsTheLeaseTheRunIsSkipped()
    {
        var runs = new FakeRunStore { Busy = true };
        var client = new FakeClient();
        var coordinator = Create(client, new FakeStore(), new FakeAnalysisStore(), runs);

        var outcome = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Full), "test", CancellationToken.None);

        Assert.Equal(CyclicalWavesV2SyncRunStatus.SkippedAlreadyRunning, outcome.Status);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Analysis_EmptyStoreWalksAllPagesWithoutDateFilter()
    {
        var client = new FakeClient { AnalysisLastPage = 3 };
        var analyses = new FakeAnalysisStore();
        var coordinator = Create(client, new FakeStore(), analyses, new FakeRunStore());

        var outcome = await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Analysis), "test", CancellationToken.None);

        Assert.Equal(["analysis:1", "analysis:2", "analysis:3"], client.Calls);
        Assert.All(client.AnalysisFromDates, Assert.Null);
        Assert.Equal(3, outcome.Counts.UpdatedItems);
        Assert.Contains("mode=Full", outcome.ErrorSummary);
    }

    [Fact]
    public async Task Analysis_IncrementalUsesTehranDateMinusOverlapAndInsertsNothingTheSecondTime()
    {
        var client = new FakeClient();
        var analyses = new FakeAnalysisStore();
        var coordinator = Create(client, new FakeStore(), analyses, new FakeRunStore());
        await coordinator.RunAsync(Request(CyclicalWavesV2SyncScope.Analysis), "test", CancellationToken.None);
        analyses.MaxPublishedAt = new DateTimeOffset(2026, 10, 27, 21, 0, 0, TimeSpan.Zero); // 28 Oct 00:30 in Tehran

        var outcome = await coordinator.RunAsync(
            new CyclicalWavesV2SyncRequest(CyclicalWavesV2SyncScope.Analysis, null, CyclicalWavesV2AnalysisMode.Incremental, CyclicalWavesV2SyncTrigger.Admin, "t"),
            "test",
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 26), client.AnalysisFromDates[^1]);
        Assert.Equal(0, outcome.Counts.UpdatedItems);
        Assert.Equal(1, outcome.Counts.UnchangedItems);
    }

    [Fact]
    public void Worker_PreviousOccurrenceFindsTheLastDailyRun()
    {
        var schedule = CyclicalWavesUtcCronSchedule.Parse("30 23 * * *");

        Assert.Equal(new DateTimeOffset(2026, 10, 30, 23, 30, 0, TimeSpan.Zero),
            CyclicalWavesV2SyncWorker.PreviousOccurrence(schedule, new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero),
            schedule.GetNextOccurrence(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("2026-10-29T23:30:00Z", true)]
    [InlineData("2026-10-30T23:31:00Z", false)]
    public async Task Worker_CatchUpRunsOnlyWhenTheLastScheduledRunWasMissed(string? lastFull, bool expectsRun)
    {
        var runs = new FakeRunStore { LastFullStart = lastFull is null ? null : DateTimeOffset.Parse(lastFull) };
        var coordinator = new RecordingCoordinator();
        var worker = CreateWorker(runs, coordinator, ingestion: true, daily: true);

        await worker.RunStartupCatchUpAsync(CyclicalWavesUtcCronSchedule.Parse("30 23 * * *"), CancellationToken.None);

        Assert.Equal(expectsRun ? 1 : 0, coordinator.Runs.Count);
        if (expectsRun)
            Assert.Equal(CyclicalWavesV2SyncTrigger.StartupCatchUp, coordinator.Runs[0].Trigger);
    }

    [Fact]
    public async Task Worker_ClaimsAndRunsARequestedRun()
    {
        var runs = new FakeRunStore();
        await runs.RequestAsync(Request(CyclicalWavesV2SyncScope.Screener), Now, CancellationToken.None);
        var coordinator = new RecordingCoordinator();
        var worker = CreateWorker(runs, coordinator, ingestion: true, daily: false);

        await worker.ProcessRequestedRunsAsync(CancellationToken.None);

        Assert.Equal(CyclicalWavesV2SyncScope.Screener, Assert.Single(coordinator.Claimed).Request.Scope);
    }

    [Theory]
    [InlineData(FinancialCopilot.Application.AI.Orchestration.FinancialDataChatProvider.NoavaranAmin)]
    [InlineData(FinancialCopilot.Application.AI.Orchestration.FinancialDataChatProvider.CyclicalWaves)]
    public async Task Worker_DisabledIngestionRunsNothingWhateverTheChatProvider(FinancialCopilot.Application.AI.Orchestration.FinancialDataChatProvider _)
    {
        var runs = new FakeRunStore();
        await runs.RequestAsync(Request(CyclicalWavesV2SyncScope.Full), Now, CancellationToken.None);
        var coordinator = new RecordingCoordinator();
        var worker = CreateWorker(runs, coordinator, ingestion: false, daily: true);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(coordinator.Runs);
        Assert.Empty(coordinator.Claimed);
    }

    private static CyclicalWavesV2SyncRequest Request(CyclicalWavesV2SyncScope scope, string? ticker = null) =>
        new(scope, ticker, null, CyclicalWavesV2SyncTrigger.Admin, "admin:1");

    private static CyclicalWavesV2SyncCoordinator Create(
        FakeClient client,
        FakeStore store,
        FakeAnalysisStore analyses,
        FakeRunStore runs) =>
        new(client, store, analyses, runs,
            new CyclicalWavesV2SyncSettings(12, 100, 2, TimeSpan.Zero, TimeSpan.FromMinutes(30), 2),
            new ManualTimeProvider(Now));

    private static CyclicalWavesV2SyncWorker CreateWorker(FakeRunStore runs, RecordingCoordinator coordinator, bool ingestion, bool daily)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICyclicalWavesV2SyncRunStore>(runs);
        services.AddSingleton<ICyclicalWavesV2SyncCoordinator>(coordinator);
        var provider = services.BuildServiceProvider();
        return new CyclicalWavesV2SyncWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CyclicalWavesV2Options { IngestionEnabled = ingestion, DailySyncEnabled = daily, Identifier = "u", Password = "p" }),
            new ManualTimeProvider(Now),
            NullLogger<CyclicalWavesV2SyncWorker>.Instance);
    }

    private sealed class FakeClient : ICyclicalWavesV2Client
    {
        public List<string> Calls { get; } = [];
        public List<string> Tickers { get; set; } = ["الف", "بد", "ج"];
        public HashSet<string> FailScreenerFor { get; } = [];
        public HashSet<string> ScreenerNotFound { get; } = [];
        public HashSet<string> StatementsNotFound { get; } = [];
        public Exception? FailAll { get; set; }
        public int AnalysisLastPage { get; set; } = 1;
        public List<DateOnly?> AnalysisFromDates { get; } = [];

        public Task<IReadOnlyList<string>> GetTickersAsync(CancellationToken cancellationToken)
        {
            Calls.Add("tickers");
            ThrowIfFailing();
            return Task.FromResult<IReadOnlyList<string>>(Tickers);
        }

        public Task<CyclicalWavesV2ScreenerSnapshot?> GetScreenerAsync(string ticker, CancellationToken cancellationToken)
        {
            Calls.Add("screener:" + ticker);
            ThrowIfFailing();
            if (FailScreenerFor.Contains(ticker))
                throw new FinancialProviderException(FinancialProviderErrorCode.RemoteUnavailable, "down");
            return Task.FromResult(ScreenerNotFound.Contains(ticker)
                ? null
                : new CyclicalWavesV2ScreenerSnapshot(ticker, null, new Dictionary<string, decimal?> { ["pe"] = 5m }, null, null, null));
        }

        public Task<CyclicalWavesV2IncomeStatementSet?> GetIncomeStatementsAsync(string ticker, int limit, CancellationToken cancellationToken)
        {
            Calls.Add("statements:" + ticker);
            ThrowIfFailing();
            return Task.FromResult(StatementsNotFound.Contains(ticker)
                ? null
                : new CyclicalWavesV2IncomeStatementSet(ticker, null, 0, []));
        }

        public Task<IReadOnlyList<CyclicalWavesV2LineDefinition>> GetIncomeStatementLinesAsync(CancellationToken cancellationToken)
        {
            Calls.Add("lines");
            ThrowIfFailing();
            return Task.FromResult<IReadOnlyList<CyclicalWavesV2LineDefinition>>([new(44, "Sales", "فروش")]);
        }

        public Task<CyclicalWavesV2AnalysisPage> GetAnalysesPageAsync(int page, int pageSize, DateOnly? fromDate, CancellationToken cancellationToken)
        {
            Calls.Add("analysis:" + page);
            AnalysisFromDates.Add(fromDate);
            ThrowIfFailing();
            return Task.FromResult(new CyclicalWavesV2AnalysisPage(
                [new CyclicalWavesV2AnalysisArticle(page, "فولاد- تحلیل", null, Now, [], [])], page, AnalysisLastPage, AnalysisLastPage));
        }

        private void ThrowIfFailing()
        {
            if (FailAll is not null) throw FailAll;
        }
    }

    private sealed class FakeStore : ICyclicalWavesV2Store
    {
        private readonly Dictionary<string, bool> _tickers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _screened = new(StringComparer.Ordinal);

        public void Seed(string ticker) => _tickers[ticker] = true;

        public Task<CyclicalWavesV2CatalogResult> UpsertCatalogAsync(IReadOnlyCollection<string> tickers, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var added = tickers.Count(ticker => !_tickers.ContainsKey(ticker));
            foreach (var ticker in tickers) _tickers[ticker] = true;
            return Task.FromResult(new CyclicalWavesV2CatalogResult(added, 0, 0, tickers.Count - added));
        }

        public Task<IReadOnlyList<string>> GetActiveTickersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(_tickers.Where(pair => pair.Value).Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).ToList());

        public Task<string?> FindCatalogTickerAsync(string ticker, CancellationToken cancellationToken) =>
            Task.FromResult(_tickers.ContainsKey(ticker) ? ticker : null);

        public Task<CyclicalWavesV2ScreenerApplyResult> ApplyScreenerAsync(CyclicalWavesV2ScreenerSnapshot snapshot, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(new CyclicalWavesV2ScreenerApplyResult(
                _screened.Add(snapshot.Ticker) ? CyclicalWavesV2ApplyOutcome.Updated : CyclicalWavesV2ApplyOutcome.Unchanged, false));

        public Task<int> UpsertLineDefinitionsAsync(IReadOnlyCollection<CyclicalWavesV2LineDefinition> definitions, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(definitions.Count);

        public Task<CyclicalWavesV2StatementApplyResult> ApplyIncomeStatementsAsync(CyclicalWavesV2IncomeStatementSet statements, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(new CyclicalWavesV2StatementApplyResult(CyclicalWavesV2ApplyOutcome.Unchanged, 0, 0, 0));
    }

    private sealed class FakeAnalysisStore : ICyclicalWavesV2AnalysisStore
    {
        private readonly HashSet<long> _ids = [];
        public DateTimeOffset? MaxPublishedAt { get; set; }

        public Task<CyclicalWavesV2AnalysisApplyResult> ApplyAnalysesPageAsync(IReadOnlyCollection<CyclicalWavesV2AnalysisArticle> articles, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var inserted = articles.Count(article => _ids.Add(article.Id));
            return Task.FromResult(new CyclicalWavesV2AnalysisApplyResult(inserted, 0, articles.Count - inserted));
        }

        public Task<DateTimeOffset?> GetMaxPublishedAtAsync(CancellationToken cancellationToken) => Task.FromResult(MaxPublishedAt);
    }

    private sealed class FakeRunStore : ICyclicalWavesV2SyncRunStore
    {
        private readonly List<(Guid Id, CyclicalWavesV2SyncRequest Request)> _requested = [];
        public bool Busy { get; set; }
        public DateTimeOffset? LastFullStart { get; set; }
        public Dictionary<Guid, (CyclicalWavesV2SyncRunStatus Status, string? Summary)> Completed { get; } = [];

        public Task<CyclicalWavesV2RequestResult> RequestAsync(CyclicalWavesV2SyncRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            _requested.Add((id, request));
            return Task.FromResult(new CyclicalWavesV2RequestResult(id, true));
        }

        public Task<Guid?> TryStartAsync(CyclicalWavesV2SyncRequest request, string lockOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(Busy ? null : Guid.NewGuid());

        public Task<CyclicalWavesV2ClaimedRun?> ClaimNextRequestedAsync(string lockOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken)
        {
            if (_requested.Count == 0) return Task.FromResult<CyclicalWavesV2ClaimedRun?>(null);
            var next = _requested[0];
            _requested.RemoveAt(0);
            return Task.FromResult<CyclicalWavesV2ClaimedRun?>(new CyclicalWavesV2ClaimedRun(next.Id, next.Request));
        }

        public Task HeartbeatAsync(Guid runId, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(Guid runId, CyclicalWavesV2SyncRunStatus status, CyclicalWavesV2RunCounts counts, string? errorSummary, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Completed[runId] = (status, errorSummary);
            return Task.CompletedTask;
        }

        public Task<int> ExpireStaleLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<DateTimeOffset?> GetLastCompletedFullRunStartAsync(CancellationToken cancellationToken) => Task.FromResult(LastFullStart);

        public Task<IReadOnlyList<CyclicalWavesV2SyncRunSummary>> GetRecentRunsAsync(int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CyclicalWavesV2SyncRunSummary>>([]);
    }

    private sealed class RecordingCoordinator : ICyclicalWavesV2SyncCoordinator
    {
        public List<CyclicalWavesV2SyncRequest> Runs { get; } = [];
        public List<CyclicalWavesV2ClaimedRun> Claimed { get; } = [];

        public Task<CyclicalWavesV2SyncOutcome> RunAsync(CyclicalWavesV2SyncRequest request, string lockOwner, CancellationToken cancellationToken)
        {
            Runs.Add(request);
            return Task.FromResult(new CyclicalWavesV2SyncOutcome(Guid.NewGuid(), CyclicalWavesV2SyncRunStatus.Succeeded, new(0, 0, 0, 0, 0), null));
        }

        public Task<CyclicalWavesV2SyncOutcome> RunClaimedAsync(CyclicalWavesV2ClaimedRun run, CancellationToken cancellationToken)
        {
            Claimed.Add(run);
            return Task.FromResult(new CyclicalWavesV2SyncOutcome(run.RunId, CyclicalWavesV2SyncRunStatus.Succeeded, new(0, 0, 0, 0, 0), null));
        }
    }
}

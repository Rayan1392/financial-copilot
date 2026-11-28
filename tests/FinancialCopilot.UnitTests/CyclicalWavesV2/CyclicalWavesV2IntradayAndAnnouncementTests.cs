using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using FinancialCopilot.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

/// <summary>
/// Change «intraday-gauge-sync-and-codal-trigger»: the market-hours schedule, the Worker's slot and queue handling, and
/// the CodalMirror announcement gauge refresh. 2026-10-31 is a Saturday.
/// </summary>
public sealed partial class CyclicalWavesV2SyncTests
{
    private static DateTimeOffset Utc(int month, int day, int hour, int minute) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void DailySchedule_DefaultRunsAtSixAndNineteenTehranEveryDay()
    {
        var schedule = CyclicalWavesUtcCronSchedule.Parse(new CyclicalWavesV2Options().DailySyncCronUtc);

        var first = schedule.GetNextOccurrence(Utc(11, 5, 0, 0)); // a Thursday
        var second = schedule.GetNextOccurrence(first);
        var third = schedule.GetNextOccurrence(second);

        Assert.Equal(Utc(11, 5, 2, 30), first);
        Assert.Equal(Utc(11, 5, 15, 30), second);
        Assert.Equal(Utc(11, 6, 2, 30), third);
    }

    [Fact]
    public void IntradaySchedule_SaturdayHasEightSlotsFromNineToHalfPastTwelveTehran()
    {
        var schedule = CyclicalWavesV2IntradaySchedule.Create("09:00", "12:30", 30);
        var slots = new List<DateTimeOffset>();
        var cursor = Utc(10, 31, 0, 0);
        while (true)
        {
            cursor = schedule.GetNextOccurrence(cursor);
            if (cursor.Day != 31) break;
            slots.Add(cursor);
        }

        Assert.Equal(8, slots.Count);
        Assert.Equal(Utc(10, 31, 5, 30), slots[0]);
        Assert.Equal(Utc(10, 31, 9, 0), slots[^1]);
        Assert.All(slots.Zip(slots.Skip(1)), pair => Assert.Equal(TimeSpan.FromMinutes(30), pair.Second - pair.First));
        Assert.Equal(DateTimeOffset.UnixEpoch.Offset, slots[0].Offset);
    }

    [Theory]
    // Friday 23:00 UTC is already Saturday 02:30 in Tehran.
    [InlineData("2026-10-30T23:00:00Z", "2026-10-31T05:30:00Z")]
    // A slot is strictly after the given time.
    [InlineData("2026-10-31T05:30:00Z", "2026-10-31T06:00:00Z")]
    // After the last slot of Wednesday the next one is Saturday.
    [InlineData("2026-11-04T09:00:00Z", "2026-11-07T05:30:00Z")]
    [InlineData("2026-11-05T06:00:00Z", "2026-11-07T05:30:00Z")]
    [InlineData("2026-11-06T06:00:00Z", "2026-11-07T05:30:00Z")]
    // Sunday is a trading day.
    [InlineData("2026-10-31T09:00:00Z", "2026-11-01T05:30:00Z")]
    public void IntradaySchedule_SkipsThursdayAndFridayAndUsesTehranDays(string after, string expected) =>
        Assert.Equal(
            DateTimeOffset.Parse(expected),
            CyclicalWavesV2IntradaySchedule.Create("09:00", "12:30", 30).GetNextOccurrence(DateTimeOffset.Parse(after)));

    [Fact]
    public void IntradaySchedule_AnIntervalThatDoesNotDivideTheWindowStopsBeforeTheEnd()
    {
        var schedule = CyclicalWavesV2IntradaySchedule.Create("09:00", "12:30", 45);

        // 09:00, 09:45, 10:30, 11:15, 12:00 Tehran; 12:45 is outside the window.
        Assert.Equal(Utc(10, 31, 8, 30), schedule.GetNextOccurrence(Utc(10, 31, 8, 0)));
        Assert.Equal(Utc(11, 1, 5, 30), schedule.GetNextOccurrence(Utc(10, 31, 8, 30)));
    }

    [Theory]
    [InlineData("09:00", "12:30", 30, true)]
    [InlineData("09:00", "12:30", 5, true)]
    [InlineData("09:00", "12:30", 240, true)]
    [InlineData("09:00", "12:30", 4, false)]
    [InlineData("09:00", "12:30", 241, false)]
    [InlineData("12:30", "09:00", 30, false)]
    [InlineData("09:00", "09:00", 30, false)]
    [InlineData("9", "12:30", 30, false)]
    [InlineData("09:00", "", 30, false)]
    [InlineData("25:00", "26:00", 30, false)]
    public void Options_ValidateTheMarketHoursWindow(string start, string end, int interval, bool valid) =>
        Assert.Equal(valid, new CyclicalWavesV2Options
        {
            IntradayWindowStartTehran = start,
            IntradayWindowEndTehran = end,
            IntradayIntervalMinutes = interval
        }.IsValid(out _));

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(1440, true)]
    [InlineData(-1, false)]
    [InlineData(1441, false)]
    public void Options_ValidateTheAnnouncementRecheckDelay(int minutes, bool valid) =>
        Assert.Equal(valid, new CyclicalWavesV2Options { CodalGaugeRecheckMinutes = minutes }.IsValid(out _));

    [Fact]
    public void Options_NewSwitchesAreOffByDefault()
    {
        var options = new CyclicalWavesV2Options();

        Assert.False(options.IntradaySyncEnabled);
        Assert.False(options.CodalGaugeRefreshEnabled);
    }

    [Fact]
    public async Task Worker_AMarketHoursSlotRunsGaugeDetailsThenEquilibriumDetails()
    {
        var coordinator = new RecordingCoordinator();
        var slot = Utc(10, 31, 5, 30);
        var worker = CreateWorker(new FakeRunStore(), coordinator, new ManualTimeProvider(slot.AddMinutes(29)), new CyclicalWavesV2Options());

        await worker.RunIntradaySlotAsync(CyclicalWavesV2IntradaySchedule.Create("09:00", "12:30", 30), slot, CancellationToken.None);

        Assert.Equal(
            [CyclicalWavesV2SyncScope.GaugeDetails, CyclicalWavesV2SyncScope.EquilibriumDetails],
            coordinator.Runs.Select(run => run.Scope));
        Assert.All(coordinator.Runs, run => Assert.Equal(CyclicalWavesV2SyncTrigger.Scheduled, run.Trigger));
        Assert.All(coordinator.Runs, run => Assert.Null(run.Ticker));
    }

    [Fact]
    public async Task Worker_AMarketHoursSlotThatIsAWholeIntervalLateIsDropped()
    {
        var coordinator = new RecordingCoordinator();
        var slot = Utc(10, 31, 5, 30);
        var worker = CreateWorker(new FakeRunStore(), coordinator, new ManualTimeProvider(slot.AddMinutes(30)), new CyclicalWavesV2Options());

        await worker.RunIntradaySlotAsync(CyclicalWavesV2IntradaySchedule.Create("09:00", "12:30", 30), slot, CancellationToken.None);

        Assert.Empty(coordinator.Runs);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public async Task Worker_RunsTheMarketHoursSlotOnlyWhenTheSwitchIsOn(bool enabled, int expectedRuns)
    {
        var coordinator = new RecordingCoordinator();
        var slot = Utc(10, 31, 5, 30);
        var time = new ManualTimeProvider(slot.AddMilliseconds(-20));
        var worker = CreateWorker(new FakeRunStore(), coordinator, time, new CyclicalWavesV2Options
        {
            IngestionEnabled = true,
            IntradaySyncEnabled = enabled,
            Identifier = "u",
            Password = "p"
        });

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.Empty(coordinator.Runs);
        time.Set(slot);
        for (var attempt = 0; attempt < 100 && coordinator.Runs.Count < expectedRuns; attempt++)
            await Task.Delay(50);
        if (!enabled) await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(expectedRuns, coordinator.Runs.Count);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(60, CyclicalWavesV2SyncWorker.MaximumQueuedRunsPerPoll)]
    public async Task Worker_DrainsTheQueueUpToTheLimitInOnePoll(int queued, int expectedClaims)
    {
        var runs = new FakeRunStore();
        for (var index = 0; index < queued; index++)
            await runs.RequestAsync(Request(CyclicalWavesV2SyncScope.TickerGaugeEquilibrium, "الف"), Now, CancellationToken.None);
        var coordinator = new RecordingCoordinator();
        var worker = CreateWorker(runs, coordinator, new ManualTimeProvider(Now), new CyclicalWavesV2Options());

        await worker.ProcessRequestedRunsAsync(CancellationToken.None);

        Assert.Equal(expectedClaims, coordinator.Claimed.Count);
    }

    [Fact]
    public async Task Announcement_QueuesAnImmediateAndADeferredRefreshOfTheCatalogTicker()
    {
        var store = new FakeStore();
        store.Seed("فولاد");
        var runs = new FakeRunStore();
        var refresh = CreateRefresh(store, runs, enabled: true, recheckMinutes: 30);

        var outcome = await refresh.RequestAsync(Announcement(" فولاد "), CancellationToken.None);

        Assert.Equal(CodalAnnouncementGaugeRefreshStatus.Queued, outcome.Status);
        Assert.Equal("فولاد", outcome.Ticker);
        Assert.Equal(2, runs.Announcements.Count);
        Assert.All(runs.Announcements, item => Assert.Equal("فولاد", item.Ticker));
        Assert.All(runs.Announcements, item => Assert.Equal("codal:MonthlyReport:-1001:42", item.RequestedBy));
        Assert.All(runs.Announcements, item => Assert.Equal(CodalAnnouncementGaugeRefreshSettings.DefaultMaximumPending, item.MaximumPending));
        Assert.Null(runs.Announcements[0].NotBeforeUtc);
        Assert.Equal(Now.AddMinutes(30), runs.Announcements[1].NotBeforeUtc);
    }

    [Fact]
    public async Task Announcement_WithoutARecheckDelayQueuesOnlyTheImmediateRefresh()
    {
        var store = new FakeStore();
        store.Seed("فولاد");
        var runs = new FakeRunStore();

        var outcome = await CreateRefresh(store, runs, enabled: true, recheckMinutes: 0).RequestAsync(Announcement("فولاد"), CancellationToken.None);

        Assert.Equal(CodalAnnouncementGaugeRefreshStatus.Queued, outcome.Status);
        Assert.Null(outcome.Deferred);
        Assert.Null(Assert.Single(runs.Announcements).NotBeforeUtc);
    }

    [Theory]
    [InlineData(false, "فولاد", CodalAnnouncementGaugeRefreshStatus.Disabled)]
    [InlineData(true, " ", CodalAnnouncementGaugeRefreshStatus.SymbolMissing)]
    [InlineData(true, "ناشناس", CodalAnnouncementGaugeRefreshStatus.NotInCatalog)]
    public async Task Announcement_QueuesNothingWhenOffOrWhenTheSymbolIsNotInTheCatalog(
        bool enabled, string symbol, CodalAnnouncementGaugeRefreshStatus expected)
    {
        var store = new FakeStore();
        store.Seed("فولاد");
        var runs = new FakeRunStore();

        var outcome = await CreateRefresh(store, runs, enabled, recheckMinutes: 30).RequestAsync(Announcement(symbol), CancellationToken.None);

        Assert.Equal(expected, outcome.Status);
        Assert.Empty(runs.Announcements);
    }

    [Theory]
    [InlineData(CyclicalWavesV2AnnouncementEnqueueResult.Duplicate, CyclicalWavesV2AnnouncementEnqueueResult.Duplicate, CodalAnnouncementGaugeRefreshStatus.Duplicate)]
    [InlineData(CyclicalWavesV2AnnouncementEnqueueResult.QueueFull, CyclicalWavesV2AnnouncementEnqueueResult.QueueFull, CodalAnnouncementGaugeRefreshStatus.QueueFull)]
    [InlineData(CyclicalWavesV2AnnouncementEnqueueResult.Duplicate, CyclicalWavesV2AnnouncementEnqueueResult.QueueFull, CodalAnnouncementGaugeRefreshStatus.QueueFull)]
    [InlineData(CyclicalWavesV2AnnouncementEnqueueResult.Duplicate, CyclicalWavesV2AnnouncementEnqueueResult.Queued, CodalAnnouncementGaugeRefreshStatus.Queued)]
    public async Task Announcement_ReportsDuplicateAndFullQueue(
        CyclicalWavesV2AnnouncementEnqueueResult immediate,
        CyclicalWavesV2AnnouncementEnqueueResult deferred,
        CodalAnnouncementGaugeRefreshStatus expected)
    {
        var store = new FakeStore();
        store.Seed("فولاد");
        var runs = new FakeRunStore { AnnouncementResult = notBefore => notBefore is null ? immediate : deferred };

        var outcome = await CreateRefresh(store, runs, enabled: true, recheckMinutes: 30).RequestAsync(Announcement("فولاد"), CancellationToken.None);

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(immediate, outcome.Immediate);
        Assert.Equal(deferred, outcome.Deferred);
    }

    [Fact]
    public async Task AnnouncementTrigger_RunsInItsOwnScopeAndSwallowsAQueueFailure()
    {
        var store = new FakeStore();
        store.Seed("فولاد");
        var runs = new FakeRunStore { AnnouncementFailure = new InvalidOperationException("database is down") };
        var resolutions = 0;
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            resolutions++;
            return CreateRefresh(store, runs, enabled: true, recheckMinutes: 30);
        });
        await using var provider = services.BuildServiceProvider();
        var trigger = new ScopedCodalAnnouncementGaugeRefreshTrigger(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScopedCodalAnnouncementGaugeRefreshTrigger>.Instance);

        await trigger.RequestAsync(Announcement("فولاد"), CancellationToken.None);
        runs.AnnouncementFailure = null;
        await trigger.RequestAsync(Announcement("فولاد"), CancellationToken.None);

        Assert.Equal(2, resolutions);
        Assert.Equal(2, runs.Announcements.Count);
    }

    [Fact]
    public async Task AnnouncementTrigger_LetsCancellationThrough()
    {
        var store = new FakeStore();
        store.Seed("فولاد");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var runs = new FakeRunStore { AnnouncementFailure = new OperationCanceledException(cancellation.Token) };
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateRefresh(store, runs, enabled: true, recheckMinutes: 30));
        await using var provider = services.BuildServiceProvider();
        var trigger = new ScopedCodalAnnouncementGaugeRefreshTrigger(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScopedCodalAnnouncementGaugeRefreshTrigger>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trigger.RequestAsync(Announcement("فولاد"), cancellation.Token));
    }

    private static CodalAnnouncementGaugeRefreshRequest Announcement(string symbol) =>
        new(symbol, CodalAnnouncementKind.MonthlyReport, -1001, 42, "corr-1");

    private static CodalAnnouncementGaugeRefresh CreateRefresh(FakeStore store, FakeRunStore runs, bool enabled, int recheckMinutes) =>
        new(store, runs, new CodalAnnouncementGaugeRefreshSettings(enabled, TimeSpan.FromMinutes(recheckMinutes)), new ManualTimeProvider(Now));

    private static CyclicalWavesV2SyncWorker CreateWorker(
        FakeRunStore runs,
        RecordingCoordinator coordinator,
        ManualTimeProvider time,
        CyclicalWavesV2Options options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICyclicalWavesV2SyncRunStore>(runs);
        services.AddSingleton<ICyclicalWavesV2SyncCoordinator>(coordinator);
        return new CyclicalWavesV2SyncWorker(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            time,
            NullLogger<CyclicalWavesV2SyncWorker>.Instance);
    }
}

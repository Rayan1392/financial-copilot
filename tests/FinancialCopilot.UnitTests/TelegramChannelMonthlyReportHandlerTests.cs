using System.Reflection;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramChannelMonthlyReportHandlerTests
{
    [Fact]
    public async Task Unsupported_update_does_not_create_a_claim()
    {
        using var fixture = new Fixture();
        var result = await fixture.Handler.HandleAsync(
            fixture.Update with { Kind = TelegramAssistantUpdateKind.Message }, fixture.Actor, default);

        Assert.Equal(TelegramAssistantResultStatus.Unsupported, result.Status);
        Assert.Empty(fixture.Auth.TelegramProcessedUpdates);
        Assert.Equal(0, fixture.RefreshCalls);
    }

    [Fact]
    public async Task Disabled_backfill_completes_without_ingestion()
    {
        using var fixture = new Fixture();
        fixture.Options.AutoBackfillEnabled = false;
        var result = await fixture.HandleAsync();

        Assert.Empty(result.Messages);
        Assert.Equal("Feature133:AutomationSkipped", fixture.Claim.Status);
        Assert.Equal(0, fixture.RefreshCalls);
    }

    [Fact]
    public async Task Unresolved_company_stops_before_ingestion()
    {
        using var fixture = new Fixture();
        fixture.Company = null;
        await fixture.HandleAsync();

        Assert.Equal("Feature133:Failed", fixture.Claim.Status);
        Assert.Contains(fixture.Logs, x => x.Contains("SymbolUnresolved"));
        Assert.Equal(0, fixture.RefreshCalls);
    }

    [Fact]
    public async Task Failed_refresh_preserves_diagnostics_and_replays_without_repeating_ingestion()
    {
        using var fixture = new Fixture();
        fixture.RefreshError = "NoDataYet - missing company/month rows";
        var result = await fixture.HandleAsync();
        var replay = await fixture.HandleAsync();

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Empty(result.Messages);
        Assert.Equal("Feature133:Failed", fixture.Claim.Status);
        Assert.Contains(fixture.Logs, x => x.Contains(fixture.RefreshError) && x.Contains(fixture.RunId.ToString()));
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Empty(replay.Messages);
        Assert.Equal(1, fixture.RefreshCalls);
    }

    [Fact]
    public async Task Completed_refresh_without_a_fresh_snapshot_stops_before_querying()
    {
        using var fixture = new Fixture();
        await fixture.HandleAsync();

        Assert.Equal("Feature133:Failed", fixture.Claim.Status);
        Assert.Contains(fixture.Logs, x => x.Contains("RefreshNotReady"));
        Assert.Equal(1, fixture.RefreshCalls);
    }

    [Fact]
    public async Task In_progress_claim_replays_an_empty_message_collection()
    {
        using var fixture = new Fixture();
        fixture.Options.AutoBackfillEnabled = false;
        await fixture.HandleAsync();
        fixture.Claim.ResponseJson = "{}";
        await fixture.Auth.SaveChangesAsync();

        var result = await fixture.HandleAsync();

        Assert.Equal(TelegramAssistantResultStatus.Replayed, result.Status);
        Assert.NotNull(result.Messages);
        Assert.Empty(result.Messages);
        Assert.Equal(0, fixture.RefreshCalls);
    }

    [Fact]
    public async Task Cancelled_ingestion_propagates_cancellation()
    {
        using var fixture = new Fixture();
        fixture.CancelRefresh = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.HandleAsync());

        Assert.Equal("Feature133:Refreshing", fixture.Claim.Status);
        Assert.DoesNotContain(fixture.Logs, x => x.Contains("RefreshFailed"));
    }

    [Fact]
    public async Task Disabled_publishing_stops_after_a_successful_refresh()
    {
        using var fixture = new Fixture();
        fixture.HasFreshSnapshot = true;
        fixture.Options.AutoPublishTrendEnabled = false;
        var result = await fixture.HandleAsync();

        Assert.Equal("Feature133:BackfillOnlyCompleted", fixture.Claim.Status);
        Assert.Empty(result.Messages);
        Assert.Equal(0, fixture.QueryCalls);
    }

    [Fact]
    public async Task Successful_response_is_rendered_and_replayed_without_querying_again()
    {
        using var fixture = new Fixture();
        fixture.HasFreshSnapshot = true;
        var result = await fixture.HandleAsync();
        var replay = await fixture.HandleAsync();

        Assert.Equal("Feature133:Ready", fixture.Claim.Status);
        Assert.Equal("rendered trend", Assert.Single(result.Messages).Text);
        Assert.Equal(fixture.Actor.ActorId, result.ActorId);
        Assert.Equal(fixture.Actor.TenantId, result.TenantId);
        Assert.Equal("rendered trend", Assert.Single(replay.Messages).Text);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Equal(1, fixture.QueryCalls);
        Assert.Equal(1, fixture.RefreshCalls);
    }

    [Fact]
    public async Task Response_for_a_different_period_is_not_rendered()
    {
        using var fixture = new Fixture();
        fixture.HasFreshSnapshot = true;
        fixture.ResponseMonth = 6;
        var result = await fixture.HandleAsync();

        Assert.Empty(result.Messages);
        Assert.Equal("Feature133:Failed", fixture.Claim.Status);
        Assert.Contains(fixture.Logs, x => x.Contains("PeriodMismatch"));
        Assert.Equal(0, fixture.RenderCalls);
    }

    private sealed class Fixture : IDisposable
    {
        public AuthDbContext Auth { get; } = new(new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private FinancialIngestionDbContext Financial { get; } = new(
            new DbContextOptionsBuilder<FinancialIngestionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public CurrentActor Actor { get; } = new(ActorType.ApiClient, Guid.NewGuid(), Guid.NewGuid(),
            AuthenticationMode.ApiClient, ApiClientId: Guid.NewGuid());
        public TelegramAssistantUpdate Update { get; } = new(
            10, TelegramAssistantUpdateKind.ChannelPost, 0, -100123, null, 20,
            null, null, "#\n#کاوه\n\nگزارش #فعالیت_ماهانه (#فروردین_۱۴۰۵)  دوره ۱ ماهه منتهی به  ۱۴۰۵/۰1/۳۱(اصلاحیه)\nسال مالی منتهی به: ۱۴۰۵/۱۲/۲۹", "fa",
            DateTimeOffset.UtcNow, "test-correlation", "channel");
        public TelegramChannelMonthlyReportOptions Options { get; } = new()
        {
            AutoBackfillEnabled = true, AutoPublishTrendEnabled = true
        };
        public ResolvedCompany? Company { get; set; } = new(
            Guid.NewGuid(), "19", "کاوه", null, null, null, null, "کاوه");
        public Guid RunId { get; } = Guid.NewGuid();
        public string? RefreshError { get; set; }
        public bool CancelRefresh { get; set; }
        public int RefreshCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public int RenderCalls { get; private set; }
        public bool HasFreshSnapshot { get; set; }
        public int ResponseMonth { get; set; } = 1;
        public List<string> Logs { get; } = [];
        public TelegramProcessedUpdateRow Claim => Auth.TelegramProcessedUpdates.Single();
        public TelegramChannelMonthlyReportHandler Handler { get; }

        public Fixture()
        {
            Financial.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
            {
                Id = Guid.NewGuid(), ExternalCompanyId = "19", ProviderName = "NoavaranCurrentApi", Name = "test"
            });
            Financial.SaveChanges();
            var companies = Stub<ICompanyResolverService>(_ => Task.FromResult(Company));
            var ingestion = Stub<ISingleCompanyMonthlyIngestionService>(method =>
            {
                Assert.Equal("ExecuteDirectAsync", method.Name);
                RefreshCalls++;
                if (CancelRefresh)
                    throw new OperationCanceledException();
                var now = DateTimeOffset.UtcNow;
                return Task.FromResult(new DataSyncProcessingResult(new DataSyncRun(
                    RunId, "test-run", ProviderDataset.MonthlyProductionSales, "19",
                    RefreshError is null ? DataSyncRunStatus.Completed : DataSyncRunStatus.Failed,
                    now, now, now, 1, RefreshError is null ? 0 : 1, RefreshError, null), false));
            });
            var snapshots = Stub<ICompanyMonthlyActivityTrendSnapshotRepository>(method =>
            {
                Assert.Equal("GetCompanyTrendAsync", method.Name);
                IReadOnlyList<CompanyMonthlyActivityTrendSnapshot> rows = HasFreshSnapshot
                    ? [new("19", "کاوه", "test", 1405, 1,
                        null, null, null, null, 100, null, null, null, false, null,
                        null, null, null, null, 0, null, null, null, null, null, null,
                        "NoavaranCurrentApi", false, false, 1, DateTimeOffset.UtcNow)]
                    : [];
                return Task.FromResult(rows);
            });
            var orchestration = Stub<IAiQueryOrchestrationService>(method =>
            {
                Assert.Equal("ExecuteAsync", method.Name);
                Assert.True(HasFreshSnapshot && Options.AutoPublishTrendEnabled);
                QueryCalls++;
                var trend = new MonthlyActivityTrendResponse("کاوه", "test", 1405, ResponseMonth, "test",
                    100, null, null, null, null, null, null, [], [], [], "NoavaranCurrentApi", DateTimeOffset.UtcNow);
                return Task.FromResult(new AiQueryResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    default, null, null, null, null, null, null, false, null, null, MonthlyActivityTrendResult: trend));
            });
            var conversations = Stub<FinancialCopilot.Application.Conversations.IConversationRepository>(method =>
            {
                Assert.Equal("CreateEmptyAsync", method.Name);
                Assert.True(HasFreshSnapshot && Options.AutoPublishTrendEnabled);
                return Task.FromResult(Guid.NewGuid());
            });
            var renderer = Stub<ITelegramAssistantResponseRenderer>(method =>
            {
                if (method.Name == "get_Version")
                    return "test-renderer";
                Assert.Equal("Render", method.Name);
                RenderCalls++;
                return new TelegramAssistantRenderedMessage[] { new(1, 1, "rendered trend") };
            });
            Handler = new TelegramChannelMonthlyReportHandler(
                Auth, Financial, companies, ingestion, snapshots,
                orchestration, conversations, renderer, new TelegramMonthlyReportRecognizer(),
                Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System, new RecordingLogger(Logs));
        }

        public Task<TelegramAssistantResult> HandleAsync() => Handler.HandleAsync(Update, Actor, default);
        public void Dispose()
        {
            Auth.Dispose();
            Financial.Dispose();
        }
    }

    public class ServiceStub : DispatchProxy
    {
        public Func<MethodInfo, object?> InvokeMethod { get; set; } =
            method => throw new InvalidOperationException($"Unexpected call to {method.Name}");
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!);
    }

    private static T Stub<T>(Func<MethodInfo, object?>? handler = null) where T : class
    {
        var stub = DispatchProxy.Create<T, ServiceStub>();
        if (handler is not null)
            ((ServiceStub)(object)stub).InvokeMethod = handler;
        return stub;
    }

    private sealed class RecordingLogger(List<string> entries) : ILogger<TelegramChannelMonthlyReportHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Add(formatter(state, exception));
    }
}

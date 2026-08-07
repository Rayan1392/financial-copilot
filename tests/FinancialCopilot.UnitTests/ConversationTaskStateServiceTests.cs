using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class ConversationTaskStateServiceTests
{
    private static readonly ConversationTaskStateScope Scope = new(Guid.Parse("3b37e9c7-9849-4966-882a-9a2f4199067f"), Guid.Parse("867c6d36-87bb-4c5f-99ea-8dde945190cb"), Guid.Parse("fc6156e4-2ca0-4a4f-9379-0dbfa2466737"));

    [Fact]
    public async Task ChartFollowUp_CarriesOnlyCompatibleCanonicalSlotsWithConversationProvenance()
    {
        var service = Create(out _);
        await service.RecordAnsweredAsync(Scope, "monthly_activity_trend", [Slot(QuerySlotType.CompanyOrSymbol, "فولاد", Guid.NewGuid()), Slot(QuerySlotType.Metric, "MONTHLY_SALES")], Guid.NewGuid(), "first", default);

        var transition = await service.ResolveFollowUpAsync(Scope, null, [Slot(QuerySlotType.Presentation, "Chart")], Guid.NewGuid(), "second", default);

        Assert.Equal(ConversationTaskStateTransitionKind.Answered, transition.Kind);
        Assert.Equal("monthly_activity_trend", transition.Current!.ActiveCapability);
        Assert.Equal(QueryValueProvenance.ConversationInferred, transition.Current.FindSlot(QuerySlotType.CompanyOrSymbol)!.Provenance);
        Assert.Equal("Chart", transition.Current.FindSlot(QuerySlotType.Presentation)!.Value);
    }

    [Fact]
    public async Task ExplicitTaskSwitch_DoesNotCarryStaleSymbolOrPresentation()
    {
        var service = Create(out _);
        await service.RecordAnsweredAsync(Scope, "monthly_activity_trend", [Slot(QuerySlotType.CompanyOrSymbol, "فولاد", Guid.NewGuid()), Slot(QuerySlotType.Presentation, "Chart")], Guid.NewGuid(), "first", default);

        var transition = await service.ResolveFollowUpAsync(Scope, "symbol_metric_lookup", [Slot(QuerySlotType.CompanyOrSymbol, "فملی", Guid.NewGuid()), Slot(QuerySlotType.Metric, "PE_TTM")], Guid.NewGuid(), "second", default);

        Assert.Equal(ConversationTaskStateTransitionKind.TaskSwitched, transition.Kind);
        Assert.Equal("فملی", transition.Current!.FindSlot(QuerySlotType.CompanyOrSymbol)!.Value);
        Assert.Null(transition.Current.FindSlot(QuerySlotType.Presentation));
    }

    [Fact]
    public async Task PendingClarification_IsResolvedByExpectedSymbolReply()
    {
        var service = Create(out _);
        var message = Guid.NewGuid();
        await service.RecordPendingAsync(Scope, "monthly_activity_trend", [], new(PendingDialogueActionKind.Clarification, QuerySlotType.CompanyOrSymbol, [], "required_input_missing", message, 1), "first", default);

        var transition = await service.ResolveFollowUpAsync(Scope, null, [Slot(QuerySlotType.CompanyOrSymbol, "فولاد", Guid.NewGuid())], Guid.NewGuid(), "second", default);

        Assert.Equal(ConversationTaskStateTransitionKind.ClarificationResolved, transition.Kind);
        Assert.Null(transition.Current!.PendingAction);
    }

    [Fact]
    public async Task ExpiredState_IsDeletedAndNeverCarried()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-07T10:00:00Z"));
        var service = Create(out var repository, clock, new(ExpiryMinutes: 1));
        await service.RecordAnsweredAsync(Scope, "monthly_activity_trend", [Slot(QuerySlotType.CompanyOrSymbol, "فولاد", Guid.NewGuid())], Guid.NewGuid(), "first", default);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await service.GetActiveAsync(Scope, default));
        Assert.Null(await repository.FindAsync(Scope, default));
    }

    [Fact]
    public async Task CorrelationReplay_DoesNotCreateANewTransition()
    {
        var service = Create(out _);
        await service.RecordAnsweredAsync(Scope, "monthly_activity_trend", [Slot(QuerySlotType.CompanyOrSymbol, "فولاد", Guid.NewGuid())], Guid.NewGuid(), "same", default);

        var replay = await service.RecordAnsweredAsync(Scope, "monthly_activity_trend", [Slot(QuerySlotType.CompanyOrSymbol, "فولاد", Guid.NewGuid())], Guid.NewGuid(), "same", default);

        Assert.Equal(ConversationTaskStateTransitionKind.Replay, replay.Kind);
        Assert.Equal(1, replay.Current!.Version);
    }

    [Fact]
    public async Task PeriodRefinement_OverridesStateWhilePreservingTheActiveTrend()
    {
        var service = Create(out _);
        await service.RecordAnsweredAsync(Scope, "monthly_activity_trend", [Slot(QuerySlotType.CompanyOrSymbol, "فولاد", Guid.NewGuid()), Slot(QuerySlotType.Period, "default")], Guid.NewGuid(), "first", default);

        var transition = await service.ResolveFollowUpAsync(Scope, null, [Slot(QuerySlotType.Period, "one_year")], Guid.NewGuid(), "second", default);

        Assert.Equal("one_year", transition.Current!.FindSlot(QuerySlotType.Period)!.Value);
        Assert.Equal("فولاد", transition.Current.FindSlot(QuerySlotType.CompanyOrSymbol)!.Value);
    }

    [Fact]
    public async Task ConcurrentExpectedVersions_AllowOnlyOneWriter()
    {
        var repository = new InMemoryRepository();
        var initial = new ConversationTaskState(Scope.ConversationId, Scope.TenantId, Scope.ActorId, 1, "monthly_activity_trend", [], null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.True((await repository.TryWriteAsync(initial, null, default)).Succeeded);

        var first = initial with { Version = 2, LastCorrelationId = "a" };
        var second = initial with { Version = 2, LastCorrelationId = "b" };
        Assert.True((await repository.TryWriteAsync(first, 1, default)).Succeeded);
        Assert.False((await repository.TryWriteAsync(second, 1, default)).Succeeded);
    }

    [Fact]
    public async Task StateIsStrictlyTenantActorAndConversationIsolated()
    {
        var service = Create(out _);
        await service.RecordAnsweredAsync(Scope, "monthly_activity_trend", [Slot(QuerySlotType.CompanyOrSymbol, "فولاد", Guid.NewGuid())], Guid.NewGuid(), "first", default);

        Assert.Null(await service.GetActiveAsync(Scope with { ActorId = Guid.NewGuid() }, default));
        Assert.Null(await service.GetActiveAsync(Scope with { TenantId = Guid.NewGuid() }, default));
        Assert.Null(await service.GetActiveAsync(Scope with { ConversationId = Guid.NewGuid() }, default));
    }

    private static ConversationTaskSlot Slot(QuerySlotType type, string value, Guid? id = null) => new(type, value, id, QueryValueProvenance.UserExplicit, 1m, Guid.NewGuid(), 0);
    private static ConversationTaskStateService Create(out InMemoryRepository repository, TimeProvider? clock = null, ConversationTaskStateOptions? options = null)
    {
        repository = new();
        return new(repository, clock ?? TimeProvider.System, options ?? new());
    }

    private sealed class InMemoryRepository : IConversationTaskStateRepository
    {
        private readonly Dictionary<ConversationTaskStateScope, ConversationTaskState> states = [];
        public Task<ConversationTaskState?> FindAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken) => Task.FromResult(states.GetValueOrDefault(scope));
        public Task<ConversationTaskStateWriteResult> TryWriteAsync(ConversationTaskState state, long? expectedVersion, CancellationToken cancellationToken)
        {
            var key = new ConversationTaskStateScope(state.ConversationId, state.TenantId, state.ActorId);
            if (states.TryGetValue(key, out var existing) && existing.Version != expectedVersion) return Task.FromResult(new ConversationTaskStateWriteResult(false, null));
            if (existing is null && expectedVersion is not null) return Task.FromResult(new ConversationTaskStateWriteResult(false, null));
            states[key] = state;
            return Task.FromResult(new ConversationTaskStateWriteResult(true, state));
        }
        public Task DeleteAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken) { states.Remove(scope); return Task.CompletedTask; }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset value = now;
        public override DateTimeOffset GetUtcNow() => value;
        public void Advance(TimeSpan amount) => value += amount;
    }
}

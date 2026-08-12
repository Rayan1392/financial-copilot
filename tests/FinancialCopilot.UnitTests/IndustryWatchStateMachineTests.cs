using FinancialCopilot.Domain.Financial.RelativeValuation;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryWatchStateMachineTests
{
    private static readonly IndustryWatchSnapshot Entry = new(true, true, 2, 99m, 2, 99m, 2, 99m);
    private static readonly IndustryWatchSnapshot Exit = new(true, true, 2, 101m, 2, 101m, 2, 101m);
    private static readonly IndustryWatchSnapshot Neutral = new(true, true, 2, 100m, 2, 99m, 2, 99m);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Entry_thresholds_are_exact_and_do_not_advance_exit(int threshold)
    {
        var state = IndustryWatchState.NotWatching;
        var entry = 0;
        for (var day = 1; day <= threshold; day++)
        {
            var result = IndustryWatchStateMachine.Evaluate(state, entry, 0, Entry,
                new IndustryWatchOptions(threshold, 3));
            state = result.NewState;
            entry = result.NewEntryStreak;
        }

        Assert.Equal(IndustryWatchState.Watching, state);
        Assert.Equal(threshold, entry);
    }

    [Fact]
    public void First_qualifying_entry_day_is_pending_until_threshold()
    {
        var result = IndustryWatchStateMachine.Evaluate(IndustryWatchState.NotWatching, 0, 0, Entry, new(3, 3));
        Assert.Equal(IndustryWatchState.EntryPending, result.NewState);
        Assert.Equal(1, result.NewEntryStreak);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Exit_thresholds_are_exact_and_do_not_advance_entry(int threshold)
    {
        var state = IndustryWatchState.Watching;
        var exit = 0;
        for (var day = 1; day <= threshold; day++)
        {
            var result = IndustryWatchStateMachine.Evaluate(state, 0, exit, Exit,
                new IndustryWatchOptions(3, threshold));
            state = result.NewState;
            exit = result.NewExitStreak;
        }

        Assert.Equal(IndustryWatchState.NotWatching, state);
        Assert.Equal(threshold, exit);
    }

    [Fact]
    public void Neutral_day_resets_pending_to_applicable_stable_state()
    {
        var entry = IndustryWatchStateMachine.Evaluate(IndustryWatchState.EntryPending, 2, 0, Neutral, new());
        var exit = IndustryWatchStateMachine.Evaluate(IndustryWatchState.ExitPending, 0, 2, Neutral, new());
        Assert.Equal(IndustryWatchState.NotWatching, entry.NewState);
        Assert.Equal(IndustryWatchState.Watching, exit.NewState);
        Assert.Equal(0, entry.NewEntryStreak + entry.NewExitStreak + exit.NewEntryStreak + exit.NewExitStreak);
    }

    [Fact]
    public void Inconclusive_pauses_without_resetting_or_changing_state()
    {
        var result = IndustryWatchStateMachine.Evaluate(
            IndustryWatchState.EntryPending, 2, 0,
            new IndustryWatchSnapshot(true, true, 1, 99m, 2, 99m, 2, 99m), new());
        Assert.Equal(IndustryWatchEvaluationOutcome.Inconclusive, result.Outcome);
        Assert.Equal(IndustryWatchState.EntryPending, result.NewState);
        Assert.Equal(2, result.NewEntryStreak);
        Assert.Equal(0, result.NewExitStreak);
    }

    [Fact]
    public void Exact_100_is_neutral_even_when_each_metric_is_exact()
    {
        var result = IndustryWatchStateMachine.Evaluate(IndustryWatchState.EntryPending, 2, 0,
            new(true, true, 2, 100m, 2, 100m, 2, 100m), new());
        Assert.Equal(IndustryWatchEvaluationOutcome.Neutral, result.Outcome);
        Assert.Equal(IndustryWatchState.NotWatching, result.NewState);
    }

    [Fact]
    public void Thresholds_are_bounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IndustryWatchStateMachine.Evaluate(
            IndustryWatchState.NotWatching, 0, 0, Entry, new(0, 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndustryWatchStateMachine.Evaluate(
            IndustryWatchState.NotWatching, 0, 0, Entry, new(3, 31)));
    }
}

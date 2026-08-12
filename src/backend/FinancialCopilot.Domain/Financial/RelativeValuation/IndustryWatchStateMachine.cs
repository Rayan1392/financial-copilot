namespace FinancialCopilot.Domain.Financial.RelativeValuation;

public enum IndustryWatchState
{
    NotWatching,
    EntryPending,
    Watching,
    ExitPending
}

public enum IndustryWatchEvaluationOutcome
{
    EntryQualifying,
    ExitQualifying,
    Neutral,
    Inconclusive
}

public sealed class IndustryWatchOptions
{
    public IndustryWatchOptions() { }

    public IndustryWatchOptions(int entryConsecutiveSnapshots, int exitConsecutiveSnapshots)
    {
        EntryConsecutiveSnapshots = entryConsecutiveSnapshots;
        ExitConsecutiveSnapshots = exitConsecutiveSnapshots;
    }

    public int EntryConsecutiveSnapshots { get; set; } = 3;
    public int ExitConsecutiveSnapshots { get; set; } = 3;

    public void Validate()
    {
        if (EntryConsecutiveSnapshots is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(EntryConsecutiveSnapshots), "Watch thresholds must be between 1 and 30.");
        if (ExitConsecutiveSnapshots is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(ExitConsecutiveSnapshots), "Watch thresholds must be between 1 and 30.");
    }
}

public sealed record IndustryWatchSnapshot(
    bool IsPublished,
    bool IsSelectedCurrent,
    int PeCleanCount,
    decimal? PeBenchmark,
    int PsCleanCount,
    decimal? PsBenchmark,
    int EquilibriumCleanCount,
    decimal? EquilibriumBenchmark)
{
    public bool IsValidDay => IsPublished && IsSelectedCurrent
        && PeCleanCount >= 2 && PeBenchmark is not null
        && PsCleanCount >= 2 && PsBenchmark is not null
        && EquilibriumCleanCount >= 2 && EquilibriumBenchmark is not null;

    public IndustryWatchEvaluationOutcome Outcome
    {
        get
        {
            if (!IsValidDay) return IndustryWatchEvaluationOutcome.Inconclusive;
            if (PeBenchmark < 100m && PsBenchmark < 100m && EquilibriumBenchmark < 100m)
                return IndustryWatchEvaluationOutcome.EntryQualifying;
            if (PeBenchmark > 100m && PsBenchmark > 100m && EquilibriumBenchmark > 100m)
                return IndustryWatchEvaluationOutcome.ExitQualifying;
            return IndustryWatchEvaluationOutcome.Neutral;
        }
    }
}

public sealed record IndustryWatchTransition(
    IndustryWatchState PreviousState,
    IndustryWatchState NewState,
    int PreviousEntryStreak,
    int NewEntryStreak,
    int PreviousExitStreak,
    int NewExitStreak,
    IndustryWatchEvaluationOutcome Outcome,
    string Reason);

public static class IndustryWatchStateMachine
{
    public static IndustryWatchTransition EvaluateOutcome(
        IndustryWatchState state, int entryStreak, int exitStreak,
        IndustryWatchEvaluationOutcome outcome, IndustryWatchOptions options)
    {
        options.Validate();
        if (entryStreak < 0 || exitStreak < 0) throw new ArgumentOutOfRangeException(nameof(entryStreak));
        if (entryStreak > 0 && exitStreak > 0) throw new ArgumentException("Entry and exit streaks are mutually exclusive.");
        if (outcome == IndustryWatchEvaluationOutcome.Inconclusive)
            return new(state, state, entryStreak, entryStreak, exitStreak, exitStreak, outcome, "InconclusivePaused");
        if (outcome == IndustryWatchEvaluationOutcome.Neutral)
        {
            var stable = state switch { IndustryWatchState.EntryPending => IndustryWatchState.NotWatching, IndustryWatchState.ExitPending => IndustryWatchState.Watching, _ => state };
            return new(state, stable, 0, 0, 0, 0, outcome, "NeutralReset");
        }
        if ((state == IndustryWatchState.NotWatching || state == IndustryWatchState.EntryPending) && outcome == IndustryWatchEvaluationOutcome.EntryQualifying)
        {
            var next = entryStreak + 1;
            var nextState = next >= options.EntryConsecutiveSnapshots ? IndustryWatchState.Watching : IndustryWatchState.EntryPending;
            return new(state, nextState, entryStreak, next, 0, 0, outcome, nextState == IndustryWatchState.Watching ? "EntryThresholdReached" : "EntryQualifyingDay");
        }
        if ((state == IndustryWatchState.Watching || state == IndustryWatchState.ExitPending) && outcome == IndustryWatchEvaluationOutcome.ExitQualifying)
        {
            var next = exitStreak + 1;
            var nextState = next >= options.ExitConsecutiveSnapshots ? IndustryWatchState.NotWatching : IndustryWatchState.ExitPending;
            return new(state, nextState, 0, 0, exitStreak, next, outcome, nextState == IndustryWatchState.NotWatching ? "ExitThresholdReached" : "ExitQualifyingDay");
        }
        return new(state, state, 0, 0, 0, 0, IndustryWatchEvaluationOutcome.Neutral, "NotApplicableReset");
    }

    public static IndustryWatchTransition Evaluate(
        IndustryWatchState state,
        int entryStreak,
        int exitStreak,
        IndustryWatchSnapshot snapshot,
        IndustryWatchOptions options)
    {
        return EvaluateOutcome(state, entryStreak, exitStreak, snapshot.Outcome, options);
    }
}

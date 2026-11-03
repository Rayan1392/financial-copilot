using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed record IndustryWatchEvaluationResult(
    Guid GroupId, Guid CalculationId, IndustryWatchEvaluationOutcome Outcome,
    IndustryWatchState State, int EntryStreak, int ExitStreak, bool NoOp);

/// <summary>Evaluates only the selected Published calculation for one comparison group.</summary>
public sealed class IndustryWatchEvaluationService(
    FinancialIngestionDbContext db, IndustryWatchOptions options, ILogger<IndustryWatchEvaluationService>? logger = null)
{
    private const string AlgorithmVersion = "watch-state-v1";

    public async Task<IndustryWatchEvaluationResult?> EvaluateAsync(
        Guid groupId, Guid calculationId, string evaluationKind,
        DateTimeOffset evaluatedAtUtc, CancellationToken cancellationToken = default,
        bool manageTransaction = true)
    {
        var started = Stopwatch.GetTimestamp();
        options.Validate();
        var calculation = await db.IndustryRelativeValuationCalculations
            .SingleOrDefaultAsync(x => x.Id == calculationId && x.GroupId == groupId
                && x.Status == "Published" && x.IsSelectedCurrent, cancellationToken);
        if (calculation is null)
        {
            logger?.LogWarning("Feature 125 watch evaluation unavailable for group {GroupId}, calculation {CalculationId}: selected Published snapshot not found.", groupId, calculationId);
            return null;
        }

        await using var transaction = manageTransaction && db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var lockKey = $"industry-group-watch:{groupId:D}:{evaluationKind}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        }

        var duplicate = await db.IndustryWatchEvaluations.AnyAsync(x =>
            x.GroupId == groupId && x.CalculationId == calculationId && x.EvaluationKind == evaluationKind,
            cancellationToken);
        if (duplicate)
        {
            var existingState = await GetStateAsync(groupId, calculation.IndustryId, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(groupId, calculationId, IndustryWatchEvaluationOutcome.Neutral,
                ParseState(existingState.State), existingState.EntryStreak, existingState.ExitStreak, true);
        }

        var metrics = await db.IndustryRelativeValuationMetrics
            .Where(x => x.CalculationId == calculationId).ToDictionaryAsync(x => x.MetricKind, cancellationToken);
        var snapshot = new IndustryWatchSnapshot(true, true,
            Value(metrics, "Pe", x => x.CleanCount), Value(metrics, "Pe", x => x.CleanAverage),
            Value(metrics, "Ps", x => x.CleanCount), Value(metrics, "Ps", x => x.CleanAverage),
            Value(metrics, "Equilibrium", x => x.CleanCount), Value(metrics, "Equilibrium", x => x.CleanAverage));
        var outcome = snapshot.Outcome;

        var prior = await db.IndustryWatchEvaluations
            .Join(db.IndustryRelativeValuationCalculations, e => e.CalculationId, c => c.Id,
                (e, c) => new { Evaluation = e, Calculation = c })
            .Where(x => x.Evaluation.GroupId == groupId && x.Evaluation.EvaluationKind == evaluationKind
                && x.Calculation.CalculationDate == calculation.CalculationDate)
            .ToListAsync(cancellationToken);
        foreach (var old in prior) old.Evaluation.IsEffective = false;

        var evaluation = new IndustryWatchEvaluationRow
        {
            Id = Guid.NewGuid(), GroupId = groupId, IndustryId = calculation.IndustryId, CalculationId = calculationId,
            EvaluationKind = evaluationKind, Outcome = outcome.ToString(), EvaluatedAtUtc = evaluatedAtUtc,
            CalculationDate = calculation.CalculationDate, AlgorithmVersion = AlgorithmVersion, IsEffective = true
        };
        db.IndustryWatchEvaluations.Add(evaluation);
        await db.SaveChangesAsync(cancellationToken);

        var state = await GetStateAsync(groupId, calculation.IndustryId, cancellationToken);
        var before = (ParseState(state.State), state.EntryStreak, state.ExitStreak);
        var effective = await db.IndustryWatchEvaluations
            .Join(db.IndustryRelativeValuationCalculations, e => e.CalculationId, c => c.Id,
                (e, c) => new { Evaluation = e, Calculation = c })
            .Where(x => x.Evaluation.GroupId == groupId && x.Evaluation.EvaluationKind == evaluationKind
                && x.Evaluation.IsEffective && x.Calculation.Status == "Published" && x.Calculation.IsSelectedCurrent)
            .OrderBy(x => x.Calculation.CalculationDate)
            .ThenBy(x => x.Calculation.Id)
            .ToListAsync(cancellationToken);

        var replayState = IndustryWatchState.NotWatching;
        var entryStreak = 0;
        var exitStreak = 0;
        IndustryWatchTransition? currentTransition = null;
        foreach (var item in effective)
        {
            var transition = IndustryWatchStateMachine.EvaluateOutcome(
                replayState, entryStreak, exitStreak, ParseOutcome(item.Evaluation.Outcome), options);
            if (item.Calculation.Id == calculationId) currentTransition = transition;
            replayState = transition.NewState; entryStreak = transition.NewEntryStreak; exitStreak = transition.NewExitStreak;
        }
        currentTransition ??= IndustryWatchStateMachine.EvaluateOutcome(before.Item1, before.Item2, before.Item3, outcome, options);

        evaluation.PreviousState = currentTransition.PreviousState.ToString();
        evaluation.NewState = currentTransition.NewState.ToString();
        evaluation.PreviousEntryStreak = currentTransition.PreviousEntryStreak;
        evaluation.NewEntryStreak = currentTransition.NewEntryStreak;
        evaluation.PreviousExitStreak = currentTransition.PreviousExitStreak;
        evaluation.NewExitStreak = currentTransition.NewExitStreak;
        evaluation.TransitionReason = currentTransition.Reason;

        state.State = replayState.ToString(); state.EntryStreak = entryStreak; state.ExitStreak = exitStreak;
        state.LastEvaluatedCalculationId = calculationId; state.LastTransitionDate = calculation.CalculationDate;
        state.LastTransitionReason = currentTransition.Reason; state.AlgorithmVersion = AlgorithmVersion;
        if (currentTransition.PreviousState != currentTransition.NewState)
            db.IndustryWatchTransitions.Add(new IndustryWatchTransitionRow
            {
                Id = Guid.NewGuid(), GroupId = groupId, IndustryId = calculation.IndustryId, CalculationId = calculationId,
                EvaluationKind = evaluationKind, PreviousState = currentTransition.PreviousState.ToString(),
                NextState = currentTransition.NewState.ToString(), EvaluationOutcome = currentTransition.Outcome.ToString(),
                PreviousEntryStreak = currentTransition.PreviousEntryStreak, NewEntryStreak = currentTransition.NewEntryStreak,
                PreviousExitStreak = currentTransition.PreviousExitStreak, NewExitStreak = currentTransition.NewExitStreak,
                TransitionDate = calculation.CalculationDate, Reason = currentTransition.Reason,
                AlgorithmVersion = AlgorithmVersion, EventIdentity = $"{groupId:D}:{calculationId:D}:{evaluationKind}",
                CreatedAtUtc = evaluatedAtUtc
            });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        logger?.LogInformation("Feature 125 watch evaluation completed for group {GroupId}, calculation {CalculationId}: outcome {Outcome}, state {State}, entry streak {EntryStreak}, exit streak {ExitStreak}, elapsed {ElapsedMs} ms.", groupId, calculationId, outcome, replayState, entryStreak, exitStreak, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new(groupId, calculationId, outcome, replayState, entryStreak, exitStreak, false);
    }

    private async Task<IndustryWatchStateRow> GetStateAsync(
        Guid groupId,
        Guid industryId,
        CancellationToken cancellationToken)
    {
        var state = await db.IndustryWatchStates.SingleOrDefaultAsync(x => x.GroupId == groupId, cancellationToken);
        if (state is not null) return state;
        state = new IndustryWatchStateRow
        {
            Id = Guid.NewGuid(), GroupId = groupId, IndustryId = industryId, AlgorithmVersion = AlgorithmVersion
        };
        db.IndustryWatchStates.Add(state); await db.SaveChangesAsync(cancellationToken); return state;
    }
    private static int Value(IReadOnlyDictionary<string, IndustryRelativeValuationMetricRow> m, string k, Func<IndustryRelativeValuationMetricRow, int> s) => m.TryGetValue(k, out var r) ? s(r) : 0;
    private static decimal? Value(IReadOnlyDictionary<string, IndustryRelativeValuationMetricRow> m, string k, Func<IndustryRelativeValuationMetricRow, decimal?> s) => m.TryGetValue(k, out var r) ? s(r) : null;
    private static IndustryWatchState ParseState(string value) => Enum.TryParse<IndustryWatchState>(value, out var state) ? state : IndustryWatchState.NotWatching;
    private static IndustryWatchEvaluationOutcome ParseOutcome(string value) => Enum.TryParse<IndustryWatchEvaluationOutcome>(value, out var outcome) ? outcome : IndustryWatchEvaluationOutcome.Inconclusive;
}

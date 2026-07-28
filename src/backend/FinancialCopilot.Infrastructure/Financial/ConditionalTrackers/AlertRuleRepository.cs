using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.ConditionalTrackers;

public sealed class EfCoreAlertRuleRepository(
    FinancialIngestionDbContext dbContext) : IAlertRuleRepository
{
    public async Task<IReadOnlyCollection<AlertRuleSnapshot>> GetAsync(
        AlertRuleActor actor,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AlertRules.AsNoTracking().Where(row =>
            row.TenantId == actor.TenantId &&
            row.ActorId == actor.ActorId &&
            row.ActorType == actor.ActorType);
        if (!includeRemoved)
            query = query.Where(row => row.State != nameof(AlertRuleState.Removed));

        var rows = await query.OrderByDescending(row => row.UpdatedAtUtc).ToArrayAsync(cancellationToken);
        return await LoadSnapshotsAsync(rows, cancellationToken);
    }

    public async Task<AlertRuleSnapshot?> FindAsync(
        AlertRuleActor actor,
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.AlertRules.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == ruleId &&
            item.TenantId == actor.TenantId &&
            item.ActorId == actor.ActorId &&
            item.ActorType == actor.ActorType,
            cancellationToken);
        if (row is null) return null;
        return (await LoadSnapshotsAsync([row], cancellationToken)).Single();
    }

    public async Task<AlertRuleSnapshot?> FindByIdempotencyKeyAsync(
        AlertRuleActor actor,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = idempotencyKey.Trim();
        var row = await dbContext.AlertRules.AsNoTracking().SingleOrDefaultAsync(item =>
            item.TenantId == actor.TenantId &&
            item.ActorId == actor.ActorId &&
            item.ActorType == actor.ActorType &&
            item.IdempotencyKey == key,
            cancellationToken);
        if (row is null) return null;
        return (await LoadSnapshotsAsync([row], cancellationToken)).Single();
    }

    public Task<int> CountLiveAsync(AlertRuleActor actor, CancellationToken cancellationToken) =>
        dbContext.AlertRules.CountAsync(row =>
            row.TenantId == actor.TenantId &&
            row.ActorId == actor.ActorId &&
            row.ActorType == actor.ActorType &&
            row.State != nameof(AlertRuleState.Removed) &&
            row.State != nameof(AlertRuleState.Completed),
            cancellationToken);

    public async Task SaveAsync(
        AlertRule rule,
        AlertRuleEvaluationState evaluationState,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.AlertRules.SingleOrDefaultAsync(item => item.Id == rule.Id, cancellationToken);
        if (row is null)
        {
            dbContext.AlertRules.Add(ToRow(rule));
        }
        else
        {
            Apply(rule, row);
        }

        var stateRow = await dbContext.AlertRuleEvaluationStates
            .SingleOrDefaultAsync(item => item.RuleId == rule.Id, cancellationToken);
        if (stateRow is null)
        {
            dbContext.AlertRuleEvaluationStates.Add(ToRow(evaluationState));
        }
        else
        {
            var originalToken = stateRow.ConcurrencyToken;
            Apply(evaluationState, stateRow);
            dbContext.Entry(stateRow).Property(item => item.ConcurrencyToken).OriginalValue = originalToken;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<AlertRuleSnapshot>> LoadSnapshotsAsync(
        IReadOnlyCollection<AlertRuleRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(row => row.Id).ToArray();
        var states = await dbContext.AlertRuleEvaluationStates.AsNoTracking()
            .Where(row => ids.Contains(row.RuleId))
            .ToDictionaryAsync(row => row.RuleId, cancellationToken);
        return rows.Select(row =>
        {
            var rule = ToDomain(row);
            var state = states.TryGetValue(row.Id, out var persisted)
                ? ToDomain(persisted)
                : AlertRuleEvaluationState.Create(row.Id);
            return new AlertRuleSnapshot(rule, state);
        }).ToArray();
    }

    internal static AlertRule ToDomain(AlertRuleRow row) =>
        AlertRule.Rehydrate(
            row.Id,
            new AlertRuleActor(row.TenantId, row.ActorId, row.ActorType),
            row.ExternalCompanyId,
            new AlertRuleDefinition(
                Enum.Parse<AlertRuleType>(row.RuleType),
                row.MetricOrEventCode,
                Enum.Parse<AlertRuleOperator>(row.Operator),
                row.Threshold,
                Enum.Parse<AlertRuleUnit>(row.Unit),
                row.BaselineWindow,
                Enum.Parse<AlertRuleRecurrence>(row.Recurrence),
                TimeSpan.FromMinutes(row.CooldownMinutes),
                Enum.Parse<AlertRuleResetPolicy>(row.ResetPolicy),
                Enum.Parse<AlertRuleSessionPolicy>(row.SessionPolicy),
                row.Hysteresis),
            Enum.Parse<AlertRuleState>(row.State),
            row.Version,
            row.OriginalText,
            row.ParserVersion,
            row.ConfirmationNonce,
            row.ConfirmationExpiresAtUtc,
            row.IdempotencyKey,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.RemovedAtUtc);

    internal static AlertRuleEvaluationState ToDomain(AlertRuleEvaluationStateRow row) =>
        AlertRuleEvaluationState.Rehydrate(
            row.RuleId,
            row.LastValue,
            row.LastObservedAtUtc,
            row.LastEvidenceIdentity,
            row.Armed,
            row.TriggerSequence,
            row.LastTriggeredAtUtc,
            row.CooldownEndsAtUtc,
            row.ConcurrencyToken);

    internal static AlertRuleRow ToRow(AlertRule rule)
    {
        var row = new AlertRuleRow { Id = rule.Id };
        Apply(rule, row);
        return row;
    }

    internal static AlertRuleEvaluationStateRow ToRow(AlertRuleEvaluationState state)
    {
        var row = new AlertRuleEvaluationStateRow { RuleId = state.RuleId };
        Apply(state, row);
        return row;
    }

    internal static void Apply(AlertRule rule, AlertRuleRow row)
    {
        row.TenantId = rule.Actor.TenantId;
        row.ActorId = rule.Actor.ActorId;
        row.ActorType = rule.Actor.ActorType;
        row.ExternalCompanyId = rule.ExternalCompanyId;
        row.RuleType = rule.Definition.RuleType.ToString();
        row.MetricOrEventCode = rule.Definition.MetricOrEventCode;
        row.Operator = rule.Definition.Operator.ToString();
        row.Threshold = rule.Definition.Threshold;
        row.Unit = rule.Definition.Unit.ToString();
        row.BaselineWindow = rule.Definition.BaselineWindow;
        row.Recurrence = rule.Definition.Recurrence.ToString();
        row.CooldownMinutes = checked((int)rule.Definition.Cooldown.TotalMinutes);
        row.ResetPolicy = rule.Definition.ResetPolicy.ToString();
        row.SessionPolicy = rule.Definition.SessionPolicy.ToString();
        row.Hysteresis = rule.Definition.Hysteresis;
        row.State = rule.State.ToString();
        row.Version = rule.Version;
        row.OriginalText = rule.OriginalText;
        row.ParserVersion = rule.ParserVersion;
        row.ConfirmationNonce = rule.ConfirmationNonce;
        row.ConfirmationExpiresAtUtc = rule.ConfirmationExpiresAtUtc;
        row.IdempotencyKey = rule.IdempotencyKey;
        row.CreatedAtUtc = rule.CreatedAtUtc;
        row.UpdatedAtUtc = rule.UpdatedAtUtc;
        row.RemovedAtUtc = rule.RemovedAtUtc;
    }

    internal static void Apply(AlertRuleEvaluationState state, AlertRuleEvaluationStateRow row)
    {
        row.LastValue = state.LastValue;
        row.LastObservedAtUtc = state.LastObservedAtUtc;
        row.LastEvidenceIdentity = state.LastEvidenceIdentity;
        row.Armed = state.Armed;
        row.TriggerSequence = state.TriggerSequence;
        row.LastTriggeredAtUtc = state.LastTriggeredAtUtc;
        row.CooldownEndsAtUtc = state.CooldownEndsAtUtc;
        row.ConcurrencyToken = state.ConcurrencyToken;
    }
}

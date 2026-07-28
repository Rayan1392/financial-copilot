using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;

namespace FinancialCopilot.Application.FinancialData.ConditionalTrackers;

public sealed record AlertRuleInput(
    AlertRuleType RuleType,
    string MetricOrEventCode,
    AlertRuleOperator Operator,
    decimal Threshold,
    AlertRuleUnit Unit,
    int? BaselineWindow,
    AlertRuleRecurrence Recurrence,
    int CooldownMinutes,
    AlertRuleResetPolicy ResetPolicy,
    AlertRuleSessionPolicy SessionPolicy,
    decimal? Hysteresis);

public sealed record CreateAlertRuleCommand(
    CurrentActor Actor,
    string ExternalCompanyId,
    AlertRuleInput Input,
    string? IdempotencyKey,
    bool ConfirmImmediately = true);

public sealed record ParseNaturalLanguageAlertRuleCommand(
    CurrentActor Actor,
    string ExternalCompanyId,
    string Text,
    string? IdempotencyKey);

public sealed record ParseNaturalLanguageAlertRuleUpdateCommand(
    CurrentActor Actor,
    Guid RuleId,
    int ExpectedVersion,
    string Text);

public sealed record ConfirmAlertRuleCommand(
    CurrentActor Actor,
    Guid RuleId,
    int ExpectedVersion,
    string ConfirmationToken);

public sealed record UpdateAlertRuleCommand(
    CurrentActor Actor,
    Guid RuleId,
    int ExpectedVersion,
    AlertRuleInput? Input,
    AlertRuleState? State);

public sealed record RemoveAlertRuleCommand(CurrentActor Actor, Guid RuleId, int? ExpectedVersion = null);

public sealed record GetMyAlertRulesQuery(CurrentActor Actor, bool IncludeRemoved = false);

public sealed record AlertRuleDto(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    string CompanyName,
    AlertRuleType RuleType,
    string MetricOrEventCode,
    AlertRuleOperator Operator,
    decimal Threshold,
    AlertRuleUnit Unit,
    int? BaselineWindow,
    AlertRuleRecurrence Recurrence,
    int CooldownMinutes,
    AlertRuleResetPolicy ResetPolicy,
    AlertRuleSessionPolicy SessionPolicy,
    decimal? Hysteresis,
    AlertRuleState State,
    int Version,
    string ConfirmationToken,
    DateTimeOffset ConfirmationExpiresAtUtc,
    string ConfirmationText,
    string? OriginalText,
    string? ParserVersion,
    decimal? LastObservedValue,
    DateTimeOffset? LastObservedAtUtc,
    DateTimeOffset? LastTriggeredAtUtc,
    DateTimeOffset? NextEligibleAtUtc,
    int TriggerSequence,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AlertRuleSnapshot(
    AlertRule Rule,
    AlertRuleEvaluationState EvaluationState);

public sealed record NaturalLanguageRuleProposal(
    AlertRuleDefinition Definition,
    string ConfirmationText,
    string ParserVersion);

public interface IGovernedAlertRuleParser
{
    NaturalLanguageRuleProposal Parse(string text);
}

public interface IAlertRuleRepository
{
    Task<IReadOnlyCollection<AlertRuleSnapshot>> GetAsync(
        AlertRuleActor actor,
        bool includeRemoved,
        CancellationToken cancellationToken);

    Task<AlertRuleSnapshot?> FindAsync(
        AlertRuleActor actor,
        Guid ruleId,
        CancellationToken cancellationToken);

    Task<AlertRuleSnapshot?> FindByIdempotencyKeyAsync(
        AlertRuleActor actor,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<int> CountLiveAsync(AlertRuleActor actor, CancellationToken cancellationToken);

    Task SaveAsync(
        AlertRule rule,
        AlertRuleEvaluationState evaluationState,
        CancellationToken cancellationToken);
}

public interface IConditionalTrackerEntitlementPolicy
{
    Task ValidateCreateAsync(
        CurrentActor actor,
        int currentLiveRuleCount,
        CancellationToken cancellationToken);

    Task<bool> CanEvaluateAsync(
        AlertRuleActor actor,
        CancellationToken cancellationToken);
}

public interface IConditionalTrackerUseCases
{
    Task<IReadOnlyCollection<AlertRuleDto>> GetAsync(
        GetMyAlertRulesQuery query,
        CancellationToken cancellationToken);

    Task<AlertRuleDto?> GetAsync(
        CurrentActor actor,
        Guid ruleId,
        CancellationToken cancellationToken);

    Task<AlertRuleDto> CreateAsync(
        CreateAlertRuleCommand command,
        CancellationToken cancellationToken);

    Task<AlertRuleDto> ParseAsync(
        ParseNaturalLanguageAlertRuleCommand command,
        CancellationToken cancellationToken);

    Task<AlertRuleDto> ConfirmAsync(
        ConfirmAlertRuleCommand command,
        CancellationToken cancellationToken);

    Task<AlertRuleDto> ParseUpdateAsync(
        ParseNaturalLanguageAlertRuleUpdateCommand command,
        CancellationToken cancellationToken);

    Task<AlertRuleDto> UpdateAsync(
        UpdateAlertRuleCommand command,
        CancellationToken cancellationToken);

    Task RemoveAsync(
        RemoveAlertRuleCommand command,
        CancellationToken cancellationToken);
}

public interface IConditionalTrackerEvaluationProcessor
{
    Task<AlertRuleEvaluationBatchResult> EvaluateDueAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public sealed record AlertRuleEvaluationBatchResult(
    int Considered,
    int Triggered,
    int Skipped,
    int Failed);

public sealed class AlertRuleValidationException(string message) : InvalidOperationException(message);

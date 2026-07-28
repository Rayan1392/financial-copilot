namespace FinancialCopilot.API.Contracts;

public sealed record CreateAlertRuleRequest(
    string ExternalCompanyId,
    string? RuleType,
    string? MetricOrEventCode,
    string? Operator,
    decimal? Threshold,
    string? Unit,
    int? BaselineWindow,
    string? Recurrence,
    int? CooldownMinutes,
    string? ResetPolicy,
    string? SessionPolicy,
    decimal? Hysteresis,
    string? NaturalLanguageText,
    string? IdempotencyKey,
    bool ConfirmImmediately = true);

public sealed record UpdateAlertRuleRequest(
    int ExpectedVersion,
    string? State,
    string? RuleType,
    string? MetricOrEventCode,
    string? Operator,
    decimal? Threshold,
    string? Unit,
    int? BaselineWindow,
    string? Recurrence,
    int? CooldownMinutes,
    string? ResetPolicy,
    string? SessionPolicy,
    decimal? Hysteresis);

public sealed record ConfirmAlertRuleRequest(int ExpectedVersion, string ConfirmationToken);

public sealed record AlertRulesResponse(IReadOnlyCollection<AlertRuleResponse> Items);

public sealed record AlertRuleResponse(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    string CompanyName,
    string RuleType,
    string MetricOrEventCode,
    string Operator,
    decimal Threshold,
    string Unit,
    int? BaselineWindow,
    string Recurrence,
    int CooldownMinutes,
    string ResetPolicy,
    string SessionPolicy,
    decimal? Hysteresis,
    string State,
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

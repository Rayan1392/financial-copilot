namespace FinancialCopilot.Domain.Financial.ConditionalTrackers;

public enum AlertRuleType
{
    Price,
    PercentageChange,
    Volume,
    TradingValue,
    BuyerPower,
    RealMoneyFlow,
    BuyQueue,
    SellQueue,
    CodalPublication,
    FinancialMetric
}

public enum AlertRuleOperator
{
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equal,
    CrossesAbove,
    CrossesBelow
}

public enum AlertRuleUnit
{
    None,
    Rial,
    Toman,
    Percent,
    Ratio,
    Shares,
    Count
}

public enum AlertRuleRecurrence { OneShot, Recurring }

public enum AlertRuleResetPolicy { CrossBack, NextMarketSession, ExplicitHysteresis }

public enum AlertRuleSessionPolicy { Any, TradingSessionOnly, ClosingSession }

public enum AlertRuleState { Draft, Active, Paused, Triggered, Completed, Removed }

public enum AlertEvaluationDecision
{
    Observed,
    Triggered,
    CooldownSuppressed,
    MissingPriorObservation,
    MissingData,
    StaleObservation,
    OutOfOrderObservation,
    OutsideSession,
    InactiveRule
}

public sealed record AlertRuleActor
{
    public AlertRuleActor(Guid tenantId, Guid actorId, string actorType)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorId));
        if (string.IsNullOrWhiteSpace(actorType)) throw new ArgumentException("Actor type is required.", nameof(actorType));
        TenantId = tenantId;
        ActorId = actorId;
        ActorType = actorType.Trim();
    }

    public Guid TenantId { get; }
    public Guid ActorId { get; }
    public string ActorType { get; }
}

public sealed record AlertRuleDefinition
{
    public AlertRuleDefinition(
        AlertRuleType ruleType,
        string metricOrEventCode,
        AlertRuleOperator @operator,
        decimal threshold,
        AlertRuleUnit unit,
        int? baselineWindow,
        AlertRuleRecurrence recurrence,
        TimeSpan cooldown,
        AlertRuleResetPolicy resetPolicy,
        AlertRuleSessionPolicy sessionPolicy,
        decimal? hysteresis)
    {
        if (string.IsNullOrWhiteSpace(metricOrEventCode))
            throw new ArgumentException("A governed metric or event code is required.", nameof(metricOrEventCode));
        if (metricOrEventCode.Trim().Length > 128)
            throw new ArgumentOutOfRangeException(nameof(metricOrEventCode), "Metric or event code cannot exceed 128 characters.");
        if (baselineWindow is <= 0 or > 250)
            throw new ArgumentOutOfRangeException(nameof(baselineWindow), "Baseline window must be between 1 and 250 observations.");
        if (cooldown < TimeSpan.Zero || cooldown > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(cooldown), "Cooldown must be between zero and 30 days.");
        if (hysteresis is < 0)
            throw new ArgumentOutOfRangeException(nameof(hysteresis), "Hysteresis cannot be negative.");
        if (resetPolicy == AlertRuleResetPolicy.ExplicitHysteresis && hysteresis is null or 0)
            throw new ArgumentException("Explicit hysteresis reset requires a positive hysteresis value.", nameof(hysteresis));

        var governedCode = metricOrEventCode.Trim().ToUpperInvariant();
        AlertRuleGovernance.Validate(ruleType, governedCode, @operator, unit, baselineWindow);
        RuleType = ruleType;
        MetricOrEventCode = governedCode;
        Operator = @operator;
        Threshold = threshold;
        Unit = unit;
        BaselineWindow = baselineWindow;
        Recurrence = recurrence;
        Cooldown = cooldown;
        ResetPolicy = resetPolicy;
        SessionPolicy = sessionPolicy;
        Hysteresis = hysteresis;
    }

    public AlertRuleType RuleType { get; }
    public string MetricOrEventCode { get; }
    public AlertRuleOperator Operator { get; }
    public decimal Threshold { get; }
    public AlertRuleUnit Unit { get; }
    public int? BaselineWindow { get; }
    public AlertRuleRecurrence Recurrence { get; }
    public TimeSpan Cooldown { get; }
    public AlertRuleResetPolicy ResetPolicy { get; }
    public AlertRuleSessionPolicy SessionPolicy { get; }
    public decimal? Hysteresis { get; }
}

public static class AlertRuleGovernance
{
    public static void Validate(
        AlertRuleType type,
        string metricOrEventCode,
        AlertRuleOperator @operator,
        AlertRuleUnit unit,
        int? baselineWindow)
    {
        var allowedUnit = type switch
        {
            AlertRuleType.Price => unit is AlertRuleUnit.Rial or AlertRuleUnit.Toman,
            AlertRuleType.PercentageChange => unit == AlertRuleUnit.Percent,
            AlertRuleType.Volume => unit is AlertRuleUnit.Shares or AlertRuleUnit.Ratio,
            AlertRuleType.TradingValue => unit is AlertRuleUnit.Rial or AlertRuleUnit.Toman or AlertRuleUnit.Ratio,
            AlertRuleType.BuyerPower => unit == AlertRuleUnit.Ratio,
            AlertRuleType.RealMoneyFlow => unit is AlertRuleUnit.Rial or AlertRuleUnit.Toman,
            AlertRuleType.BuyQueue or AlertRuleType.SellQueue => unit is AlertRuleUnit.Shares or AlertRuleUnit.Rial or AlertRuleUnit.Toman,
            AlertRuleType.CodalPublication => unit == AlertRuleUnit.None,
            AlertRuleType.FinancialMetric => unit is not AlertRuleUnit.None,
            _ => false
        };
        if (!allowedUnit) throw new ArgumentException($"Unit '{unit}' is not supported for rule type '{type}'.");
        if (type == AlertRuleType.CodalPublication && @operator != AlertRuleOperator.Equal)
            throw new ArgumentException("Codal publication rules use the Equal operator.");
        if (baselineWindow.HasValue && type is not (AlertRuleType.Volume or AlertRuleType.TradingValue))
            throw new ArgumentException("A baseline window is supported only for volume and trading-value rules.");

        var expectedCode = type switch
        {
            AlertRuleType.Price => "LATEST_PRICE",
            AlertRuleType.PercentageChange => "DAILY_CHANGE_PCT",
            AlertRuleType.Volume => "VOLUME",
            AlertRuleType.TradingValue => "TRADING_VALUE",
            AlertRuleType.BuyerPower => "BUYER_POWER",
            AlertRuleType.RealMoneyFlow => "REAL_MONEY_FLOW",
            AlertRuleType.BuyQueue => "BUY_QUEUE",
            AlertRuleType.SellQueue => "SELL_QUEUE",
            _ => null
        };
        if (expectedCode is not null && !metricOrEventCode.Equals(expectedCode, StringComparison.Ordinal))
            throw new ArgumentException($"Rule type '{type}' requires governed code '{expectedCode}'.");
        if (type == AlertRuleType.CodalPublication && metricOrEventCode is not
            ("CODAL_ANNOUNCEMENT_PUBLISHED" or "CODAL_MONTHLY_ACTIVITY_PUBLISHED" or "CODAL_FINANCIAL_STATEMENT_PUBLISHED"))
            throw new ArgumentException("The Codal event code is not governed.");
        if (!metricOrEventCode.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_'))
            throw new ArgumentException("Metric and event codes must be canonical uppercase tokens.");
    }
}

public sealed class AlertRule
{
    private AlertRule(
        Guid id,
        AlertRuleActor actor,
        string externalCompanyId,
        AlertRuleDefinition definition,
        AlertRuleState state,
        int version,
        string? originalText,
        string? parserVersion,
        string confirmationNonce,
        DateTimeOffset confirmationExpiresAtUtc,
        string? idempotencyKey,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? removedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Rule id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(externalCompanyId)) throw new ArgumentException("External company id is required.", nameof(externalCompanyId));
        if (externalCompanyId.Trim().Length > 64) throw new ArgumentOutOfRangeException(nameof(externalCompanyId));
        if (originalText?.Trim().Length > 500) throw new ArgumentOutOfRangeException(nameof(originalText));
        if (parserVersion?.Trim().Length > 64) throw new ArgumentOutOfRangeException(nameof(parserVersion));
        if (idempotencyKey?.Trim().Length > 128) throw new ArgumentOutOfRangeException(nameof(idempotencyKey));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (string.IsNullOrWhiteSpace(confirmationNonce)) throw new ArgumentException("Confirmation nonce is required.", nameof(confirmationNonce));
        Id = id;
        Actor = actor;
        ExternalCompanyId = externalCompanyId.Trim();
        Definition = definition;
        State = state;
        Version = version;
        OriginalText = originalText?.Trim();
        ParserVersion = parserVersion?.Trim();
        ConfirmationNonce = confirmationNonce;
        ConfirmationExpiresAtUtc = confirmationExpiresAtUtc;
        IdempotencyKey = idempotencyKey?.Trim();
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RemovedAtUtc = removedAtUtc;
    }

    public Guid Id { get; }
    public AlertRuleActor Actor { get; }
    public string ExternalCompanyId { get; }
    public AlertRuleDefinition Definition { get; private set; }
    public AlertRuleState State { get; private set; }
    public int Version { get; private set; }
    public string? OriginalText { get; private set; }
    public string? ParserVersion { get; private set; }
    public string ConfirmationNonce { get; private set; }
    public DateTimeOffset ConfirmationExpiresAtUtc { get; private set; }
    public string? IdempotencyKey { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? RemovedAtUtc { get; private set; }

    public static AlertRule CreateDraft(
        AlertRuleActor actor,
        string externalCompanyId,
        AlertRuleDefinition definition,
        string? originalText,
        string? parserVersion,
        string? idempotencyKey,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), actor, externalCompanyId, definition, AlertRuleState.Draft, 1,
            originalText, parserVersion, NewConfirmationNonce(), now.AddMinutes(15), idempotencyKey, now, now, null);

    public static AlertRule Rehydrate(
        Guid id, AlertRuleActor actor, string externalCompanyId, AlertRuleDefinition definition,
        AlertRuleState state, int version, string? originalText, string? parserVersion,
        string confirmationNonce, DateTimeOffset confirmationExpiresAtUtc, string? idempotencyKey, DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc, DateTimeOffset? removedAtUtc) =>
        new(id, actor, externalCompanyId, definition, state, version, originalText, parserVersion,
            confirmationNonce, confirmationExpiresAtUtc, idempotencyKey, createdAtUtc, updatedAtUtc, removedAtUtc);

    public void Confirm(int expectedVersion, string nonce, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (State != AlertRuleState.Draft) throw new InvalidOperationException("Only draft rules can be confirmed.");
        if (!string.Equals(ConfirmationNonce, nonce, StringComparison.Ordinal))
            throw new InvalidOperationException("The confirmation token is invalid or stale.");
        if (now > ConfirmationExpiresAtUtc)
            throw new InvalidOperationException("The confirmation token has expired.");
        State = AlertRuleState.Active;
        Touch(now);
    }

    public void Update(
        AlertRuleDefinition definition,
        int expectedVersion,
        DateTimeOffset now,
        string? originalText = null,
        string? parserVersion = null)
    {
        EnsureVersion(expectedVersion);
        if (State is AlertRuleState.Removed or AlertRuleState.Completed)
            throw new InvalidOperationException("Removed or completed rules cannot be changed.");
        Definition = definition;
        if (originalText is not null) OriginalText = originalText;
        if (parserVersion is not null) ParserVersion = parserVersion;
        State = AlertRuleState.Draft;
        ConfirmationNonce = NewConfirmationNonce();
        ConfirmationExpiresAtUtc = now.AddMinutes(15);
        Touch(now);
    }

    public void Pause(int expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (State != AlertRuleState.Active) throw new InvalidOperationException("Only active rules can be paused.");
        State = AlertRuleState.Paused;
        Touch(now);
    }

    public void Resume(int expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (State != AlertRuleState.Paused) throw new InvalidOperationException("Only paused rules can be resumed.");
        State = AlertRuleState.Active;
        Touch(now);
    }

    public void Remove(int? expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion.HasValue) EnsureVersion(expectedVersion.Value);
        if (State == AlertRuleState.Removed) return;
        State = AlertRuleState.Removed;
        RemovedAtUtc = now;
        Touch(now);
    }

    internal void CompleteAfterTrigger(DateTimeOffset now)
    {
        State = AlertRuleState.Completed;
        Touch(now);
    }

    private void EnsureVersion(int expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The rule version is stale.");
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAtUtc = now;
    }

    private static string NewConfirmationNonce() => Guid.NewGuid().ToString("N")[..12];
}

public sealed record AlertObservation(
    string EvidenceIdentity,
    decimal? Value,
    AlertRuleUnit Unit,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset SourceFreshnessUtc,
    string SourceProvider,
    string? SourcePeriod,
    bool IsTradingSession,
    bool IsClosingSession,
    string EvidenceJson);

public sealed record AlertRuleTrigger(
    int Sequence,
    int RuleVersion,
    string EvidenceIdentity,
    decimal ObservedValue,
    decimal Threshold,
    AlertRuleOperator Operator,
    AlertRuleUnit Unit,
    string SourceProvider,
    string? SourcePeriod,
    DateTimeOffset SourceFreshnessUtc,
    DateTimeOffset TriggeredAtUtc,
    string EvidenceJson);

public sealed record AlertEvaluationOutcome(
    AlertEvaluationDecision Decision,
    AlertRuleTrigger? Trigger = null,
    string? Reason = null);

public sealed class AlertRuleEvaluationState
{
    private AlertRuleEvaluationState(
        Guid ruleId, decimal? lastValue, DateTimeOffset? lastObservedAtUtc, string? lastEvidenceIdentity,
        bool armed, int triggerSequence, DateTimeOffset? lastTriggeredAtUtc,
        DateTimeOffset? cooldownEndsAtUtc, Guid concurrencyToken)
    {
        RuleId = ruleId;
        LastValue = lastValue;
        LastObservedAtUtc = lastObservedAtUtc;
        LastEvidenceIdentity = lastEvidenceIdentity;
        Armed = armed;
        TriggerSequence = triggerSequence;
        LastTriggeredAtUtc = lastTriggeredAtUtc;
        CooldownEndsAtUtc = cooldownEndsAtUtc;
        ConcurrencyToken = concurrencyToken;
    }

    public Guid RuleId { get; }
    public decimal? LastValue { get; private set; }
    public DateTimeOffset? LastObservedAtUtc { get; private set; }
    public string? LastEvidenceIdentity { get; private set; }
    public bool Armed { get; private set; }
    public int TriggerSequence { get; private set; }
    public DateTimeOffset? LastTriggeredAtUtc { get; private set; }
    public DateTimeOffset? CooldownEndsAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public static AlertRuleEvaluationState Create(Guid ruleId) =>
        new(ruleId, null, null, null, true, 0, null, null, Guid.NewGuid());

    public static AlertRuleEvaluationState Rehydrate(
        Guid ruleId, decimal? lastValue, DateTimeOffset? lastObservedAtUtc, string? lastEvidenceIdentity,
        bool armed, int triggerSequence, DateTimeOffset? lastTriggeredAtUtc,
        DateTimeOffset? cooldownEndsAtUtc, Guid concurrencyToken) =>
        new(ruleId, lastValue, lastObservedAtUtc, lastEvidenceIdentity, armed, triggerSequence,
            lastTriggeredAtUtc, cooldownEndsAtUtc, concurrencyToken);

    public AlertEvaluationOutcome Evaluate(
        AlertRule rule,
        AlertObservation observation,
        DateTimeOffset now,
        TimeSpan maximumAge)
    {
        if (rule.State != AlertRuleState.Active)
            return new(AlertEvaluationDecision.InactiveRule, Reason: $"Rule state is {rule.State}.");
        if (observation.Value is null)
            return new(AlertEvaluationDecision.MissingData, Reason: "The canonical observation has no value.");
        if (observation.Unit != rule.Definition.Unit)
            return new(AlertEvaluationDecision.MissingData, Reason: "Observation unit is incompatible with the rule unit.");
        if (now - observation.SourceFreshnessUtc > maximumAge)
            return new(AlertEvaluationDecision.StaleObservation, Reason: "The source observation is stale.");
        if (LastObservedAtUtc.HasValue && observation.ObservedAtUtc <= LastObservedAtUtc.Value)
            return new(AlertEvaluationDecision.OutOfOrderObservation, Reason: "The observation is not newer than the saved evaluation state.");
        if (rule.Definition.SessionPolicy == AlertRuleSessionPolicy.TradingSessionOnly && !observation.IsTradingSession ||
            rule.Definition.SessionPolicy == AlertRuleSessionPolicy.ClosingSession && !observation.IsClosingSession)
            return new(AlertEvaluationDecision.OutsideSession, Reason: "The observation is outside the configured market session.");

        var value = observation.Value.Value;
        RearmIfEligible(rule, value, observation.ObservedAtUtc);
        var isCrossing = rule.Definition.Operator is AlertRuleOperator.CrossesAbove or AlertRuleOperator.CrossesBelow;
        var hasPrior = LastValue.HasValue;
        var matches = Matches(rule.Definition, LastValue, value);
        var cooldownActive = CooldownEndsAtUtc.HasValue && CooldownEndsAtUtc.Value > now;
        SaveObservation(value, observation);

        if (isCrossing && !hasPrior)
            return new(AlertEvaluationDecision.MissingPriorObservation, Reason: "A crossing rule requires a prior eligible observation.");
        if (!Armed || !matches)
            return new(AlertEvaluationDecision.Observed);

        Armed = false;
        if (cooldownActive)
        {
            ConcurrencyToken = Guid.NewGuid();
            return new(AlertEvaluationDecision.CooldownSuppressed, Reason: "Rule cooldown is active.");
        }

        var sequence = ++TriggerSequence;
        LastTriggeredAtUtc = now;
        CooldownEndsAtUtc = now + rule.Definition.Cooldown;
        ConcurrencyToken = Guid.NewGuid();
        var triggeredRuleVersion = rule.Version;

        if (rule.Definition.Recurrence == AlertRuleRecurrence.OneShot)
            rule.CompleteAfterTrigger(now);

        var trigger = new AlertRuleTrigger(
            sequence, triggeredRuleVersion, observation.EvidenceIdentity, value, rule.Definition.Threshold,
            rule.Definition.Operator, rule.Definition.Unit, observation.SourceProvider,
            observation.SourcePeriod, observation.SourceFreshnessUtc, now, observation.EvidenceJson);
        return new(AlertEvaluationDecision.Triggered, trigger);
    }

    private void RearmIfEligible(AlertRule rule, decimal currentValue, DateTimeOffset observedAtUtc)
    {
        if (Armed || rule.Definition.Recurrence != AlertRuleRecurrence.Recurring) return;
        Armed = rule.Definition.ResetPolicy switch
        {
            AlertRuleResetPolicy.CrossBack => !ConditionIsTrue(rule.Definition, currentValue),
            AlertRuleResetPolicy.NextMarketSession => LastTriggeredAtUtc.HasValue &&
                DateOnly.FromDateTime(observedAtUtc.UtcDateTime) > DateOnly.FromDateTime(LastTriggeredAtUtc.Value.UtcDateTime),
            AlertRuleResetPolicy.ExplicitHysteresis => CrossedHysteresisReset(rule.Definition, currentValue),
            _ => false
        };
    }

    private void SaveObservation(decimal value, AlertObservation observation)
    {
        LastValue = value;
        LastObservedAtUtc = observation.ObservedAtUtc;
        LastEvidenceIdentity = observation.EvidenceIdentity;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static bool Matches(AlertRuleDefinition definition, decimal? prior, decimal current) =>
        definition.Operator switch
        {
            AlertRuleOperator.GreaterThan => current > definition.Threshold,
            AlertRuleOperator.GreaterThanOrEqual => current >= definition.Threshold,
            AlertRuleOperator.LessThan => current < definition.Threshold,
            AlertRuleOperator.LessThanOrEqual => current <= definition.Threshold,
            AlertRuleOperator.Equal => current == definition.Threshold,
            AlertRuleOperator.CrossesAbove => prior <= definition.Threshold && current > definition.Threshold,
            AlertRuleOperator.CrossesBelow => prior >= definition.Threshold && current < definition.Threshold,
            _ => false
        };

    private static bool ConditionIsTrue(AlertRuleDefinition definition, decimal current) =>
        definition.Operator switch
        {
            AlertRuleOperator.GreaterThan or AlertRuleOperator.CrossesAbove => current > definition.Threshold,
            AlertRuleOperator.GreaterThanOrEqual => current >= definition.Threshold,
            AlertRuleOperator.LessThan or AlertRuleOperator.CrossesBelow => current < definition.Threshold,
            AlertRuleOperator.LessThanOrEqual => current <= definition.Threshold,
            AlertRuleOperator.Equal => current == definition.Threshold,
            _ => false
        };

    private static bool CrossedHysteresisReset(AlertRuleDefinition definition, decimal current)
    {
        var hysteresis = definition.Hysteresis ?? 0m;
        return definition.Operator switch
        {
            AlertRuleOperator.GreaterThan or AlertRuleOperator.GreaterThanOrEqual or AlertRuleOperator.CrossesAbove =>
                current <= definition.Threshold - hysteresis,
            AlertRuleOperator.LessThan or AlertRuleOperator.LessThanOrEqual or AlertRuleOperator.CrossesBelow =>
                current >= definition.Threshold + hysteresis,
            AlertRuleOperator.Equal => Math.Abs(current - definition.Threshold) > hysteresis,
            _ => false
        };
    }
}

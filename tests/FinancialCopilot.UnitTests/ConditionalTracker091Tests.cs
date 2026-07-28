using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;

namespace FinancialCopilot.UnitTests;

public sealed class ConditionalTracker091Tests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Governance_RejectsUnsupportedTypeOperatorUnitCombinations()
    {
        Assert.Throws<ArgumentException>(() => Definition(
            AlertRuleType.Price,
            AlertRuleOperator.GreaterThan,
            AlertRuleUnit.Percent));
        Assert.Throws<ArgumentException>(() => Definition(
            AlertRuleType.CodalPublication,
            AlertRuleOperator.GreaterThan,
            AlertRuleUnit.None));
        Assert.Throws<ArgumentException>(() => new AlertRuleDefinition(
            AlertRuleType.FinancialMetric, "PE_TTM", AlertRuleOperator.LessThan, 5m,
            AlertRuleUnit.Ratio, baselineWindow: 20, AlertRuleRecurrence.Recurring,
            TimeSpan.Zero, AlertRuleResetPolicy.CrossBack, AlertRuleSessionPolicy.Any, null));
    }

    [Theory]
    [InlineData(AlertRuleType.Price, "LATEST_PRICE", AlertRuleUnit.Rial)]
    [InlineData(AlertRuleType.PercentageChange, "DAILY_CHANGE_PCT", AlertRuleUnit.Percent)]
    [InlineData(AlertRuleType.Volume, "VOLUME", AlertRuleUnit.Shares)]
    [InlineData(AlertRuleType.TradingValue, "TRADING_VALUE", AlertRuleUnit.Toman)]
    [InlineData(AlertRuleType.BuyerPower, "BUYER_POWER", AlertRuleUnit.Ratio)]
    [InlineData(AlertRuleType.RealMoneyFlow, "REAL_MONEY_FLOW", AlertRuleUnit.Rial)]
    [InlineData(AlertRuleType.BuyQueue, "BUY_QUEUE", AlertRuleUnit.Shares)]
    [InlineData(AlertRuleType.SellQueue, "SELL_QUEUE", AlertRuleUnit.Toman)]
    [InlineData(AlertRuleType.CodalPublication, "CODAL_ANNOUNCEMENT_PUBLISHED", AlertRuleUnit.None)]
    [InlineData(AlertRuleType.FinancialMetric, "PE_TTM", AlertRuleUnit.Ratio)]
    public void Governance_AcceptsEverySupportedRuleFamily(
        AlertRuleType type,
        string code,
        AlertRuleUnit unit)
    {
        var definition = new AlertRuleDefinition(
            type,
            code,
            type == AlertRuleType.CodalPublication ? AlertRuleOperator.Equal : AlertRuleOperator.GreaterThan,
            type == AlertRuleType.CodalPublication ? 1m : 10m,
            unit,
            null,
            AlertRuleRecurrence.Recurring,
            TimeSpan.Zero,
            AlertRuleResetPolicy.CrossBack,
            AlertRuleSessionPolicy.Any,
            null);

        Assert.Equal(type, definition.RuleType);
        Assert.Equal(code, definition.MetricOrEventCode);
    }

    [Fact]
    public void CrossingRule_RequiresPriorObservation_ThenProducesEvidenceBackedTrigger()
    {
        var rule = ActiveRule(Definition(
            AlertRuleType.Price,
            AlertRuleOperator.CrossesBelow,
            AlertRuleUnit.Rial,
            threshold: 100m));
        var state = AlertRuleEvaluationState.Create(rule.Id);

        var first = state.Evaluate(rule, Observation(110m, "quote-1", Now), Now, TimeSpan.FromMinutes(5));
        var crossing = state.Evaluate(rule, Observation(90m, "quote-2", Now.AddMinutes(1)), Now.AddMinutes(1), TimeSpan.FromMinutes(5));

        Assert.Equal(AlertEvaluationDecision.MissingPriorObservation, first.Decision);
        Assert.Equal(AlertEvaluationDecision.Triggered, crossing.Decision);
        Assert.NotNull(crossing.Trigger);
        Assert.Equal("quote-2", crossing.Trigger!.EvidenceIdentity);
        Assert.Equal(90m, crossing.Trigger.ObservedValue);
        Assert.Equal(100m, crossing.Trigger.Threshold);
        Assert.Equal(1, crossing.Trigger.Sequence);
    }

    [Fact]
    public void RecurringRule_RearmsOnCrossBack_AndCooldownSuppressesSecondCrossing()
    {
        var rule = ActiveRule(new AlertRuleDefinition(
            AlertRuleType.Price, "LATEST_PRICE", AlertRuleOperator.CrossesBelow, 100m,
            AlertRuleUnit.Rial, null, AlertRuleRecurrence.Recurring, TimeSpan.FromMinutes(30),
            AlertRuleResetPolicy.CrossBack, AlertRuleSessionPolicy.TradingSessionOnly, null));
        var state = AlertRuleEvaluationState.Create(rule.Id);

        state.Evaluate(rule, Observation(110m, "q1", Now), Now, TimeSpan.FromMinutes(5));
        var first = state.Evaluate(rule, Observation(90m, "q2", Now.AddMinutes(1)), Now.AddMinutes(1), TimeSpan.FromMinutes(5));
        state.Evaluate(rule, Observation(110m, "q3", Now.AddMinutes(2)), Now.AddMinutes(2), TimeSpan.FromMinutes(5));
        var suppressed = state.Evaluate(rule, Observation(90m, "q4", Now.AddMinutes(3)), Now.AddMinutes(3), TimeSpan.FromMinutes(5));

        Assert.Equal(AlertEvaluationDecision.Triggered, first.Decision);
        Assert.Equal(AlertEvaluationDecision.CooldownSuppressed, suppressed.Decision);
        Assert.Equal(1, state.TriggerSequence);
        Assert.False(state.Armed);
    }

    [Fact]
    public void OneShotRule_CompletesAfterTrigger_AndOutOfOrderObservationIsRejected()
    {
        var rule = ActiveRule(new AlertRuleDefinition(
            AlertRuleType.PercentageChange, "DAILY_CHANGE_PCT", AlertRuleOperator.GreaterThanOrEqual, 5m,
            AlertRuleUnit.Percent, null, AlertRuleRecurrence.OneShot, TimeSpan.Zero,
            AlertRuleResetPolicy.CrossBack, AlertRuleSessionPolicy.TradingSessionOnly, null));
        var state = AlertRuleEvaluationState.Create(rule.Id);

        var trigger = state.Evaluate(rule, Observation(6m, "pct-1", Now, AlertRuleUnit.Percent), Now, TimeSpan.FromMinutes(5));
        var outOfOrder = state.Evaluate(rule, Observation(7m, "pct-old", Now.AddMinutes(-1), AlertRuleUnit.Percent), Now, TimeSpan.FromMinutes(5));

        Assert.Equal(AlertEvaluationDecision.Triggered, trigger.Decision);
        Assert.Equal(AlertRuleState.Completed, rule.State);
        Assert.Equal(AlertEvaluationDecision.InactiveRule, outOfOrder.Decision);
    }

    [Fact]
    public void StaleObservation_DoesNotMutateEvaluationState()
    {
        var rule = ActiveRule(Definition(AlertRuleType.Price, AlertRuleOperator.GreaterThan, AlertRuleUnit.Rial));
        var state = AlertRuleEvaluationState.Create(rule.Id);
        var stale = Observation(200m, "stale", Now.AddHours(-2));

        var result = state.Evaluate(rule, stale, Now, TimeSpan.FromMinutes(15));

        Assert.Equal(AlertEvaluationDecision.StaleObservation, result.Decision);
        Assert.Null(state.LastObservedAtUtc);
    }

    [Fact]
    public void ExplicitHysteresis_RequiresResetBandBeforeRecurringRuleRearms()
    {
        var rule = ActiveRule(new AlertRuleDefinition(
            AlertRuleType.Price, "LATEST_PRICE", AlertRuleOperator.CrossesAbove, 100m,
            AlertRuleUnit.Rial, null, AlertRuleRecurrence.Recurring, TimeSpan.Zero,
            AlertRuleResetPolicy.ExplicitHysteresis, AlertRuleSessionPolicy.Any, 10m));
        var state = AlertRuleEvaluationState.Create(rule.Id);

        state.Evaluate(rule, Observation(90m, "h1", Now), Now, TimeSpan.FromMinutes(5));
        var first = state.Evaluate(rule, Observation(110m, "h2", Now.AddMinutes(1)), Now.AddMinutes(1), TimeSpan.FromMinutes(5));
        state.Evaluate(rule, Observation(95m, "h3", Now.AddMinutes(2)), Now.AddMinutes(2), TimeSpan.FromMinutes(5));
        state.Evaluate(rule, Observation(89m, "h4", Now.AddMinutes(3)), Now.AddMinutes(3), TimeSpan.FromMinutes(5));
        var second = state.Evaluate(rule, Observation(101m, "h5", Now.AddMinutes(4)), Now.AddMinutes(4), TimeSpan.FromMinutes(5));

        Assert.Equal(AlertEvaluationDecision.Triggered, first.Decision);
        Assert.Equal(AlertEvaluationDecision.Triggered, second.Decision);
        Assert.Equal(2, state.TriggerSequence);
    }

    [Fact]
    public void NextMarketSessionPolicy_RearmsOnANewerSessionDate()
    {
        var rule = ActiveRule(new AlertRuleDefinition(
            AlertRuleType.Price, "LATEST_PRICE", AlertRuleOperator.GreaterThan, 100m,
            AlertRuleUnit.Rial, null, AlertRuleRecurrence.Recurring, TimeSpan.Zero,
            AlertRuleResetPolicy.NextMarketSession, AlertRuleSessionPolicy.Any, null));
        var state = AlertRuleEvaluationState.Create(rule.Id);

        var first = state.Evaluate(rule, Observation(110m, "day-1", Now), Now, TimeSpan.FromDays(2));
        var second = state.Evaluate(
            rule,
            Observation(120m, "day-2", Now.AddDays(1)),
            Now.AddDays(1),
            TimeSpan.FromDays(2));

        Assert.Equal(AlertEvaluationDecision.Triggered, first.Decision);
        Assert.Equal(AlertEvaluationDecision.Triggered, second.Decision);
        Assert.Equal(2, state.TriggerSequence);
    }

    [Fact]
    public void TradingSessionRule_SkipsObservationOutsideSession()
    {
        var rule = ActiveRule(Definition(AlertRuleType.Price, AlertRuleOperator.GreaterThan, AlertRuleUnit.Rial));
        var state = AlertRuleEvaluationState.Create(rule.Id);
        var outside = Observation(200m, "closed", Now) with { IsTradingSession = false };

        var result = state.Evaluate(rule, outside, Now, TimeSpan.FromMinutes(5));

        Assert.Equal(AlertEvaluationDecision.OutsideSession, result.Decision);
        Assert.Null(state.LastObservedAtUtc);
    }

    [Fact]
    public void Parser_NormalizesPersianDigits_AndProducesGovernedDraftDefinition()
    {
        var parser = new GovernedAlertRuleParser();

        var proposal = parser.Parse("اگر قیمت کمتر از ۱۲۵۰ تومان شد هشدار بده");

        Assert.Equal(AlertRuleType.Price, proposal.Definition.RuleType);
        Assert.Equal(AlertRuleOperator.LessThan, proposal.Definition.Operator);
        Assert.Equal(1250m, proposal.Definition.Threshold);
        Assert.Equal(AlertRuleUnit.Toman, proposal.Definition.Unit);
        Assert.Equal(GovernedAlertRuleParser.Version, proposal.ParserVersion);
    }

    [Fact]
    public void Parser_MapsCodalMonthlyPublication_WithoutExecutableExpression()
    {
        var parser = new GovernedAlertRuleParser();
        var proposal = parser.Parse("وقتی گزارش ماهانه کدال منتشر شد خبر بده");

        Assert.Equal(AlertRuleType.CodalPublication, proposal.Definition.RuleType);
        Assert.Equal("CODAL_MONTHLY_ACTIVITY_PUBLISHED", proposal.Definition.MetricOrEventCode);
        Assert.Equal(AlertRuleUnit.None, proposal.Definition.Unit);
        Assert.Throws<AlertRuleValidationException>(() => parser.Parse("price > 10; DROP TABLE alerts"));
    }

    [Fact]
    public void DraftConfirmation_RejectsExpiredToken()
    {
        var rule = AlertRule.CreateDraft(
            new AlertRuleActor(Guid.NewGuid(), Guid.NewGuid(), "User"),
            "1001",
            Definition(AlertRuleType.Price, AlertRuleOperator.GreaterThan, AlertRuleUnit.Rial),
            "price above 100",
            GovernedAlertRuleParser.Version,
            null,
            Now);

        var error = Assert.Throws<InvalidOperationException>(() =>
            rule.Confirm(rule.Version, rule.ConfirmationNonce, Now.AddMinutes(16)));

        Assert.Contains("expired", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AlertRuleState.Draft, rule.State);
    }

    [Fact]
    public void EqualityAndCrossingBoundary_AreDeterministic()
    {
        var inclusive = ActiveRule(Definition(
            AlertRuleType.Price,
            AlertRuleOperator.GreaterThanOrEqual,
            AlertRuleUnit.Rial,
            threshold: 100m));
        var inclusiveState = AlertRuleEvaluationState.Create(inclusive.Id);
        var equality = inclusiveState.Evaluate(
            inclusive,
            Observation(100m, "equal", Now),
            Now,
            TimeSpan.FromMinutes(5));

        var crossing = ActiveRule(Definition(
            AlertRuleType.Price,
            AlertRuleOperator.CrossesAbove,
            AlertRuleUnit.Rial,
            threshold: 100m));
        var crossingState = AlertRuleEvaluationState.Create(crossing.Id);
        crossingState.Evaluate(crossing, Observation(90m, "below", Now), Now, TimeSpan.FromMinutes(5));
        var atThreshold = crossingState.Evaluate(
            crossing,
            Observation(100m, "at", Now.AddMinutes(1)),
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var aboveThreshold = crossingState.Evaluate(
            crossing,
            Observation(101m, "above", Now.AddMinutes(2)),
            Now.AddMinutes(2),
            TimeSpan.FromMinutes(5));

        Assert.Equal(AlertEvaluationDecision.Triggered, equality.Decision);
        Assert.Equal(AlertEvaluationDecision.Observed, atThreshold.Decision);
        Assert.Equal(AlertEvaluationDecision.Triggered, aboveThreshold.Decision);
    }

    [Fact]
    public void MissingAndOutOfOrderObservations_AreRejectedWithoutReplacingState()
    {
        var rule = ActiveRule(Definition(AlertRuleType.Price, AlertRuleOperator.GreaterThan, AlertRuleUnit.Rial));
        var state = AlertRuleEvaluationState.Create(rule.Id);
        var missing = Observation(0m, "missing", Now) with { Value = null };
        var missingResult = state.Evaluate(rule, missing, Now, TimeSpan.FromMinutes(5));
        state.Evaluate(rule, Observation(90m, "current", Now), Now, TimeSpan.FromMinutes(5));
        var outOfOrder = state.Evaluate(
            rule,
            Observation(110m, "old", Now.AddSeconds(-1)),
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        Assert.Equal(AlertEvaluationDecision.MissingData, missingResult.Decision);
        Assert.Equal(AlertEvaluationDecision.OutOfOrderObservation, outOfOrder.Decision);
        Assert.Equal(90m, state.LastValue);
        Assert.Equal("current", state.LastEvidenceIdentity);
    }

    [Fact]
    public void DraftConfirmation_RejectsVersionMismatchWithoutActivatingRule()
    {
        var rule = AlertRule.CreateDraft(
            new AlertRuleActor(Guid.NewGuid(), Guid.NewGuid(), "User"),
            "1001",
            Definition(AlertRuleType.Price, AlertRuleOperator.GreaterThan, AlertRuleUnit.Rial),
            null,
            null,
            null,
            Now);

        Assert.Throws<InvalidOperationException>(() =>
            rule.Confirm(rule.Version + 1, rule.ConfirmationNonce, Now));
        Assert.Equal(AlertRuleState.Draft, rule.State);
    }

    private static AlertRuleDefinition Definition(
        AlertRuleType type,
        AlertRuleOperator @operator,
        AlertRuleUnit unit,
        decimal threshold = 100m) =>
        new(type, type switch
            {
                AlertRuleType.CodalPublication => "CODAL_ANNOUNCEMENT_PUBLISHED",
                AlertRuleType.PercentageChange => "DAILY_CHANGE_PCT",
                _ => "LATEST_PRICE"
            },
            @operator, threshold, unit, null, AlertRuleRecurrence.Recurring, TimeSpan.Zero,
            AlertRuleResetPolicy.CrossBack, AlertRuleSessionPolicy.TradingSessionOnly, null);

    private static AlertRule ActiveRule(AlertRuleDefinition definition)
    {
        var rule = AlertRule.CreateDraft(
            new AlertRuleActor(Guid.NewGuid(), Guid.NewGuid(), "User"),
            "1001", definition, null, null, null, Now);
        rule.Confirm(rule.Version, rule.ConfirmationNonce, Now);
        return rule;
    }

    private static AlertObservation Observation(
        decimal value,
        string identity,
        DateTimeOffset observedAt,
        AlertRuleUnit unit = AlertRuleUnit.Rial) =>
        new(identity, value, unit, observedAt, observedAt, "TestProvider", "1405/04/23",
            IsTradingSession: true, IsClosingSession: false, EvidenceJson: "{\"source\":\"test\"}");
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;
using FinancialCopilot.Domain.Financial.FollowedSymbols;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.FinancialData.ConditionalTrackers;

public sealed class ConditionalTrackerUseCases(
    IAlertRuleRepository rules,
    IFollowedCompanyResolver companies,
    IConditionalTrackerEntitlementPolicy entitlements,
    IGovernedAlertRuleParser parser,
    IFinancialMetricRegistry metricRegistry,
    TimeProvider timeProvider) : IConditionalTrackerUseCases
{
    public async Task<IReadOnlyCollection<AlertRuleDto>> GetAsync(
        GetMyAlertRulesQuery query,
        CancellationToken cancellationToken)
    {
        var snapshots = await rules.GetAsync(ToActor(query.Actor), query.IncludeRemoved, cancellationToken);
        var companyMap = await companies.ResolveManyAsync(
            snapshots.Select(item => item.Rule.ExternalCompanyId).Distinct().ToArray(), cancellationToken);
        return snapshots.Select(item => Map(item, companyMap)).ToArray();
    }

    public async Task<AlertRuleDto?> GetAsync(
        CurrentActor actor,
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        var snapshot = await rules.FindAsync(ToActor(actor), ruleId, cancellationToken);
        if (snapshot is null) return null;
        var companyMap = await companies.ResolveManyAsync([snapshot.Rule.ExternalCompanyId], cancellationToken);
        return Map(snapshot, companyMap);
    }

    public async Task<AlertRuleDto> CreateAsync(
        CreateAlertRuleCommand command,
        CancellationToken cancellationToken)
    {
        var actor = ToActor(command.Actor);
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await rules.FindByIdempotencyKeyAsync(actor, command.IdempotencyKey, cancellationToken);
            if (existing is not null) return await MapAsync(existing, cancellationToken);
        }

        var company = await ResolveCompanyAsync(command.ExternalCompanyId, cancellationToken);
        await entitlements.ValidateCreateAsync(
            command.Actor,
            await rules.CountLiveAsync(actor, cancellationToken),
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var definition = ToDefinition(command.Input);
        ValidateDefinition(definition, now);
        var rule = AlertRule.CreateDraft(
            actor,
            company.ExternalCompanyId,
            definition,
            originalText: null,
            parserVersion: null,
            command.IdempotencyKey,
            now);
        if (command.ConfirmImmediately)
            rule.Confirm(rule.Version, rule.ConfirmationNonce, now);
        var snapshot = new AlertRuleSnapshot(rule, AlertRuleEvaluationState.Create(rule.Id));
        await rules.SaveAsync(rule, snapshot.EvaluationState, cancellationToken);
        return Map(snapshot, new Dictionary<string, CanonicalFollowedCompany>(StringComparer.Ordinal)
        {
            [company.ExternalCompanyId] = company
        });
    }

    public async Task<AlertRuleDto> ParseAsync(
        ParseNaturalLanguageAlertRuleCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Text) || command.Text.Length > 500)
            throw new AlertRuleValidationException("Natural-language rule text must contain 1 to 500 characters.");
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await rules.FindByIdempotencyKeyAsync(
                ToActor(command.Actor), command.IdempotencyKey, cancellationToken);
            if (existing is not null) return await MapAsync(existing, cancellationToken);
        }
        var proposal = parser.Parse(command.Text);
        var dto = await CreateAsync(
            new CreateAlertRuleCommand(
                command.Actor,
                command.ExternalCompanyId,
                FromDefinition(proposal.Definition),
                command.IdempotencyKey,
                ConfirmImmediately: false),
            cancellationToken);

        var actor = ToActor(command.Actor);
        var snapshot = await rules.FindAsync(actor, dto.Id, cancellationToken)
            ?? throw new AlertRuleValidationException("Parsed rule could not be reloaded.");
        var parsedRule = AlertRule.Rehydrate(
            snapshot.Rule.Id, snapshot.Rule.Actor, snapshot.Rule.ExternalCompanyId, snapshot.Rule.Definition,
            snapshot.Rule.State, snapshot.Rule.Version, command.Text, proposal.ParserVersion,
            snapshot.Rule.ConfirmationNonce, snapshot.Rule.ConfirmationExpiresAtUtc, snapshot.Rule.IdempotencyKey,
            snapshot.Rule.CreatedAtUtc, snapshot.Rule.UpdatedAtUtc, snapshot.Rule.RemovedAtUtc);
        await rules.SaveAsync(parsedRule, snapshot.EvaluationState, cancellationToken);
        return await MapAsync(new AlertRuleSnapshot(parsedRule, snapshot.EvaluationState), cancellationToken);
    }

    public async Task<AlertRuleDto> ConfirmAsync(
        ConfirmAlertRuleCommand command,
        CancellationToken cancellationToken)
    {
        var snapshot = await RequireOwnedAsync(command.Actor, command.RuleId, cancellationToken);
        snapshot.Rule.Confirm(command.ExpectedVersion, command.ConfirmationToken, timeProvider.GetUtcNow());
        await rules.SaveAsync(snapshot.Rule, snapshot.EvaluationState, cancellationToken);
        return await MapAsync(snapshot, cancellationToken);
    }

    public async Task<AlertRuleDto> ParseUpdateAsync(
        ParseNaturalLanguageAlertRuleUpdateCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Text) || command.Text.Length > 500)
            throw new AlertRuleValidationException("Natural-language rule text must contain 1 to 500 characters.");
        var snapshot = await RequireOwnedAsync(command.Actor, command.RuleId, cancellationToken);
        var proposal = parser.Parse(command.Text);
        var now = timeProvider.GetUtcNow();
        ValidateDefinition(proposal.Definition, now);
        snapshot.Rule.Update(
            proposal.Definition,
            command.ExpectedVersion,
            now,
            command.Text,
            proposal.ParserVersion);
        var evaluationState = AlertRuleEvaluationState.Create(snapshot.Rule.Id);
        await rules.SaveAsync(snapshot.Rule, evaluationState, cancellationToken);
        return await MapAsync(new AlertRuleSnapshot(snapshot.Rule, evaluationState), cancellationToken);
    }

    public async Task<AlertRuleDto> UpdateAsync(
        UpdateAlertRuleCommand command,
        CancellationToken cancellationToken)
    {
        var snapshot = await RequireOwnedAsync(command.Actor, command.RuleId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var evaluationState = snapshot.EvaluationState;
        if (command.Input is not null)
        {
            var definition = ToDefinition(command.Input);
            ValidateDefinition(definition, now);
            snapshot.Rule.Update(definition, command.ExpectedVersion, now);
            evaluationState = AlertRuleEvaluationState.Create(snapshot.Rule.Id);
        }
        else if (command.State == AlertRuleState.Paused)
            snapshot.Rule.Pause(command.ExpectedVersion, now);
        else if (command.State == AlertRuleState.Active)
            snapshot.Rule.Resume(command.ExpectedVersion, now);
        else
            throw new AlertRuleValidationException("PATCH must provide a normalized rule or a supported lifecycle state.");
        await rules.SaveAsync(snapshot.Rule, evaluationState, cancellationToken);
        return await MapAsync(new AlertRuleSnapshot(snapshot.Rule, evaluationState), cancellationToken);
    }

    public async Task RemoveAsync(RemoveAlertRuleCommand command, CancellationToken cancellationToken)
    {
        var snapshot = await RequireOwnedAsync(command.Actor, command.RuleId, cancellationToken);
        snapshot.Rule.Remove(command.ExpectedVersion, timeProvider.GetUtcNow());
        await rules.SaveAsync(snapshot.Rule, snapshot.EvaluationState, cancellationToken);
    }

    private async Task<AlertRuleSnapshot> RequireOwnedAsync(
        CurrentActor actor,
        Guid ruleId,
        CancellationToken cancellationToken) =>
        await rules.FindAsync(ToActor(actor), ruleId, cancellationToken)
        ?? throw new AlertRuleValidationException("Alert rule was not found.");

    private async Task<CanonicalFollowedCompany> ResolveCompanyAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var reference = externalCompanyId.Trim();
        var resolution = await companies.ResolveReferenceAsync(reference, cancellationToken);
        if (resolution.IsAmbiguous)
            throw new AlertRuleValidationException(
                $"Company reference '{reference}' is ambiguous. Candidates: " +
                string.Join(", ", resolution.Candidates.Select(item => $"{item.Symbol} ({item.ExternalCompanyId})")));
        return resolution.Company
            ?? throw new AlertRuleValidationException("Unknown canonical company id or symbol.");
    }

    private async Task<AlertRuleDto> MapAsync(AlertRuleSnapshot snapshot, CancellationToken cancellationToken)
    {
        var map = await companies.ResolveManyAsync([snapshot.Rule.ExternalCompanyId], cancellationToken);
        return Map(snapshot, map);
    }

    internal static AlertRuleActor ToActor(CurrentActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());

    internal static AlertRuleDefinition ToDefinition(AlertRuleInput input) =>
        new(input.RuleType, input.MetricOrEventCode, input.Operator, input.Threshold, input.Unit,
            input.BaselineWindow, input.Recurrence, TimeSpan.FromMinutes(input.CooldownMinutes),
            input.ResetPolicy, input.SessionPolicy, input.Hysteresis);

    private void ValidateDefinition(AlertRuleDefinition definition, DateTimeOffset now)
    {
        if (definition.RuleType != AlertRuleType.FinancialMetric) return;
        FinancialMetricDefinition metric;
        try
        {
            metric = metricRegistry.ResolveDefinition(
                new MetricCode(definition.MetricOrEventCode),
                DateOnly.FromDateTime(now.UtcDateTime));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw new AlertRuleValidationException($"The financial metric code '{definition.MetricOrEventCode}' is not governed.");
        }

        var compatible = metric.Unit.Code.ToLowerInvariant() switch
        {
            "percent" => definition.Unit == AlertRuleUnit.Percent,
            "ratio" => definition.Unit == AlertRuleUnit.Ratio,
            "quantity" => definition.Unit is AlertRuleUnit.Count or AlertRuleUnit.Shares,
            "amount" or "amount-per-share" or "amount-per-unit" =>
                definition.Unit is AlertRuleUnit.Rial or AlertRuleUnit.Toman,
            _ => false
        };
        if (!compatible)
            throw new AlertRuleValidationException(
                $"Unit '{definition.Unit}' is incompatible with governed metric unit '{metric.Unit.Code}'.");
    }

    private static AlertRuleInput FromDefinition(AlertRuleDefinition definition) =>
        new(definition.RuleType, definition.MetricOrEventCode, definition.Operator, definition.Threshold,
            definition.Unit, definition.BaselineWindow, definition.Recurrence,
            checked((int)definition.Cooldown.TotalMinutes), definition.ResetPolicy,
            definition.SessionPolicy, definition.Hysteresis);

    private static AlertRuleDto Map(
        AlertRuleSnapshot snapshot,
        IReadOnlyDictionary<string, CanonicalFollowedCompany> companies)
    {
        var rule = snapshot.Rule;
        var state = snapshot.EvaluationState;
        companies.TryGetValue(rule.ExternalCompanyId, out var company);
        return new AlertRuleDto(
            rule.Id, rule.ExternalCompanyId, company?.Symbol ?? rule.ExternalCompanyId,
            company?.CompanyName ?? rule.ExternalCompanyId, rule.Definition.RuleType,
            rule.Definition.MetricOrEventCode, rule.Definition.Operator, rule.Definition.Threshold,
            rule.Definition.Unit, rule.Definition.BaselineWindow, rule.Definition.Recurrence,
            checked((int)rule.Definition.Cooldown.TotalMinutes), rule.Definition.ResetPolicy,
            rule.Definition.SessionPolicy, rule.Definition.Hysteresis, rule.State, rule.Version,
            rule.State == AlertRuleState.Draft ? rule.ConfirmationNonce : string.Empty,
            rule.ConfirmationExpiresAtUtc, BuildConfirmationText(rule), rule.OriginalText, rule.ParserVersion,
            state.LastValue, state.LastObservedAtUtc, state.LastTriggeredAtUtc, state.CooldownEndsAtUtc,
            state.TriggerSequence, rule.CreatedAtUtc, rule.UpdatedAtUtc);
    }

    private static string BuildConfirmationText(AlertRule rule) =>
        $"هشدار {rule.Definition.MetricOrEventCode} برای {rule.ExternalCompanyId}: " +
        $"{rule.Definition.Operator} {rule.Definition.Threshold.ToString(CultureInfo.InvariantCulture)} {rule.Definition.Unit}";
}

public sealed partial class GovernedAlertRuleParser : IGovernedAlertRuleParser
{
    public const string Version = "conditional-tracker-parser-v1";

    public NaturalLanguageRuleProposal Parse(string text)
    {
        var normalized = NormalizeDigits(text).Trim();
        if (normalized.Contains("sql", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("script", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(';'))
            throw new AlertRuleValidationException("Executable expressions, SQL, and scripts are not supported.");

        var numberMatch = NumberRegex().Match(normalized);
        var type = ResolveType(normalized);
        var threshold = type == AlertRuleType.CodalPublication
            ? 1m
            : numberMatch.Success && decimal.TryParse(numberMatch.Value.Replace(',', '.'), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new AlertRuleValidationException("A numeric threshold could not be identified.");
        var @operator = ResolveOperator(normalized, type);
        var unit = ResolveUnit(normalized, type);
        var code = ResolveCode(normalized, type);
        var baseline = ResolveBaseline(normalized, type);
        var recurrence = normalized.Contains("یکبار", StringComparison.Ordinal) ||
                         normalized.Contains("one time", StringComparison.OrdinalIgnoreCase)
            ? AlertRuleRecurrence.OneShot
            : AlertRuleRecurrence.Recurring;
        var definition = new AlertRuleDefinition(
            type, code, @operator, threshold, unit, baseline, recurrence,
            TimeSpan.FromMinutes(30), AlertRuleResetPolicy.CrossBack,
            type == AlertRuleType.CodalPublication ? AlertRuleSessionPolicy.Any : AlertRuleSessionPolicy.TradingSessionOnly,
            hysteresis: null);
        var confirmation = $"اگر {code} {@operator} {threshold.ToString(CultureInfo.InvariantCulture)} {unit} شد، هشدار بده.";
        return new NaturalLanguageRuleProposal(definition, confirmation, Version);
    }

    private static AlertRuleType ResolveType(string text)
    {
        if (ContainsAny(text, "کدال", "گزارش", "codal")) return AlertRuleType.CodalPublication;
        if (ContainsAny(text, "قدرت خریدار", "buyer power")) return AlertRuleType.BuyerPower;
        if (ContainsAny(text, "پول حقیقی", "real money")) return AlertRuleType.RealMoneyFlow;
        if (ContainsAny(text, "صف خرید", "buy queue")) return AlertRuleType.BuyQueue;
        if (ContainsAny(text, "صف فروش", "sell queue")) return AlertRuleType.SellQueue;
        if (ContainsAny(text, "ارزش معاملات", "trading value")) return AlertRuleType.TradingValue;
        if (ContainsAny(text, "حجم", "volume")) return AlertRuleType.Volume;
        if (ContainsAny(text, "درصد تغییر", "بازده", "change percent")) return AlertRuleType.PercentageChange;
        if (ContainsAny(text, "p/e", "p/s", "eps", "roe", "فروش", "سود")) return AlertRuleType.FinancialMetric;
        if (ContainsAny(text, "قیمت", "price")) return AlertRuleType.Price;
        throw new AlertRuleValidationException("The rule family is ambiguous or unsupported.");
    }

    private static AlertRuleOperator ResolveOperator(string text, AlertRuleType type)
    {
        if (type == AlertRuleType.CodalPublication) return AlertRuleOperator.Equal;
        if (ContainsAny(text, "عبور کرد بالای", "قطع کرد بالا", "crosses above")) return AlertRuleOperator.CrossesAbove;
        if (ContainsAny(text, "عبور کرد پایین", "قطع کرد پایین", "crosses below")) return AlertRuleOperator.CrossesBelow;
        if (ContainsAny(text, "بیشتر یا مساوی", ">=", "at least")) return AlertRuleOperator.GreaterThanOrEqual;
        if (ContainsAny(text, "کمتر یا مساوی", "<=", "at most")) return AlertRuleOperator.LessThanOrEqual;
        if (ContainsAny(text, "بیشتر", "بالاتر", "greater", "above", ">")) return AlertRuleOperator.GreaterThan;
        if (ContainsAny(text, "کمتر", "پایین", "less", "below", "<")) return AlertRuleOperator.LessThan;
        if (ContainsAny(text, "برابر", "equal", "=")) return AlertRuleOperator.Equal;
        throw new AlertRuleValidationException("The comparison operator is ambiguous or unsupported.");
    }

    private static AlertRuleUnit ResolveUnit(string text, AlertRuleType type) => type switch
    {
        AlertRuleType.CodalPublication => AlertRuleUnit.None,
        AlertRuleType.PercentageChange => AlertRuleUnit.Percent,
        AlertRuleType.BuyerPower => AlertRuleUnit.Ratio,
        AlertRuleType.Volume => ContainsAny(text, "برابر میانگین", "x average") ? AlertRuleUnit.Ratio : AlertRuleUnit.Shares,
        AlertRuleType.TradingValue => ContainsAny(text, "برابر میانگین", "x average") ? AlertRuleUnit.Ratio : MoneyUnit(text),
        AlertRuleType.RealMoneyFlow or AlertRuleType.BuyQueue or AlertRuleType.SellQueue or AlertRuleType.Price => MoneyUnit(text),
        AlertRuleType.FinancialMetric when ContainsAny(text, "درصد", "percent", "roe") => AlertRuleUnit.Percent,
        AlertRuleType.FinancialMetric when ContainsAny(text, "p/e", "p/s", "نسبت", "ratio") => AlertRuleUnit.Ratio,
        AlertRuleType.FinancialMetric => MoneyUnit(text),
        _ => throw new AlertRuleValidationException("The unit is ambiguous or unsupported.")
    };

    private static AlertRuleUnit MoneyUnit(string text) =>
        ContainsAny(text, "تومان", "toman") ? AlertRuleUnit.Toman : AlertRuleUnit.Rial;

    private static string ResolveCode(string text, AlertRuleType type) => type switch
    {
        AlertRuleType.Price => "LATEST_PRICE",
        AlertRuleType.PercentageChange => "DAILY_CHANGE_PCT",
        AlertRuleType.Volume => "VOLUME",
        AlertRuleType.TradingValue => "TRADING_VALUE",
        AlertRuleType.BuyerPower => "BUYER_POWER",
        AlertRuleType.RealMoneyFlow => "REAL_MONEY_FLOW",
        AlertRuleType.BuyQueue => "BUY_QUEUE",
        AlertRuleType.SellQueue => "SELL_QUEUE",
        AlertRuleType.CodalPublication => ContainsAny(text, "ماهانه", "monthly") ? "CODAL_MONTHLY_ACTIVITY_PUBLISHED" :
            ContainsAny(text, "صورت مالی", "financial statement") ? "CODAL_FINANCIAL_STATEMENT_PUBLISHED" : "CODAL_ANNOUNCEMENT_PUBLISHED",
        AlertRuleType.FinancialMetric when ContainsAny(text, "p/e") => "PE_TTM",
        AlertRuleType.FinancialMetric when ContainsAny(text, "p/s") => "PS_TTM",
        AlertRuleType.FinancialMetric when ContainsAny(text, "eps") => "EPS",
        AlertRuleType.FinancialMetric when ContainsAny(text, "roe") => "ROE",
        AlertRuleType.FinancialMetric when ContainsAny(text, "فروش", "sales") => "MONTHLY_SALES",
        AlertRuleType.FinancialMetric => "NET_PROFIT",
        _ => throw new AlertRuleValidationException("The metric or event code is unsupported.")
    };

    private static int? ResolveBaseline(string text, AlertRuleType type)
    {
        if (type is not (AlertRuleType.Volume or AlertRuleType.TradingValue) ||
            !ContainsAny(text, "میانگین", "average")) return null;
        var match = BaselineRegex().Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var days) ? Math.Clamp(days, 1, 250) : 20;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeDigits(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            builder.Append(character switch
            {
                '۰' or '٠' => '0', '۱' or '١' => '1', '۲' or '٢' => '2', '۳' or '٣' => '3',
                '۴' or '٤' => '4', '۵' or '٥' => '5', '۶' or '٦' => '6', '۷' or '٧' => '7',
                '۸' or '٨' => '8', '۹' or '٩' => '9', '٫' => '.', '٬' => ',', _ => character
            });
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(\d{1,3})\s*(?:روز|day)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BaselineRegex();
}

using System.Text.Json;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.ConditionalTrackers;

public sealed class ConditionalTrackerEvaluationProcessor(
    FinancialIngestionDbContext dbContext,
    IConditionalTrackerEntitlementPolicy entitlements,
    INotificationIntentPublisher notifications,
    TimeProvider timeProvider,
    ILogger<ConditionalTrackerEvaluationProcessor> logger) : IConditionalTrackerEvaluationProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Meter Meter = new("FinancialCopilot.ConditionalTrackers", "1.0.0");
    private static readonly Counter<long> Evaluations = Meter.CreateCounter<long>("financialcopilot.tracker.evaluations");
    private static readonly Gauge<long> ActiveRules = Meter.CreateGauge<long>("financialcopilot.tracker.active_rules");
    private static readonly Counter<long> Triggers = Meter.CreateCounter<long>("financialcopilot.tracker.triggers");
    private static readonly Counter<long> Skips = Meter.CreateCounter<long>("financialcopilot.tracker.skips");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("financialcopilot.tracker.failures");
    private static readonly Counter<long> DuplicateSuppressions = Meter.CreateCounter<long>("financialcopilot.tracker.duplicates");
    private static readonly Counter<long> Resets = Meter.CreateCounter<long>("financialcopilot.tracker.resets");
    private static readonly Counter<long> CooldownSuppressions = Meter.CreateCounter<long>("financialcopilot.tracker.cooldown_suppressions");
    private static readonly Counter<long> NotificationHandoffs = Meter.CreateCounter<long>("financialcopilot.tracker.notification_handoffs");
    private static readonly Histogram<double> EvaluationLagSeconds = Meter.CreateHistogram<double>("financialcopilot.tracker.evaluation_lag_seconds");

    public async Task<AlertRuleEvaluationBatchResult> EvaluateDueAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount is <= 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var active = nameof(AlertRuleState.Active);
        var activeCounts = await dbContext.AlertRules.AsNoTracking()
            .Where(row => row.State == active)
            .GroupBy(row => row.RuleType)
            .Select(group => new { RuleType = group.Key, Count = group.LongCount() })
            .ToArrayAsync(cancellationToken);
        foreach (var count in activeCounts)
            ActiveRules.Record(count.Count, new TagList { { "rule.type", count.RuleType } });
        var ruleIds = await dbContext.AlertRules.AsNoTracking()
            .Where(row => row.State == active)
            .OrderBy(row => dbContext.AlertRuleEvaluationStates
                .Where(state => state.RuleId == row.Id)
                .Select(state => state.LastEvaluatedAtUtc)
                .FirstOrDefault())
            .ThenBy(row => row.UpdatedAtUtc)
            .Select(row => row.Id)
            .Take(maximumCount)
            .ToArrayAsync(cancellationToken);

        var triggered = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var ruleId in ruleIds)
        {
            try
            {
                var result = await EvaluateOneAsync(ruleId, cancellationToken);
                if (result == AlertEvaluationDecision.Triggered) triggered++;
                else skipped++;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                skipped++;
                DuplicateSuppressions.Add(1);
                logger.LogInformation(exception, "Tracker rule {RuleId} was evaluated concurrently; duplicate work was suppressed.", ruleId);
                dbContext.ChangeTracker.Clear();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                Failures.Add(1);
                logger.LogError(exception, "Conditional tracker evaluation failed for rule {RuleId}.", ruleId);
                dbContext.ChangeTracker.Clear();
            }
        }

        return new AlertRuleEvaluationBatchResult(ruleIds.Length, triggered, skipped, failed);
    }

    private async Task<AlertEvaluationDecision> EvaluateOneAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var row = await dbContext.AlertRules.SingleAsync(item => item.Id == ruleId, cancellationToken);
        var stateRow = await dbContext.AlertRuleEvaluationStates.SingleOrDefaultAsync(
            item => item.RuleId == ruleId, cancellationToken);
        if (stateRow is null)
        {
            stateRow = EfCoreAlertRuleRepository.ToRow(AlertRuleEvaluationState.Create(ruleId));
            dbContext.AlertRuleEvaluationStates.Add(stateRow);
        }

        var rule = EfCoreAlertRuleRepository.ToDomain(row);
        var state = EfCoreAlertRuleRepository.ToDomain(stateRow);
        var now = timeProvider.GetUtcNow();
        var tags = new TagList { { "rule.type", rule.Definition.RuleType.ToString() } };
        Evaluations.Add(1, tags);
        if (!await entitlements.CanEvaluateAsync(rule.Actor, cancellationToken))
        {
            await SaveDecisionAsync(stateRow, "ExpiredEntitlement", "The active plan no longer permits tracker evaluation.", now, cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            Skips.Add(1, new TagList { { "reason", "expired_entitlement" }, { "rule.type", rule.Definition.RuleType.ToString() } });
            return AlertEvaluationDecision.InactiveRule;
        }

        var resolved = await ResolveObservationAsync(rule, cancellationToken);
        AlertEvaluationOutcome outcome;
        if (resolved is null)
        {
            outcome = new AlertEvaluationOutcome(AlertEvaluationDecision.MissingData, Reason: "No compatible canonical observation is available.");
        }
        else
        {
            var wasArmed = state.Armed;
            outcome = state.Evaluate(rule, resolved.Observation, now, resolved.MaximumAge);
            if (!wasArmed && state.Armed) Resets.Add(1, tags);
            if (outcome.Decision == AlertEvaluationDecision.CooldownSuppressed) CooldownSuppressions.Add(1, tags);
            EvaluationLagSeconds.Record(Math.Max(0d, (now - resolved.Observation.SourceFreshnessUtc).TotalSeconds), tags);
        }

        var originalToken = stateRow.ConcurrencyToken;
        EfCoreAlertRuleRepository.Apply(rule, row);
        EfCoreAlertRuleRepository.Apply(state, stateRow);
        dbContext.Entry(stateRow).Property(item => item.ConcurrencyToken).OriginalValue = originalToken;
        stateRow.LastEvaluatedAtUtc = now;
        stateRow.LastDecision = outcome.Decision.ToString();
        stateRow.LastSkipReason = outcome.Reason;

        if (outcome.Trigger is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            Skips.Add(1, new TagList { { "reason", outcome.Decision.ToString() }, { "rule.type", rule.Definition.RuleType.ToString() } });
            return outcome.Decision;
        }

        var trigger = outcome.Trigger;
        var deduplicationKey = $"tracker:v1:{rule.Id}:{trigger.RuleVersion}:{trigger.EvidenceIdentity}:{trigger.Sequence}";
        var triggerRow = new AlertRuleTriggerRow
        {
            Id = Guid.NewGuid(),
            RuleId = rule.Id,
            RuleVersion = trigger.RuleVersion,
            TriggerSequence = trigger.Sequence,
            EvidenceIdentity = trigger.EvidenceIdentity,
            DeduplicationKey = deduplicationKey,
            ObservedValue = trigger.ObservedValue,
            Threshold = trigger.Threshold,
            Operator = trigger.Operator.ToString(),
            Unit = trigger.Unit.ToString(),
            SourceProvider = trigger.SourceProvider,
            SourcePeriod = trigger.SourcePeriod,
            SourceFreshnessUtc = trigger.SourceFreshnessUtc,
            TriggeredAtUtc = trigger.TriggeredAtUtc,
            EvidenceJson = trigger.EvidenceJson
        };
        dbContext.AlertRuleTriggers.Add(triggerRow);

        var payload = JsonSerializer.Serialize(new
        {
            ruleId = rule.Id,
            ruleVersion = trigger.RuleVersion,
            triggerSequence = trigger.Sequence,
            externalCompanyId = rule.ExternalCompanyId,
            ruleType = rule.Definition.RuleType.ToString(),
            metricOrEventCode = rule.Definition.MetricOrEventCode,
            observedValue = trigger.ObservedValue,
            threshold = trigger.Threshold,
            @operator = trigger.Operator.ToString(),
            unit = trigger.Unit.ToString(),
            baselineWindow = rule.Definition.BaselineWindow,
            sourceProvider = trigger.SourceProvider,
            sourcePeriod = trigger.SourcePeriod,
            sourceFreshnessUtc = trigger.SourceFreshnessUtc,
            evidenceIdentity = trigger.EvidenceIdentity,
            evidence = JsonSerializer.Deserialize<JsonElement>(trigger.EvidenceJson),
            recurring = rule.Definition.Recurrence == AlertRuleRecurrence.Recurring,
            resetPolicy = rule.Definition.ResetPolicy.ToString()
        }, JsonOptions);
        var intent = await notifications.EnqueueAsync(
            new NotificationIntentRequest(
                new NotificationActor(rule.Actor.TenantId, rule.Actor.ActorId, rule.Actor.ActorType),
                NotificationChannel.Telegram,
                "ConditionalTrackerTriggered",
                rule.Id.ToString("N"),
                deduplicationKey,
                InsightSeverity.Important,
                payload,
                now,
                now.AddDays(1),
                triggerRow.Id.ToString("N")),
            cancellationToken);
        triggerRow.NotificationIntentId = intent.Id;
        NotificationHandoffs.Add(1, tags);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        Triggers.Add(1, tags);
        return AlertEvaluationDecision.Triggered;
    }

    private async Task<ResolvedAlertObservation?> ResolveObservationAsync(
        AlertRule rule,
        CancellationToken cancellationToken)
    {
        return rule.Definition.RuleType switch
        {
            AlertRuleType.Price => await ResolveQuoteAsync(rule, priceChange: false, cancellationToken),
            AlertRuleType.PercentageChange => await ResolveQuoteAsync(rule, priceChange: true, cancellationToken),
            AlertRuleType.Volume => await ResolveTradeAsync(rule, tradingValue: false, cancellationToken),
            AlertRuleType.TradingValue => await ResolveTradeAsync(rule, tradingValue: true, cancellationToken),
            AlertRuleType.BuyerPower or AlertRuleType.RealMoneyFlow or AlertRuleType.BuyQueue or AlertRuleType.SellQueue =>
                await ResolveFeatureAsync(rule, cancellationToken),
            AlertRuleType.FinancialMetric => await ResolveFinancialMetricAsync(rule, cancellationToken),
            AlertRuleType.CodalPublication => await ResolveCodalAsync(rule, cancellationToken),
            _ => null
        };
    }

    private async Task<ResolvedAlertObservation?> ResolveQuoteAsync(
        AlertRule rule,
        bool priceChange,
        CancellationToken cancellationToken)
    {
        var quote = await (
            from company in dbContext.Companies.AsNoTracking()
            join instrument in dbContext.TradingInstruments.AsNoTracking() on company.Id equals instrument.NormalizedCompanyId
            join row in dbContext.LatestMarketQuotes.AsNoTracking() on instrument.Id equals row.TradingInstrumentId
            where company.ExternalCompanyId == rule.ExternalCompanyId && instrument.IsActive
            orderby row.AsOf descending
            select new { instrument.Symbol, row.LatestPrice, row.PriceChangePercentage, row.AsOf, row.ProviderName, row.TradingDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (quote is null) return null;
        var raw = priceChange ? quote.PriceChangePercentage : quote.LatestPrice;
        var value = priceChange ? raw : ConvertMoney(raw, rule.Definition.Unit);
        return Build(rule, value, quote.AsOf, quote.AsOf, quote.ProviderName, quote.TradingDate.ToString("yyyy-MM-dd"),
            $"quote:{quote.ProviderName}:{quote.Symbol}:{quote.AsOf:O}",
            new { quote.Symbol, value, quote.LatestPrice, quote.PriceChangePercentage, quote.AsOf, quote.ProviderName },
            TimeSpan.FromMinutes(20));
    }

    private async Task<ResolvedAlertObservation?> ResolveTradeAsync(
        AlertRule rule,
        bool tradingValue,
        CancellationToken cancellationToken)
    {
        var instrument = await (
            from company in dbContext.Companies.AsNoTracking()
            join row in dbContext.TradingInstruments.AsNoTracking() on company.Id equals row.NormalizedCompanyId
            where company.ExternalCompanyId == rule.ExternalCompanyId && row.IsActive
            orderby row.LastSynchronizedAt descending
            select new { row.Id, row.Symbol })
            .FirstOrDefaultAsync(cancellationToken);
        if (instrument is null) return null;

        var intraday = await dbContext.IntradayTradeSnapshots.AsNoTracking()
            .Where(row => row.TradingInstrumentId == instrument.Id)
            .OrderByDescending(row => row.ReceivedAt)
            .Select(row => new { row.Volume, row.TotalCapital, ObservedAt = row.ReceivedAt, row.TradingDate, row.ProviderName })
            .FirstOrDefaultAsync(cancellationToken);
        var daily = intraday is null
            ? await dbContext.DailyInstrumentTrades.AsNoTracking()
                .Where(row => row.TradingInstrumentId == instrument.Id)
                .OrderByDescending(row => row.TradingDate)
                .ThenByDescending(row => row.SourceInsertedAt)
                .Select(row => new { row.Volume, row.TotalCapital, ObservedAt = row.SourceInsertedAt, row.TradingDate, row.ProviderName })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var volume = intraday?.Volume ?? daily?.Volume;
        var totalCapital = intraday?.TotalCapital ?? daily?.TotalCapital;
        var observedAt = intraday?.ObservedAt ?? daily?.ObservedAt;
        var tradingDate = intraday?.TradingDate ?? daily?.TradingDate;
        var provider = intraday?.ProviderName ?? daily?.ProviderName;
        if (!observedAt.HasValue || !tradingDate.HasValue || provider is null) return null;

        var raw = tradingValue ? totalCapital : volume;
        if (!raw.HasValue) return null;
        decimal value;
        if (rule.Definition.BaselineWindow.HasValue)
        {
            var history = await dbContext.DailyInstrumentTrades.AsNoTracking()
                .Where(row => row.TradingInstrumentId == instrument.Id && row.TradingDate < tradingDate.Value)
                .OrderByDescending(row => row.TradingDate)
                .Take(rule.Definition.BaselineWindow.Value)
                .Select(row => tradingValue ? row.TotalCapital : row.Volume)
                .ToArrayAsync(cancellationToken);
            var baseline = history.Length == 0 ? 0m : history.Average();
            if (baseline <= 0m) return null;
            value = raw.Value / baseline;
        }
        else
        {
            value = tradingValue ? ConvertMoney(raw.Value, rule.Definition.Unit) : raw.Value;
        }

        return Build(rule, value, observedAt.Value, observedAt.Value, provider, tradingDate.Value.ToString("yyyy-MM-dd"),
            $"trade:{provider}:{instrument.Symbol}:{observedAt:O}",
            new { instrument.Symbol, value, volume, totalCapital, baselineWindow = rule.Definition.BaselineWindow, observedAt, provider },
            TimeSpan.FromHours(24));
    }

    private async Task<ResolvedAlertObservation?> ResolveFeatureAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        var row = await dbContext.FeatureSnapshots.AsNoTracking()
            .Where(item => item.ExternalCompanyId == rule.ExternalCompanyId &&
                           item.FeatureCode == rule.Definition.MetricOrEventCode && item.Value != null)
            .OrderByDescending(item => item.ObservedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || row.Value is null) return null;
        var value = rule.Definition.Unit is AlertRuleUnit.Rial or AlertRuleUnit.Toman
            ? ConvertMoney(row.Value.Value, rule.Definition.Unit)
            : row.Value.Value;
        return Build(rule, value, row.ObservedAt, row.LastSynchronizedAt, "FeatureSnapshot", row.PeriodEnd.ToString("yyyy-MM-dd"),
            $"feature:{row.FeatureCode}:{row.FeatureVersion}:{row.InputFingerprint}",
            new { value, row.FeatureCode, row.FeatureVersion, row.PolicyVersion, row.SourceEvidenceJson, row.ObservedAt },
            TimeSpan.FromHours(24));
    }

    private async Task<ResolvedAlertObservation?> ResolveFinancialMetricAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        var row = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(item => item.ExternalCompanyId == rule.ExternalCompanyId &&
                           item.MetricCode == rule.Definition.MetricOrEventCode && item.Value != null)
            .OrderByDescending(item => item.PeriodEnd)
            .ThenByDescending(item => item.ObservedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || row.Value is null) return null;
        var value = rule.Definition.Unit is AlertRuleUnit.Rial or AlertRuleUnit.Toman
            ? ConvertMoney(row.Value.Value, rule.Definition.Unit)
            : row.Value.Value;
        return Build(rule, value, row.ObservedAt, row.LastSynchronizedAt, "DerivedMetrics", row.PeriodEnd.ToString("yyyy-MM-dd"),
            $"metric:{row.Id}:{row.MetricVersion}:{row.CalculationPolicyVersion}",
            new { value, row.MetricCode, row.MetricVersion, row.CalculationPolicyVersion, row.PeriodType, row.PeriodStart, row.PeriodEnd, row.SourceEvidenceJson, row.ObservedAt },
            TimeSpan.FromDays(45));
    }

    private async Task<ResolvedAlertObservation?> ResolveCodalAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        var query = dbContext.InsightEvents.AsNoTracking().Where(row =>
            row.ExternalCompanyId == rule.ExternalCompanyId &&
            row.InsightType == nameof(InsightType.CodalAnnouncementMatched) &&
            row.DetectedAtUtc >= rule.CreatedAtUtc);
        if (rule.Definition.MetricOrEventCode == "CODAL_MONTHLY_ACTIVITY_PUBLISHED")
            query = query.Where(row => row.SourceEntityType == nameof(InsightSourceEntityType.MonthlyReport));
        if (rule.Definition.MetricOrEventCode == "CODAL_FINANCIAL_STATEMENT_PUBLISHED")
            query = query.Where(row => row.SourceEntityType == nameof(InsightSourceEntityType.FinancialStatement));
        var row = await query.OrderByDescending(item => item.DetectedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        return Build(rule, 1m, row.DetectedAtUtc, row.DetectedAtUtc, row.SourceProviderName, row.SourcePeriod,
            row.DeduplicationKey,
            new { insightEventId = row.Id, row.Title, row.Summary, evidence = JsonSerializer.Deserialize<JsonElement>(row.EvidenceJson), row.SourceEntityType, row.SourceEntityId, row.DetectedAtUtc },
            TimeSpan.FromDays(7));
    }

    private static ResolvedAlertObservation Build(
        AlertRule rule,
        decimal value,
        DateTimeOffset observedAt,
        DateTimeOffset sourceFreshness,
        string provider,
        string? period,
        string identity,
        object evidence,
        TimeSpan maximumAge)
    {
        var session = GetSession(observedAt);
        return new ResolvedAlertObservation(
            new AlertObservation(identity, value, rule.Definition.Unit, observedAt, sourceFreshness,
                provider, period, session.IsTrading, session.IsClosing, JsonSerializer.Serialize(evidence, JsonOptions)),
            maximumAge);
    }

    private static decimal ConvertMoney(decimal value, AlertRuleUnit unit) =>
        unit == AlertRuleUnit.Toman ? value / 10m : value;

    private static (bool IsTrading, bool IsClosing) GetSession(DateTimeOffset observedAt)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        var local = TimeZoneInfo.ConvertTime(observedAt, zone);
        var businessDay = local.DayOfWeek is not (DayOfWeek.Thursday or DayOfWeek.Friday);
        var time = TimeOnly.FromDateTime(local.DateTime);
        return (businessDay && time >= new TimeOnly(9, 0) && time <= new TimeOnly(12, 30),
            businessDay && time >= new TimeOnly(12, 20) && time <= new TimeOnly(12, 30));
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private async Task SaveDecisionAsync(
        AlertRuleEvaluationStateRow state,
        string decision,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        state.LastEvaluatedAtUtc = now;
        state.LastDecision = decision;
        state.LastSkipReason = reason;
        state.ConcurrencyToken = Guid.NewGuid();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record ResolvedAlertObservation(AlertObservation Observation, TimeSpan MaximumAge);
}

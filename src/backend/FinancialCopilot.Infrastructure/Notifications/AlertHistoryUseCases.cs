using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Notifications;

public sealed class AlertHistoryUseCases(
    FinancialIngestionDbContext dbContext,
    INotificationUseCases notificationUseCases,
    INotificationEntitlementPolicy entitlements,
    IOptions<AlertHistoryOptions> options,
    TimeProvider timeProvider,
    ILogger<AlertHistoryUseCases> logger) : IAlertHistoryUseCases, IAlertOutcomeHandoffProcessor
{
    private const string ReactionVersion = "alert-reaction-v1";
    private const string DetectorFallbackVersion = "detector-from-notification-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Meter Meter = new("FinancialCopilot.AlertHistory", "1.0.0");
    private static readonly Counter<long> CreatedCounter = Meter.CreateCounter<long>("alert_history.records_created");
    private static readonly Counter<long> DuplicateCounter = Meter.CreateCounter<long>("alert_history.duplicates_prevented");
    private static readonly Counter<long> FeedbackCounter = Meter.CreateCounter<long>("alert_history.feedback_recorded");
    private static readonly Counter<long> DismissCounter = Meter.CreateCounter<long>("alert_history.dismissed");
    private static readonly Counter<long> MuteCounter = Meter.CreateCounter<long>("alert_history.muted");
    private static readonly Histogram<double> CreationLag = Meter.CreateHistogram<double>("alert_history.creation_lag_seconds");

    private readonly AlertHistoryOptions _options = options.Value;

    public async Task<AlertOutcomeHandoffBatchResult> ProcessPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        maximumCount = Math.Clamp(maximumCount, 1, Math.Clamp(_options.HandoffBatchSize, 1, 1_000));
        var handoffs = await dbContext.NotificationOutcomeHandoffs
            .Where(row => row.Status == "Pending")
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .Take(maximumCount)
            .ToArrayAsync(cancellationToken);

        var result = new MutableBatchResult();
        foreach (var handoff in handoffs)
        {
            result.Considered++;
            try
            {
                var created = await ProcessHandoffAsync(handoff, cancellationToken);
                if (created) result.Created++;
                else result.Duplicates++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                result.Failed++;
                logger.LogError(exception,
                    "Alert history handoff {NotificationOutcomeHandoffId} failed; actor and evidence are redacted.",
                    handoff.Id);
            }
        }

        return result.ToResult();
    }

    public async Task<AlertHistoryPage> GetHistoryAsync(
        AlertHistoryQuery query,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(query.Actor, cancellationToken);
        var actorType = query.Actor.ActorType.ToString();
        var pageSize = Math.Clamp(query.PageSize, 1, Math.Clamp(_options.MaximumPageSize, 1, 500));
        var rowsQuery = dbContext.UserAlertRecords.AsNoTracking()
            .Where(row => row.TenantId == query.Actor.TenantId &&
                          row.ActorId == query.Actor.ActorId &&
                          row.ActorType == actorType &&
                          row.RedactedAtUtc == null);

        if (!string.IsNullOrWhiteSpace(query.SymbolKey))
            rowsQuery = rowsQuery.Where(row => row.SymbolKey == query.SymbolKey.Trim());
        if (!string.IsNullOrWhiteSpace(query.EventType))
            rowsQuery = rowsQuery.Where(row => row.EventType == query.EventType.Trim());
        if (!string.IsNullOrWhiteSpace(query.Category))
            rowsQuery = rowsQuery.Where(row => row.Category == query.Category.Trim());
        if (!string.IsNullOrWhiteSpace(query.DeliveryStatus))
            rowsQuery = rowsQuery.Where(row => row.DeliveryStatus == query.DeliveryStatus.Trim());
        if (query.Dismissed is true)
            rowsQuery = rowsQuery.Where(row => row.DismissedAtUtc != null);
        if (query.Dismissed is false)
            rowsQuery = rowsQuery.Where(row => row.DismissedAtUtc == null);

        var now = timeProvider.GetUtcNow();
        var earliest = now.AddDays(-Math.Clamp(_options.MaximumQueryRangeDays, 1, 3_650));
        var from = query.FromUtc is null || query.FromUtc < earliest ? earliest : query.FromUtc.Value;
        var to = query.ToUtc is null || query.ToUtc > now ? now : query.ToUtc.Value;
        rowsQuery = rowsQuery.Where(row => row.CreatedAtUtc >= from && row.CreatedAtUtc <= to);

        if (TryDecodeCursor(query.Cursor, out var cursorCreatedAt, out var cursorId))
        {
            rowsQuery = rowsQuery.Where(row =>
                row.CreatedAtUtc < cursorCreatedAt ||
                (row.CreatedAtUtc == cursorCreatedAt && row.Id.CompareTo(cursorId) < 0));
        }

        var rows = await rowsQuery
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);

        var pageRows = rows.Take(pageSize).ToArray();
        var nextCursor = rows.Length > pageSize && pageRows.Length > 0
            ? EncodeCursor(pageRows[^1].CreatedAtUtc, pageRows[^1].Id)
            : null;
        return new AlertHistoryPage(pageRows.Select(MapSummary).ToArray(), nextCursor, pageSize,
            rows.Length > pageSize, RetentionPolicy());
    }

    public async Task<UserAlertDetailDto?> GetDetailAsync(
        CurrentActor actor,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(actor, cancellationToken);
        return await MapDetailAsync(actor, alertId, cancellationToken);
    }

    public async Task<AlertWhyDto?> GetWhyAsync(
        CurrentActor actor,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var detail = await GetDetailAsync(actor, alertId, cancellationToken);
        return detail is null
            ? null
            : new AlertWhyDto(alertId, detail.Record.WhyText, detail.Record.EvidenceHash,
                detail.EvidenceSnapshotJson, WhyMethodology());
    }

    public async Task<UserAlertDetailDto?> DismissAsync(
        DismissAlertCommand command,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(command.Actor, cancellationToken);
        var row = await FindOwnedAsync(command.Actor, command.AlertId, cancellationToken);
        if (row is null) return null;
        row.DismissedAtUtc ??= timeProvider.GetUtcNow();
        row.RestoredAtUtc = null;
        DismissCounter.Add(1, KeyValuePair.Create<string, object?>("event_type", row.EventType));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(command.Actor, command.AlertId, cancellationToken);
    }

    public async Task<UserAlertDetailDto?> RestoreAsync(
        RestoreAlertCommand command,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(command.Actor, cancellationToken);
        var row = await FindOwnedAsync(command.Actor, command.AlertId, cancellationToken);
        if (row is null) return null;
        row.DismissedAtUtc = null;
        row.RestoredAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(command.Actor, command.AlertId, cancellationToken);
    }

    public async Task<UserAlertDetailDto?> RecordFeedbackAsync(
        FeedbackAlertCommand command,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(command.Actor, cancellationToken);
        var row = await FindOwnedAsync(command.Actor, command.AlertId, cancellationToken);
        if (row is null) return null;
        row.Feedback = Bounded(command.Feedback, 1000);
        row.FeedbackAtUtc = timeProvider.GetUtcNow();
        FeedbackCounter.Add(1, KeyValuePair.Create<string, object?>("event_type", row.EventType));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(command.Actor, command.AlertId, cancellationToken);
    }

    public async Task<UserAlertDetailDto?> MuteAsync(
        MuteAlertCommand command,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(command.Actor, cancellationToken);
        if (!command.Confirmed)
            throw new NotificationValidationException("Mute requires explicit confirmation because it changes future notifications.");
        var row = await FindOwnedAsync(command.Actor, command.AlertId, cancellationToken);
        if (row is null) return null;

        var preferences = await notificationUseCases.GetPreferencesAsync(command.Actor, cancellationToken);
        var input = ToInput(preferences);
        if (command.Scope.Equals("Category", StringComparison.OrdinalIgnoreCase))
        {
            var categories = input.Categories.Where(item =>
                    !item.EventType.Equals(row.Category, StringComparison.OrdinalIgnoreCase))
                .Append(new NotificationCategoryPreferenceInput(row.Category, false))
                .ToArray();
            input = input with { Categories = categories };
            row.MutedScope = "Category";
        }
        else if (command.Scope.Equals("Symbol", StringComparison.OrdinalIgnoreCase))
        {
            var symbols = input.Symbols.Where(item => item.ExternalCompanyId != row.SymbolKey)
                .Append(new NotificationSymbolPreferenceInput(row.SymbolKey, true))
                .ToArray();
            input = input with { Symbols = symbols };
            row.MutedScope = "Symbol";
        }
        else
        {
            throw new NotificationValidationException("Mute scope must be Symbol or Category.");
        }

        await notificationUseCases.UpdatePreferencesAsync(new UpdateNotificationPreferenceCommand(
            command.Actor, preferences.Version, input, "AlertHistory", command.CorrelationId), cancellationToken);
        row.MutedAtUtc = timeProvider.GetUtcNow();
        MuteCounter.Add(1, KeyValuePair.Create<string, object?>("scope", row.MutedScope));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(command.Actor, command.AlertId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<AlertReactionDto>> RefreshReactionAsync(
        RefreshAlertReactionCommand command,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(command.Actor, cancellationToken);
        var row = await FindOwnedAsync(command.Actor, command.AlertId, cancellationToken);
        if (row is null) return [];
        await EnsureReactionSnapshotsAsync(row, timeProvider.GetUtcNow(), forceRefresh: true, command.HorizonCode,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await dbContext.UserAlertReactionSnapshots.AsNoTracking()
            .Where(item => item.UserAlertRecordId == row.Id &&
                           (command.HorizonCode == null || item.HorizonCode == command.HorizonCode))
            .OrderBy(item => item.HorizonCode)
            .Select(item => MapReaction(item))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<string?> BuildAiContextAsync(
        CurrentActor actor,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var detail = await GetDetailAsync(actor, alertId, cancellationToken);
        if (detail is null) return null;
        var builder = new StringBuilder();
        builder.AppendLine("You are answering a follow-up about one actor-owned alert.");
        builder.AppendLine("Use only the immutable alert evidence below for alert-specific numbers. Do not change numbers, infer advice, or add unsupported performance claims.");
        builder.AppendLine($"AlertId: {detail.Record.Id:D}");
        builder.AppendLine($"EvidenceHash: {detail.Record.EvidenceHash}");
        builder.AppendLine($"Why: {detail.Record.WhyText}");
        builder.AppendLine("ImmutableEvidenceJson:");
        builder.AppendLine(detail.EvidenceSnapshotJson);
        return builder.ToString();
    }

    private async Task<bool> ProcessHandoffAsync(
        NotificationOutcomeHandoffRow handoff,
        CancellationToken cancellationToken)
    {
        if (await dbContext.UserAlertRecords.AnyAsync(row =>
                row.NotificationIntentId == handoff.NotificationIntentId &&
                row.OutcomeSequence == handoff.Sequence, cancellationToken))
        {
            handoff.Status = "Processed";
            handoff.ProcessedAtUtc ??= timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            DuplicateCounter.Add(1);
            return false;
        }

        var intent = await dbContext.NotificationIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == handoff.NotificationIntentId, cancellationToken);
        if (intent is null)
        {
            handoff.Status = "Poisoned";
            handoff.ProcessedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        var trigger = await dbContext.AlertRuleTriggers.AsNoTracking()
            .Where(row => row.NotificationIntentId == intent.Id)
            .OrderByDescending(row => row.TriggeredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var rule = trigger is null ? null : await dbContext.AlertRules.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == trigger.RuleId, cancellationToken);
        var insight = intent.SourceEventId is null ? null : await dbContext.InsightEvents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == intent.SourceEventId.Value, cancellationToken);
        var attempts = await dbContext.NotificationDeliveryAttempts.AsNoTracking()
            .Where(row => row.NotificationIntentId == intent.Id)
            .OrderBy(row => row.StartedAtUtc)
            .ThenBy(row => row.AttemptNumber)
            .ToArrayAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var snapshot = BuildEvidenceSnapshot(intent, handoff, trigger, rule, insight, attempts);
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        var hash = Hash(snapshotJson);
        var why = BuildWhyText(intent, handoff, trigger, rule, insight);
        var row = new UserAlertRecordRow
        {
            Id = Guid.NewGuid(),
            TenantId = intent.TenantId,
            ActorId = intent.ActorId,
            ActorType = intent.ActorType,
            NotificationIntentId = intent.Id,
            OutcomeHandoffId = handoff.Id,
            OutcomeSequence = handoff.Sequence,
            SourceEventId = intent.SourceEventId,
            AlertRuleId = rule?.Id,
            AlertRuleTriggerId = trigger?.Id,
            SymbolKey = Bounded(intent.EntityKey, 128),
            EventType = Bounded(intent.EventType, 128),
            Category = Bounded(string.IsNullOrWhiteSpace(intent.Category) ? intent.EventType : intent.Category, 128),
            Severity = Bounded(intent.Severity, 32),
            DeliveryStatus = Bounded(handoff.TerminalStatus, 32),
            DeliveryReason = Bounded(handoff.Reason, 64),
            DeliveredAtUtc = intent.DeliveredAtUtc,
            SuppressedAtUtc = intent.SuppressedAtUtc,
            DeadLetteredAtUtc = intent.DeadLetteredAtUtc,
            TerminalAtUtc = handoff.CreatedAtUtc,
            EvidenceReference = intent.EvidenceReference ?? handoff.EvidenceReference,
            EvidenceSnapshotJson = snapshotJson,
            EvidenceHash = hash,
            DetectorVersion = Bounded(ExtractDetectorVersion(intent.PayloadJson) ?? DetectorFallbackVersion, 64),
            RuleVersion = trigger?.RuleVersion ?? rule?.Version,
            PreferenceVersion = intent.PreferenceVersion,
            PolicyVersion = Bounded(intent.PolicyVersion ?? NotificationPreferencePolicy.Version, 64),
            WhyText = Bounded(why, 4000),
            SimilarityKey = BuildSimilarityKey(intent, trigger, insight),
            CorrelationId = Bounded(handoff.CorrelationId, 128),
            CreatedAtUtc = now,
            RetainEvidenceUntilUtc = now.AddDays(Math.Clamp(_options.EvidenceRetentionDays, 30, 3_650)),
            RetainFeedbackUntilUtc = now.AddDays(Math.Clamp(_options.FeedbackRetentionDays, 30, 3_650))
        };
        dbContext.UserAlertRecords.Add(row);
        foreach (var attempt in attempts)
        {
            dbContext.UserAlertDeliveryTimeline.Add(new UserAlertDeliveryTimelineRow
            {
                Id = Guid.NewGuid(),
                UserAlertRecordId = row.Id,
                NotificationIntentId = intent.Id,
                Status = attempt.Status,
                Reason = attempt.ErrorCode ?? "transport-attempt",
                AttemptNumber = attempt.AttemptNumber,
                ProviderMessageId = attempt.ProviderMessageId,
                ErrorCode = attempt.ErrorCode,
                OccurredAtUtc = attempt.CompletedAtUtc ?? attempt.StartedAtUtc
            });
        }

        dbContext.UserAlertDeliveryTimeline.Add(new UserAlertDeliveryTimelineRow
        {
            Id = Guid.NewGuid(),
            UserAlertRecordId = row.Id,
            NotificationIntentId = intent.Id,
            Status = handoff.TerminalStatus,
            Reason = handoff.Reason,
            OccurredAtUtc = handoff.CreatedAtUtc
        });
        await EnsureReactionSnapshotsAsync(row, now, forceRefresh: false, horizonCode: null, cancellationToken);
        handoff.Status = "Processed";
        handoff.ProcessedAtUtc = now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            CreatedCounter.Add(1, KeyValuePair.Create<string, object?>("event_type", row.EventType));
            CreationLag.Record(Math.Max(0, (now - handoff.CreatedAtUtc).TotalSeconds));
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var trackedHandoff = await dbContext.NotificationOutcomeHandoffs
                .SingleAsync(row => row.Id == handoff.Id, cancellationToken);
            trackedHandoff.Status = "Processed";
            trackedHandoff.ProcessedAtUtc ??= now;
            await dbContext.SaveChangesAsync(cancellationToken);
            DuplicateCounter.Add(1);
            return false;
        }
    }

    private async Task EnsureReactionSnapshotsAsync(
        UserAlertRecordRow row,
        DateTimeOffset now,
        bool forceRefresh,
        string? horizonCode,
        CancellationToken cancellationToken)
    {
        foreach (var horizon in new[] { "H1", "H24", "D5" }
                     .Where(item => horizonCode is null || item.Equals(horizonCode, StringComparison.OrdinalIgnoreCase)))
        {
            var existing = await dbContext.UserAlertReactionSnapshots.SingleOrDefaultAsync(item =>
                item.UserAlertRecordId == row.Id && item.HorizonCode == horizon &&
                item.InputRevision == row.EvidenceHash, cancellationToken);
            if (existing is null)
            {
                dbContext.UserAlertReactionSnapshots.Add(new UserAlertReactionSnapshotRow
                {
                    Id = Guid.NewGuid(),
                    UserAlertRecordId = row.Id,
                    HorizonCode = horizon,
                    Status = "Unavailable",
                    CalculationVersion = ReactionVersion,
                    Reason = "Canonical post-alert quote horizon is not available yet; no price reaction is calculated from guessed prices.",
                    InputRevision = row.EvidenceHash,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            else if (forceRefresh)
            {
                existing.Status = "Unavailable";
                existing.CalculationVersion = ReactionVersion;
                existing.Reason = "Canonical post-alert quote horizon is still unavailable; snapshot refreshed without mutating alert evidence and without guessed prices.";
                existing.CalculatedAtUtc = now;
                existing.UpdatedAtUtc = now;
            }
        }
    }

    private async Task<UserAlertDetailDto?> MapDetailAsync(
        CurrentActor actor,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.UserAlertRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == alertId &&
                                          item.TenantId == actor.TenantId &&
                                          item.ActorId == actor.ActorId &&
                                          item.ActorType == actor.ActorType.ToString() &&
                                          item.RedactedAtUtc == null, cancellationToken);
        if (row is null) return null;
        var timeline = await dbContext.UserAlertDeliveryTimeline.AsNoTracking()
            .Where(item => item.UserAlertRecordId == row.Id)
            .OrderBy(item => item.OccurredAtUtc)
            .Select(item => MapTimeline(item))
            .ToArrayAsync(cancellationToken);
        var reactions = await dbContext.UserAlertReactionSnapshots.AsNoTracking()
            .Where(item => item.UserAlertRecordId == row.Id)
            .OrderBy(item => item.HorizonCode)
            .Select(item => MapReaction(item))
            .ToArrayAsync(cancellationToken);
        var similar = await dbContext.UserAlertRecords.AsNoTracking()
            .Where(item => item.TenantId == row.TenantId &&
                           item.ActorId == row.ActorId &&
                           item.ActorType == row.ActorType &&
                           item.Id != row.Id &&
                           item.SimilarityKey == row.SimilarityKey &&
                           item.RedactedAtUtc == null)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(5)
            .Select(item => new AlertSimilarEventDto(
                item.Id, item.SymbolKey, item.EventType, item.Category, item.CreatedAtUtc,
                "Same actor, detector/version similarity key, symbol/category/event type, and latest bounded history window."))
            .ToArrayAsync(cancellationToken);

        return new UserAlertDetailDto(MapSummary(row), row.SourceEventId, row.AlertRuleId,
            row.NotificationIntentId, row.EvidenceReference, row.EvidenceSnapshotJson,
            row.DetectorVersion, row.RuleVersion, row.PreferenceVersion, row.PolicyVersion,
            timeline, reactions, similar, RetentionPolicy());
    }

    private Task<UserAlertRecordRow?> FindOwnedAsync(
        CurrentActor actor,
        Guid alertId,
        CancellationToken cancellationToken) =>
        dbContext.UserAlertRecords.SingleOrDefaultAsync(row => row.Id == alertId &&
            row.TenantId == actor.TenantId &&
            row.ActorId == actor.ActorId &&
            row.ActorType == actor.ActorType.ToString() &&
            row.RedactedAtUtc == null, cancellationToken);

    private static UserAlertRecordDto MapSummary(UserAlertRecordRow row) => new(
        row.Id, row.SymbolKey, row.EventType, row.Category, row.Severity, row.DeliveryStatus,
        row.DeliveryReason, row.CreatedAtUtc, row.DeliveredAtUtc, row.DismissedAtUtc,
        row.MutedAtUtc, row.WhyText, row.EvidenceHash, row.CorrelationId);

    private static AlertDeliveryTimelineDto MapTimeline(UserAlertDeliveryTimelineRow row) => new(
        row.OccurredAtUtc, row.Status, row.Reason, row.AttemptNumber, row.ProviderMessageId, row.ErrorCode);

    private static AlertReactionDto MapReaction(UserAlertReactionSnapshotRow row) => new(
        row.HorizonCode, row.Status, row.CalculationVersion, row.AnchorPrice, row.AnchorAtUtc,
        row.ReactionPercent, row.Reason, row.CalculatedAtUtc);

    private static NotificationPreferenceInput ToInput(NotificationPreferenceDto value) => new(
        value.TimeZoneId, value.DeliveryMode, value.QuietHoursStart, value.QuietHoursEnd,
        value.MinimumSeverity, value.DailyCap, value.DigestTime, value.CooldownMinutes,
        value.Categories.Select(item => new NotificationCategoryPreferenceInput(
            item.EventType, item.Enabled, item.MinimumSeverity, item.CooldownMinutes)).ToArray(),
        value.Symbols.Select(item => new NotificationSymbolPreferenceInput(
            item.ExternalCompanyId, item.Muted)).ToArray());

    private static object BuildEvidenceSnapshot(
        NotificationIntentRow intent,
        NotificationOutcomeHandoffRow handoff,
        AlertRuleTriggerRow? trigger,
        AlertRuleRow? rule,
        InsightEventRow? insight,
        IReadOnlyCollection<NotificationDeliveryAttemptRow> attempts) => new
        {
            notificationIntent = new
            {
                intent.Id,
                intent.TenantId,
                intent.ActorId,
                intent.ActorType,
                intent.Channel,
                intent.EventType,
                intent.EntityKey,
                intent.Category,
                intent.Severity,
                intent.PayloadJson,
                intent.SourceEventId,
                intent.EvidenceReference,
                intent.PolicyVersion,
                intent.PreferenceVersion,
                intent.DecisionReason,
                intent.DecisionExplanation,
                intent.DecisionAtUtc,
                intent.CreatedAtUtc,
                intent.NotBeforeUtc,
                intent.ExpiresAtUtc,
                intent.CorrelationId
            },
            outcome = new
            {
                handoff.Id,
                handoff.Sequence,
                handoff.TerminalStatus,
                handoff.Reason,
                handoff.EvidenceReference,
                handoff.CorrelationId,
                handoff.CreatedAtUtc
            },
            alertRuleTrigger = trigger is null ? null : new
            {
                trigger.Id,
                trigger.RuleId,
                trigger.RuleVersion,
                trigger.TriggerSequence,
                trigger.EvidenceIdentity,
                trigger.ObservedValue,
                trigger.Threshold,
                trigger.Operator,
                trigger.Unit,
                trigger.SourceProvider,
                trigger.SourcePeriod,
                trigger.SourceFreshnessUtc,
                trigger.TriggeredAtUtc,
                trigger.EvidenceJson
            },
            alertRule = rule is null ? null : new
            {
                rule.Id,
                rule.ExternalCompanyId,
                rule.RuleType,
                rule.MetricOrEventCode,
                rule.Operator,
                rule.Threshold,
                rule.Unit,
                rule.BaselineWindow,
                rule.Recurrence,
                rule.CooldownMinutes,
                rule.ResetPolicy,
                rule.SessionPolicy,
                rule.Hysteresis,
                rule.Version,
                rule.ParserVersion
            },
            insightEvent = insight is null ? null : new
            {
                insight.Id,
                insight.ExternalCompanyId,
                insight.Symbol,
                insight.IndustryCode,
                insight.InsightType,
                insight.Severity,
                insight.ImportanceScore,
                insight.ConfidenceScore,
                insight.Title,
                insight.Summary,
                insight.Reason,
                insight.EvidenceJson,
                insight.SourceProviderName,
                insight.SourceEntityType,
                insight.SourceEntityId,
                insight.SourcePeriod,
                insight.DetectedAtUtc
            },
            deliveryAttempts = attempts.Select(attempt => new
            {
                attempt.Id,
                attempt.PartNumber,
                attempt.Status,
                attempt.AttemptNumber,
                attempt.ProviderMessageId,
                attempt.ErrorCode,
                attempt.StartedAtUtc,
                attempt.CompletedAtUtc,
                attempt.NextRetryAtUtc
            }).ToArray(),
            retention = new
            {
                evidence = "Evidence is retained for audit/explainability, then redacted with a tombstone while preserving non-identifying operational counts.",
                feedback = "Feedback is retained separately from immutable evidence and can be redacted without changing the source alert facts."
            }
        };

    private static string BuildWhyText(
        NotificationIntentRow intent,
        NotificationOutcomeHandoffRow handoff,
        AlertRuleTriggerRow? trigger,
        AlertRuleRow? rule,
        InsightEventRow? insight)
    {
        var builder = new StringBuilder();
        builder.Append($"Alert {intent.EventType} for {intent.EntityKey}: ");
        if (trigger is not null)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"observed {trigger.ObservedValue} {trigger.Unit} {trigger.Operator} threshold {trigger.Threshold} at {trigger.SourceFreshnessUtc:O}");
            if (!string.IsNullOrWhiteSpace(trigger.SourcePeriod))
                builder.Append($"; source period {trigger.SourcePeriod}");
            builder.Append($"; source {trigger.SourceProvider}");
        }
        else if (insight is not null)
        {
            builder.Append($"{insight.Title}; importance {insight.ImportanceScore}; confidence {insight.ConfidenceScore}; source freshness {insight.DetectedAtUtc:O}");
        }
        else
        {
            builder.Append($"notification evidence reference {intent.EvidenceReference ?? "not provided"}");
        }

        if (rule is not null)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"; matched rule {rule.MetricOrEventCode} {rule.Operator} {rule.Threshold} {rule.Unit}, version {rule.Version}");
            if (rule.BaselineWindow is not null) builder.Append($", baseline window {rule.BaselineWindow}");
        }

        if (!string.IsNullOrWhiteSpace(intent.DecisionExplanation))
            builder.Append($"; preference/policy decision: {intent.DecisionExplanation}");
        builder.Append($"; delivery outcome {handoff.TerminalStatus} because {handoff.Reason}");
        builder.Append($"; policy version {intent.PolicyVersion ?? NotificationPreferencePolicy.Version}");
        if (intent.PreferenceVersion is not null) builder.Append($"; preference version {intent.PreferenceVersion}");
        return builder.ToString();
    }

    private static string? ExtractDetectorVersion(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("detectorVersion", out var detector) &&
                detector.ValueKind == JsonValueKind.String)
                return detector.GetString();
            if (document.RootElement.TryGetProperty("DetectorVersion", out var detectorPascal) &&
                detectorPascal.ValueKind == JsonValueKind.String)
                return detectorPascal.GetString();
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string BuildSimilarityKey(
        NotificationIntentRow intent,
        AlertRuleTriggerRow? trigger,
        InsightEventRow? insight)
    {
        var detector = ExtractDetectorVersion(intent.PayloadJson) ?? DetectorFallbackVersion;
        var magnitudeBand = trigger is null ? "no-threshold" : Band(trigger.ObservedValue, trigger.Threshold);
        var industry = insight?.IndustryCode ?? "unknown-industry";
        return Bounded($"{detector}|{intent.EntityKey}|{industry}|{intent.Category}|{intent.EventType}|{magnitudeBand}", 512);
    }

    private static string Band(decimal observed, decimal threshold)
    {
        if (threshold == 0) return "threshold-zero";
        var ratio = Math.Abs(observed / threshold);
        return ratio switch
        {
            < 0.9m => "below-0.9x",
            < 1.1m => "near-1x",
            < 1.5m => "1.1x-1.5x",
            _ => "above-1.5x"
        };
    }

    private static string Hash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private static string RetentionPolicy() =>
        "Immutable evidence is retained for explainability/audit and can be privacy-redacted into a tombstone; delivery timeline, feedback, and reaction snapshots are stored separately so correction or deletion never rewrites source evidence.";

    private static string WhyMethodology() =>
        "Deterministic from persisted alert evidence: observed value, threshold/operator, baseline/source freshness, matched rule/preference/policy version, and terminal delivery/suppression reason.";

    private static string Bounded(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string EncodeCursor(DateTimeOffset createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{id:N}"));

    private static bool TryDecodeCursor(string? cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor)) return false;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = text.Split('|');
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out id)) return false;
            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class MutableBatchResult
    {
        public int Considered { get; set; }
        public int Created { get; set; }
        public int Duplicates { get; set; }
        public int Failed { get; set; }
        public AlertOutcomeHandoffBatchResult ToResult() => new(Considered, Created, Duplicates, Failed);
    }
}

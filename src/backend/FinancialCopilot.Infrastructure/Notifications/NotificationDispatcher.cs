using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Notifications;

public sealed class NotificationDispatcher(
    FinancialIngestionDbContext dbContext,
    INotificationEntitlementPolicy entitlements,
    INotificationRecipientResolver recipients,
    ITelegramNotificationTransport telegram,
    IOptions<NotificationDispatcherOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    private static readonly Meter Meter = new("FinancialCopilot.Notifications", "1.0.0");
    private static readonly Counter<long> Decisions = Meter.CreateCounter<long>("notification.decisions");
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("notification.deliveries");
    private static readonly Counter<long> Retries = Meter.CreateCounter<long>("notification.retries");
    private static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>("notification.dead_letters");
    private static readonly Counter<long> DuplicateParts = Meter.CreateCounter<long>("notification.duplicate_parts_prevented");
    private static readonly Histogram<double> DeliveryLatency = Meter.CreateHistogram<double>("notification.delivery_latency_ms");
    private static readonly Histogram<double> ProviderLatency = Meter.CreateHistogram<double>("notification.provider_latency_ms");
    private static readonly Histogram<long> QueueDepth = Meter.CreateHistogram<long>("notification.queue_depth");
    private static readonly Histogram<double> QueueAge = Meter.CreateHistogram<double>("notification.queue_age_seconds");
    private static readonly Histogram<long> DigestSize = Meter.CreateHistogram<long>("notification.digest_size");
    private readonly NotificationDispatcherOptions _options = options.Value;

    public async Task<NotificationDispatchBatchResult> DispatchDueAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        var total = new MutableResult();
        var now = timeProvider.GetUtcNow();
        await RedactExpiredTransportErrorsAsync(now, cancellationToken);
        maximumCount = Math.Clamp(maximumCount, 1, Math.Clamp(_options.BatchSize, 1, 1_000));
        var dueStates = new[]
        {
            NotificationIntentState.Pending.ToString(), NotificationIntentState.Deferred.ToString(),
            NotificationIntentState.Batched.ToString(), NotificationIntentState.FailedRetryable.ToString()
        };
        var ids = await dbContext.NotificationIntents.AsNoTracking()
            .Where(row => dueStates.Contains(row.Status) && row.NotBeforeUtc <= now &&
                          (row.NextAttemptAtUtc == null || row.NextAttemptAtUtc <= now) &&
                          (row.LeaseExpiresAtUtc == null || row.LeaseExpiresAtUtc <= now))
            .OrderByDescending(row => row.Severity == InsightSeverity.Critical.ToString())
            .ThenBy(row => row.NotBeforeUtc).ThenBy(row => row.CreatedAtUtc)
            .Select(row => row.Id).Take(maximumCount).ToArrayAsync(cancellationToken);
        QueueDepth.Record(ids.Length);
        var oldest = await dbContext.NotificationIntents.AsNoTracking()
            .Where(row => dueStates.Contains(row.Status))
            .OrderBy(row => row.CreatedAtUtc).Select(row => (DateTimeOffset?)row.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (oldest is not null) QueueAge.Record(Math.Max(0, (now - oldest.Value).TotalSeconds));

        foreach (var id in ids)
        {
            var row = await TryClaimAsync(id, cancellationToken);
            if (row is null) continue;
            total.Claimed++;
            try
            {
                await ProcessAsync(row, total, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                total.Failed++;
                logger.LogError(exception,
                    "Notification dispatch failed for intent {NotificationIntentId}; actor and payload are redacted.", row.Id);
                await MarkRetryOrDeadLetterAsync([row], "DispatcherFailure",
                    "Notification dispatch failed before a provider outcome was available.", null, total, cancellationToken);
            }
        }

        return total.ToResult();
    }

    private async Task ProcessAsync(
        NotificationIntentRow row,
        MutableResult total,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (row.ExpiresAtUtc is not null && row.ExpiresAtUtc <= now)
        {
            await MarkTerminalAsync(row, NotificationIntentState.Expired,
                NotificationSuppressionReason.Expired, now, total, cancellationToken);
            total.Expired++;
            return;
        }

        var actor = new NotificationActor(row.TenantId, row.ActorId, row.ActorType);
        var preferenceRow = await dbContext.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(item =>
            item.TenantId == row.TenantId && item.ActorId == row.ActorId && item.ActorType == row.ActorType,
            cancellationToken);
        var preference = preferenceRow is null
            ? NotificationPreference.CreateDefault(new NotificationOwner(row.TenantId, row.ActorId, row.ActorType), now)
            : ToDomain(preferenceRow);
        var category = preferenceRow is null ? null : await dbContext.NotificationCategoryPreferences.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PreferenceId == preferenceRow.Id && item.EventType == row.Category,
                cancellationToken);
        var symbol = preferenceRow is null ? null : await dbContext.NotificationSymbolPreferences.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PreferenceId == preferenceRow.Id && item.ExternalCompanyId == row.EntityKey,
                cancellationToken);
        var zone = NotificationSchedule.ResolveTimeZone(preference.TimeZoneId);
        var local = NotificationSchedule.ToLocal(now, zone);
        var quiet = NotificationPreferencePolicy.IsQuietHours(TimeOnly.FromDateTime(local.DateTime),
            preference.QuietHoursStart, preference.QuietHoursEnd);
        DateTimeOffset? quietEnd = quiet && preference.QuietHoursStart is not null && preference.QuietHoursEnd is not null
            ? NotificationSchedule.NextQuietHoursEndUtc(now, zone,
                preference.QuietHoursStart.Value, preference.QuietHoursEnd.Value)
            : null;
        var (dayStart, dayEnd) = NotificationSchedule.LocalDayUtc(now, zone);
        var deliveredToday = await dbContext.NotificationIntents.AsNoTracking().CountAsync(item =>
            item.TenantId == row.TenantId && item.ActorId == row.ActorId && item.ActorType == row.ActorType &&
            item.DeliveredAtUtc >= dayStart && item.DeliveredAtUtc < dayEnd, cancellationToken);
        var lastSimilar = await dbContext.NotificationIntents.AsNoTracking()
            .Where(item => item.TenantId == row.TenantId && item.ActorId == row.ActorId &&
                           item.ActorType == row.ActorType && item.CooldownKey == row.CooldownKey &&
                           item.DeliveredAtUtc != null && item.Id != row.Id)
            .OrderByDescending(item => item.DeliveredAtUtc).Select(item => item.DeliveredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var entitled = await entitlements.CanDeliverAsync(actor, cancellationToken);
        var decision = NotificationPreferencePolicy.Evaluate(new NotificationPolicyContext(
            preference, Enum.Parse<InsightSeverity>(row.Severity),
            category?.MinimumSeverity is null ? preference.MinimumSeverity : Enum.Parse<InsightSeverity>(category.MinimumSeverity),
            category?.CooldownMinutes ?? preference.CooldownMinutes,
            entitled, category?.Enabled ?? true, symbol?.Muted ?? false, deliveredToday, now,
            row.ExpiresAtUtc, quiet, quietEnd,
            NotificationSchedule.NextDigestUtc(now, zone, preference.DigestTime),
            lastSimilar, row.BatchId is not null));
        row.PolicyVersion = decision.PolicyVersion;
        row.PreferenceVersion = preferenceRow?.Version ?? 0;
        row.DecisionAtUtc = now;
        row.DecisionReason = decision.Reason.ToString();
        row.DecisionExplanation = Bounded(decision.Explanation, 512);
        Decisions.Add(1, KeyValuePair.Create<string, object?>("action", decision.Action.ToString()),
            KeyValuePair.Create<string, object?>("reason", decision.Reason.ToString()));

        switch (decision.Action)
        {
            case NotificationPolicyAction.Expire:
                await MarkTerminalAsync(row, NotificationIntentState.Expired,
                    NotificationSuppressionReason.Expired, now, total, cancellationToken);
                total.Expired++;
                return;
            case NotificationPolicyAction.Suppress:
                await MarkTerminalAsync(row, NotificationIntentState.Suppressed,
                    decision.Reason, now, total, cancellationToken);
                total.Suppressed++;
                return;
            case NotificationPolicyAction.Defer:
                TransitionFromSending(row, NotificationIntentState.Deferred);
                row.NotBeforeUtc = decision.NotBeforeUtc;
                ReleaseLease(row);
                await dbContext.SaveChangesAsync(cancellationToken);
                total.Deferred++;
                return;
            case NotificationPolicyAction.Batch:
                TransitionFromSending(row, NotificationIntentState.Batched);
                row.NotBeforeUtc = decision.NotBeforeUtc;
                row.BatchId = await FindOrCreateBatchAsync(row, decision.NotBeforeUtc, now, cancellationToken);
                ReleaseLease(row);
                await dbContext.SaveChangesAsync(cancellationToken);
                total.Batched++;
                return;
        }

        var recipient = await recipients.ResolveTelegramAsync(actor, cancellationToken);
        if (recipient is null)
        {
            await MarkTerminalAsync(row, NotificationIntentState.Suppressed,
                NotificationSuppressionReason.MissingTelegramLink, now, total, cancellationToken);
            total.Suppressed++;
            return;
        }

        if (row.BatchId is not null)
            await DeliverBatchAsync(row, recipient.ChatId, total, cancellationToken);
        else
            await DeliverAsync([row], recipient.ChatId, Render(row), total, cancellationToken);
    }

    private async Task DeliverBatchAsync(
        NotificationIntentRow claimed,
        long chatId,
        MutableResult total,
        CancellationToken cancellationToken)
    {
        var batch = await dbContext.NotificationBatches.SingleAsync(item => item.Id == claimed.BatchId, cancellationToken);
        var siblings = await dbContext.NotificationIntents
            .Where(item => item.BatchId == batch.Id && item.Id != claimed.Id &&
                           item.Status == NotificationIntentState.Batched.ToString() &&
                           item.NotBeforeUtc <= timeProvider.GetUtcNow())
            .OrderByDescending(item => item.Severity == InsightSeverity.Critical.ToString())
            .ThenBy(item => item.CreatedAtUtc)
            .Take(Math.Max(0, batch.MaximumItems - 1)).ToArrayAsync(cancellationToken);
        foreach (var sibling in siblings)
        {
            NotificationIntentLifecycle.EnsureTransition(NotificationIntentState.Batched, NotificationIntentState.Sending);
            sibling.Status = NotificationIntentState.Sending.ToString();
            sibling.LeaseToken = claimed.LeaseToken;
            sibling.LeaseExpiresAtUtc = claimed.LeaseExpiresAtUtc;
            sibling.ConcurrencyToken = Guid.NewGuid();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        var intents = new[] { claimed }.Concat(siblings).ToArray();
        DigestSize.Record(intents.Length);
        var content = new StringBuilder("Notification digest\n\n");
        foreach (var item in intents)
            content.AppendLine(Render(item)).AppendLine();
        await DeliverAsync(intents, chatId, content.ToString().Trim(), total, cancellationToken);
        if (intents.All(item => item.Status == NotificationIntentState.Delivered.ToString()))
        {
            batch.Status = "Delivered";
            batch.DeliveredAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DeliverAsync(
        IReadOnlyCollection<NotificationIntentRow> intents,
        long chatId,
        string message,
        MutableResult total,
        CancellationToken cancellationToken)
    {
        var parts = Split(message, Math.Clamp(_options.MessagePartLength, 500, 4_000));
        for (var partNumber = 1; partNumber <= parts.Count; partNumber++)
        {
            var partKeys = intents.ToDictionary(item => item.Id,
                item => $"NOTIFY:{item.Id:N}:PART:{partNumber}");
            var alreadyDelivered = await dbContext.NotificationDeliveryAttempts.AsNoTracking()
                .Where(item => partKeys.Values.Contains(item.DeliveryPartKey) && item.Status == "Delivered")
                .Select(item => item.DeliveryPartKey).ToArrayAsync(cancellationToken);
            if (alreadyDelivered.Length == intents.Count)
            {
                DuplicateParts.Add(intents.Count);
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var attempts = new List<NotificationDeliveryAttemptRow>();
            foreach (var intent in intents.Where(item => !alreadyDelivered.Contains(partKeys[item.Id], StringComparer.Ordinal)))
            {
                var number = await dbContext.NotificationDeliveryAttempts.CountAsync(
                    item => item.NotificationIntentId == intent.Id && item.PartNumber == partNumber,
                    cancellationToken) + 1;
                var attempt = new NotificationDeliveryAttemptRow
                {
                    Id = Guid.NewGuid(), NotificationIntentId = intent.Id, PartNumber = partNumber,
                    DeliveryPartKey = partKeys[intent.Id],
                    IdempotencyKey = $"{partKeys[intent.Id]}:ATTEMPT:{number}", Status = "Sending",
                    AttemptNumber = number, StartedAtUtc = now
                };
                attempts.Add(attempt);
                dbContext.NotificationDeliveryAttempts.Add(attempt);
                intent.AttemptCount = Math.Max(intent.AttemptCount, number);
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            var providerStarted = timeProvider.GetTimestamp();
            var result = await telegram.SendAsync(chatId, parts[partNumber - 1],
                intents.Count == 1 ? partKeys[intents.First().Id] : $"BATCH:{intents.First().BatchId:N}:PART:{partNumber}",
                cancellationToken);
            ProviderLatency.Record(timeProvider.GetElapsedTime(providerStarted).TotalMilliseconds);
            foreach (var attempt in attempts)
            {
                attempt.CompletedAtUtc = timeProvider.GetUtcNow();
                attempt.ProviderMessageId = result.ProviderMessageId;
                attempt.ErrorCode = result.ErrorCode;
                attempt.ErrorRedacted = result.RedactedError;
                attempt.Status = result.Outcome == NotificationTransportOutcome.Delivered ? "Delivered" :
                    result.Outcome == NotificationTransportOutcome.RetryableFailure ? "FailedRetryable" : "FailedPermanent";
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            if (result.Outcome == NotificationTransportOutcome.RetryableFailure)
            {
                await MarkRetryOrDeadLetterAsync(intents, result.ErrorCode ?? "Retryable",
                    result.RedactedError ?? "Telegram delivery failed transiently.", result.RetryAfter,
                    total, cancellationToken);
                return;
            }
            if (result.Outcome == NotificationTransportOutcome.PermanentFailure)
            {
                foreach (var intent in intents)
                    await MarkTerminalAsync(intent, NotificationIntentState.DeadLettered,
                        NotificationSuppressionReason.PermanentTransportFailure, timeProvider.GetUtcNow(), total,
                        cancellationToken, result.ErrorCode, result.RedactedError);
                total.DeadLettered += intents.Count;
                DeadLetters.Add(intents.Count, KeyValuePair.Create<string, object?>("reason", result.ErrorCode));
                return;
            }
        }

        var deliveredAt = timeProvider.GetUtcNow();
        foreach (var intent in intents)
        {
            await MarkTerminalAsync(intent, NotificationIntentState.Delivered,
                NotificationSuppressionReason.None, deliveredAt, total, cancellationToken);
            total.Delivered++;
            Deliveries.Add(1, KeyValuePair.Create<string, object?>("event_type", intent.EventType));
            DeliveryLatency.Record(Math.Max(0, (deliveredAt - intent.CreatedAtUtc).TotalMilliseconds));
        }
    }

    private async Task MarkRetryOrDeadLetterAsync(
        IReadOnlyCollection<NotificationIntentRow> intents,
        string errorCode,
        string error,
        TimeSpan? retryAfter,
        MutableResult total,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var intent in intents)
        {
            intent.LastErrorCode = Bounded(errorCode, 64);
            intent.LastErrorRedacted = Bounded(error, 512);
            if (intent.AttemptCount >= Math.Clamp(_options.MaximumAttempts, 1, 20))
            {
                await MarkTerminalAsync(intent, NotificationIntentState.DeadLettered,
                    NotificationSuppressionReason.PermanentTransportFailure, now, total,
                    cancellationToken, errorCode, error);
                total.DeadLettered++;
                DeadLetters.Add(1, KeyValuePair.Create<string, object?>("reason", errorCode));
                continue;
            }
            NotificationIntentLifecycle.EnsureTransition(NotificationIntentState.Sending,
                NotificationIntentState.FailedRetryable);
            intent.Status = NotificationIntentState.FailedRetryable.ToString();
            var exponential = Math.Min(_options.MaximumBackoffSeconds,
                _options.InitialBackoffSeconds * Math.Pow(2, Math.Max(0, intent.AttemptCount - 1)));
            var jitter = Random.Shared.NextDouble() * Math.Max(1, exponential * 0.2);
            intent.NextAttemptAtUtc = now.Add(retryAfter ?? TimeSpan.FromSeconds(exponential + jitter));
            intent.NotBeforeUtc = intent.NextAttemptAtUtc.Value;
            ReleaseLease(intent);
            intent.ConcurrencyToken = Guid.NewGuid();
            total.Retried++;
            Retries.Add(1, KeyValuePair.Create<string, object?>("reason", errorCode));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkTerminalAsync(
        NotificationIntentRow row,
        NotificationIntentState state,
        NotificationSuppressionReason reason,
        DateTimeOffset now,
        MutableResult total,
        CancellationToken cancellationToken,
        string? errorCode = null,
        string? error = null)
    {
        var current = Enum.Parse<NotificationIntentState>(row.Status);
        if (current != state) NotificationIntentLifecycle.EnsureTransition(current, state);
        row.Status = state.ToString();
        row.DecisionReason = reason.ToString();
        row.DecisionAtUtc ??= now;
        row.LastErrorCode = errorCode is null ? row.LastErrorCode : Bounded(errorCode, 64);
        row.LastErrorRedacted = error is null ? row.LastErrorRedacted : Bounded(error, 512);
        row.DeliveredAtUtc = state == NotificationIntentState.Delivered ? now : row.DeliveredAtUtc;
        row.SuppressedAtUtc = state == NotificationIntentState.Suppressed ? now : row.SuppressedAtUtc;
        row.DeadLetteredAtUtc = state == NotificationIntentState.DeadLettered ? now : row.DeadLetteredAtUtc;
        row.ConcurrencyToken = Guid.NewGuid();
        ReleaseLease(row);
        var sequence = await dbContext.NotificationOutcomeHandoffs.CountAsync(
            item => item.NotificationIntentId == row.Id, cancellationToken) + 1;
        dbContext.NotificationOutcomeHandoffs.Add(new NotificationOutcomeHandoffRow
        {
            Id = Guid.NewGuid(), NotificationIntentId = row.Id, Sequence = sequence,
            TenantId = row.TenantId, ActorId = row.ActorId, ActorType = row.ActorType,
            TerminalStatus = state.ToString(), Reason = reason.ToString(),
            EvidenceReference = row.EvidenceReference,
            CorrelationId = row.CorrelationId ?? row.Id.ToString("N"), CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid> FindOrCreateBatchAsync(
        NotificationIntentRow row,
        DateTimeOffset scheduledFor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.NotificationBatches.SingleOrDefaultAsync(item =>
            item.TenantId == row.TenantId && item.ActorId == row.ActorId &&
            item.ActorType == row.ActorType && item.Channel == row.Channel &&
            item.ScheduledForUtc == scheduledFor && item.Status == "Open", cancellationToken);
        if (existing is not null) return existing.Id;
        var batch = new NotificationBatchRow
        {
            Id = Guid.NewGuid(), TenantId = row.TenantId, ActorId = row.ActorId,
            ActorType = row.ActorType, Channel = row.Channel, ScheduledForUtc = scheduledFor,
            Status = "Open", MaximumItems = Math.Clamp(_options.DigestMaximumItems, 1, 100),
            CreatedAtUtc = now
        };
        dbContext.NotificationBatches.Add(batch);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return batch.Id;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(batch).State = EntityState.Detached;
            var concurrent = await dbContext.NotificationBatches.SingleOrDefaultAsync(item =>
                item.TenantId == row.TenantId && item.ActorId == row.ActorId &&
                item.ActorType == row.ActorType && item.Channel == row.Channel &&
                item.ScheduledForUtc == scheduledFor, cancellationToken);
            if (concurrent is null) throw;
            return concurrent.Id;
        }
    }

    private async Task<NotificationIntentRow?> TryClaimAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var lease = Guid.NewGuid();
        var dueStates = new[]
        {
            NotificationIntentState.Pending.ToString(), NotificationIntentState.Deferred.ToString(),
            NotificationIntentState.Batched.ToString(), NotificationIntentState.FailedRetryable.ToString()
        };
        if (dbContext.Database.IsRelational())
        {
            var changed = await dbContext.NotificationIntents.Where(row => row.Id == id &&
                    dueStates.Contains(row.Status) && row.NotBeforeUtc <= now &&
                    (row.NextAttemptAtUtc == null || row.NextAttemptAtUtc <= now) &&
                    (row.LeaseExpiresAtUtc == null || row.LeaseExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, NotificationIntentState.Sending.ToString())
                    .SetProperty(row => row.LeaseToken, lease)
                    .SetProperty(row => row.LeaseExpiresAtUtc,
                        now.AddSeconds(Math.Clamp(_options.LeaseSeconds, 30, 600)))
                    .SetProperty(row => row.ConcurrencyToken, Guid.NewGuid()), cancellationToken);
            if (changed != 1) return null;
            return await dbContext.NotificationIntents.SingleAsync(row => row.Id == id, cancellationToken);
        }

        var value = await dbContext.NotificationIntents.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (value is null || !dueStates.Contains(value.Status) || value.NotBeforeUtc > now ||
            value.NextAttemptAtUtc > now || value.LeaseExpiresAtUtc > now) return null;
        var current = Enum.Parse<NotificationIntentState>(value.Status);
        NotificationIntentLifecycle.EnsureTransition(current, NotificationIntentState.Sending);
        value.Status = NotificationIntentState.Sending.ToString();
        value.LeaseToken = lease;
        value.LeaseExpiresAtUtc = now.AddSeconds(Math.Clamp(_options.LeaseSeconds, 30, 600));
        value.ConcurrencyToken = Guid.NewGuid();
        await dbContext.SaveChangesAsync(cancellationToken);
        return value;
    }

    private async Task RedactExpiredTransportErrorsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-Math.Clamp(_options.TransportErrorRetentionDays, 1, 365));
        if (dbContext.Database.IsRelational())
        {
            await dbContext.NotificationDeliveryAttempts
                .Where(row => row.CompletedAtUtc < cutoff && row.ErrorRedacted != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ErrorRedacted, (string?)null),
                    cancellationToken);
            await dbContext.NotificationIntents
                .Where(row => row.DecisionAtUtc < cutoff && row.LastErrorRedacted != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastErrorRedacted, (string?)null),
                    cancellationToken);
            return;
        }

        var attempts = await dbContext.NotificationDeliveryAttempts
            .Where(row => row.CompletedAtUtc < cutoff && row.ErrorRedacted != null).ToArrayAsync(cancellationToken);
        foreach (var row in attempts) row.ErrorRedacted = null;
        var intents = await dbContext.NotificationIntents
            .Where(row => row.DecisionAtUtc < cutoff && row.LastErrorRedacted != null).ToArrayAsync(cancellationToken);
        foreach (var row in intents) row.LastErrorRedacted = null;
        if (attempts.Length > 0 || intents.Length > 0) await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void TransitionFromSending(NotificationIntentRow row, NotificationIntentState next)
    {
        NotificationIntentLifecycle.EnsureTransition(NotificationIntentState.Sending, next);
        row.Status = next.ToString();
        row.ConcurrencyToken = Guid.NewGuid();
    }

    private static void ReleaseLease(NotificationIntentRow row)
    {
        row.LeaseToken = null;
        row.LeaseExpiresAtUtc = null;
    }

    private static NotificationPreference ToDomain(NotificationPreferenceRow row) =>
        NotificationPreference.Rehydrate(row.Id,
            new NotificationOwner(row.TenantId, row.ActorId, row.ActorType), row.TimeZoneId,
            Enum.Parse<NotificationDeliveryMode>(row.DeliveryMode), row.QuietHoursStart,
            row.QuietHoursEnd, Enum.Parse<InsightSeverity>(row.MinimumSeverity), row.DailyCap,
            row.DigestTime, row.CooldownMinutes, row.Version, row.ConcurrencyToken,
            row.CreatedAtUtc, row.UpdatedAtUtc);

    private static string Render(NotificationIntentRow row)
    {
        var result = new StringBuilder();
        result.AppendLine($"{row.EventType} — {row.EntityKey}");
        result.AppendLine($"Severity: {row.Severity}");
        foreach (var fact in ExtractFacts(row.PayloadJson).Take(12)) result.AppendLine(fact);
        if (!string.IsNullOrWhiteSpace(row.EvidenceReference))
            result.AppendLine($"Evidence: {row.EvidenceReference}");
        result.Append("Informational and evidence-based; not financial advice.");
        return result.ToString();
    }

    private static IEnumerable<string> ExtractFacts(string payloadJson)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(payloadJson); }
        catch (JsonException) { yield break; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) yield break;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("credential", StringComparison.OrdinalIgnoreCase)) continue;
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(value))
                    yield return $"{property.Name}: {Bounded(value, 500)}";
            }
        }
    }

    private static IReadOnlyList<string> Split(string value, int maximumLength)
    {
        var parts = new List<string>();
        var remaining = value.Trim();
        while (remaining.Length > maximumLength)
        {
            var at = remaining.LastIndexOf('\n', maximumLength);
            if (at < maximumLength / 2) at = maximumLength;
            parts.Add(remaining[..at].Trim());
            remaining = remaining[at..].Trim();
        }
        if (remaining.Length > 0) parts.Add(remaining);
        return parts.Count == 0 ? ["Notification content was empty."] : parts;
    }

    private static string Bounded(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed class MutableResult
    {
        public int Claimed;
        public int Delivered;
        public int Deferred;
        public int Batched;
        public int Suppressed;
        public int Expired;
        public int Retried;
        public int DeadLettered;
        public int Failed;

        public NotificationDispatchBatchResult ToResult() => new(Claimed, Delivered, Deferred, Batched,
            Suppressed, Expired, Retried, DeadLettered, Failed);
    }
}

using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.FollowedSymbols;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Notifications;

public sealed class NotificationUseCases(
    FinancialIngestionDbContext dbContext,
    IFollowedSymbolRepository followedSymbols,
    INotificationEntitlementPolicy entitlements,
    TimeProvider timeProvider) : INotificationUseCases
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<NotificationPreferenceDto> GetPreferencesAsync(
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(actor, cancellationToken);
        var row = await FindPreferenceAsync(actor, cancellationToken);
        return row is null
            ? MapDefault(actor, timeProvider.GetUtcNow())
            : await MapAsync(row, cancellationToken);
    }

    public async Task<NotificationPreferenceDto> UpdatePreferencesAsync(
        UpdateNotificationPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(command.Actor, cancellationToken);
        ValidateInput(command.Input);
        var timeZone = NotificationSchedule.ResolveTimeZone(command.Input.TimeZoneId);
        _ = timeZone;
        await ValidateFollowedSymbolsAsync(command, cancellationToken);

        var now = timeProvider.GetUtcNow();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var row = await FindPreferenceAsync(command.Actor, cancellationToken);
        NotificationPreference preference;
        var action = row is null ? "Created" : "Updated";
        if (row is null)
        {
            if (command.ExpectedVersion != 0)
                throw new NotificationValidationException("Use expected version 0 to create notification preferences.");
            preference = NotificationPreference.Create(ToOwner(command.Actor), command.Input.TimeZoneId,
                command.Input.DeliveryMode,
                command.Input.QuietHoursStart, command.Input.QuietHoursEnd, command.Input.MinimumSeverity,
                command.Input.DailyCap, command.Input.DigestTime, command.Input.CooldownMinutes, now);
            row = ToRow(preference);
            dbContext.NotificationPreferences.Add(row);
        }
        else
        {
            preference = ToDomain(row);
            preference.Update(command.ExpectedVersion, command.Input.TimeZoneId, command.Input.DeliveryMode,
                command.Input.QuietHoursStart, command.Input.QuietHoursEnd, command.Input.MinimumSeverity,
                command.Input.DailyCap, command.Input.DigestTime, command.Input.CooldownMinutes, now);
            Apply(row, preference);
        }

        var oldCategories = await dbContext.NotificationCategoryPreferences
            .Where(item => item.PreferenceId == row.Id).ToArrayAsync(cancellationToken);
        var oldSymbols = await dbContext.NotificationSymbolPreferences
            .Where(item => item.PreferenceId == row.Id).ToArrayAsync(cancellationToken);
        dbContext.NotificationCategoryPreferences.RemoveRange(oldCategories);
        dbContext.NotificationSymbolPreferences.RemoveRange(oldSymbols);
        dbContext.NotificationCategoryPreferences.AddRange(command.Input.Categories.Select(item =>
            new NotificationCategoryPreferenceRow
            {
                Id = Guid.NewGuid(), PreferenceId = row.Id, EventType = item.EventType.Trim(),
                Enabled = item.Enabled, MinimumSeverity = item.MinimumSeverity?.ToString(),
                CooldownMinutes = item.CooldownMinutes
            }));
        dbContext.NotificationSymbolPreferences.AddRange(command.Input.Symbols.Select(item =>
            new NotificationSymbolPreferenceRow
            {
                Id = Guid.NewGuid(), PreferenceId = row.Id,
                ExternalCompanyId = item.ExternalCompanyId.Trim(), Muted = item.Muted
            }));
        dbContext.NotificationPreferenceAudits.Add(new NotificationPreferenceAuditRow
        {
            Id = Guid.NewGuid(), PreferenceId = row.Id, TenantId = row.TenantId,
            ActorId = row.ActorId, ActorType = row.ActorType, Action = action,
            Source = NormalizeBounded(command.Source, 32, "Api"), Version = row.Version,
            SnapshotJson = JsonSerializer.Serialize(command.Input, JsonOptions),
            CorrelationId = NormalizeBounded(command.CorrelationId, 128, row.Id.ToString("N")),
            OccurredAtUtc = now
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw new NotificationValidationException("Notification preferences changed concurrently; reload and retry.");
        }
        return await MapAsync(row, cancellationToken);
    }

    public async Task<NotificationHistoryPage> GetHistoryAsync(
        CurrentActor actor,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await entitlements.ValidateManageAsync(actor, cancellationToken);
        offset = Math.Max(0, offset);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var rows = await dbContext.NotificationIntents.AsNoTracking()
            .Where(row => row.TenantId == actor.TenantId && row.ActorId == actor.ActorId &&
                          row.ActorType == actor.ActorType.ToString())
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Skip(offset).Take(pageSize + 1).ToArrayAsync(cancellationToken);
        return new NotificationHistoryPage(rows.Take(pageSize).Select(MapHistory).ToArray(), offset,
            pageSize, rows.Length > pageSize);
    }

    private async Task ValidateFollowedSymbolsAsync(
        UpdateNotificationPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Input.Symbols.Count == 0) return;
        var actor = new FollowedSymbolActor(command.Actor.TenantId, command.Actor.ActorId,
            command.Actor.ActorType.ToString());
        var followed = await followedSymbols.GetAsync(actor, cancellationToken);
        var allowed = followed.Select(item => item.ExternalCompanyId).ToHashSet(StringComparer.Ordinal);
        var invalid = command.Input.Symbols.Select(item => item.ExternalCompanyId.Trim())
            .Where(item => !allowed.Contains(item)).Distinct(StringComparer.Ordinal).ToArray();
        if (invalid.Length > 0)
            throw new NotificationValidationException(
                $"Symbol notification overrides require currently followed canonical companies: {string.Join(", ", invalid)}.");
    }

    private Task<NotificationPreferenceRow?> FindPreferenceAsync(CurrentActor actor, CancellationToken cancellationToken) =>
        dbContext.NotificationPreferences.SingleOrDefaultAsync(row => row.TenantId == actor.TenantId &&
            row.ActorId == actor.ActorId && row.ActorType == actor.ActorType.ToString(), cancellationToken);

    private async Task<NotificationPreferenceDto> MapAsync(
        NotificationPreferenceRow row,
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.NotificationCategoryPreferences.AsNoTracking()
            .Where(item => item.PreferenceId == row.Id).OrderBy(item => item.EventType)
            .Select(item => new NotificationCategoryPreferenceDto(item.EventType, item.Enabled,
                item.MinimumSeverity == null ? null : Enum.Parse<InsightSeverity>(item.MinimumSeverity),
                item.CooldownMinutes)).ToArrayAsync(cancellationToken);
        var symbols = await dbContext.NotificationSymbolPreferences.AsNoTracking()
            .Where(item => item.PreferenceId == row.Id).OrderBy(item => item.ExternalCompanyId)
            .Select(item => new NotificationSymbolPreferenceDto(item.ExternalCompanyId, item.Muted))
            .ToArrayAsync(cancellationToken);
        return new NotificationPreferenceDto(row.Id, row.TimeZoneId,
            Enum.Parse<NotificationDeliveryMode>(row.DeliveryMode), row.QuietHoursStart, row.QuietHoursEnd,
            Enum.Parse<InsightSeverity>(row.MinimumSeverity), row.DailyCap, row.DigestTime,
            row.CooldownMinutes, row.Version, categories, symbols, NotificationPreferencePolicy.Version,
            Explanation(), row.UpdatedAtUtc);
    }

    private static NotificationPreferenceDto MapDefault(CurrentActor actor, DateTimeOffset now)
    {
        var value = NotificationPreference.CreateDefault(ToOwner(actor), now);
        return new NotificationPreferenceDto(Guid.Empty, value.TimeZoneId, value.DeliveryMode,
            value.QuietHoursStart, value.QuietHoursEnd, value.MinimumSeverity, value.DailyCap,
            value.DigestTime, value.CooldownMinutes, 0, [], [], NotificationPreferencePolicy.Version,
            Explanation(), now);
    }

    private static string Explanation() =>
        "Precedence: entitlement, explicit symbol mute, category preference, minimum severity, daily cap, cooldown, quiet hours, then immediate/digest mode. Critical events may bypass cap, cooldown, and quiet hours, but never entitlement or explicit mutes.";

    private static NotificationHistoryItemDto MapHistory(NotificationIntentRow row) =>
        new(row.Id, row.EventType, row.EntityKey, Enum.Parse<InsightSeverity>(row.Severity),
            Enum.Parse<NotificationIntentState>(row.Status),
            Enum.TryParse<NotificationSuppressionReason>(row.DecisionReason, out var reason)
                ? reason : NotificationSuppressionReason.None,
            row.EvidenceReference, row.CreatedAtUtc, row.DeliveredAtUtc, row.LastErrorCode,
            row.AttemptCount, row.CorrelationId ?? row.Id.ToString("N"));

    private static NotificationPreference ToDomain(NotificationPreferenceRow row) =>
        NotificationPreference.Rehydrate(row.Id,
            new NotificationOwner(row.TenantId, row.ActorId, row.ActorType), row.TimeZoneId,
            Enum.Parse<NotificationDeliveryMode>(row.DeliveryMode), row.QuietHoursStart,
            row.QuietHoursEnd, Enum.Parse<InsightSeverity>(row.MinimumSeverity), row.DailyCap,
            row.DigestTime, row.CooldownMinutes, row.Version, row.ConcurrencyToken,
            row.CreatedAtUtc, row.UpdatedAtUtc);

    private static NotificationPreferenceRow ToRow(NotificationPreference preference) => new()
    {
        Id = preference.Id, TenantId = preference.Owner.TenantId, ActorId = preference.Owner.ActorId,
        ActorType = preference.Owner.ActorType, TimeZoneId = preference.TimeZoneId,
        DeliveryMode = preference.DeliveryMode.ToString(), QuietHoursStart = preference.QuietHoursStart,
        QuietHoursEnd = preference.QuietHoursEnd, MinimumSeverity = preference.MinimumSeverity.ToString(),
        DailyCap = preference.DailyCap, DigestTime = preference.DigestTime,
        CooldownMinutes = preference.CooldownMinutes, Version = preference.Version,
        ConcurrencyToken = preference.ConcurrencyToken, CreatedAtUtc = preference.CreatedAtUtc,
        UpdatedAtUtc = preference.UpdatedAtUtc
    };

    private static void Apply(NotificationPreferenceRow row, NotificationPreference preference)
    {
        row.TimeZoneId = preference.TimeZoneId;
        row.DeliveryMode = preference.DeliveryMode.ToString();
        row.QuietHoursStart = preference.QuietHoursStart;
        row.QuietHoursEnd = preference.QuietHoursEnd;
        row.MinimumSeverity = preference.MinimumSeverity.ToString();
        row.DailyCap = preference.DailyCap;
        row.DigestTime = preference.DigestTime;
        row.CooldownMinutes = preference.CooldownMinutes;
        row.Version = preference.Version;
        row.ConcurrencyToken = preference.ConcurrencyToken;
        row.UpdatedAtUtc = preference.UpdatedAtUtc;
    }

    private static NotificationOwner ToOwner(CurrentActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());

    private static void ValidateInput(NotificationPreferenceInput input)
    {
        _ = NotificationPreference.Rehydrate(Guid.NewGuid(),
            new NotificationOwner(Guid.NewGuid(), Guid.NewGuid(), "Validation"), input.TimeZoneId,
            input.DeliveryMode, input.QuietHoursStart, input.QuietHoursEnd, input.MinimumSeverity,
            input.DailyCap, input.DigestTime, input.CooldownMinutes, 1, Guid.NewGuid(),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        if (input.Categories.Count > 100 || input.Symbols.Count > 500)
            throw new NotificationValidationException("Notification overrides exceed the bounded update size.");
        if (input.Categories.Any(item => string.IsNullOrWhiteSpace(item.EventType) || item.EventType.Trim().Length > 128))
            throw new NotificationValidationException("Each notification category requires a bounded event type.");
        if (input.Categories.GroupBy(item => item.EventType.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new NotificationValidationException("Notification category overrides must be unique.");
        if (input.Categories.Any(item => item.CooldownMinutes is < 0 or > 1_440))
            throw new NotificationValidationException("Category cooldown must be between 0 and 1440 minutes.");
        if (input.Symbols.Any(item => string.IsNullOrWhiteSpace(item.ExternalCompanyId) || item.ExternalCompanyId.Trim().Length > 64))
            throw new NotificationValidationException("Each symbol override requires a canonical external company id.");
        if (input.Symbols.GroupBy(item => item.ExternalCompanyId.Trim(), StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new NotificationValidationException("Notification symbol overrides must be unique.");
    }

    private static string NormalizeBounded(string? value, int maximumLength, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}

internal static class NotificationSchedule
{
    public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw new NotificationValidationException($"Unknown timezone '{timeZoneId}'.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new NotificationValidationException($"Timezone '{timeZoneId}' is invalid on this host.");
        }
    }

    public static DateTimeOffset ToLocal(DateTimeOffset utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(utc, zone);

    public static DateTimeOffset NextQuietHoursEndUtc(
        DateTimeOffset nowUtc,
        TimeZoneInfo zone,
        TimeOnly start,
        TimeOnly end)
    {
        var local = ToLocal(nowUtc, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        if (start > end && TimeOnly.FromDateTime(local.DateTime) >= start) date = date.AddDays(1);
        return ToUtc(date, end, zone);
    }

    public static DateTimeOffset NextDigestUtc(DateTimeOffset nowUtc, TimeZoneInfo zone, TimeOnly digestTime)
    {
        var local = ToLocal(nowUtc, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        if (TimeOnly.FromDateTime(local.DateTime) >= digestTime) date = date.AddDays(1);
        return ToUtc(date, digestTime, zone);
    }

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) LocalDayUtc(
        DateTimeOffset nowUtc,
        TimeZoneInfo zone)
    {
        var date = DateOnly.FromDateTime(ToLocal(nowUtc, zone).DateTime);
        return (ToUtc(date, TimeOnly.MinValue, zone), ToUtc(date.AddDays(1), TimeOnly.MinValue, zone));
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}

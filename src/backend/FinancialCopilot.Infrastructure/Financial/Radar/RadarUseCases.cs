using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Radar;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Radar;

public sealed class RadarUseCases(
    IRadarRepository repository,
    IFollowedSymbolRepository followedSymbols,
    IRadarEntitlementPolicy entitlements,
    INotificationIntentPublisher notifications,
    IOptions<RadarOptions> options,
    TimeProvider timeProvider) : IRadarUseCases
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RadarOptions _options = options.Value;

    public async Task<RadarProfileDto> GetAsync(GetMyRadarQuery query, CancellationToken cancellationToken)
    {
        var actor = ToActor(query.Actor);
        var snapshot = await repository.FindAsync(actor, includeRemoved: true, cancellationToken);
        if (snapshot is null) return Empty(query.Actor);
        return await MapAsync(query.Actor, snapshot, cancellationToken);
    }

    public async Task<RadarProfileDto> UpdateAsync(UpdateMyRadarCommand command, CancellationToken cancellationToken)
    {
        var actor = ToActor(command.Actor);
        var followed = await followedSymbols.GetAsync(ToFollowedActor(actor), cancellationToken);
        if (command.Input.State == RadarState.Active)
            await entitlements.ValidateManageAsync(command.Actor, followed.Count, cancellationToken);
        var snapshot = await repository.FindAsync(actor, includeRemoved: true, cancellationToken);
        RadarProfile profile;
        string action;
        if (snapshot is null)
        {
            if (command.ExpectedVersion != 0)
                throw new RadarValidationException("Use expected version 0 to create a radar profile.");
            profile = RadarProfile.Create(actor, command.Input.EventTypes, command.Input.MinimumSeverity,
                command.Input.MinimumImportance, command.Input.Sensitivity, command.Input.DeliveryMode,
                command.Input.State, timeProvider.GetUtcNow());
            action = "Created";
        }
        else
        {
            profile = snapshot.Profile;
            var wasRemoved = profile.State == RadarState.Removed;
            profile.Update(command.ExpectedVersion, command.Input.EventTypes, command.Input.MinimumSeverity,
                command.Input.MinimumImportance, command.Input.Sensitivity, command.Input.DeliveryMode,
                command.Input.State, timeProvider.GetUtcNow());
            action = wasRemoved ? "Restored" : command.Input.State == RadarState.Paused ? "Paused" : "Updated";
        }

        await repository.SaveProfileAsync(profile, action, command.Source, cancellationToken);
        var updated = await repository.FindAsync(actor, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved radar profile was not found.");
        return await MapAsync(command.Actor, updated, cancellationToken);
    }

    public async Task RemoveAsync(RemoveMyRadarCommand command, CancellationToken cancellationToken)
    {
        var snapshot = await repository.FindAsync(ToActor(command.Actor), false, cancellationToken)
            ?? throw new RadarValidationException("Radar profile was not found.");
        snapshot.Profile.Remove(command.ExpectedVersion, timeProvider.GetUtcNow());
        await repository.SaveProfileAsync(snapshot.Profile, "Removed", command.Source, cancellationToken);
    }

    public async Task<RadarProfileDto> UpsertOverrideAsync(
        UpsertRadarSymbolOverrideCommand command,
        CancellationToken cancellationToken)
    {
        var actor = ToActor(command.Actor);
        var snapshot = await repository.FindAsync(actor, false, cancellationToken)
            ?? throw new RadarValidationException("Create the radar profile before adding symbol overrides.");
        var followed = await followedSymbols.FindAsync(ToFollowedActor(actor), command.ExternalCompanyId.Trim(), cancellationToken)
            ?? throw new RadarValidationException("Radar overrides are allowed only for currently followed symbols.");
        var followedCount = (await followedSymbols.GetAsync(ToFollowedActor(actor), cancellationToken)).Count;
        await entitlements.ValidateManageAsync(command.Actor, followedCount, cancellationToken);
        var existing = snapshot.SymbolOverrides.SingleOrDefault(item => item.ExternalCompanyId == followed.ExternalCompanyId);
        RadarSymbolOverride value;
        if (existing is null)
        {
            if (command.ExpectedVersion is not null and not 0)
                throw new RadarValidationException("Use expected version 0 when creating a radar symbol override.");
            value = RadarSymbolOverride.Create(snapshot.Profile.Id, followed.ExternalCompanyId,
                command.Input.State, command.Input.EventTypes, command.Input.MinimumSeverity,
                command.Input.MinimumImportance, command.Input.Sensitivity, timeProvider.GetUtcNow());
        }
        else
        {
            value = existing;
            value.Update(command.ExpectedVersion ?? throw new RadarValidationException("ExpectedVersion is required."),
                command.Input.State, command.Input.EventTypes, command.Input.MinimumSeverity,
                command.Input.MinimumImportance, command.Input.Sensitivity, timeProvider.GetUtcNow());
        }

        await repository.SaveOverrideAsync(snapshot.Profile, value, existing is null ? "OverrideCreated" : "OverrideUpdated",
            command.Source, cancellationToken);
        return await GetAsync(new GetMyRadarQuery(command.Actor), cancellationToken);
    }

    public async Task<RadarProfileDto> RemoveOverrideAsync(
        RemoveRadarSymbolOverrideCommand command,
        CancellationToken cancellationToken)
    {
        var actor = ToActor(command.Actor);
        var snapshot = await repository.FindAsync(actor, false, cancellationToken)
            ?? throw new RadarValidationException("Radar profile was not found.");
        var value = snapshot.SymbolOverrides.SingleOrDefault(item => item.ExternalCompanyId == command.ExternalCompanyId.Trim())
            ?? throw new RadarValidationException("Radar symbol override was not found.");
        value.Remove(command.ExpectedVersion, timeProvider.GetUtcNow());
        await repository.SaveOverrideAsync(snapshot.Profile, value, "OverrideRemoved", command.Source, cancellationToken);
        return await GetAsync(new GetMyRadarQuery(command.Actor), cancellationToken);
    }

    public async Task<Guid> SendTestNotificationAsync(
        SendRadarTestNotificationCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Trim().Length > 128)
            throw new RadarValidationException("A bounded idempotency key is required for a test notification.");
        var profile = await repository.FindAsync(ToActor(command.Actor), false, cancellationToken)
            ?? throw new RadarValidationException("Radar profile was not found.");
        var followedCount = (await followedSymbols.GetAsync(ToFollowedActor(profile.Profile.Actor), cancellationToken)).Count;
        await entitlements.ValidateManageAsync(command.Actor, followedCount, cancellationToken);
        var payload = JsonSerializer.Serialize(new
        {
            synthetic = true,
            billable = false,
            radarProfileId = profile.Profile.Id,
            radarPolicyVersion = RadarSelectionPolicy.Version,
            message = "Synthetic radar test notification; no market event or financial fact is asserted."
        }, JsonOptions);
        var intent = await notifications.EnqueueAsync(new NotificationIntentRequest(
            new NotificationActor(command.Actor.TenantId, command.Actor.ActorId, command.Actor.ActorType.ToString()),
            NotificationChannel.Telegram, "RadarTestNotification", $"radar:{profile.Profile.Id:N}",
            $"RADAR-TEST:{profile.Profile.Id:N}:{command.IdempotencyKey.Trim().ToUpperInvariant()}",
            InsightSeverity.Informational, payload, timeProvider.GetUtcNow(), timeProvider.GetUtcNow().AddMinutes(15),
            command.CorrelationId), cancellationToken);
        return intent.Id;
    }

    private async Task<RadarProfileDto> MapAsync(
        CurrentActor currentActor,
        RadarProfileSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var followed = await followedSymbols.GetAsync(ToFollowedActor(snapshot.Profile.Actor), cancellationToken);
        var symbols = followed.ToDictionary(item => item.ExternalCompanyId, item => item.Symbol, StringComparer.Ordinal);
        return new RadarProfileDto(snapshot.Profile.Id, snapshot.Profile.State, snapshot.Profile.EventTypes,
            snapshot.Profile.MinimumSeverity, snapshot.Profile.MinimumImportance, snapshot.Profile.Sensitivity,
            snapshot.Profile.DeliveryMode, snapshot.Profile.Version,
            snapshot.SymbolOverrides.Select(value => new RadarSymbolOverrideDto(
                value.Id, value.ExternalCompanyId, symbols.GetValueOrDefault(value.ExternalCompanyId, value.ExternalCompanyId),
                value.State, value.EventTypes, value.MinimumSeverity, value.MinimumImportance,
                value.Sensitivity, value.Version, value.UpdatedAtUtc)).ToArray(),
            _options.EvaluationCadenceSeconds, snapshot.LastEvaluatedAtUtc, snapshot.LastSourceFreshnessUtc,
            Disclosure(snapshot.LastSourceFreshnessUtc), snapshot.Profile.CreatedAtUtc, snapshot.Profile.UpdatedAtUtc);
    }

    private RadarProfileDto Empty(CurrentActor actor)
    {
        var now = timeProvider.GetUtcNow();
        return new RadarProfileDto(Guid.Empty, RadarState.Paused, DefaultEventTypes(), InsightSeverity.Notice,
            50m, RadarSensitivity.Balanced, RadarDeliveryMode.Immediate, 0, [],
            _options.EvaluationCadenceSeconds, null, null, Disclosure(null), now, now);
    }

    private string Disclosure(DateTimeOffset? freshness) => freshness.HasValue
        ? $"Radar evaluates every {_options.EvaluationCadenceSeconds} seconds; latest upstream evidence freshness is {freshness.Value:O}. Delivery cannot be faster than the source."
        : $"Radar evaluates every {_options.EvaluationCadenceSeconds} seconds; upstream freshness is not yet available, so sub-minute delivery is not promised.";

    internal static IReadOnlyCollection<InsightType> DefaultEventTypes() => Enum.GetValues<InsightType>();
    internal static RadarActor ToActor(CurrentActor actor) => new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());
    private static FinancialCopilot.Domain.Financial.FollowedSymbols.FollowedSymbolActor ToFollowedActor(RadarActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType);
}

public sealed class AllowRadarNotificationPolicyGate : IRadarNotificationPolicyGate
{
    public Task<RadarNotificationGateDecision> EvaluateAsync(
        RadarActor actor, InsightSeverity severity, RadarDeliveryMode deliveryMode,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new RadarNotificationGateDecision(
            true, RadarSuppressionReason.None, now, "notification-precedence-097-pending-v1"));
}

public sealed class RadarOptions
{
    public const string SectionName = "Radar";
    public bool Enabled { get; set; } = true;
    public int EvaluationCadenceSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
    public int MaximumEventsPerProfile { get; set; } = 200;
    public int InitialLookbackHours { get; set; } = 24;
    public int LeaseSeconds { get; set; } = 90;
    public int RetryCount { get; set; } = 3;
}

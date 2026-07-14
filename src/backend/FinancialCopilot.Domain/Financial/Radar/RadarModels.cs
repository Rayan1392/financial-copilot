using FinancialCopilot.Domain.Financial.Insights;

namespace FinancialCopilot.Domain.Financial.Radar;

public enum RadarState { Active, Paused, Removed }

public enum RadarSensitivity { Broad, Balanced, Focused }

public enum RadarDeliveryMode { Immediate, Digest }

public enum RadarMatchDecision { Matched, CompositeMatched, Suppressed }

public enum RadarSuppressionReason
{
    None,
    ProfileInactive,
    SymbolOverrideInactive,
    EventTypeDisabled,
    BelowMinimumSeverity,
    BelowMinimumImportance,
    StaleSource,
    SymbolNotFollowed,
    EntitlementDenied,
    GlobalNotificationPolicy,
    ComponentOfComposite,
    AlreadyProcessed
}

public sealed record RadarActor
{
    public RadarActor(Guid tenantId, Guid actorId, string actorType)
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

public sealed record RadarSensitivityThreshold(
    InsightSeverity MinimumSeverity,
    decimal MinimumImportance,
    TimeSpan MaximumSourceAge,
    string PolicyVersion);

public static class RadarSensitivityPolicy
{
    public const string Version = "radar-sensitivity-v1";

    public static RadarSensitivityThreshold Resolve(RadarSensitivity sensitivity) => sensitivity switch
    {
        RadarSensitivity.Broad => new(InsightSeverity.Informational, 30m, TimeSpan.FromMinutes(60), Version),
        RadarSensitivity.Balanced => new(InsightSeverity.Notice, 50m, TimeSpan.FromMinutes(30), Version),
        RadarSensitivity.Focused => new(InsightSeverity.Important, 70m, TimeSpan.FromMinutes(15), Version),
        _ => throw new ArgumentOutOfRangeException(nameof(sensitivity))
    };
}

public sealed class RadarProfile
{
    private RadarProfile(
        Guid id,
        RadarActor actor,
        RadarState state,
        IReadOnlyCollection<InsightType> eventTypes,
        InsightSeverity minimumSeverity,
        decimal minimumImportance,
        RadarSensitivity sensitivity,
        RadarDeliveryMode deliveryMode,
        int version,
        Guid concurrencyToken,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? removedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Radar profile id is required.", nameof(id));
        Validate(eventTypes, minimumImportance, version);
        Id = id;
        Actor = actor;
        State = state;
        EventTypes = NormalizeTypes(eventTypes);
        MinimumSeverity = minimumSeverity;
        MinimumImportance = minimumImportance;
        Sensitivity = sensitivity;
        DeliveryMode = deliveryMode;
        Version = version;
        ConcurrencyToken = concurrencyToken == Guid.Empty ? Guid.NewGuid() : concurrencyToken;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RemovedAtUtc = removedAtUtc;
    }

    public Guid Id { get; }
    public RadarActor Actor { get; }
    public RadarState State { get; private set; }
    public IReadOnlyCollection<InsightType> EventTypes { get; private set; }
    public InsightSeverity MinimumSeverity { get; private set; }
    public decimal MinimumImportance { get; private set; }
    public RadarSensitivity Sensitivity { get; private set; }
    public RadarDeliveryMode DeliveryMode { get; private set; }
    public int Version { get; private set; }
    public Guid ConcurrencyToken { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? RemovedAtUtc { get; private set; }

    public static RadarProfile Create(
        RadarActor actor,
        IReadOnlyCollection<InsightType> eventTypes,
        InsightSeverity minimumSeverity,
        decimal minimumImportance,
        RadarSensitivity sensitivity,
        RadarDeliveryMode deliveryMode,
        RadarState state,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), actor, state == RadarState.Removed ? throw new RadarValidationException("A new radar profile cannot be removed.") : state,
            eventTypes, minimumSeverity, minimumImportance,
            sensitivity, deliveryMode, 1, Guid.NewGuid(), now, now, null);

    public static RadarProfile Rehydrate(
        Guid id, RadarActor actor, RadarState state, IReadOnlyCollection<InsightType> eventTypes,
        InsightSeverity minimumSeverity, decimal minimumImportance, RadarSensitivity sensitivity,
        RadarDeliveryMode deliveryMode, int version, Guid concurrencyToken,
        DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc, DateTimeOffset? removedAtUtc) =>
        new(id, actor, state, eventTypes, minimumSeverity, minimumImportance, sensitivity,
            deliveryMode, version, concurrencyToken, createdAtUtc, updatedAtUtc, removedAtUtc);

    public void Update(
        int expectedVersion,
        IReadOnlyCollection<InsightType> eventTypes,
        InsightSeverity minimumSeverity,
        decimal minimumImportance,
        RadarSensitivity sensitivity,
        RadarDeliveryMode deliveryMode,
        RadarState state,
        DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        if (state == RadarState.Removed) throw new InvalidOperationException("Use Remove for the radar lifecycle transition.");
        Validate(eventTypes, minimumImportance, Version);
        EventTypes = NormalizeTypes(eventTypes);
        MinimumSeverity = minimumSeverity;
        MinimumImportance = minimumImportance;
        Sensitivity = sensitivity;
        DeliveryMode = deliveryMode;
        State = state;
        RemovedAtUtc = null;
        Touch(now);
    }

    public void Remove(int expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        State = RadarState.Removed;
        RemovedAtUtc = now;
        Touch(now);
    }

    private void EnsureVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
            throw new RadarValidationException($"Radar profile version conflict. Expected {Version}, received {expectedVersion}.");
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = now;
    }

    private static IReadOnlyCollection<InsightType> NormalizeTypes(IReadOnlyCollection<InsightType> types) =>
        types.Distinct().OrderBy(type => type).ToArray();

    private static void Validate(IReadOnlyCollection<InsightType> eventTypes, decimal minimumImportance, int version)
    {
        if (eventTypes.Count == 0) throw new RadarValidationException("At least one radar event category is required.");
        if (minimumImportance is < 0m or > 100m) throw new RadarValidationException("Minimum importance must be between 0 and 100.");
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
    }
}

public sealed class RadarSymbolOverride
{
    private RadarSymbolOverride(
        Guid id, Guid profileId, string externalCompanyId, RadarState state,
        IReadOnlyCollection<InsightType>? eventTypes, InsightSeverity? minimumSeverity,
        decimal? minimumImportance, RadarSensitivity? sensitivity, int version,
        Guid concurrencyToken, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc,
        DateTimeOffset? removedAtUtc)
    {
        if (id == Guid.Empty || profileId == Guid.Empty) throw new ArgumentException("Radar override identity is required.");
        if (string.IsNullOrWhiteSpace(externalCompanyId) || externalCompanyId.Trim().Length > 64)
            throw new RadarValidationException("A canonical external company id is required.");
        if (minimumImportance is < 0m or > 100m) throw new RadarValidationException("Minimum importance must be between 0 and 100.");
        Id = id;
        ProfileId = profileId;
        ExternalCompanyId = externalCompanyId.Trim();
        State = state;
        EventTypes = eventTypes?.Distinct().OrderBy(type => type).ToArray();
        MinimumSeverity = minimumSeverity;
        MinimumImportance = minimumImportance;
        Sensitivity = sensitivity;
        Version = version;
        ConcurrencyToken = concurrencyToken == Guid.Empty ? Guid.NewGuid() : concurrencyToken;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RemovedAtUtc = removedAtUtc;
    }

    public Guid Id { get; }
    public Guid ProfileId { get; }
    public string ExternalCompanyId { get; }
    public RadarState State { get; private set; }
    public IReadOnlyCollection<InsightType>? EventTypes { get; private set; }
    public InsightSeverity? MinimumSeverity { get; private set; }
    public decimal? MinimumImportance { get; private set; }
    public RadarSensitivity? Sensitivity { get; private set; }
    public int Version { get; private set; }
    public Guid ConcurrencyToken { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? RemovedAtUtc { get; private set; }

    public static RadarSymbolOverride Create(
        Guid profileId, string externalCompanyId, RadarState state,
        IReadOnlyCollection<InsightType>? eventTypes, InsightSeverity? minimumSeverity,
        decimal? minimumImportance, RadarSensitivity? sensitivity, DateTimeOffset now) =>
        new(Guid.NewGuid(), profileId, externalCompanyId, state, eventTypes, minimumSeverity,
            minimumImportance, sensitivity, 1, Guid.NewGuid(), now, now, null);

    public static RadarSymbolOverride Rehydrate(
        Guid id, Guid profileId, string externalCompanyId, RadarState state,
        IReadOnlyCollection<InsightType>? eventTypes, InsightSeverity? minimumSeverity,
        decimal? minimumImportance, RadarSensitivity? sensitivity, int version,
        Guid concurrencyToken, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc,
        DateTimeOffset? removedAtUtc) =>
        new(id, profileId, externalCompanyId, state, eventTypes, minimumSeverity,
            minimumImportance, sensitivity, version, concurrencyToken, createdAtUtc, updatedAtUtc, removedAtUtc);

    public void Update(
        int expectedVersion, RadarState state, IReadOnlyCollection<InsightType>? eventTypes,
        InsightSeverity? minimumSeverity, decimal? minimumImportance, RadarSensitivity? sensitivity,
        DateTimeOffset now)
    {
        if (expectedVersion != Version) throw new RadarValidationException("Radar symbol override version conflict.");
        if (state == RadarState.Removed) throw new RadarValidationException("Use Remove for the override lifecycle transition.");
        if (minimumImportance is < 0m or > 100m) throw new RadarValidationException("Minimum importance must be between 0 and 100.");
        State = state;
        EventTypes = eventTypes?.Distinct().OrderBy(type => type).ToArray();
        MinimumSeverity = minimumSeverity;
        MinimumImportance = minimumImportance;
        Sensitivity = sensitivity;
        Touch(now);
    }

    public void Remove(int expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion != Version) throw new RadarValidationException("Radar symbol override version conflict.");
        State = RadarState.Removed;
        RemovedAtUtc = now;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = now;
    }
}

public sealed record RadarEventFact(
    Guid InsightEventId,
    string ExternalCompanyId,
    InsightType InsightType,
    InsightSeverity Severity,
    decimal Importance,
    decimal Confidence,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset SourceFreshnessUtc,
    string EvidenceIdentity);

public sealed record RadarMatchEvaluation(
    RadarMatchDecision Decision,
    RadarSuppressionReason SuppressionReason,
    RadarSensitivity EffectiveSensitivity,
    InsightSeverity EffectiveMinimumSeverity,
    decimal EffectiveMinimumImportance,
    string SensitivityPolicyVersion,
    decimal HistoricalPercentile,
    decimal MatchScore);

public static class RadarSelectionPolicy
{
    public const string Version = "radar-selection-v1";
    public static readonly TimeSpan CompositeWindow = TimeSpan.FromMinutes(30);

    public static RadarMatchEvaluation Evaluate(
        RadarProfile profile,
        RadarSymbolOverride? symbolOverride,
        RadarEventFact fact,
        IReadOnlyCollection<decimal> historicalImportance,
        DateTimeOffset now)
    {
        var sensitivity = symbolOverride?.Sensitivity ?? profile.Sensitivity;
        var threshold = RadarSensitivityPolicy.Resolve(sensitivity);
        var minimumSeverity = Max(symbolOverride?.MinimumSeverity ?? profile.MinimumSeverity, threshold.MinimumSeverity);
        var minimumImportance = Math.Max(symbolOverride?.MinimumImportance ?? profile.MinimumImportance, threshold.MinimumImportance);
        var percentile = HistoricalPercentile(fact.Importance, historicalImportance);
        var score = Math.Clamp(fact.Importance * 0.7m + fact.Confidence * 0.2m + percentile * 0.1m, 0m, 100m);
        RadarSuppressionReason reason;
        if (profile.State != RadarState.Active) reason = RadarSuppressionReason.ProfileInactive;
        else if (symbolOverride?.State is RadarState.Paused or RadarState.Removed) reason = RadarSuppressionReason.SymbolOverrideInactive;
        else if (!(symbolOverride?.EventTypes ?? profile.EventTypes).Contains(fact.InsightType)) reason = RadarSuppressionReason.EventTypeDisabled;
        else if (fact.Severity < minimumSeverity) reason = RadarSuppressionReason.BelowMinimumSeverity;
        else if (fact.Importance < minimumImportance) reason = RadarSuppressionReason.BelowMinimumImportance;
        else if (now - fact.SourceFreshnessUtc > threshold.MaximumSourceAge) reason = RadarSuppressionReason.StaleSource;
        else reason = RadarSuppressionReason.None;
        return new RadarMatchEvaluation(
            reason == RadarSuppressionReason.None ? RadarMatchDecision.Matched : RadarMatchDecision.Suppressed,
            reason, sensitivity, minimumSeverity, minimumImportance,
            $"{Version}/{threshold.PolicyVersion}", percentile, Math.Round(score, 2));
    }

    public static decimal CompositeScore(IReadOnlyCollection<RadarEventFact> components)
    {
        if (components.Count < 2) throw new RadarValidationException("A composite radar event requires at least two components.");
        var average = components.Average(component => component.Importance);
        return Math.Round(Math.Min(100m, average + Math.Min(20m, (components.Count - 1) * 10m)), 2);
    }

    private static InsightSeverity Max(InsightSeverity left, InsightSeverity right) => left >= right ? left : right;

    private static decimal HistoricalPercentile(decimal current, IReadOnlyCollection<decimal> history) =>
        history.Count == 0 ? 100m : Math.Round(history.Count(value => value <= current) * 100m / history.Count, 2);
}

public sealed class RadarValidationException(string message) : InvalidOperationException(message);

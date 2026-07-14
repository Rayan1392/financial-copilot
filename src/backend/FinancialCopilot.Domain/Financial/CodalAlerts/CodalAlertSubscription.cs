using FinancialCopilot.Domain.Financial.Insights;

namespace FinancialCopilot.Domain.Financial.CodalAlerts;

public enum CodalAnnouncementType
{
    FinancialStatement,
    MonthlyActivity
}

public enum CodalAnnouncementImportance
{
    Any = 0,
    Notice = 1,
    Important = 2,
    Critical = 3
}

public enum CodalAlertSubscriptionState
{
    Active,
    Paused
}

public sealed record CodalAlertActor
{
    public CodalAlertActor(Guid tenantId, Guid actorId, string actorType)
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

public sealed class CodalAlertSubscription
{
    private CodalAnnouncementType[] _announcementTypes;

    private CodalAlertSubscription(
        Guid id,
        CodalAlertActor actor,
        string externalCompanyId,
        IReadOnlyCollection<CodalAnnouncementType> announcementTypes,
        CodalAnnouncementImportance minimumImportance,
        bool rawAlertEnabled,
        bool aiSummaryEnabled,
        CodalAlertSubscriptionState state,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Subscription id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(externalCompanyId)) throw new ArgumentException("External company id is required.", nameof(externalCompanyId));
        if (announcementTypes.Count == 0) throw new ArgumentException("At least one announcement type is required.", nameof(announcementTypes));
        if (!rawAlertEnabled && !aiSummaryEnabled) throw new ArgumentException("At least one alert mode must be enabled.");

        Id = id;
        Actor = actor;
        ExternalCompanyId = externalCompanyId.Trim();
        _announcementTypes = announcementTypes.Distinct().OrderBy(item => item).ToArray();
        MinimumImportance = minimumImportance;
        RawAlertEnabled = rawAlertEnabled;
        AiSummaryEnabled = aiSummaryEnabled;
        State = state;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public CodalAlertActor Actor { get; }

    public string ExternalCompanyId { get; }

    public IReadOnlyCollection<CodalAnnouncementType> AnnouncementTypes => _announcementTypes;

    public CodalAnnouncementImportance MinimumImportance { get; private set; }

    public bool RawAlertEnabled { get; private set; }

    public bool AiSummaryEnabled { get; private set; }

    public CodalAlertSubscriptionState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool Active => State == CodalAlertSubscriptionState.Active;

    public static CodalAlertSubscription Create(
        CodalAlertActor actor,
        string externalCompanyId,
        IReadOnlyCollection<CodalAnnouncementType> announcementTypes,
        CodalAnnouncementImportance minimumImportance,
        bool rawAlertEnabled,
        bool aiSummaryEnabled,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            actor,
            externalCompanyId,
            announcementTypes,
            minimumImportance,
            rawAlertEnabled,
            aiSummaryEnabled,
            CodalAlertSubscriptionState.Active,
            now,
            now);

    public static CodalAlertSubscription Rehydrate(
        Guid id,
        CodalAlertActor actor,
        string externalCompanyId,
        IReadOnlyCollection<CodalAnnouncementType> announcementTypes,
        CodalAnnouncementImportance minimumImportance,
        bool rawAlertEnabled,
        bool aiSummaryEnabled,
        CodalAlertSubscriptionState state,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc) =>
        new(id, actor, externalCompanyId, announcementTypes, minimumImportance, rawAlertEnabled, aiSummaryEnabled, state, createdAtUtc, updatedAtUtc);

    public bool Matches(CodalAnnouncementType announcementType, InsightSeverity severity) =>
        Active &&
        _announcementTypes.Contains(announcementType) &&
        ImportanceRank(ToImportance(severity)) >= ImportanceRank(MinimumImportance);

    public void Update(
        IReadOnlyCollection<CodalAnnouncementType> announcementTypes,
        CodalAnnouncementImportance minimumImportance,
        bool rawAlertEnabled,
        bool aiSummaryEnabled,
        CodalAlertSubscriptionState state,
        DateTimeOffset now)
    {
        if (announcementTypes.Count == 0) throw new ArgumentException("At least one announcement type is required.", nameof(announcementTypes));
        if (!rawAlertEnabled && !aiSummaryEnabled) throw new ArgumentException("At least one alert mode must be enabled.");

        _announcementTypes = announcementTypes.Distinct().OrderBy(item => item).ToArray();
        MinimumImportance = minimumImportance;
        RawAlertEnabled = rawAlertEnabled;
        AiSummaryEnabled = aiSummaryEnabled;
        State = state;
        UpdatedAtUtc = now;
    }

    private static CodalAnnouncementImportance ToImportance(InsightSeverity severity) => severity switch
    {
        InsightSeverity.Critical => CodalAnnouncementImportance.Critical,
        InsightSeverity.Important => CodalAnnouncementImportance.Important,
        InsightSeverity.Notice => CodalAnnouncementImportance.Notice,
        _ => CodalAnnouncementImportance.Any
    };

    private static int ImportanceRank(CodalAnnouncementImportance importance) => importance switch
    {
        CodalAnnouncementImportance.Critical => 3,
        CodalAnnouncementImportance.Important => 2,
        CodalAnnouncementImportance.Notice => 1,
        _ => 0
    };
}

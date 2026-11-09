namespace FinancialCopilot.Application.Administration;

/// <summary>
/// Aggregated, PII-free user facts for the admin analytics page. Each cell is one non-empty
/// combination of dimensions with its user count, so the client can slice, cross-filter,
/// pivot and drill down without another round trip and without ever seeing a single user.
/// </summary>
public sealed record AdminUserAnalyticsCube(
    IReadOnlyCollection<AdminUserAnalyticsCell> Cells,
    IReadOnlyCollection<AdminUserAnalyticsPlan> Plans,
    bool SubscriptionBreakdownAvailable,
    string ReportingTimeZone,
    DateTimeOffset AsOfUtc);

/// <param name="RegisteredOn">Registration day in the reporting time zone; null when no registration time was recorded.</param>
public sealed record AdminUserAnalyticsCell(
    string AcquisitionSource,
    string Plan,
    string Status,
    string Activity,
    DateOnly? RegisteredOn,
    int Users);

public sealed record AdminUserAnalyticsPlan(string Code, string Name);

/// <summary>One tenant member as the analytics policy needs to see it; never leaves the server.</summary>
public sealed record AdminUserAnalyticsFact(
    string AcquisitionSource,
    bool IsEnabled,
    DateTimeOffset? LockoutEnd,
    DateTimeOffset? RegisteredAtUtc,
    DateTimeOffset? LastSuccessfulLoginAtUtc,
    string? PlanCode);

/// <summary>Classification rules for the admin user analytics cube (one owner for each rule).</summary>
public static class AdminUserAnalytics
{
    /// <summary>The user has no active subscription on any customer account.</summary>
    public const string NoPlan = "none";

    /// <summary>The caller may not read subscriptions, so the plan dimension is withheld.</summary>
    public const string RestrictedPlan = "restricted";

    public const string StatusEnabled = "enabled";
    public const string StatusLocked = "locked";
    public const string StatusDisabled = "disabled";

    public const string ActiveLast7Days = "active7d";
    public const string ActiveLast30Days = "active30d";
    public const string ActiveLast90Days = "active90d";
    public const string Dormant = "dormant";
    public const string NeverLoggedIn = "never";

    public const string ReportingTimeZoneId = "Asia/Tehran";

    /// <summary>Highest tier first; plan codes outside this list rank between paid tiers and Free.</summary>
    private static readonly string[] PlanPrecedence = ["Max", "Pro", "Plus"];
    private const string FreePlanCode = "Free";

    private static readonly TimeZoneInfo ReportingTimeZone = ResolveTehranTimeZone();

    public static string ResolveStatus(bool isEnabled, DateTimeOffset? lockoutEnd, DateTimeOffset asOfUtc) =>
        !isEnabled ? StatusDisabled : lockoutEnd > asOfUtc ? StatusLocked : StatusEnabled;

    public static string ResolveActivity(DateTimeOffset? lastSuccessfulLoginAtUtc, DateTimeOffset asOfUtc)
    {
        if (lastSuccessfulLoginAtUtc is not { } lastLogin)
        {
            return NeverLoggedIn;
        }

        var age = asOfUtc - lastLogin;
        return age <= TimeSpan.FromDays(7) ? ActiveLast7Days
            : age <= TimeSpan.FromDays(30) ? ActiveLast30Days
            : age <= TimeSpan.FromDays(90) ? ActiveLast90Days
            : Dormant;
    }

    /// <summary>A user with several active customer accounts is reported under their highest plan.</summary>
    public static string ResolvePlan(IEnumerable<string?> activePlanCodes)
    {
        var codes = activePlanCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (codes.Length == 0)
        {
            return NoPlan;
        }

        return codes
            .OrderBy(PlanRank)
            .ThenBy(code => code, StringComparer.Ordinal)
            .First();
    }

    public static DateOnly? ToReportingDate(DateTimeOffset? instant) =>
        instant is { } value
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, ReportingTimeZone).DateTime)
            : null;

    public static AdminUserAnalyticsCube Build(
        IEnumerable<AdminUserAnalyticsFact> facts,
        IReadOnlyCollection<AdminUserAnalyticsPlan> plans,
        bool subscriptionBreakdownAvailable,
        DateTimeOffset asOfUtc)
    {
        var cells = facts
            .GroupBy(fact => new CellKey(
                fact.AcquisitionSource,
                subscriptionBreakdownAvailable ? fact.PlanCode ?? NoPlan : RestrictedPlan,
                ResolveStatus(fact.IsEnabled, fact.LockoutEnd, asOfUtc),
                ResolveActivity(fact.LastSuccessfulLoginAtUtc, asOfUtc),
                ToReportingDate(fact.RegisteredAtUtc)))
            .Select(group => new AdminUserAnalyticsCell(
                group.Key.AcquisitionSource,
                group.Key.Plan,
                group.Key.Status,
                group.Key.Activity,
                group.Key.RegisteredOn,
                group.Count()))
            .OrderBy(cell => cell.RegisteredOn ?? DateOnly.MinValue)
            .ThenBy(cell => cell.AcquisitionSource, StringComparer.Ordinal)
            .ThenBy(cell => cell.Plan, StringComparer.Ordinal)
            .ThenBy(cell => cell.Status, StringComparer.Ordinal)
            .ThenBy(cell => cell.Activity, StringComparer.Ordinal)
            .ToArray();

        return new AdminUserAnalyticsCube(
            cells,
            subscriptionBreakdownAvailable ? plans : [],
            subscriptionBreakdownAvailable,
            ReportingTimeZoneId,
            asOfUtc);
    }

    private static int PlanRank(string code)
    {
        var index = Array.IndexOf(PlanPrecedence, code);
        if (index >= 0)
        {
            return index;
        }

        return string.Equals(code, FreePlanCode, StringComparison.Ordinal)
            ? PlanPrecedence.Length + 1
            : PlanPrecedence.Length;
    }

    private static TimeZoneInfo ResolveTehranTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ReportingTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
    }

    private sealed record CellKey(
        string AcquisitionSource,
        string Plan,
        string Status,
        string Activity,
        DateOnly? RegisteredOn);
}

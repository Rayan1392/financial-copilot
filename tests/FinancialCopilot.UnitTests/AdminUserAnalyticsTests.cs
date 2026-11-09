using FinancialCopilot.Application.Administration;

namespace FinancialCopilot.UnitTests;

public sealed class AdminUserAnalyticsTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(new[] { "Free", "Plus" }, "Plus")]
    [InlineData(new[] { "Plus", "Max", "Pro" }, "Max")]
    [InlineData(new[] { "Free", "Enterprise" }, "Enterprise")]
    [InlineData(new[] { "Pro", "Enterprise" }, "Pro")]
    [InlineData(new[] { "Free" }, "Free")]
    [InlineData(new string[0], AdminUserAnalytics.NoPlan)]
    public void ResolvePlan_ReportsTheHighestActivePlan(string[] codes, string expected) =>
        Assert.Equal(expected, AdminUserAnalytics.ResolvePlan(codes));

    [Fact]
    public void ResolveStatus_DisabledWinsOverLockout()
    {
        var future = AsOf.AddHours(1);
        Assert.Equal(AdminUserAnalytics.StatusDisabled, AdminUserAnalytics.ResolveStatus(false, future, AsOf));
        Assert.Equal(AdminUserAnalytics.StatusLocked, AdminUserAnalytics.ResolveStatus(true, future, AsOf));
        Assert.Equal(AdminUserAnalytics.StatusEnabled, AdminUserAnalytics.ResolveStatus(true, AsOf.AddHours(-1), AsOf));
        Assert.Equal(AdminUserAnalytics.StatusEnabled, AdminUserAnalytics.ResolveStatus(true, null, AsOf));
    }

    [Theory]
    [InlineData(null, AdminUserAnalytics.NeverLoggedIn)]
    [InlineData(0, AdminUserAnalytics.ActiveLast7Days)]
    [InlineData(7, AdminUserAnalytics.ActiveLast7Days)]
    [InlineData(8, AdminUserAnalytics.ActiveLast30Days)]
    [InlineData(30, AdminUserAnalytics.ActiveLast30Days)]
    [InlineData(31, AdminUserAnalytics.ActiveLast90Days)]
    [InlineData(90, AdminUserAnalytics.ActiveLast90Days)]
    [InlineData(91, AdminUserAnalytics.Dormant)]
    public void ResolveActivity_BucketsByDaysSinceLastLogin(int? daysAgo, string expected) =>
        Assert.Equal(
            expected,
            AdminUserAnalytics.ResolveActivity(daysAgo is { } days ? AsOf.AddDays(-days) : null, AsOf));

    [Fact]
    public void ToReportingDate_UsesTehranLocalDay()
    {
        // 21:00 UTC is 00:30 the next day in Tehran (UTC+03:30).
        var lateUtc = new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 11, 9), AdminUserAnalytics.ToReportingDate(lateUtc));
        Assert.Null(AdminUserAnalytics.ToReportingDate(null));
    }

    [Fact]
    public void Build_GroupsUsersIntoNonEmptyCells()
    {
        var registered = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var facts = new[]
        {
            Fact("tahlilapp", "Plus", registered),
            Fact("tahlilapp", "Plus", registered.AddHours(2)),
            Fact("tahlilapp", null, registered),
            Fact("native", "Free", null),
        };

        var cube = AdminUserAnalytics.Build(facts, [new AdminUserAnalyticsPlan("Plus", "Plus")], true, AsOf);

        Assert.True(cube.SubscriptionBreakdownAvailable);
        Assert.Equal(4, cube.Cells.Sum(cell => cell.Users));
        Assert.Equal(3, cube.Cells.Count);
        var plus = Assert.Single(cube.Cells, cell => cell.AcquisitionSource == "tahlilapp" && cell.Plan == "Plus");
        Assert.Equal(2, plus.Users);
        Assert.Equal(new DateOnly(2026, 11, 1), plus.RegisteredOn);
        Assert.Contains(cube.Cells, cell => cell.Plan == AdminUserAnalytics.NoPlan && cell.Users == 1);
        Assert.Contains(cube.Cells, cell => cell.AcquisitionSource == "native" && cell.RegisteredOn is null);
        Assert.Equal(AdminUserAnalytics.ReportingTimeZoneId, cube.ReportingTimeZone);
    }

    [Fact]
    public void Build_WithholdsPlansWithoutSubscriptionPermission()
    {
        var facts = new[] { Fact("native", "Max", AsOf), Fact("native", null, AsOf) };

        var cube = AdminUserAnalytics.Build(facts, [new AdminUserAnalyticsPlan("Max", "Max")], false, AsOf);

        Assert.False(cube.SubscriptionBreakdownAvailable);
        Assert.Empty(cube.Plans);
        var cell = Assert.Single(cube.Cells);
        Assert.Equal(AdminUserAnalytics.RestrictedPlan, cell.Plan);
        Assert.Equal(2, cell.Users);
    }

    private static AdminUserAnalyticsFact Fact(string source, string? plan, DateTimeOffset? registeredAtUtc) =>
        new(source, true, null, registeredAtUtc, AsOf.AddDays(-1), plan);
}

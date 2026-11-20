using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Administration;

namespace FinancialCopilot.UnitTests;

public sealed class AdminConversationLog164Tests
{
    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_AcceptsTheThreePageSizes(int pageSize) =>
        Assert.Empty(AdminConversationLogQueryRules.Validate(new AdminConversationLogQuery(PageSize: pageSize)));

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(101)]
    public void Validate_RejectsOtherPageSizes(int pageSize) =>
        Assert.Contains(AdminConversationLogQueryRules.Validate(new AdminConversationLogQuery(PageSize: pageSize)),
            error => error.Key == "pageSize");

    [Fact]
    public void Validate_RejectsPageBelowOne() =>
        Assert.Contains(AdminConversationLogQueryRules.Validate(new AdminConversationLogQuery(Page: 0)), error => error.Key == "page");

    [Fact]
    public void Validate_RejectsBlankAndOverlongSearches()
    {
        var errors = AdminConversationLogQueryRules.Validate(new AdminConversationLogQuery(
            Text: "   ", User: new string('a', AdminConversationLogQueryRules.MaximumUserSearchLength + 1)));

        Assert.Contains(errors, error => error.Key == "q");
        Assert.Contains(errors, error => error.Key == "user");
    }

    [Fact]
    public void Validate_RejectsEmptyOrReversedRange()
    {
        var at = DateTimeOffset.Parse("2026-11-01T00:00:00Z");

        Assert.Contains(AdminConversationLogQueryRules.Validate(new AdminConversationLogQuery(FromUtc: at, ToUtc: at)),
            error => error.Key == "fromUtc");
        Assert.Empty(AdminConversationLogQueryRules.Validate(new AdminConversationLogQuery(FromUtc: at, ToUtc: at.AddDays(1))));
    }

    [Theory]
    [InlineData("failed", AdminConversationLogStatus.Failed)]
    [InlineData("NoResponse", AdminConversationLogStatus.NoResponse)]
    [InlineData(" temporarilyUnavailable ", AdminConversationLogStatus.TemporarilyUnavailable)]
    public void ParseStatus_AcceptsNamesIgnoringCase(string value, AdminConversationLogStatus expected) =>
        Assert.Equal(expected, AdminConversationLogQueryRules.ParseStatus(value));

    [Theory]
    [InlineData("3")]
    [InlineData("unknown")]
    [InlineData("")]
    public void ParseStatus_RejectsNumbersAndUnknownNames(string value) =>
        Assert.Null(AdminConversationLogQueryRules.ParseStatus(value));

    [Fact]
    public void NormalizeSearchText_MapsArabicYehAndKafToPersian() =>
        Assert.Equal("کیان", AdminConversationLogQueryRules.NormalizeSearchText(" كيان "));

    [Fact]
    public void ContainsPattern_EscapesLikeWildcards() =>
        Assert.Equal(@"%50\% \_x\\y%", AdminConversationLogQueryRules.ContainsPattern(@"50% _x\y"));

    [Fact]
    public void StatusOf_UsesOutcomeAndTreatsMissingAsAnswered()
    {
        Assert.Equal(AdminConversationLogStatus.NoResponse, AdminConversationLogQueryRules.StatusOf(false, null));
        Assert.Equal(AdminConversationLogStatus.Answered, AdminConversationLogQueryRules.StatusOf(true, null));
        Assert.Equal(AdminConversationLogStatus.Answered, AdminConversationLogQueryRules.StatusOf(true, 99));
        Assert.Equal(AdminConversationLogStatus.Failed,
            AdminConversationLogQueryRules.StatusOf(true, (int)DialogueOutcome.Failed));
    }

    [Fact]
    public void EveryOutcomeHasAStatusAndRoundTripsToItsStoredValue()
    {
        foreach (var outcome in Enum.GetValues<DialogueOutcome>())
        {
            var status = AdminConversationLogQueryRules.FromOutcome(outcome);
            Assert.Equal(outcome.ToString(), status.ToString());
            Assert.Equal((int)outcome, AdminConversationLogQueryRules.StoredOutcomeOf(status));
        }

        Assert.Null(AdminConversationLogQueryRules.StoredOutcomeOf(AdminConversationLogStatus.NoResponse));
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    public void TotalPages_RoundsUp(int count, int size, int expected) =>
        Assert.Equal(expected, AdminConversationLogQueryRules.TotalPages(count, size));
}

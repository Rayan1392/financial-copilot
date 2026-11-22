using FinancialCopilot.Application.InAppMessages;

namespace FinancialCopilot.UnitTests;

public sealed class InAppMessage165Tests
{
    [Fact]
    public void User_replies_keep_the_first_pending_time_until_staff_answers()
    {
        var time = new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
        var conversation = new SupportConversationState(true, 1, null);
        var first = conversation.Append(MessageAudience.Recipient, null, time);
        var second = first.Append(MessageAudience.Recipient, null, time.AddMinutes(1));

        Assert.Equal(time, second.PendingSinceUtc);
        Assert.Equal(3, second.Revision);
        var answered = second.Append(MessageAudience.Staff, 3, time.AddMinutes(2));
        Assert.Null(answered.PendingSinceUtc);
        Assert.Equal(time.AddMinutes(3), answered.Append(MessageAudience.Recipient, null, time.AddMinutes(3)).PendingSinceUtc);
    }

    [Fact]
    public void Reply_modes_and_staff_revisions_are_enforced()
    {
        var notice = new SupportConversationState(false, 1, null);
        var support = new SupportConversationState(true, 2, null);

        var oneWay = Assert.Throws<InAppMessageException>(() => notice.Append(MessageAudience.Recipient, null, DateTimeOffset.UnixEpoch));
        var oldRevision = Assert.Throws<InAppMessageException>(() => support.Append(MessageAudience.Staff, 1, DateTimeOffset.UnixEpoch));

        Assert.Equal("message-replies-disabled", oneWay.Code);
        Assert.Equal("message-revision-conflict", oldRevision.Code);
    }
}

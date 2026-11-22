namespace FinancialCopilot.Application.InAppMessages;

public sealed record SupportConversationState(bool RepliesEnabled, long Revision, DateTimeOffset? PendingSinceUtc)
{
    public SupportConversationState Append(MessageAudience audience, long? expectedRevision, DateTimeOffset now)
    {
        if (!RepliesEnabled)
            throw new InAppMessageException(MessageFailure.Conflict, "message-replies-disabled");

        var fromStaff = audience == MessageAudience.Staff;
        if (fromStaff && expectedRevision != Revision)
            throw new InAppMessageException(MessageFailure.Conflict, "message-revision-conflict");

        return this with
        {
            Revision = checked(Revision + 1),
            PendingSinceUtc = fromStaff ? null : PendingSinceUtc ?? now
        };
    }
}

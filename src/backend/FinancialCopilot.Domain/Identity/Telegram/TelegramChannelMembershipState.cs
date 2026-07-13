namespace FinancialCopilot.Domain.Identity.Telegram;

public enum TelegramChannelMembershipStatus
{
    Creator,
    Administrator,
    Member,
    RestrictedMember,
    Left,
    Kicked,
    NotFound,
    UnknownProviderFailure
}

public enum TelegramMembershipFailureCategory
{
    None,
    BotTokenMissing,
    ChannelNotConfigured,
    ProviderUnavailable,
    ProviderRejected,
    UnexpectedResponse
}

public static class TelegramChannelMembershipStatusExtensions
{
    public static bool IsEligible(this TelegramChannelMembershipStatus status) =>
        status is TelegramChannelMembershipStatus.Creator or
            TelegramChannelMembershipStatus.Administrator or
            TelegramChannelMembershipStatus.Member or
            TelegramChannelMembershipStatus.RestrictedMember;
}

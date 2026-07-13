namespace FinancialCopilot.Domain.Identity.Telegram;

public enum TelegramLinkTokenPurpose
{
    WebToTelegram = 1,
    TelegramToWeb = 2
}

public enum TelegramLinkTokenStatus
{
    Pending = 1,
    Consumed = 2,
    Expired = 3,
    Revoked = 4
}

public enum TelegramLinkAuditAction
{
    TokenIssued = 1,
    TokenRevoked = 2,
    Linked = 3,
    Unlinked = 4,
    Relinked = 5,
    ReplayRejected = 6,
    ConflictRejected = 7
}

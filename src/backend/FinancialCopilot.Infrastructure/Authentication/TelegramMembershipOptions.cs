namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramMembershipOptions
{
    public const string SectionName = "Telegram:Membership";

    public string RequiredChannelId { get; set; } = string.Empty;
    public string BotTokenEnvironmentVariable { get; set; } = "TELEGRAM_BOT_TOKEN";
    public int VerificationCacheMinutes { get; set; } = 60;
    public int ProviderFailureCacheMinutes { get; set; } = 5;
    public decimal DailyFreeCredits { get; set; } = 5m;
    public string PolicyVersion { get; set; } = "telegram-free-daily-v1";
}

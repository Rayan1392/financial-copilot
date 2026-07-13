namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramMembershipRevalidationOptions
{
    public const string SectionName = "Telegram:Membership:Revalidation";

    public bool Enabled { get; set; } = true;
    public int CadenceSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 25;
    public int MaxConcurrency { get; set; } = 4;
    public int LeaseSeconds { get; set; } = 300;
    public int RetryCount { get; set; } = 3;
    public int InitialBackoffSeconds { get; set; } = 30;
    public int MaxBackoffSeconds { get; set; } = 900;
}

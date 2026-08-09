namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramGatewayOptions
{
    public const string SectionName = "Telegram:Gateway";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceSecret { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaximumClockSkewSeconds { get; set; } = 120;
}

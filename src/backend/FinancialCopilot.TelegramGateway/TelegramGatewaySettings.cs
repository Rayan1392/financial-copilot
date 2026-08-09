namespace FinancialCopilot.TelegramGateway;

public sealed class TelegramGatewaySettings
{
    public const string SectionName = "TelegramGateway";
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = string.Empty;
    public string PrimaryApiBaseUrl { get; set; } = string.Empty;
    public string PrimaryApiKey { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceSecret { get; set; } = string.Empty;
    public bool RequireHttps { get; set; } = true;
    public int MaximumClockSkewSeconds { get; set; } = 120;
    public int RateLimitPermitLimit { get; set; } = 120;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public int RateLimitQueueLimit { get; set; }
    public int PollIntervalSeconds { get; set; } = 1;
    public int LongPollTimeoutSeconds { get; set; } = 25;
    public int RequestTimeoutSeconds { get; set; } = 40;
    public int Limit { get; set; } = 50;
    public bool DeleteWebhookOnStart { get; set; } = true;
    public string OffsetFilePath { get; set; } = "telegram-gateway-offset.txt";
    public string IdempotencyFilePath { get; set; } = "telegram-gateway-idempotency.json";
}

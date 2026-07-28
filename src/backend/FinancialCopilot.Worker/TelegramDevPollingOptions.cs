namespace FinancialCopilot.Worker;

public sealed class TelegramDevPollingOptions
{
    public const string SectionName = "Telegram:DevPolling";

    public bool Enabled { get; init; }
    public string BotToken { get; init; } = string.Empty;
    public string BackendBaseUrl { get; init; } = "http://localhost:5074";
    public string BackendApiKey { get; init; } = string.Empty;
    public int PollIntervalSeconds { get; init; } = 2;
    public int RequestTimeoutSeconds { get; init; } = 30;
    public int Limit { get; init; } = 10;
    public bool DeleteWebhookOnStart { get; init; } = true;
}

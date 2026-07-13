namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramLinkOptions
{
    public const string SectionName = "Telegram:AccountLinking";

    public string BotUsername { get; set; } = "financial_copilot_bot";
    public string WebConfirmationBaseUrl { get; set; } = "http://localhost:5173/telegram/link/confirm";
    public int TokenLifetimeMinutes { get; set; } = 10;
}

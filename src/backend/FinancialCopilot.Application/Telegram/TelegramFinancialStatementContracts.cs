namespace FinancialCopilot.Application.Telegram;

public interface ITelegramChannelFinancialStatementHandler
{
    Task<TelegramAssistantResult?> HandleIfRecognizedAsync(
        TelegramAssistantUpdate update,
        FinancialCopilot.Application.Authentication.CurrentActor actor,
        CancellationToken cancellationToken);

    Task<TelegramFinancialStatementRecoveryResult> RecoverSkippedAsync(
        long telegramChatId,
        long telegramMessageId,
        CancellationToken cancellationToken);
}

public sealed record TelegramFinancialStatementRecoveryResult(string Code, string Status, string Message);

namespace FinancialCopilot.Application.Telegram;

public interface ITelegramChannelFinancialStatementHandler
{
    Task<TelegramAssistantResult?> HandleIfRecognizedAsync(
        TelegramAssistantUpdate update,
        FinancialCopilot.Application.Authentication.CurrentActor actor,
        CancellationToken cancellationToken);
}

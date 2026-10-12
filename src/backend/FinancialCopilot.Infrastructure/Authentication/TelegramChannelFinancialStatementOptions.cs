namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramChannelFinancialStatementOptions
{
    public const string SectionName = "ChannelFinancialStatements";
    public bool AutoBackfillEnabled { get; set; }
}

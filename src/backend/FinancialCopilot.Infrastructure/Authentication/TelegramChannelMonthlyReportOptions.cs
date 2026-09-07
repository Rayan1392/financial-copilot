namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramChannelMonthlyReportOptions
{
    public const string SectionName = "ChannelMonthlyReports";
    public bool AutoBackfillEnabled { get; set; }
    public bool AutoPublishTrendEnabled { get; set; }
    public string[] AllowedChannelIds { get; set; } = [];
    public string? AuthorizedApiClientId { get; set; }
    public string? AuthorizedTenantId { get; set; }
    public int MaximumTextLength { get; set; } = 4096;
    public int ProcessingTimeoutSeconds { get; set; } = 100;
}

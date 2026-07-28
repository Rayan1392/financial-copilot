namespace FinancialCopilot.Infrastructure.Financial.MarketReports;

public sealed class MarketReportOptions
{
    public const string SectionName = "MarketReports";

    public bool ScheduledGenerationEnabled { get; init; } = true;
    public int ScheduleCadenceMinutes { get; init; } = 15;
    public int MaximumPublicInsights { get; init; } = 8;
    public int MaximumPersonalInsights { get; init; } = 12;
    public int PersonalDailyGenerationLimit { get; init; } = 2;
    public int LeaseMinutes { get; init; } = 5;
    public int MaximumAttempts { get; init; } = 3;
    public int EvidenceRetentionDays { get; init; } = 730;
    public int TransientModelPayloadRetentionDays { get; init; } = 7;
    public string[] Segments { get; init; } = ["all"];
}

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class ComprehensiveAnalysisBlogOptions
{
    public const string SectionName = "ComprehensiveAnalysisBlog";

    public int PageSize { get; init; } = 10;

    public int RequestDelayMs { get; init; } = 300;

    public string DailySyncCron { get; init; } = "0 6 * * *";
}

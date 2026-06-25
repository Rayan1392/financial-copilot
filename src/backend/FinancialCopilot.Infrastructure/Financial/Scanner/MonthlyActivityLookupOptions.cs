namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public enum MonthlyActivityDirectLookupSourceMode
{
    DerivedMetrics = 0,
    TrendSnapshot = 1
}

public sealed class MonthlyActivityLookupOptions
{
    public const string SectionName = "MonthlyActivityLookup";

    public MonthlyActivityDirectLookupSourceMode DirectLookupSourceMode { get; set; } =
        MonthlyActivityDirectLookupSourceMode.DerivedMetrics;
}

namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

public sealed class StockMarketDbProviderOptions
{
    public const string SectionName = "StockMarketDb";

    public string ProviderName { get; init; } = "StockMarketDb";

    public string ConnectionString { get; init; } = string.Empty;

    public bool UsePersistedMarketQuotes { get; init; }

    public int CommandTimeoutSeconds { get; init; } = 30;

    public int PageSize { get; init; } = 5000;

    public int OverlapMinutes { get; init; } = 10;

    public int RetainIntradayTradeDays { get; init; } = 30;

    public int RetainIntradayIndexDays { get; init; } = 30;
}

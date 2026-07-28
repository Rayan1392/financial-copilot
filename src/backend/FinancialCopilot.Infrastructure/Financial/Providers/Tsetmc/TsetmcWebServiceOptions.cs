namespace FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;

public sealed class TsetmcWebServiceOptions
{
    public const string SectionName = "TsetmcWebService";

    public string ProviderName { get; init; } = "TsetmcWebService";

    public string ServiceUrl { get; init; } = "http://service.tsetmc.com/WebService/TsePublicV2.asmx";

    public string UserName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 60;

    public int RetryCount { get; init; } = 3;

    /// <summary>Flows to request for TradeLastDay (intraday trades). Valid values: 0–5.</summary>
    public byte[] IntradayTradeFlows { get; init; } = [];

    /// <summary>Flows to request for Instrument dimension. Valid values: 5–7 (equity/ETF/rights).</summary>
    public byte[] InstrumentFlows { get; init; } = [];

    /// <summary>Start date for daily trade backfill (yyyyMMdd format, e.g. "20200101").</summary>
    public string DailyTradeFromDate { get; init; } = "20200101";

    /// <summary>End date for daily trade backfill (yyyyMMdd format). Null means today.</summary>
    public string? DailyTradeToDate { get; init; }

    /// <summary>Start date for daily index backfill (yyyyMMdd format, e.g. "20200101").</summary>
    public string DailyIndexFromDate { get; init; } = "20200101";

    /// <summary>End date for daily index backfill (yyyyMMdd format). Null means today.</summary>
    public string? DailyIndexToDate { get; init; }

    public bool Enabled { get; init; } = false;
}

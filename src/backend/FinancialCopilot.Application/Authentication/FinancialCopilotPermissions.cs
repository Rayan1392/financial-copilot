namespace FinancialCopilot.Application.Authentication;

public static class FinancialCopilotPermissions
{
    public const string AiQuery = "ai.query";
    public const string AiScannerExecute = "ai.scanner.execute";
    public const string AiStockAnalysisExecute = "ai.stock-analysis.execute";
    public const string FinancialReportsRead = "financial-reports.read";
    public const string WatchlistReadSelf = "watchlist.read.self";
    public const string WatchlistWriteSelf = "watchlist.write.self";
    public const string PortfolioReadSelf = "portfolio.read.self";
    public const string PortfolioWriteSelf = "portfolio.write.self";
    public const string AiPortfolioAnalysisExecute = "ai.portfolio-analysis.execute";
    public const string AiDeepResearchExecute = "ai.deep-research.execute";
    public const string ConversationReadSelf = "conversation.read.self";
    public const string ConversationWriteSelf = "conversation.write.self";
    public const string UsageReadSelf = "usage.read.self";
    public const string MemoryManageSelf = "memory.manage.self";
    public const string DataSyncManage = "data.sync.manage";
    public const string BillingManage = "billing.manage";

    public static readonly IReadOnlyCollection<string> All =
    [
        AiQuery,
        AiScannerExecute,
        AiStockAnalysisExecute,
        FinancialReportsRead,
        WatchlistReadSelf,
        WatchlistWriteSelf,
        PortfolioReadSelf,
        PortfolioWriteSelf,
        AiPortfolioAnalysisExecute,
        AiDeepResearchExecute,
        ConversationReadSelf,
        ConversationWriteSelf,
        UsageReadSelf,
        MemoryManageSelf,
        DataSyncManage,
        BillingManage
    ];

    public static readonly IReadOnlyCollection<string> WebUserDefaults =
    [
        AiQuery,
        AiScannerExecute,
        AiStockAnalysisExecute,
        FinancialReportsRead,
        WatchlistReadSelf,
        WatchlistWriteSelf,
        ConversationReadSelf,
        ConversationWriteSelf,
        UsageReadSelf,
        MemoryManageSelf
    ];
}

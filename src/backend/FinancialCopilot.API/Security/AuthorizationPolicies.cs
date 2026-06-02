namespace FinancialCopilot.API.Security;

public static class AuthorizationPolicies
{
    public const string AiFacade = "AiFacade";
    public const string ApiClientOnly = "ApiClientOnly";
    public const string BillingAdmin = "BillingAdmin";
    public const string DataAdmin = "DataAdmin";
    public const string UsageReadSelf = "UsageReadSelf";
    public const string WatchlistReadSelf = "WatchlistReadSelf";
    public const string WatchlistWriteSelf = "WatchlistWriteSelf";
    public const string MarketSummaryRead = "MarketSummaryRead";
}

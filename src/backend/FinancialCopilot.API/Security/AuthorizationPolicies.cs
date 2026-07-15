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
    public const string TrackerReadSelf = "TrackerReadSelf";
    public const string TrackerWriteSelf = "TrackerWriteSelf";
    public const string RadarReadSelf = "RadarReadSelf";
    public const string RadarWriteSelf = "RadarWriteSelf";
    public const string NotificationReadSelf = "NotificationReadSelf";
    public const string NotificationManageSelf = "NotificationManageSelf";
    public const string MarketSummaryRead = "MarketSummaryRead";
    public const string TelegramLinkManageSelf = "TelegramLinkManageSelf";
    public const string TelegramMembershipReadSelf = "TelegramMembershipReadSelf";
    public const string AdminUsersRead = "AdminUsersRead";
    public const string AdminUsersManage = "AdminUsersManage";
    public const string AdminRolesRead = "AdminRolesRead";
    public const string AdminRolesManage = "AdminRolesManage";
    public const string AdminPermissionsRead = "AdminPermissionsRead";
    public const string AdminPermissionsManage = "AdminPermissionsManage";
    public const string AdminTenantsRead = "AdminTenantsRead";
    public const string AdminTenantsManage = "AdminTenantsManage";
    public const string AdminPlansRead = "AdminPlansRead";
    public const string AdminPlansManage = "AdminPlansManage";
    public const string AdminSubscriptionsRead = "AdminSubscriptionsRead";
    public const string AdminSubscriptionsManage = "AdminSubscriptionsManage";
    public const string AdminUsageLedgerRead = "AdminUsageLedgerRead";
    public const string AdminCreditsAdjust = "AdminCreditsAdjust";
    public const string AdminBillingAuditRead = "AdminBillingAuditRead";
    public const string AdminSecurityAuditRead = "AdminSecurityAuditRead";
}

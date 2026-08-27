namespace FinancialCopilot.Application.Authentication;

public static class FinancialCopilotPermissions
{
    public const string AiQuery = "ai.query";
    public const string AiScannerExecute = "ai.scanner.execute";
    public const string AiStockAnalysisExecute = "ai.stock-analysis.execute";
    public const string FinancialReportsRead = "financial-reports.read";
    public const string WatchlistReadSelf = "watchlist.read.self";
    public const string WatchlistWriteSelf = "watchlist.write.self";
    public const string TrackerReadSelf = "tracker.read.self";
    public const string TrackerWriteSelf = "tracker.write.self";
    public const string RadarReadSelf = "radar.read.self";
    public const string RadarWriteSelf = "radar.write.self";
    public const string NotificationReadSelf = "notification.read.self";
    public const string NotificationManageSelf = "notification.manage.self";
    public const string PortfolioReadSelf = "portfolio.read.self";
    public const string PortfolioWriteSelf = "portfolio.write.self";
    public const string AiPortfolioAnalysisExecute = "ai.portfolio-analysis.execute";
    public const string AiDeepResearchExecute = "ai.deep-research.execute";
    public const string ConversationReadSelf = "conversation.read.self";
    public const string ConversationWriteSelf = "conversation.write.self";
    public const string UsageReadSelf = "usage.read.self";
    public const string MemoryManageSelf = "memory.manage.self";
    public const string TelegramLinkManageSelf = "telegram.link.manage.self";
    public const string TelegramMembershipReadSelf = "telegram.membership.read.self";
    public const string DataSyncManage = "data.sync.manage";
    public const string NoavaranMonthlyBackfillExecute = "noavaran.monthly-backfill.execute";
    public const string BillingManage = "billing.manage";
    public const string AdminUsersRead = "admin.users.read";
    public const string AdminUsersManage = "admin.users.manage";
    public const string AdminRolesRead = "admin.roles.read";
    public const string AdminRolesManage = "admin.roles.manage";
    public const string AdminPermissionsRead = "admin.permissions.read";
    public const string AdminPermissionsManage = "admin.permissions.manage";
    public const string AdminTenantsRead = "admin.tenants.read";
    public const string AdminTenantsManage = "admin.tenants.manage";
    public const string AdminPlansRead = "admin.plans.read";
    public const string AdminPlansManage = "admin.plans.manage";
    public const string AdminSubscriptionsRead = "admin.subscriptions.read";
    public const string AdminSubscriptionsManage = "admin.subscriptions.manage";
    public const string AdminUsageLedgerRead = "admin.usage-ledger.read";
    public const string AdminCreditsAdjust = "admin.credits.adjust";
    public const string AdminBillingAuditRead = "admin.billing-audit.read";
    public const string AdminSecurityAuditRead = "admin.security-audit.read";

    public static readonly IReadOnlyCollection<string> All =
    [
        AiQuery,
        AiScannerExecute,
        AiStockAnalysisExecute,
        FinancialReportsRead,
        WatchlistReadSelf,
        WatchlistWriteSelf,
        TrackerReadSelf,
        TrackerWriteSelf,
        RadarReadSelf,
        RadarWriteSelf,
        NotificationReadSelf,
        NotificationManageSelf,
        PortfolioReadSelf,
        PortfolioWriteSelf,
        AiPortfolioAnalysisExecute,
        AiDeepResearchExecute,
        ConversationReadSelf,
        ConversationWriteSelf,
        UsageReadSelf,
        MemoryManageSelf,
        TelegramLinkManageSelf,
        TelegramMembershipReadSelf,
        DataSyncManage,
        NoavaranMonthlyBackfillExecute,
        BillingManage,
        AdminUsersRead,
        AdminUsersManage,
        AdminRolesRead,
        AdminRolesManage,
        AdminPermissionsRead,
        AdminPermissionsManage,
        AdminTenantsRead,
        AdminTenantsManage,
        AdminPlansRead,
        AdminPlansManage,
        AdminSubscriptionsRead,
        AdminSubscriptionsManage,
        AdminUsageLedgerRead,
        AdminCreditsAdjust,
        AdminBillingAuditRead,
        AdminSecurityAuditRead
    ];

    public static readonly IReadOnlyCollection<string> AdminAll =
    [
        AdminUsersRead,
        AdminUsersManage,
        AdminRolesRead,
        AdminRolesManage,
        AdminPermissionsRead,
        AdminPermissionsManage,
        AdminTenantsRead,
        AdminTenantsManage,
        AdminPlansRead,
        AdminPlansManage,
        AdminSubscriptionsRead,
        AdminSubscriptionsManage,
        AdminUsageLedgerRead,
        AdminCreditsAdjust,
        AdminBillingAuditRead,
        AdminSecurityAuditRead
    ];

    public static readonly IReadOnlyCollection<string> WebUserDefaults =
    [
        AiQuery,
        AiScannerExecute,
        AiStockAnalysisExecute,
        FinancialReportsRead,
        WatchlistReadSelf,
        WatchlistWriteSelf,
        TrackerReadSelf,
        TrackerWriteSelf,
        RadarReadSelf,
        RadarWriteSelf,
        NotificationReadSelf,
        NotificationManageSelf,
        ConversationReadSelf,
        ConversationWriteSelf,
        UsageReadSelf,
        MemoryManageSelf,
        TelegramLinkManageSelf,
        TelegramMembershipReadSelf
    ];
}

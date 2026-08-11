using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class FinancialIngestionDbContext(DbContextOptions<FinancialIngestionDbContext> options) : DbContext(options)
{
    public DbSet<NormalizedCompanyRow> Companies => Set<NormalizedCompanyRow>();

    public DbSet<NoavaranEligibleCompanyRow> NoavaranEligibleCompanies =>
        Set<NoavaranEligibleCompanyRow>();

    public DbSet<NormalizedIndustryRow> Industries => Set<NormalizedIndustryRow>();

    public DbSet<NormalizedIndustryGroupRow> IndustryGroups => Set<NormalizedIndustryGroupRow>();

    public DbSet<NormalizedMarketRow> Markets => Set<NormalizedMarketRow>();

    public DbSet<NormalizedFinancialStatementRow> FinancialStatements => Set<NormalizedFinancialStatementRow>();

    public DbSet<NormalizedFinancialStatementLineItemRow> FinancialStatementLineItems =>
        Set<NormalizedFinancialStatementLineItemRow>();

    public DbSet<FinancialStatementSourceItemCatalogRow> FinancialStatementSourceItems =>
        Set<FinancialStatementSourceItemCatalogRow>();

    public DbSet<FinancialStatementSourceItemMetricMappingRow> FinancialStatementSourceItemMetricMappings =>
        Set<FinancialStatementSourceItemMetricMappingRow>();

    public DbSet<NormalizedMonthlyReportRow> MonthlyReports => Set<NormalizedMonthlyReportRow>();

    public DbSet<NormalizedMonthlyReportLineItemRow> MonthlyReportLineItems =>
        Set<NormalizedMonthlyReportLineItemRow>();

    public DbSet<DataSyncRunRow> SyncRuns => Set<DataSyncRunRow>();

    public DbSet<MetricRecalculationRequestRow> MetricRecalculationRequests =>
        Set<MetricRecalculationRequestRow>();

    public DbSet<CodalDbSyncStateRow> CodalDbSyncStates => Set<CodalDbSyncStateRow>();

    public DbSet<NadpcoApiSyncStateRow> NadpcoApiSyncStates => Set<NadpcoApiSyncStateRow>();

    public DbSet<NadpcoScheduledSyncRunRow> NadpcoScheduledSyncRuns => Set<NadpcoScheduledSyncRunRow>();

    public DbSet<ArchiveImportRunRow> ArchiveImportRuns => Set<ArchiveImportRunRow>();

    public DbSet<ArchiveFreezeStateRow> ArchiveFreezeStates => Set<ArchiveFreezeStateRow>();

    public DbSet<MonthlyActivityBackfillStateRow> MonthlyActivityBackfillStates =>
        Set<MonthlyActivityBackfillStateRow>();

    public DbSet<NadpcoFundamentalIndexObservationRow> NadpcoFundamentalIndexObservations =>
        Set<NadpcoFundamentalIndexObservationRow>();

    public DbSet<FundamentalIndexCatchUpRunRow> FundamentalIndexCatchUpRuns =>
        Set<FundamentalIndexCatchUpRunRow>();

    public DbSet<TradingInstrumentRow> TradingInstruments => Set<TradingInstrumentRow>();
    public DbSet<IntradayTradeSnapshotRow> IntradayTradeSnapshots => Set<IntradayTradeSnapshotRow>();
    public DbSet<DailyInstrumentTradeRow> DailyInstrumentTrades => Set<DailyInstrumentTradeRow>();
    public DbSet<IntradayIndexSnapshotRow> IntradayIndexSnapshots => Set<IntradayIndexSnapshotRow>();
    public DbSet<DailyIndexSnapshotRow> DailyIndexSnapshots => Set<DailyIndexSnapshotRow>();
    public DbSet<LatestMarketQuoteRow> LatestMarketQuotes => Set<LatestMarketQuoteRow>();
    public DbSet<StockMarketSyncStateRow> StockMarketSyncStates => Set<StockMarketSyncStateRow>();
    public DbSet<MarketQuoteMismatchRow> MarketQuoteMismatches => Set<MarketQuoteMismatchRow>();
    public DbSet<WatchlistSymbolRow> WatchlistSymbols => Set<WatchlistSymbolRow>();

    public DbSet<MarketPulseSnapshotRow> MarketPulseSnapshots => Set<MarketPulseSnapshotRow>();

    public DbSet<MarketReportRow> MarketReports => Set<MarketReportRow>();

    public DbSet<FollowedSymbolRow> FollowedSymbols => Set<FollowedSymbolRow>();

    public DbSet<CodalAlertSubscriptionRow> CodalAlertSubscriptions => Set<CodalAlertSubscriptionRow>();

    public DbSet<NotificationIntentRow> NotificationIntents => Set<NotificationIntentRow>();

    public DbSet<NotificationPreferenceRow> NotificationPreferences => Set<NotificationPreferenceRow>();

    public DbSet<NotificationCategoryPreferenceRow> NotificationCategoryPreferences => Set<NotificationCategoryPreferenceRow>();

    public DbSet<NotificationSymbolPreferenceRow> NotificationSymbolPreferences => Set<NotificationSymbolPreferenceRow>();

    public DbSet<NotificationPreferenceAuditRow> NotificationPreferenceAudits => Set<NotificationPreferenceAuditRow>();

    public DbSet<NotificationBatchRow> NotificationBatches => Set<NotificationBatchRow>();

    public DbSet<NotificationDeliveryAttemptRow> NotificationDeliveryAttempts => Set<NotificationDeliveryAttemptRow>();

    public DbSet<NotificationOutcomeHandoffRow> NotificationOutcomeHandoffs => Set<NotificationOutcomeHandoffRow>();

    public DbSet<NotificationOperationAuditRow> NotificationOperationAudits => Set<NotificationOperationAuditRow>();

    public DbSet<AlertRuleRow> AlertRules => Set<AlertRuleRow>();

    public DbSet<AlertRuleEvaluationStateRow> AlertRuleEvaluationStates => Set<AlertRuleEvaluationStateRow>();

    public DbSet<AlertRuleTriggerRow> AlertRuleTriggers => Set<AlertRuleTriggerRow>();

    public DbSet<CodalAlertSummaryRow> CodalAlertSummaries => Set<CodalAlertSummaryRow>();

    public DbSet<UserAlertRecordRow> UserAlertRecords => Set<UserAlertRecordRow>();

    public DbSet<UserAlertDeliveryTimelineRow> UserAlertDeliveryTimeline => Set<UserAlertDeliveryTimelineRow>();

    public DbSet<UserAlertReactionSnapshotRow> UserAlertReactionSnapshots => Set<UserAlertReactionSnapshotRow>();

    public DbSet<MissingAnswerFeedbackRow> MissingAnswerFeedbacks => Set<MissingAnswerFeedbackRow>();

    public DbSet<CompanyProductRevenueMixRow> CompanyProductRevenueMix => Set<CompanyProductRevenueMixRow>();

    public DbSet<CompanyMonthlyActivityTrendSnapshotRow> CompanyMonthlyActivityTrendSnapshots =>
        Set<CompanyMonthlyActivityTrendSnapshotRow>();

    public DbSet<CompanyPsGaugeSnapshotRow> CompanyPsGaugeSnapshots => Set<CompanyPsGaugeSnapshotRow>();

    public DbSet<CompanyPsHistoryPointRow> CompanyPsHistoryPoints => Set<CompanyPsHistoryPointRow>();

    public DbSet<CompanyPsSeriesSyncStateRow> CompanyPsSeriesSyncStates => Set<CompanyPsSeriesSyncStateRow>();

    public DbSet<CompanyPsVisualizationLeaseRow> CompanyPsVisualizationLeases => Set<CompanyPsVisualizationLeaseRow>();

    public DbSet<IndustryRelativeValuationSourceFactRow> IndustryRelativeValuationSourceFacts =>
        Set<IndustryRelativeValuationSourceFactRow>();

    public DbSet<IndustryRelativeValuationSourceLeaseRow> IndustryRelativeValuationSourceLeases =>
        Set<IndustryRelativeValuationSourceLeaseRow>();

    public DbSet<IndustryRelativeValuationCalculationRow> IndustryRelativeValuationCalculations =>
        Set<IndustryRelativeValuationCalculationRow>();
    public DbSet<IndustryRelativeValuationMetricRow> IndustryRelativeValuationMetrics =>
        Set<IndustryRelativeValuationMetricRow>();
    public DbSet<CompanyIndustryRelativeValuationRow> CompanyIndustryRelativeValuations =>
        Set<CompanyIndustryRelativeValuationRow>();
    public DbSet<IndustryWatchStateRow> IndustryWatchStates => Set<IndustryWatchStateRow>();
    public DbSet<IndustryWatchTransitionRow> IndustryWatchTransitions => Set<IndustryWatchTransitionRow>();
    public DbSet<IndustryWatchEvaluationRow> IndustryWatchEvaluations => Set<IndustryWatchEvaluationRow>();
    public DbSet<IndustryRelativeValuationOutboxRow> IndustryRelativeValuationOutbox =>
        Set<IndustryRelativeValuationOutboxRow>();

    public DbSet<MonthlySalesQualityRankingSnapshotRow> MonthlySalesQualityRankingSnapshots =>
        Set<MonthlySalesQualityRankingSnapshotRow>();

    public DbSet<InsightEventRow> InsightEvents => Set<InsightEventRow>();

    public DbSet<UserInsightStateRow> UserInsightStates => Set<UserInsightStateRow>();

    public DbSet<RadarProfileRow> RadarProfiles => Set<RadarProfileRow>();

    public DbSet<RadarSymbolOverrideRow> RadarSymbolOverrides => Set<RadarSymbolOverrideRow>();

    public DbSet<RadarEventMatchRow> RadarEventMatches => Set<RadarEventMatchRow>();

    public DbSet<RadarPreferenceAuditRow> RadarPreferenceAudits => Set<RadarPreferenceAuditRow>();

    public DbSet<SavedFilterRow> SavedFilters => Set<SavedFilterRow>();

    public DbSet<DerivedMetricRow> DerivedMetrics => Set<DerivedMetricRow>();

    public DbSet<FeatureDefinitionRow> FeatureDefinitions => Set<FeatureDefinitionRow>();

    public DbSet<FeatureSnapshotRow> FeatureSnapshots => Set<FeatureSnapshotRow>();

    public DbSet<FeatureComputationJobRow> FeatureComputationJobs => Set<FeatureComputationJobRow>();

    public DbSet<ComprehensiveAnalysisRow> ComprehensiveAnalyses => Set<ComprehensiveAnalysisRow>();

    public DbSet<ComprehensiveAnalysisTagRow> ComprehensiveAnalysisTags => Set<ComprehensiveAnalysisTagRow>();

    public DbSet<ComprehensiveAnalysisCategoryRow> ComprehensiveAnalysisCategories => Set<ComprehensiveAnalysisCategoryRow>();

    public DbSet<ComprehensiveAnalysisSyncLogRow> ComprehensiveAnalysisSyncLogs => Set<ComprehensiveAnalysisSyncLogRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FinancialIngestionDbContext).Assembly,
            type => type.Namespace == typeof(FinancialIngestionDbContext).Namespace);
}

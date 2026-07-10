using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class FinancialIngestionDbContext(DbContextOptions<FinancialIngestionDbContext> options) : DbContext(options)
{
    public DbSet<NormalizedCompanyRow> Companies => Set<NormalizedCompanyRow>();

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

    public DbSet<FollowedSymbolRow> FollowedSymbols => Set<FollowedSymbolRow>();

    public DbSet<MissingAnswerFeedbackRow> MissingAnswerFeedbacks => Set<MissingAnswerFeedbackRow>();

    public DbSet<CompanyProductRevenueMixRow> CompanyProductRevenueMix => Set<CompanyProductRevenueMixRow>();

    public DbSet<CompanyMonthlyActivityTrendSnapshotRow> CompanyMonthlyActivityTrendSnapshots =>
        Set<CompanyMonthlyActivityTrendSnapshotRow>();

    public DbSet<MonthlySalesQualityRankingSnapshotRow> MonthlySalesQualityRankingSnapshots =>
        Set<MonthlySalesQualityRankingSnapshotRow>();

    public DbSet<InsightEventRow> InsightEvents => Set<InsightEventRow>();

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

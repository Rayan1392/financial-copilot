using Microsoft.EntityFrameworkCore;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FinancialProviderDbContext(DbContextOptions<FinancialProviderDbContext> options) : DbContext(options)
{
    public DbSet<ProviderRawPayloadRow> ProviderRawPayloads => Set<ProviderRawPayloadRow>();
    public DbSet<InvestmentFundRow> InvestmentFunds => Set<InvestmentFundRow>();
    public DbSet<FundPortfolioReportRow> FundPortfolioReports => Set<FundPortfolioReportRow>();
    public DbSet<FundPortfolioReportSheetRow> FundPortfolioReportSheets => Set<FundPortfolioReportSheetRow>();
    public DbSet<FundPortfolioExtractionIssueRow> FundPortfolioExtractionIssues => Set<FundPortfolioExtractionIssueRow>();
    public DbSet<FundPortfolioImportRunRow> FundPortfolioImportRuns => Set<FundPortfolioImportRunRow>();
    public DbSet<FundPortfolioImportItemRow> FundPortfolioImportItems => Set<FundPortfolioImportItemRow>();
    public DbSet<FundPortfolioMappingReviewRow> FundPortfolioMappingReviews => Set<FundPortfolioMappingReviewRow>();
    public DbSet<FundPortfolioOperationAuditRow> FundPortfolioOperationAudits => Set<FundPortfolioOperationAuditRow>();
    public DbSet<FundPortfolioSourceWatermarkRow> FundPortfolioSourceWatermarks => Set<FundPortfolioSourceWatermarkRow>();
    public DbSet<FundPortfolioReportStatusHistoryRow> FundPortfolioReportStatusHistory => Set<FundPortfolioReportStatusHistoryRow>();
    public DbSet<FundPortfolioGovernedMappingRow> FundPortfolioGovernedMappings => Set<FundPortfolioGovernedMappingRow>();
    public DbSet<FundPortfolioSourceTraceRow> FundPortfolioSourceTraces => Set<FundPortfolioSourceTraceRow>();
    public DbSet<FundEquityPositionSnapshotRow> FundEquityPositionSnapshots => Set<FundEquityPositionSnapshotRow>();
    public DbSet<FundEquityPeriodActivityRow> FundEquityPeriodActivities => Set<FundEquityPeriodActivityRow>();
    public DbSet<FundEquitySectionTotalRow> FundEquitySectionTotals => Set<FundEquitySectionTotalRow>();
    public DbSet<FundAssetAllocationSnapshotRow> FundAssetAllocationSnapshots => Set<FundAssetAllocationSnapshotRow>();
    public DbSet<FundCommodityCertificatePositionRow> FundCommodityCertificatePositions => Set<FundCommodityCertificatePositionRow>();
    public DbSet<FundBankDepositPositionRow> FundBankDepositPositions => Set<FundBankDepositPositionRow>();
    public DbSet<FundDerivativePositionRow> FundDerivativePositions => Set<FundDerivativePositionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FinancialProviderDbContext).Assembly,
            type => type.Namespace == typeof(FinancialProviderDbContext).Namespace);
}

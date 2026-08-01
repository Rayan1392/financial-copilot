using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FinancialProviderDbContext(DbContextOptions<FinancialProviderDbContext> options) : DbContext(options)
{
    public DbSet<ProviderRawPayloadRow> ProviderRawPayloads => Set<ProviderRawPayloadRow>();
    public DbSet<InvestmentFundRow> InvestmentFunds => Set<InvestmentFundRow>();
    public DbSet<FundPortfolioReportRow> FundPortfolioReports => Set<FundPortfolioReportRow>();
    public DbSet<FundPortfolioReportSheetRow> FundPortfolioReportSheets => Set<FundPortfolioReportSheetRow>();
    public DbSet<FundPortfolioExtractionIssueRow> FundPortfolioExtractionIssues => Set<FundPortfolioExtractionIssueRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FinancialProviderDbContext).Assembly,
            type => type.Namespace == typeof(FinancialProviderDbContext).Namespace);
}

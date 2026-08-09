using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed record FundSectorStrategyFacts(
    IReadOnlyCollection<FundSectorHoldingFact> CurrentHoldings,
    IReadOnlyCollection<FundSectorHoldingFact> PreviousHoldings,
    IReadOnlyCollection<FundAssetAllocationFact> CurrentAllocation,
    IReadOnlyCollection<FundAssetAllocationFact> PreviousAllocation);

public interface IFundSectorStrategyFactReader
{
    Task<FundSectorStrategyFacts> ReadAsync(Guid currentReportId, Guid? previousReportId, CancellationToken cancellationToken);
}

public sealed class EfCoreFundSectorStrategyFactReader(
    FinancialProviderDbContext providerDb,
    FinancialIngestionDbContext ingestionDb) : IFundSectorStrategyFactReader
{
    public async Task<FundSectorStrategyFacts> ReadAsync(
        Guid currentReportId,
        Guid? previousReportId,
        CancellationToken cancellationToken)
    {
        var currentReport = await providerDb.FundPortfolioReports.AsNoTracking()
            .Where(report => report.Id == currentReportId)
            .Select(report => new { report.ProviderName })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException($"Fund report '{currentReportId}' was not found.");
        var current = await ReadHoldingsAsync(currentReportId, currentReport.ProviderName, cancellationToken);
        var previous = previousReportId is { } previousId
            ? await ReadHoldingsAsync(previousId, currentReport.ProviderName, cancellationToken)
            : [];
        var currentAllocation = await ReadAllocationAsync(currentReportId, cancellationToken);
        var previousAllocation = previousReportId is { } previousAllocationId
            ? await ReadAllocationAsync(previousAllocationId, cancellationToken)
            : [];
        return new(current, previous, currentAllocation, previousAllocation);
    }

    private async Task<IReadOnlyCollection<FundSectorHoldingFact>> ReadHoldingsAsync(
        Guid reportId,
        string providerName,
        CancellationToken cancellationToken)
    {
        var rows = await providerDb.FundEquityPositionSnapshots.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod && row.PositionState == FundPositionState.Ending)
            .Select(row => new { row.Id, row.ExternalCompanyId, row.NormalizedSecurityName, row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, row.ResolutionStatus })
            .ToArrayAsync(cancellationToken);
        var externalIds = rows.Where(row => row.ExternalCompanyId != null).Select(row => row.ExternalCompanyId!).Distinct().ToArray();
        var companies = await ingestionDb.Companies.AsNoTracking()
            .Where(company => company.ProviderName == providerName && externalIds.Contains(company.ExternalCompanyId))
            .Select(company => new { company.ExternalCompanyId, company.IndustryId })
            .ToArrayAsync(cancellationToken);
        var industryIds = companies.Where(company => company.IndustryId.HasValue).Select(company => company.IndustryId!.Value).Distinct().ToArray();
        var industries = await ingestionDb.Industries.AsNoTracking()
            .Where(industry => industryIds.Contains(industry.Id))
            .Select(industry => new { industry.Id, industry.ExternalId, industry.Name })
            .ToDictionaryAsync(industry => industry.Id, cancellationToken);
        var companyIndustries = companies.ToDictionary(
            company => company.ExternalCompanyId,
            company => company.IndustryId is { } industryId && industries.TryGetValue(industryId, out var industry)
                ? (industry.ExternalId, industry.Name)
                : ((string ExternalId, string Name)?)null);
        return rows.Select(row =>
        {
            var industry = row.ExternalCompanyId != null ? companyIndustries.GetValueOrDefault(row.ExternalCompanyId) : null;
            return new FundSectorHoldingFact(row.Id, row.ExternalCompanyId, row.NormalizedSecurityName, industry?.ExternalId, industry?.Name, row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, row.ResolutionStatus);
        }).ToArray();
    }

    private async Task<IReadOnlyCollection<FundAssetAllocationFact>> ReadAllocationAsync(Guid reportId, CancellationToken cancellationToken) =>
        await providerDb.FundAssetAllocationSnapshots.AsNoTracking()
            .Where(row => row.ReportId == reportId && row.PeriodContext == FundWorkbookPeriodContext.CurrentPeriod && !row.IsSectionTotal)
            .Select(row => new FundAssetAllocationFact(row.AssetClass, row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, row.HasSourceFormulaError))
            .ToArrayAsync(cancellationToken);
}

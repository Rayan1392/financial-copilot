using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoCompanyCatalogCleanSlateService(FinancialIngestionDbContext dbContext)
    : INadpcoCompanyCatalogCleanSlateService
{
    public async Task<NadpcoCompanyCatalogCleanSlateResult> ClearAsync(CancellationToken cancellationToken)
    {
        var companyIds = await dbContext.Companies
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        if (companyIds.Count == 0)
        {
            return new NadpcoCompanyCatalogCleanSlateResult(0, 0, 0, 0, 0, 0, 0);
        }

        var symbolIds = await dbContext.Symbols
            .Where(row => companyIds.Contains(row.CompanyId))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var metricRequests = await dbContext.MetricRecalculationRequests
            .ToListAsync(cancellationToken);
        var jobs = await dbContext.FeatureComputationJobs
            .Where(row => row.SymbolId.HasValue && symbolIds.Contains(row.SymbolId.Value))
            .ToListAsync(cancellationToken);
        var featureSnapshots = await dbContext.FeatureSnapshots
            .Where(row => symbolIds.Contains(row.SymbolId))
            .ToListAsync(cancellationToken);
        var derivedMetrics = await dbContext.DerivedMetrics
            .Where(row => symbolIds.Contains(row.SymbolId))
            .ToListAsync(cancellationToken);
        var symbols = await dbContext.Symbols
            .Where(row => companyIds.Contains(row.CompanyId))
            .ToListAsync(cancellationToken);
        var tradingInstruments = await dbContext.TradingInstruments
            .Where(row => row.NormalizedCompanyId.HasValue && companyIds.Contains(row.NormalizedCompanyId.Value))
            .ToListAsync(cancellationToken);
        var companies = await dbContext.Companies.ToListAsync(cancellationToken);

        dbContext.MetricRecalculationRequests.RemoveRange(metricRequests);
        dbContext.FeatureComputationJobs.RemoveRange(jobs);
        dbContext.FeatureSnapshots.RemoveRange(featureSnapshots);
        dbContext.DerivedMetrics.RemoveRange(derivedMetrics);
        dbContext.Symbols.RemoveRange(symbols);
        foreach (var instrument in tradingInstruments)
        {
            instrument.NormalizedCompanyId = null;
        }

        dbContext.Companies.RemoveRange(companies);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new NadpcoCompanyCatalogCleanSlateResult(
            metricRequests.Count,
            jobs.Count,
            featureSnapshots.Count,
            derivedMetrics.Count,
            symbols.Count,
            tradingInstruments.Count,
            companies.Count);
    }
}

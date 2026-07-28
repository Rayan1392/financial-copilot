using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoCompanyCatalogCleanSlateService(FinancialIngestionDbContext dbContext)
    : INadpcoCompanyCatalogCleanSlateService
{
    public async Task<NadpcoCompanyCatalogCleanSlateResult> ClearAsync(CancellationToken cancellationToken)
    {
        // Spec 068: Symbols table removed. DerivedMetrics/FeatureSnapshots/FeatureComputationJobs
        // are now keyed by ExternalCompanyId (string), not SymbolId (Guid).
        var companies = await dbContext.Companies.ToListAsync(cancellationToken);
        if (companies.Count == 0)
        {
            return new NadpcoCompanyCatalogCleanSlateResult(0, 0, 0, 0, 0, 0, 0);
        }

        var externalCompanyIds = companies.Select(c => c.ExternalCompanyId).ToList();
        var companyIds = companies.Select(c => c.Id).ToList();

        var metricRequests = await dbContext.MetricRecalculationRequests
            .ToListAsync(cancellationToken);
        var jobs = await dbContext.FeatureComputationJobs
            .Where(row => row.ExternalCompanyId != null && externalCompanyIds.Contains(row.ExternalCompanyId))
            .ToListAsync(cancellationToken);
        var featureSnapshots = await dbContext.FeatureSnapshots
            .Where(row => externalCompanyIds.Contains(row.ExternalCompanyId))
            .ToListAsync(cancellationToken);
        var derivedMetrics = await dbContext.DerivedMetrics
            .Where(row => externalCompanyIds.Contains(row.ExternalCompanyId))
            .ToListAsync(cancellationToken);
        var tradingInstruments = await dbContext.TradingInstruments
            .Where(row => row.NormalizedCompanyId.HasValue && companyIds.Contains(row.NormalizedCompanyId.Value))
            .ToListAsync(cancellationToken);

        dbContext.MetricRecalculationRequests.RemoveRange(metricRequests);
        dbContext.FeatureComputationJobs.RemoveRange(jobs);
        dbContext.FeatureSnapshots.RemoveRange(featureSnapshots);
        dbContext.DerivedMetrics.RemoveRange(derivedMetrics);
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
            SymbolsDeleted: 0, // Spec 068: Symbols table removed
            tradingInstruments.Count,
            companies.Count);
    }
}

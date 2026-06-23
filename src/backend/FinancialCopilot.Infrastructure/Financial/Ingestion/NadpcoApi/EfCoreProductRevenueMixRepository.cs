using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class EfCoreProductRevenueMixRepository(
    FinancialIngestionDbContext dbContext)
    : ICompanyProductRevenueMixRepository
{
    public async Task<ProductRevenueMixResponse?> GetLatestAsync(
        string externalCompanyId,
        CancellationToken ct = default)
    {
        var rows = await dbContext.CompanyProductRevenueMix
            .Where(r => r.ExternalCompanyId == externalCompanyId)
            .OrderByDescending(r => r.ReportYear)
            .ThenByDescending(r => r.ReportMonth)
            .ToListAsync(ct);

        return BuildResponse(rows);
    }

    public async Task<ProductRevenueMixResponse?> GetByPeriodAsync(
        string externalCompanyId,
        int year,
        byte month,
        CancellationToken ct = default)
    {
        var rows = await dbContext.CompanyProductRevenueMix
            .Where(r => r.ExternalCompanyId == externalCompanyId
                     && r.ReportYear == year
                     && r.ReportMonth == month)
            .OrderBy(r => r.ProductRank)
            .ToListAsync(ct);

        return BuildResponse(rows);
    }

    public async Task UpsertAsync(
        IReadOnlyList<ProductRevenueMixUpsertRow> rows,
        CancellationToken ct = default)
    {
        if (rows.Count == 0) return;

        var first = rows[0];

        // Delete existing rows for this company/month before inserting new ones.
        var existing = await dbContext.CompanyProductRevenueMix
            .Where(r => r.ExternalCompanyId == first.ExternalCompanyId
                     && r.ReportYear == first.ReportYear
                     && r.ReportMonth == first.ReportMonth)
            .ToListAsync(ct);

        if (existing.Count > 0)
            dbContext.CompanyProductRevenueMix.RemoveRange(existing);

        foreach (var row in rows)
        {
            dbContext.CompanyProductRevenueMix.Add(new CompanyProductRevenueMixRow
            {
                Id = row.Id,
                ExternalCompanyId = row.ExternalCompanyId,
                CompanySymbol = row.CompanySymbol,
                CompanyName = row.CompanyName,
                ReportYear = row.ReportYear,
                ReportMonth = row.ReportMonth,
                FiscalEndDate = row.FiscalEndDate,
                ProductName = row.ProductName,
                ProductionQuantity = row.ProductionQuantity,
                SalesQuantity = row.SalesQuantity,
                SalesRate = row.SalesRate,
                SalesAmount = row.SalesAmount,
                TotalCompanySalesAmount = row.TotalCompanySalesAmount,
                RevenueSharePercentage = row.RevenueSharePercentage,
                ProductRank = row.ProductRank,
                IsDominantProduct = row.IsDominantProduct,
                SourceProviderName = row.SourceProviderName,
                CalculatedAtUtc = row.CalculatedAtUtc
            });
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private static ProductRevenueMixResponse? BuildResponse(
        IReadOnlyList<CompanyProductRevenueMixRow> rows)
    {
        if (rows.Count == 0) return null;

        // Take the latest period's rows (all should be same period after ordering).
        var latest = rows
            .GroupBy(r => new { r.ReportYear, r.ReportMonth })
            .OrderByDescending(g => g.Key.ReportYear)
            .ThenByDescending(g => g.Key.ReportMonth)
            .First()
            .ToList();

        var first = latest[0];
        var products = latest
            .OrderBy(r => r.ProductRank)
            .Select(r => new ProductRevenueMixProductItem(
                r.ProductName,
                r.SalesAmount,
                r.RevenueSharePercentage,
                r.ProductRank,
                r.IsDominantProduct,
                r.ProductionQuantity,
                r.SalesQuantity,
                r.SalesRate))
            .ToList();

        return new ProductRevenueMixResponse(
            first.CompanySymbol ?? first.ExternalCompanyId,
            first.CompanyName,
            first.ReportYear,
            first.ReportMonth,
            first.TotalCompanySalesAmount,
            first.SourceProviderName,
            products);
    }
}

using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class CompanyProductRevenueMixCalculator(
    FinancialIngestionDbContext dbContext,
    ICompanyProductRevenueMixRepository repository)
    : ICompanyProductRevenueMixCalculator
{
    private const string ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName;
    private const decimal DominantThreshold = 30m;

    public async Task RecalculateAsync(
        string externalCompanyId,
        int jalaliYear,
        byte jalaliMonth,
        string? bourseSymbol,
        string? companyTitle,
        string? fiscalEndDate,
        CancellationToken ct = default)
    {
        // Load all OutputType=0 (single-month) ProductSales reports for this company/month.
        var reports = await dbContext.MonthlyReports
            .Where(r => r.ProviderName == ProviderName
                     && r.ExternalCompanyId == externalCompanyId
                     && r.ReportType == "ProductSales"
                     && r.OutputType == 0)
            .ToListAsync(ct);

        if (reports.Count == 0) return;

        // Filter to the target Jalali period via PeriodStart.
        var (periodStart, _) = JalaliDateResolver.ResolveMonth(jalaliYear, jalaliMonth);
        var matchingReports = reports
            .Where(r => r.PeriodStart == periodStart)
            .Select(r => r.Id)
            .ToHashSet();

        if (matchingReports.Count == 0) return;

        var lineItems = await dbContext.MonthlyReportLineItems
            .Where(li => matchingReports.Contains(li.MonthlyReportId) && li.SalesAmount != null)
            .ToListAsync(ct);

        if (lineItems.Count == 0) return;

        // Group by normalized product name, summing across any sub-reports.
        var byProduct = lineItems
            .GroupBy(li => NormalizeProductName(li.Title))
            .Select(g => new
            {
                ProductName = g.Key,
                SalesAmount = g.Sum(li => li.SalesAmount ?? 0m),
                ProductionQuantity = g.Any(li => li.ProductionQuantity.HasValue)
                    ? g.Sum(li => li.ProductionQuantity ?? 0m)
                    : (decimal?)null,
                SalesQuantity = g.Any(li => li.SalesQuantity.HasValue)
                    ? g.Sum(li => li.SalesQuantity ?? 0m)
                    : (decimal?)null,
                SalesRate = g.FirstOrDefault(li => li.SalesRate.HasValue)?.SalesRate
            })
            .Where(p => p.SalesAmount > 0)
            .OrderByDescending(p => p.SalesAmount)
            .ToList();

        if (byProduct.Count == 0) return;

        var total = byProduct.Sum(p => p.SalesAmount);
        var now = DateTimeOffset.UtcNow;

        var upsertRows = byProduct
            .Select((p, index) =>
            {
                var share = total > 0 ? p.SalesAmount / total * 100m : 0m;
                return new ProductRevenueMixUpsertRow(
                    Id: Guid.NewGuid(),
                    ExternalCompanyId: externalCompanyId,
                    CompanySymbol: bourseSymbol,
                    CompanyName: companyTitle,
                    ReportYear: jalaliYear,
                    ReportMonth: jalaliMonth,
                    FiscalEndDate: fiscalEndDate,
                    ProductName: p.ProductName,
                    ProductionQuantity: p.ProductionQuantity,
                    SalesQuantity: p.SalesQuantity,
                    SalesRate: p.SalesRate,
                    SalesAmount: p.SalesAmount,
                    TotalCompanySalesAmount: total,
                    RevenueSharePercentage: Math.Round(share, 4),
                    ProductRank: index + 1,
                    IsDominantProduct: share >= DominantThreshold,
                    SourceProviderName: ProviderName,
                    CalculatedAtUtc: now);
            })
            .ToList();

        await repository.UpsertAsync(upsertRows, ct);
    }

    // Reuses PersianSymbolNormalizer for char normalization; keeps spaces for product name display.
    private static string NormalizeProductName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        // Arabic→Persian char mapping (same rules as PersianSymbolNormalizer but keeps whitespace).
        var chars = title.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                'ي' => 'ی', // Arabic Ye → Persian Ye
                'ك' => 'ک', // Arabic Kaf → Persian Kaf
                _ => chars[i]
            };
        }

        return new string(chars).Trim();
    }
}

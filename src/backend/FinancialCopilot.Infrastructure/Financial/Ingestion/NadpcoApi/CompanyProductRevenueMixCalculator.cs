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
        // The composition reads the same single-month source as the sales trend: ProductSales
        // (OutputType 0, or null from legacy payloads) when it has rows, otherwise ServiceSales.
        // A narrower filter here left companies with a visible trend but no composition.
        var (periodStart, _) = JalaliDateResolver.ResolveMonth(jalaliYear, jalaliMonth);
        var candidateReports = await dbContext.MonthlyReports
            .Where(r => r.ProviderName == ProviderName
                     && r.ExternalCompanyId == externalCompanyId
                     && r.IsAccepted
                     && r.PeriodStart == periodStart)
            .Where(MonthlySalesSourceSelection.IsSingleMonthCandidate)
            .ToListAsync(ct);

        if (candidateReports.Count == 0) return;

        var candidateReportIds = candidateReports.Select(r => r.Id).ToHashSet();
        var candidateLineItems = await dbContext.MonthlyReportLineItems
            .Where(li => candidateReportIds.Contains(li.MonthlyReportId))
            .ToListAsync(ct);

        var reportIdsWithLineItems = candidateLineItems.Select(li => li.MonthlyReportId).ToHashSet();
        var sourceReportIds = MonthlySalesSourceSelection
            .Choose(candidateReports, reportIdsWithLineItems.Contains)
            .Reports
            .Select(r => r.Id)
            .ToHashSet();

        var lineItems = candidateLineItems
            .Where(li => sourceReportIds.Contains(li.MonthlyReportId) && li.SalesAmount != null)
            .ToList();

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

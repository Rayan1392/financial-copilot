using FinancialCopilot.Application.FinancialData.Metadata;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Metadata;

public sealed class EfCoreAssistedQueryMetadataService(FinancialIngestionDbContext dbContext)
    : IAssistedQueryMetadataService
{
    private static readonly IReadOnlyCollection<PeriodMetadataItem> Periods =
    [
        Period(FiscalPeriodType.Monthly, "Monthly", "ماهانه"),
        Period(FiscalPeriodType.ThreeMonths, "Three months", "سه ماهه"),
        Period(FiscalPeriodType.SixMonths, "Six months", "شش ماهه"),
        Period(FiscalPeriodType.NineMonths, "Nine months", "نه ماهه"),
        Period(FiscalPeriodType.TwelveMonths, "Twelve months", "دوازده ماهه"),
        Period(FiscalPeriodType.LatestQuarter, "Latest quarter", "آخرین فصل"),
        Period(FiscalPeriodType.LatestMonth, "Latest month", "آخرین ماه"),
        Period(FiscalPeriodType.TrailingTwelveMonths, "Trailing twelve months", "دوازده ماه اخیر")
    ];

    public IReadOnlyCollection<PeriodMetadataItem> GetPeriods() => Periods;

    public async Task<IReadOnlyCollection<SymbolMetadataItem>> SearchSymbolsAsync(
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        // Spec 068: Symbols table removed. Query Companies directly; use TseSymbol as the display
        // symbol code, falling back to CompanySymbol, then ExternalCompanyId.
        var query =
            from company in dbContext.Companies.AsNoTracking()
            join industry in dbContext.Industries.AsNoTracking() on company.IndustryId equals industry.Id into industries
            from industry in industries.DefaultIfEmpty()
            select new
            {
                SymbolCode = company.TseSymbol ?? company.CompanySymbol ?? company.ExternalCompanyId,
                company.Name,
                company.NameEnglish,
                IndustryName = industry == null ? null : industry.Name
            };

        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(row =>
                (row.SymbolCode != null && row.SymbolCode.ToLower().Contains(normalizedSearch)) ||
                row.Name.ToLower().Contains(normalizedSearch) ||
                (row.NameEnglish != null && row.NameEnglish.ToLower().Contains(normalizedSearch)));
        }

        var rows = await query
            .OrderBy(row => row.SymbolCode)
            .ThenBy(row => row.Name)
            .Take(limit * 3)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.SymbolCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(limit)
            .Select(row => new SymbolMetadataItem(
                row.SymbolCode,
                row.Name,
                row.NameEnglish,
                row.IndustryName))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<IndustryMetadataItem>> SearchIndustriesAsync(
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Industries.AsNoTracking();
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(row => row.Name.ToLower().Contains(normalizedSearch));
        }

        var rows = await query
            .OrderBy(row => row.Name)
            .Take(limit * 3)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(limit)
            .Select(row => new IndustryMetadataItem(row.ExternalId, row.Name))
            .ToArray();
    }

    private static PeriodMetadataItem Period(
        FiscalPeriodType type,
        string displayName,
        string displayNameFa) =>
        new(type.ToString(), displayName, displayNameFa);

    private static string? NormalizeSearch(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
}

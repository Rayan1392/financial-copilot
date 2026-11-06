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
                company.ExternalCompanyId,
                SymbolCode = company.TseSymbol ?? company.CompanySymbol ?? company.ExternalCompanyId,
                company.Name,
                company.NameEnglish,
                IndustryName = industry == null ? null : industry.Name
            };

        var normalizedSearch = NormalizeSearch(search);

        // Relevance ranking runs in SQL *before* Take, so a strong symbol match is never cut off by
        // alphabetically earlier rows whose company name merely contains the term (e.g. searching
        // «خودرو» must return the «خودرو» symbol first, not «خاور…» rows of «ایران خودرو دیزل»).
        // Lower rank = better match:
        //   0 exact symbol · 1 symbol prefix · 2 exact company name · 3 company-name prefix
        //   4 symbol contains · 5 company name contains (Persian or English)
        //
        // Both sides are folded the same way (see NormalizeSearch): Arabic «ي»/«ى»/«ك» stored by some
        // vendor feeds must match Persian «ی»/«ک» typed by the user, and vice versa. The stored
        // columns are folded in SQL (nested replace()) with the same constants as the search term.
        var ranked = normalizedSearch is null
            ? query.Select(row => new { Row = row, Rank = 0 })
            : query
                .Select(row => new
                {
                    Row = row,
                    Symbol = row.SymbolCode.ToLower()
                        .Replace(ArabicYeh, PersianYeh).Replace(ArabicAlefMaksura, PersianYeh).Replace(ArabicKaf, PersianKaf),
                    Name = row.Name.ToLower()
                        .Replace(ArabicYeh, PersianYeh).Replace(ArabicAlefMaksura, PersianYeh).Replace(ArabicKaf, PersianKaf),
                    NameEnglish = row.NameEnglish == null ? null : row.NameEnglish.ToLower()
                })
                .Where(item =>
                    item.Symbol.Contains(normalizedSearch) ||
                    item.Name.Contains(normalizedSearch) ||
                    (item.NameEnglish != null && item.NameEnglish.Contains(normalizedSearch)))
                .Select(item => new
                {
                    item.Row,
                    Rank =
                        item.Symbol == normalizedSearch ? 0 :
                        item.Symbol.StartsWith(normalizedSearch) ? 1 :
                        item.Name == normalizedSearch ? 2 :
                        item.Name.StartsWith(normalizedSearch) ||
                            (item.NameEnglish != null && item.NameEnglish.StartsWith(normalizedSearch)) ? 3 :
                        item.Symbol.Contains(normalizedSearch) ? 4 :
                        5
                });

        var rows = (await ranked
                .OrderBy(item => item.Rank)
                .ThenBy(item => item.Row.SymbolCode)
                .ThenBy(item => item.Row.Name)
                .Take(limit * 3)
                .ToListAsync(cancellationToken))
            .Select(item => item.Row);

        return rows
            .GroupBy(row => row.SymbolCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(limit)
            .Select(row => new SymbolMetadataItem(
                row.ExternalCompanyId,
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
            query = query.Where(row => row.Name.ToLower()
                .Replace(ArabicYeh, PersianYeh).Replace(ArabicAlefMaksura, PersianYeh).Replace(ArabicKaf, PersianKaf)
                .Contains(normalizedSearch));
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

    // Arabic look-alike letters folded to their Persian form on both sides of a search comparison.
    // Strings (not chars) so EF Core translates the column-side Replace calls to SQL replace().
    private const string ArabicYeh = "ي";          // ي
    private const string ArabicAlefMaksura = "ى";  // ى
    private const string ArabicKaf = "ك";          // ك
    private const string PersianYeh = "ی";         // ی
    private const string PersianKaf = "ک";         // ک

    private static string? NormalizeSearch(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim().ToLowerInvariant()
                .Replace(ArabicYeh, PersianYeh)
                .Replace(ArabicAlefMaksura, PersianYeh)
                .Replace(ArabicKaf, PersianKaf);
}

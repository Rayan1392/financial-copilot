using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

internal sealed class CompanyDisclosureFeedRepository(FinancialIngestionDbContext dbContext)
    : ICompanyDisclosureFeedRepository
{
    private const int MaximumPageSize = 100;

    public async Task<CompanyDisclosureFeedPage> QueryAsync(
        CompanyDisclosureFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Page < 1)
            throw new ArgumentOutOfRangeException(nameof(query.Page));
        if (query.PageSize is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(query.PageSize));
        if (query.PublishedFrom > query.PublishedTo || query.ReceivedFrom > query.ReceivedTo)
            throw new ArgumentException("The start of a date range cannot be after its end.", nameof(query));

        // Npgsql requires UTC DateTimeOffset values for PostgreSQL timestamptz parameters.
        // API/web filters may legitimately arrive with Tehran's +03:30 offset.
        query = query with
        {
            ReceivedFrom = query.ReceivedFrom?.ToUniversalTime(),
            ReceivedTo = query.ReceivedTo?.ToUniversalTime()
        };

        var selectedTypes = query.Types is { Count: > 0 }
            ? query.Types.ToHashSet()
            : Enum.GetValues<CompanyDisclosureType>().ToHashSet();

        var companies = await dbContext.Companies.AsNoTracking()
            .Select(company => new CompanyIdentity(
                company.Id, company.ProviderName, company.ExternalCompanyId,
                company.Ticker ?? company.CompanySymbol ?? company.TseSymbol,
                company.Name))
            .ToListAsync(cancellationToken);
        var companyById = companies.ToDictionary(company => company.Id);
        var companyByProviderAndExternalId = companies
            .GroupBy(company => (company.ProviderName, company.ExternalCompanyId))
            .ToDictionary(group => group.Key, group => group.First());

        var results = new List<CompanyDisclosureFeedItem>();

        if (selectedTypes.Contains(CompanyDisclosureType.MonthlyProductionSales))
        {
            var rows = await FilterMonthlyReportsAsync(
                dbContext.MonthlyReports.AsNoTracking(), query, cancellationToken);
            results.AddRange(rows.Select(row =>
            {
                var company = ResolveCompany(row.CompanyId, row.ProviderName, row.ExternalCompanyId,
                    companyById, companyByProviderAndExternalId);
                return new CompanyDisclosureFeedItem(
                    $"monthly:{row.ProviderName}:{row.ExternalReportId}",
                    $"monthly:{row.ProviderName}:{row.ExternalCompanyId}:{row.ReportType ?? "unknown"}:{row.PeriodStart:yyyyMMdd}",
                    CompanyDisclosureType.MonthlyProductionSales,
                    row.ProviderName,
                    row.ExternalCompanyId,
                    company?.Id,
                    company?.Symbol,
                    company?.Name,
                    MonthlyTitle(row.VendorPeriodDate ?? row.PeriodEnd),
                    row.PublishedAt,
                    row.VendorPeriodDate ?? row.PeriodEnd,
                    row.LastSynchronizedAt,
                    row.ExternalReportId,
                    RevisionNumber: 1,
                    IsRevised: false,
                    CoverageStatus: company is null ? DisclosureCoverageStatus.UnmappedCompany : DisclosureCoverageStatus.Complete,
                    FreshnessReasonCode: "PersistedNormalizedRecord");
            }));
        }

        var statementTypes = selectedTypes
            .Where(type => type != CompanyDisclosureType.MonthlyProductionSales)
            .ToHashSet();
        if (statementTypes.Count > 0)
        {
            var rows = await FilterFinancialStatementsAsync(
                dbContext.FinancialStatements.AsNoTracking(), query, cancellationToken);
            results.AddRange(rows.Select(row =>
            {
                var type = MapStatementType(row.StatementType);
                if (type is null || !statementTypes.Contains(type.Value))
                    return null;
                if (query.ConsolidationScope == DisclosureConsolidationScope.NonConsolidated && row.IsComposing)
                    return null;
                if (query.ConsolidationScope == DisclosureConsolidationScope.Consolidated && !row.IsComposing)
                    return null;
                var company = ResolveCompany(row.CompanyId, row.ProviderName, row.ExternalCompanyId,
                    companyById, companyByProviderAndExternalId);
                return new CompanyDisclosureFeedItem(
                    $"statement:{row.ProviderName}:{row.ExternalStatementId}:{row.StatementType}:{row.IsAudited}:{row.IsRepresented}:{row.IsComposing}",
                    $"statement:{row.ProviderName}:{row.ExternalCompanyId}:{row.StatementType}:{row.PeriodType}:{row.PeriodEnd:yyyyMMdd}:{row.IsComposing}",
                    type.Value,
                    row.ProviderName,
                    row.ExternalCompanyId,
                    company?.Id,
                    company?.Symbol,
                    company?.Name,
                    StatementTitle(row.VendorPeriodDate ?? row.PeriodEnd),
                    row.PublishedAt,
                    row.VendorPeriodDate ?? row.PeriodEnd,
                    row.LastSynchronizedAt,
                    row.ExternalStatementId,
                    RevisionNumber: 1,
                    IsRevised: false,
                    CoverageStatus: company is null ? DisclosureCoverageStatus.UnmappedCompany : DisclosureCoverageStatus.Complete,
                    FreshnessReasonCode: "PersistedNormalizedRecord",
                    IsAudited: row.IsAudited,
                    IsRepresented: row.IsRepresented,
                    IsComposing: row.IsComposing,
                    ReportingPeriodType: row.PeriodType);
            }).OfType<CompanyDisclosureFeedItem>());
        }

        // Normalization persists only successful records. When a provider emits a correction as a
        // separate source record in the same logical period, expose the newest normalized revision.
        var latestRevisions = results.GroupBy(item => item.LogicalDisclosureId)
            .Select(group =>
            {
                var selected = group.OrderByDescending(item => item.ReceivedAt)
                    .ThenBy(item => item.DisclosureId)
                    .First();
                return selected with
                {
                    RevisionNumber = group.Count(),
                    IsRevised = group.Count() > 1
                };
            });

        var filtered = latestRevisions.Where(item => Matches(item, query))
            .OrderByDescending(item => item.PublishedAt.HasValue).ThenByDescending(item => item.PublishedAt)
            .ThenByDescending(item => item.ReceivedAt).ThenBy(item => item.DisclosureId).ToList();
        var totalCount = filtered.Count;
        var coverageStatus = filtered.Any(item => item.CoverageStatus == DisclosureCoverageStatus.UnmappedCompany)
            ? DisclosureCoverageStatus.UnmappedCompany
            : DisclosureCoverageStatus.Complete;
        return new CompanyDisclosureFeedPage(
            filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(),
            query.Page, query.PageSize, totalCount, DateTimeOffset.UtcNow, coverageStatus);
    }

    private static bool Matches(CompanyDisclosureFeedItem item, CompanyDisclosureFeedQuery query)
    {
        if (query.ProviderNames is { Count: > 0 } &&
            !query.ProviderNames.Contains(item.ProviderName, StringComparer.OrdinalIgnoreCase)) return false;
        if (query.PublishedFrom.HasValue && (!item.PublishedAt.HasValue || item.PublishedAt < query.PublishedFrom)) return false;
        if (query.PublishedTo.HasValue && (!item.PublishedAt.HasValue || item.PublishedAt > query.PublishedTo)) return false;
        if (query.ReceivedFrom.HasValue && item.ReceivedAt < query.ReceivedFrom) return false;
        if (query.ReceivedTo.HasValue && item.ReceivedAt > query.ReceivedTo) return false;
        if (string.IsNullOrWhiteSpace(query.SymbolOrCompany)) return true;
        var term = query.SymbolOrCompany.Trim();
        return (item.Symbol?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.CompanyName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static Task<List<NormalizedMonthlyReportRow>> FilterMonthlyReportsAsync(
        IQueryable<NormalizedMonthlyReportRow> source,
        CompanyDisclosureFeedQuery query,
        CancellationToken cancellationToken)
    {
        if (query.ProviderNames is { Count: > 0 })
            source = source.Where(row => query.ProviderNames.Contains(row.ProviderName));
        if (query.ReceivedFrom.HasValue)
            source = source.Where(row => row.LastSynchronizedAt >= query.ReceivedFrom.Value);
        if (query.ReceivedTo.HasValue)
            source = source.Where(row => row.LastSynchronizedAt <= query.ReceivedTo.Value);
        return source.ToListAsync(cancellationToken);
    }

    private static Task<List<NormalizedFinancialStatementRow>> FilterFinancialStatementsAsync(
        IQueryable<NormalizedFinancialStatementRow> source,
        CompanyDisclosureFeedQuery query,
        CancellationToken cancellationToken)
    {
        if (query.ProviderNames is { Count: > 0 })
            source = source.Where(row => query.ProviderNames.Contains(row.ProviderName));
        if (query.ReceivedFrom.HasValue)
            source = source.Where(row => row.LastSynchronizedAt >= query.ReceivedFrom.Value);
        if (query.ReceivedTo.HasValue)
            source = source.Where(row => row.LastSynchronizedAt <= query.ReceivedTo.Value);
        return source.ToListAsync(cancellationToken);
    }

    private static CompanyIdentity? ResolveCompany(Guid? companyId, string providerName, string externalCompanyId,
        IReadOnlyDictionary<Guid, CompanyIdentity> byId,
        IReadOnlyDictionary<(string ProviderName, string ExternalCompanyId), CompanyIdentity> byProviderAndExternalId) =>
        companyId is Guid id && byId.TryGetValue(id, out var byCompanyId) ? byCompanyId :
        byProviderAndExternalId.GetValueOrDefault((providerName, externalCompanyId));

    private static CompanyDisclosureType? MapStatementType(string value) => value switch
    {
        "IncomeStatement" => CompanyDisclosureType.IncomeStatement,
        "BalanceSheet" => CompanyDisclosureType.BalanceSheet,
        "CashFlow" => CompanyDisclosureType.CashFlowStatement,
        _ => null
    };

    // Provider statement titles commonly embed the company name and are not consistent across sources.
    // Financial disclosures therefore use one canonical, company-neutral title.
    private static string StatementTitle(DateOnly periodEnd) =>
        $"صورت مالی دوره منتهی به {FormatJalaliDate(periodEnd)}";

    private static string MonthlyTitle(DateOnly periodEnd) =>
        $"گزارش فعالیت ماهانه تولید و فروش — دوره منتهی به {FormatJalaliDate(periodEnd)}";

    private static string FormatJalaliDate(DateOnly value) =>
        ShamsiMonthCalculator.FormatJalaliDate(new DateTimeOffset(
            value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3.5)));

    private sealed record CompanyIdentity(Guid Id, string ProviderName, string ExternalCompanyId, string? Symbol, string Name);
}

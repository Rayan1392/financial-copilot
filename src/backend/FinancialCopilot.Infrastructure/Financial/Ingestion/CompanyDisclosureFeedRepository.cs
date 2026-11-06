using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

internal sealed class CompanyDisclosureFeedRepository(
    FinancialIngestionDbContext dbContext,
    CompanyDisclosureFeedCache cache)
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

        return await cache.GetOrCreateAsync(
            query,
            token => QueryUncachedAsync(query, token),
            cancellationToken);
    }

    private async Task<CompanyDisclosureFeedPage> QueryUncachedAsync(
        CompanyDisclosureFeedQuery query,
        CancellationToken cancellationToken)
    {

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

        var knownCompanyIds = companies.Select(company => company.Id).ToArray();
        var matchingCompanies = string.IsNullOrWhiteSpace(query.SymbolOrCompany)
            ? companies
            : companies.Where(company =>
                (company.Symbol?.Contains(query.SymbolOrCompany, StringComparison.OrdinalIgnoreCase) ?? false) ||
                company.Name.Contains(query.SymbolOrCompany, StringComparison.OrdinalIgnoreCase)).ToList();
        var matchingCompanyIds = matchingCompanies.Select(company => company.Id).ToArray();
        var matchingCompanyKeys = matchingCompanies
            .Select(company => CompanyKey(company.ProviderName, company.ExternalCompanyId)).ToArray();
        var knownCompanyKeys = companyByProviderAndExternalId.Keys
            .Select(key => CompanyKey(key.ProviderName, key.ExternalCompanyId)).ToArray();
        var providerNames = query.ProviderNames?.ToArray() ?? [];

        var monthlySource = dbContext.MonthlyReports.AsNoTracking().Where(row => row.IsAccepted);
        monthlySource = ApplySourceFilters(monthlySource, providerNames, query.ReceivedFrom, query.ReceivedTo);
        var monthlyGroups = monthlySource.GroupBy(row => new
            {
                row.ProviderName, row.ExternalCompanyId, row.ReportType, row.PeriodStart
            })
            .Select(group => new
            {
                group.Key.ProviderName,
                group.Key.ExternalCompanyId,
                group.Key.ReportType,
                group.Key.PeriodStart,
                LatestReceivedAt = group.Max(row => row.LastSynchronizedAt),
                RevisionNumber = group.Count()
            });
        var monthlyRows = from row in monthlySource
                          join revisionGroup in monthlyGroups on new
                          {
                              row.ProviderName, row.ExternalCompanyId, row.ReportType,
                              row.PeriodStart, row.LastSynchronizedAt
                          } equals new
                          {
                              revisionGroup.ProviderName, revisionGroup.ExternalCompanyId, revisionGroup.ReportType,
                              revisionGroup.PeriodStart, LastSynchronizedAt = revisionGroup.LatestReceivedAt
                          }
                          where row.Id == monthlySource
                              .Where(candidate => candidate.ProviderName == revisionGroup.ProviderName &&
                                  candidate.ExternalCompanyId == revisionGroup.ExternalCompanyId &&
                                  candidate.ReportType == revisionGroup.ReportType &&
                                  candidate.PeriodStart == revisionGroup.PeriodStart &&
                                  candidate.LastSynchronizedAt == revisionGroup.LatestReceivedAt)
                              .OrderBy(candidate => candidate.ExternalReportId).ThenBy(candidate => candidate.Id)
                              .Select(candidate => candidate.Id).First()
                          orderby row.PublishedAt.HasValue descending,
                              row.PublishedAt descending,
                              row.LastSynchronizedAt descending,
                              row.ProviderName,
                              row.ExternalReportId
                          select new DisclosureRow
                          {
                              Type = CompanyDisclosureType.MonthlyProductionSales,
                              ProviderName = row.ProviderName,
                              ExternalCompanyId = row.ExternalCompanyId,
                              CompanyId = row.CompanyId,
                              SourceRecordId = row.ExternalReportId,
                              PublishedAt = row.PublishedAt,
                              ReportingPeriodEnd = row.VendorPeriodDate ?? row.PeriodEnd,
                              ReceivedAt = row.LastSynchronizedAt,
                              RevisionNumber = revisionGroup.RevisionNumber,
                              MonthlyReportType = row.ReportType,
                              MonthlyPeriodStart = row.PeriodStart
                          };
        monthlyRows = ApplyPublishedAndCompanyFilters(
            monthlyRows, query, knownCompanyIds, matchingCompanyIds, matchingCompanyKeys);

        var statementTypes = selectedTypes
            .Where(type => type != CompanyDisclosureType.MonthlyProductionSales)
            .Select(StatementTypeName)
            .Where(type => type is not null)
            .Cast<string>()
            .ToArray();
        var statementRows = dbContext.FinancialStatements.AsNoTracking()
            .Where(row => statementTypes.Contains(row.StatementType));
        statementRows = ApplySourceFilters(statementRows, providerNames, query.ReceivedFrom, query.ReceivedTo);
        if (query.ConsolidationScope == DisclosureConsolidationScope.NonConsolidated)
            statementRows = statementRows.Where(row => !row.IsComposing);
        else if (query.ConsolidationScope == DisclosureConsolidationScope.Consolidated)
            statementRows = statementRows.Where(row => row.IsComposing);

        var statementGroups = statementRows.GroupBy(row => new
            {
                row.ProviderName, row.ExternalCompanyId, row.StatementType, row.PeriodType,
                row.PeriodEnd, row.IsComposing
            })
            .Select(group => new
            {
                group.Key.ProviderName,
                group.Key.ExternalCompanyId,
                group.Key.StatementType,
                group.Key.PeriodType,
                group.Key.PeriodEnd,
                group.Key.IsComposing,
                LatestReceivedAt = group.Max(row => row.LastSynchronizedAt),
                RevisionNumber = group.Count()
            });
        var latestStatementRows = from row in statementRows
                                  join revisionGroup in statementGroups on new
                                  {
                                      row.ProviderName, row.ExternalCompanyId, row.StatementType,
                                      row.PeriodType, row.PeriodEnd, row.IsComposing, row.LastSynchronizedAt
                                  } equals new
                                  {
                                      revisionGroup.ProviderName, revisionGroup.ExternalCompanyId, revisionGroup.StatementType,
                                      revisionGroup.PeriodType, revisionGroup.PeriodEnd, revisionGroup.IsComposing,
                                      LastSynchronizedAt = revisionGroup.LatestReceivedAt
                                  }
                                  where row.Id == statementRows
                                      .Where(candidate => candidate.ProviderName == revisionGroup.ProviderName &&
                                          candidate.ExternalCompanyId == revisionGroup.ExternalCompanyId &&
                                          candidate.StatementType == revisionGroup.StatementType &&
                                          candidate.PeriodType == revisionGroup.PeriodType &&
                                          candidate.PeriodEnd == revisionGroup.PeriodEnd &&
                                          candidate.IsComposing == revisionGroup.IsComposing &&
                                          candidate.LastSynchronizedAt == revisionGroup.LatestReceivedAt)
                                      .OrderBy(candidate => candidate.ExternalStatementId)
                                      .ThenBy(candidate => candidate.StatementType)
                                      .ThenBy(candidate => candidate.IsAudited)
                                      .ThenBy(candidate => candidate.IsRepresented)
                                      .ThenBy(candidate => candidate.IsComposing)
                                      .ThenBy(candidate => candidate.Id)
                                      .Select(candidate => candidate.Id).First()
                                  orderby row.PublishedAt.HasValue descending,
                                      row.PublishedAt descending,
                                      row.LastSynchronizedAt descending,
                                      row.ProviderName,
                                      row.ExternalStatementId,
                                      row.StatementType,
                                      row.IsAudited,
                                      row.IsRepresented,
                                      row.IsComposing
                                  select new DisclosureRow
                                  {
                                      Type = MapStatementType(row.StatementType)!.Value,
                                      ProviderName = row.ProviderName,
                                      ExternalCompanyId = row.ExternalCompanyId,
                                      CompanyId = row.CompanyId,
                                      SourceRecordId = row.ExternalStatementId,
                                      PublishedAt = row.PublishedAt,
                                      ReportingPeriodEnd = row.VendorPeriodDate ?? row.PeriodEnd,
                                      ReceivedAt = row.LastSynchronizedAt,
                                      RevisionNumber = revisionGroup.RevisionNumber,
                                      StatementType = row.StatementType,
                                      ReportingPeriodType = row.PeriodType,
                                      IsAudited = row.IsAudited,
                                      IsRepresented = row.IsRepresented,
                                      IsComposing = row.IsComposing
                                  };
        latestStatementRows = ApplyPublishedAndCompanyFilters(
            latestStatementRows, query, knownCompanyIds, matchingCompanyIds, matchingCompanyKeys);

        var monthlyCount = selectedTypes.Contains(CompanyDisclosureType.MonthlyProductionSales)
            ? await monthlyRows.CountAsync(cancellationToken)
            : 0;
        var statementCount = statementTypes.Length > 0
            ? await latestStatementRows.CountAsync(cancellationToken)
            : 0;
        var totalCount = monthlyCount + statementCount;

        var pageOffset = (int)Math.Min(int.MaxValue, ((long)query.Page - 1) * query.PageSize);
        var candidateCount = (int)Math.Min(int.MaxValue, (long)pageOffset + query.PageSize);
        var monthlyCandidates = selectedTypes.Contains(CompanyDisclosureType.MonthlyProductionSales)
            ? await monthlyRows.Take(candidateCount).ToListAsync(cancellationToken)
            : [];
        var statementCandidates = statementTypes.Length > 0
            ? await latestStatementRows.Take(candidateCount).ToListAsync(cancellationToken)
            : [];

        var pageItems = monthlyCandidates.Concat(statementCandidates)
            .Select(row => ToFeedItem(row, companyById, companyByProviderAndExternalId))
            .OrderByDescending(item => item.PublishedAt.HasValue).ThenByDescending(item => item.PublishedAt)
            .ThenByDescending(item => item.ReceivedAt).ThenBy(item => item.DisclosureId)
            .Skip(pageOffset).Take(query.PageSize).ToArray();

        var hasUnmappedMonthly = selectedTypes.Contains(CompanyDisclosureType.MonthlyProductionSales) &&
            await monthlyRows.AnyAsync(row => row.CompanyId.HasValue
                ? !knownCompanyIds.Contains(row.CompanyId.Value) && !knownCompanyKeys.Contains(row.ProviderName + "\u001f" + row.ExternalCompanyId)
                : !knownCompanyKeys.Contains(row.ProviderName + "\u001f" + row.ExternalCompanyId), cancellationToken);
        var hasUnmappedStatements = statementTypes.Length > 0 &&
            await latestStatementRows.AnyAsync(row => row.CompanyId.HasValue
                ? !knownCompanyIds.Contains(row.CompanyId.Value) && !knownCompanyKeys.Contains(row.ProviderName + "\u001f" + row.ExternalCompanyId)
                : !knownCompanyKeys.Contains(row.ProviderName + "\u001f" + row.ExternalCompanyId), cancellationToken);
        var coverageStatus = hasUnmappedMonthly || hasUnmappedStatements
            ? DisclosureCoverageStatus.UnmappedCompany
            : DisclosureCoverageStatus.Complete;
        return new CompanyDisclosureFeedPage(
            pageItems, query.Page, query.PageSize, totalCount, DateTimeOffset.UtcNow, coverageStatus);
    }

    private static IQueryable<DisclosureRow> ApplyPublishedAndCompanyFilters(
        IQueryable<DisclosureRow> rows,
        CompanyDisclosureFeedQuery query,
        Guid[] knownCompanyIds,
        Guid[] matchingCompanyIds,
        string[] matchingCompanyKeys)
    {
        if (query.PublishedFrom.HasValue)
            rows = rows.Where(row => row.PublishedAt.HasValue && row.PublishedAt.Value >= query.PublishedFrom.Value);
        if (query.PublishedTo.HasValue)
            rows = rows.Where(row => row.PublishedAt.HasValue && row.PublishedAt.Value <= query.PublishedTo.Value);
        if (!string.IsNullOrWhiteSpace(query.SymbolOrCompany))
            rows = rows.Where(row => row.CompanyId.HasValue
                ? matchingCompanyIds.Contains(row.CompanyId.Value) ||
                    !knownCompanyIds.Contains(row.CompanyId.Value) && matchingCompanyKeys.Contains(row.ProviderName + "\u001f" + row.ExternalCompanyId)
                : matchingCompanyKeys.Contains(row.ProviderName + "\u001f" + row.ExternalCompanyId));
        return rows;
    }

    private static IQueryable<NormalizedMonthlyReportRow> ApplySourceFilters(
        IQueryable<NormalizedMonthlyReportRow> rows,
        string[] providerNames,
        DateTimeOffset? receivedFrom,
        DateTimeOffset? receivedTo)
    {
        if (providerNames.Length > 0) rows = rows.Where(row => providerNames.Contains(row.ProviderName));
        if (receivedFrom.HasValue) rows = rows.Where(row => row.LastSynchronizedAt >= receivedFrom.Value);
        if (receivedTo.HasValue) rows = rows.Where(row => row.LastSynchronizedAt <= receivedTo.Value);
        return rows;
    }

    private static IQueryable<NormalizedFinancialStatementRow> ApplySourceFilters(
        IQueryable<NormalizedFinancialStatementRow> rows,
        string[] providerNames,
        DateTimeOffset? receivedFrom,
        DateTimeOffset? receivedTo)
    {
        if (providerNames.Length > 0) rows = rows.Where(row => providerNames.Contains(row.ProviderName));
        if (receivedFrom.HasValue) rows = rows.Where(row => row.LastSynchronizedAt >= receivedFrom.Value);
        if (receivedTo.HasValue) rows = rows.Where(row => row.LastSynchronizedAt <= receivedTo.Value);
        return rows;
    }

    private static CompanyDisclosureFeedItem ToFeedItem(
        DisclosureRow row,
        IReadOnlyDictionary<Guid, CompanyIdentity> companyById,
        IReadOnlyDictionary<(string ProviderName, string ExternalCompanyId), CompanyIdentity> companyByProviderAndExternalId)
    {
        var company = ResolveCompany(row.CompanyId, row.ProviderName, row.ExternalCompanyId,
            companyById, companyByProviderAndExternalId);
        var logicalDisclosureId = row.Type == CompanyDisclosureType.MonthlyProductionSales
            ? $"monthly:{row.ProviderName}:{row.ExternalCompanyId}:{row.MonthlyReportType ?? "unknown"}:{row.MonthlyPeriodStart:yyyyMMdd}"
            : $"statement:{row.ProviderName}:{row.ExternalCompanyId}:{row.StatementType}:{row.ReportingPeriodType}:{row.ReportingPeriodEnd:yyyyMMdd}:{row.IsComposing}";
        var disclosureId = row.Type == CompanyDisclosureType.MonthlyProductionSales
            ? $"monthly:{row.ProviderName}:{row.SourceRecordId}"
            : $"statement:{row.ProviderName}:{row.SourceRecordId}:{row.StatementType}:{row.IsAudited}:{row.IsRepresented}:{row.IsComposing}";
        var title = row.Type == CompanyDisclosureType.MonthlyProductionSales
            ? MonthlyTitle(row.ReportingPeriodEnd)
            : StatementTitle(row.ReportingPeriodEnd);
        return new CompanyDisclosureFeedItem(
            disclosureId, logicalDisclosureId, row.Type, row.ProviderName, row.ExternalCompanyId,
            company?.Id, company?.Symbol, company?.Name, title, row.PublishedAt, row.ReportingPeriodEnd,
            row.ReceivedAt, row.SourceRecordId, row.RevisionNumber, row.RevisionNumber > 1,
            company is null ? DisclosureCoverageStatus.UnmappedCompany : DisclosureCoverageStatus.Complete,
            "PersistedNormalizedRecord", row.IsAudited, row.IsRepresented, row.IsComposing, row.ReportingPeriodType);
    }

    private static string CompanyKey(string providerName, string externalCompanyId) =>
        $"{providerName}\u001f{externalCompanyId}";

    private static string? StatementTypeName(CompanyDisclosureType type) => type switch
    {
        CompanyDisclosureType.IncomeStatement => "IncomeStatement",
        CompanyDisclosureType.BalanceSheet => "BalanceSheet",
        CompanyDisclosureType.CashFlowStatement => "CashFlow",
        _ => null
    };

    private sealed record DisclosureRow
    {
        public CompanyDisclosureType Type { get; init; }
        public string ProviderName { get; init; } = string.Empty;
        public string ExternalCompanyId { get; init; } = string.Empty;
        public Guid? CompanyId { get; init; }
        public string SourceRecordId { get; init; } = string.Empty;
        public DateOnly? PublishedAt { get; init; }
        public DateOnly ReportingPeriodEnd { get; init; }
        public DateTimeOffset ReceivedAt { get; init; }
        public int RevisionNumber { get; init; }
        public string? MonthlyReportType { get; init; }
        public DateOnly? MonthlyPeriodStart { get; init; }
        public string? StatementType { get; init; }
        public string? ReportingPeriodType { get; init; }
        public bool IsAudited { get; init; }
        public bool IsRepresented { get; init; }
        public bool IsComposing { get; init; }
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

    // Date-only overload: the period end is a calendar date, not an instant (avoids the previous-day shift).
    private static string FormatJalaliDate(DateOnly value) => ShamsiMonthCalculator.FormatJalaliDate(value);

    private sealed record CompanyIdentity(Guid Id, string ProviderName, string ExternalCompanyId, string? Symbol, string Name);
}

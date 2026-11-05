using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Feature 154 bounded, read-only balance-sheet reader (design §5). It returns plain records only, reuses
/// the shared WarningsJson evidence parser and never calls a provider. Statements are linked by
/// (ProviderName, ExternalCompanyId), so a blank CompanyId is valid. Line items join the catalog explicitly
/// because FinancialStatementId has no declared foreign key (R-08). The existing EF command timeout applies.
/// </summary>
internal sealed class EfCoreBalanceSheetStatementReader(FinancialIngestionDbContext dbContext) : IBalanceSheetStatementReader
{
    private const string BalanceSheet = "BalanceSheet";
    private const int FiscalEvidenceWindow = 24;
    private static readonly string[] SupportedPeriodTypes = ["ThreeMonths", "SixMonths", "NineMonths", "TwelveMonths"];

    public async Task<BalanceSheetHeaderSet> ReadHeadersAsync(string providerName, string externalCompanyId,
        CancellationToken cancellationToken = default)
    {
        var balanceSheets = await StandaloneBalanceSheets(providerName, externalCompanyId)
            .OrderByDescending(row => row.PeriodEnd)
            .ThenByDescending(row => row.LastSynchronizedAt)
            .Take(BalanceSheetReportQueryUseCase.HeaderWindow)
            .Select(row => new HeaderRow(row.Id, row.ExternalStatementId, row.PeriodEnd, row.PeriodType, row.IsAudited,
                row.IsRepresented, row.IsComposing, row.PublishedAt, row.WarningsJson))
            .ToListAsync(cancellationToken);

        // Fiscal evidence from any statement type of the same provider and company, so a previous fiscal-year
        // end is known even when no balance sheet exists at that date.
        var evidence = await dbContext.FinancialStatements.AsNoTracking()
            .Where(row => row.ProviderName == providerName && row.ExternalCompanyId == externalCompanyId)
            .OrderByDescending(row => row.PeriodEnd)
            .Take(FiscalEvidenceWindow)
            .Select(row => new { row.PeriodEnd, row.WarningsJson })
            .ToListAsync(cancellationToken);

        return new BalanceSheetHeaderSet(
            balanceSheets.Select(ToHeader).ToArray(),
            evidence.Select(row => new BalanceSheetFiscalEvidence(row.PeriodEnd,
                EfCoreFinancialStatementTableRepository.StatementMetadata.Parse(row.WarningsJson).JalaliFiscalYearEnd)).ToArray());
    }

    public async Task<IReadOnlyList<BalanceSheetStatementHeader>> ReadHeadersAtDatesAsync(string providerName,
        string externalCompanyId, IReadOnlyCollection<DateOnly> periodEnds, CancellationToken cancellationToken = default)
    {
        if (periodEnds.Count == 0) return [];
        var dates = periodEnds.Distinct().ToArray();
        var rows = await StandaloneBalanceSheets(providerName, externalCompanyId)
            .Where(row => dates.Contains(row.PeriodEnd))
            .Select(row => new HeaderRow(row.Id, row.ExternalStatementId, row.PeriodEnd, row.PeriodType, row.IsAudited,
                row.IsRepresented, row.IsComposing, row.PublishedAt, row.WarningsJson))
            .ToListAsync(cancellationToken);
        return rows.Select(ToHeader).ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>>> ReadLineItemsAsync(
        string providerName, IReadOnlyCollection<Guid> statementIds, IReadOnlyCollection<int> sourceItemIds,
        CancellationToken cancellationToken = default)
    {
        if (statementIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>>();
        var statements = statementIds.Distinct().ToArray();
        var items = sourceItemIds.Distinct().ToArray();
        var rows = await dbContext.FinancialStatementLineItems.AsNoTracking()
            .Where(line => statements.Contains(line.FinancialStatementId) && line.SourceItemCatalogId != null)
            .Join(dbContext.FinancialStatementSourceItems.AsNoTracking()
                    .Where(catalog => catalog.ProviderName == providerName && catalog.StatementType == BalanceSheet &&
                                      items.Contains(catalog.SourceItemId)),
                line => line.SourceItemCatalogId,
                catalog => catalog.Id,
                (line, catalog) => new { line.FinancialStatementId, catalog.SourceItemId, line.Id, line.Value, catalog.TitleFa })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.FinancialStatementId)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<BalanceSheetItemObservation>)group
                    .OrderBy(row => row.SourceItemId).ThenBy(row => row.Id)
                    .Select(row => new BalanceSheetItemObservation(row.SourceItemId, row.Id, row.Value, row.TitleFa))
                    .ToArray());
    }

    public Task<bool> ConsolidatedExistsAsync(string providerName, string externalCompanyId,
        CancellationToken cancellationToken = default) =>
        dbContext.FinancialStatements.AsNoTracking()
            .AnyAsync(row => row.ProviderName == providerName && row.ExternalCompanyId == externalCompanyId &&
                             row.StatementType == BalanceSheet && row.IsComposing, cancellationToken);

    private IQueryable<NormalizedFinancialStatementRow> StandaloneBalanceSheets(string providerName, string externalCompanyId) =>
        dbContext.FinancialStatements.AsNoTracking()
            .Where(row => row.ProviderName == providerName && row.ExternalCompanyId == externalCompanyId &&
                          row.StatementType == BalanceSheet && !row.IsComposing &&
                          SupportedPeriodTypes.Contains(row.PeriodType));

    private static BalanceSheetStatementHeader ToHeader(HeaderRow row)
    {
        var metadata = EfCoreFinancialStatementTableRepository.StatementMetadata.Parse(row.WarningsJson);
        return new BalanceSheetStatementHeader(row.Id, row.ExternalStatementId, row.PeriodEnd, row.PeriodType,
            row.IsAudited, row.IsRepresented, row.IsComposing, row.PublishedAt, metadata.JalaliAnnouncementDate,
            metadata.JalaliFiscalYearEnd, metadata.AssumedScale, metadata.SourceIsComposing);
    }

    private sealed record HeaderRow(Guid Id, string ExternalStatementId, DateOnly PeriodEnd, string PeriodType,
        bool IsAudited, bool IsRepresented, bool IsComposing, DateOnly? PublishedAt, string? WarningsJson);
}

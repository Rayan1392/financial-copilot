using FinancialCopilot.Application.FinancialData.CashFlowReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>Read-only, bounded Codal statement access for Feature 161.</summary>
internal sealed class EfCoreCashFlowStatementReader(FinancialIngestionDbContext db) : ICashFlowStatementReader
{
    private static readonly string[] PeriodTypes = ["ThreeMonths", "SixMonths", "NineMonths", "TwelveMonths"];
    private static readonly int[] ExcludedIds = CashFlowLineRegistry.ExcludedValueItems.ToArray();
    private const int MaxHeaderRows = 48;

    public async Task<CashFlowHeaderSet> ReadHeadersAsync(string providerName, string externalCompanyId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.ProviderName == providerName && x.ExternalCompanyId == externalCompanyId &&
                        x.StatementType == "CashFlow" && !x.IsComposing && PeriodTypes.Contains(x.PeriodType))
            .OrderByDescending(x => x.PeriodEnd).ThenByDescending(x => x.PublishedAt)
            .ThenByDescending(x => x.ExternalStatementId).ThenByDescending(x => x.Id)
            .Take(MaxHeaderRows).ToListAsync(cancellationToken);
        var headers = rows.Select(MapHeader).Where(x => x.SourceIsComposing is null or false).ToArray();
        return new(headers, headers.Select(x => new CashFlowFiscalEvidence(x.PeriodEnd, x.JalaliFiscalYearEnd))
            .Distinct().ToArray());
    }

    public async Task<IReadOnlyList<CashFlowStatementHeader>> ReadHeadersAtDatesAsync(string providerName,
        string externalCompanyId, IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken = default)
    {
        if (dates.Count == 0) return [];
        var requested = dates.Distinct().Take(CashFlowReportContract.HeaderWindow).ToArray();
        var rows = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.ProviderName == providerName && x.ExternalCompanyId == externalCompanyId &&
                        x.StatementType == "CashFlow" && !x.IsComposing && PeriodTypes.Contains(x.PeriodType) &&
                        requested.Contains(x.PeriodEnd))
            .OrderByDescending(x => x.PeriodEnd).ThenByDescending(x => x.PublishedAt)
            .ThenByDescending(x => x.ExternalStatementId).ThenByDescending(x => x.Id)
            .Take(MaxHeaderRows).ToListAsync(cancellationToken);
        // A saturated date batch might have cut off peer versions. Fail closed for each saturated date.
        var saturatedDates = rows.Count >= MaxHeaderRows
            ? rows.GroupBy(x => x.PeriodEnd).Where(g => g.Count() >= MaxHeaderRows).Select(g => g.Key).ToHashSet()
            : [];
        return rows.Where(x => !saturatedDates.Contains(x.PeriodEnd)).Select(MapHeader)
            .Where(x => x.SourceIsComposing is null or false).ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>>> ReadLineItemsAsync(
        string providerName, IReadOnlyCollection<Guid> statementIds, IReadOnlyCollection<int> sourceItemIds,
        CancellationToken cancellationToken = default)
    {
        var ids = statementIds.Distinct().Take(CashFlowReportContract.MaxCashFlowStatements).ToArray();
        var allowed = sourceItemIds.Where(x => !CashFlowLineRegistry.ExcludedValueItems.Contains(x))
            .Distinct().ToArray();
        if (ids.Length == 0 || allowed.Length == 0) return new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>();
        var rowLimit = ids.Length * allowed.Length * 4;
        var lines = await db.FinancialStatementLineItems.AsNoTracking()
            .Where(x => ids.Contains(x.FinancialStatementId) && x.SourceItemCatalogId.HasValue)
            .Join(db.FinancialStatements.AsNoTracking().Where(x => x.ProviderName == providerName &&
                    x.StatementType == "CashFlow" && !x.IsComposing && ids.Contains(x.Id)),
                line => line.FinancialStatementId, statement => statement.Id, (line, _) => line)
            .Join(db.FinancialStatementSourceItems.AsNoTracking()
                    .Where(x => x.ProviderName == providerName && x.StatementType == "CashFlow" &&
                                allowed.Contains(x.SourceItemId) && !ExcludedIds.Contains(x.SourceItemId)),
                line => line.SourceItemCatalogId, item => item.Id,
                (line, item) => new { line.FinancialStatementId, LineItemId = line.Id,
                    item.SourceItemId, line.Value, SourceTitle = item.TitleFa })
            .Take(rowLimit + 1).ToListAsync(cancellationToken);
        if (lines.Count > rowLimit) return new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>();
        return lines.GroupBy(x => x.FinancialStatementId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<CashFlowItemObservation>)g.Select(x => new CashFlowItemObservation(
                x.SourceItemId, x.LineItemId, x.Value, x.SourceTitle)).ToArray());
    }

    public async Task<CashFlowAllLineItemsReadResult> ReadAllLineItemsAsync(string providerName,
        IReadOnlyCollection<Guid> statementIds, CancellationToken cancellationToken = default)
    {
        var ids = statementIds.Distinct().Take(CashFlowReportContract.MaxCashFlowStatements).ToArray();
        if (ids.Length == 0) return new(new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), true, 0);
        var eligibleIds = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.ProviderName == providerName && x.StatementType == "CashFlow" && !x.IsComposing && ids.Contains(x.Id))
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
        if (eligibleIds.Length == 0) return new(new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), true, 0);
        var lines = await db.FinancialStatementLineItems.AsNoTracking()
            .Where(x => eligibleIds.Contains(x.FinancialStatementId) && x.SourceItemCatalogId.HasValue)
            .Join(db.FinancialStatements.AsNoTracking().Where(x => x.ProviderName == providerName &&
                    x.StatementType == "CashFlow" && !x.IsComposing && eligibleIds.Contains(x.Id)),
                line => line.FinancialStatementId, statement => statement.Id, (line, _) => line)
            .Join(db.FinancialStatementSourceItems.AsNoTracking()
                    .Where(x => x.ProviderName == providerName && x.StatementType == "CashFlow" &&
                                !ExcludedIds.Contains(x.SourceItemId)),
                line => line.SourceItemCatalogId, item => item.Id,
                (line, item) => new { line.FinancialStatementId, LineItemId = line.Id,
                    item.SourceItemId, line.Value, SourceTitle = item.TitleFa })
            .Take(CashFlowReportContract.MaxCashFlowSourceLineItems + 1).ToListAsync(cancellationToken);
        if (lines.Count > CashFlowReportContract.MaxCashFlowSourceLineItems)
            return new(new Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>>(), false, lines.Count);

        var result = eligibleIds.ToDictionary(id => id, _ => (IReadOnlyList<CashFlowItemObservation>)Array.Empty<CashFlowItemObservation>());
        foreach (var group in lines.GroupBy(x => x.FinancialStatementId))
            result[group.Key] = group.Select(x => new CashFlowItemObservation(x.SourceItemId, x.LineItemId, x.Value, x.SourceTitle)).ToArray();
        return new(result, true, lines.Count);
    }

    public async Task<IReadOnlyList<CashFlowIncomeStatementObservation>> ReadIncomeStatementsAsync(
        string providerName, string externalCompanyId, IReadOnlyCollection<CashFlowStatementHeader> periods,
        CancellationToken cancellationToken = default)
    {
        var requested = periods.DistinctBy(x => (x.PeriodEnd, x.PeriodType)).Take(2).ToArray();
        if (requested.Length == 0) return [];
        var dates = requested.Select(x => x.PeriodEnd).Distinct().ToArray();
        var types = requested.Select(x => x.PeriodType).Distinct().ToArray();
        var rows = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.ProviderName == providerName && x.ExternalCompanyId == externalCompanyId &&
                        x.StatementType == "IncomeStatement" && !x.IsComposing && types.Contains(x.PeriodType) &&
                        dates.Contains(x.PeriodEnd))
            .OrderByDescending(x => x.PeriodEnd).ThenByDescending(x => x.PeriodType)
            .ThenByDescending(x => x.IsAudited).ThenByDescending(x => x.IsRepresented)
            .ThenByDescending(x => x.PublishedAt).ThenByDescending(x => x.ExternalStatementId).ThenByDescending(x => x.Id)
            .Take(MaxHeaderRows).ToListAsync(cancellationToken);
        var saturatedPeriods = rows.Count >= MaxHeaderRows
            ? rows.GroupBy(x => (x.PeriodEnd, x.PeriodType)).Where(g => g.Count() >= MaxHeaderRows)
                .Select(g => g.Key).ToHashSet()
            : [];
        var candidates = rows.Where(x => !saturatedPeriods.Contains((x.PeriodEnd, x.PeriodType)))
            .Select(x => (Row: x, Header: MapHeader(x)))
            .Where(x => x.Header.SourceIsComposing is null or false && x.Header.AssumedScale == "MillionRials")
            .GroupBy(x => (x.Row.PeriodEnd, x.Row.PeriodType));
        var selected = new List<(NormalizedFinancialStatementRow Row, CashFlowStatementHeader Header)>();
        foreach (var group in candidates)
        {
            var ranked = FinancialStatementVersionPolicy.Rank(group, x => new(x.Row.IsAudited, x.Row.IsRepresented, x.Row.PublishedAt));
            var top = FinancialStatementVersionPolicy.TopTieGroup(ranked, x => new(x.Row.IsAudited, x.Row.IsRepresented, x.Row.PublishedAt));
            if (top.Count > CashFlowReportContract.MaxIncomeStatements || selected.Count + top.Count > CashFlowReportContract.MaxIncomeStatements)
                continue;
            selected.AddRange(top);
        }
        if (selected.Count == 0) return [];
        var statementIds = selected.Select(x => x.Row.Id).ToArray();
        var sourceIds = new[] { 15, 143, 313 };
        var items = await db.FinancialStatementLineItems.AsNoTracking()
            .Where(x => statementIds.Contains(x.FinancialStatementId) && x.SourceItemCatalogId.HasValue)
            .Join(db.FinancialStatements.AsNoTracking().Where(x => x.ProviderName == providerName &&
                    x.StatementType == "IncomeStatement" && !x.IsComposing && statementIds.Contains(x.Id)),
                line => line.FinancialStatementId, statement => statement.Id, (line, _) => line)
            .Join(db.FinancialStatementSourceItems.AsNoTracking()
                    .Where(x => x.ProviderName == providerName && x.StatementType == "IncomeStatement" && sourceIds.Contains(x.SourceItemId)),
                line => line.SourceItemCatalogId, item => item.Id,
                (line, item) => new { line.FinancialStatementId, LineItemId = line.Id, item.SourceItemId, line.Value, SourceTitle = item.TitleFa })
            .Take(CashFlowReportContract.MaxIncomeStatements * sourceIds.Length * 4 + 1).ToListAsync(cancellationToken);
        if (items.Count > CashFlowReportContract.MaxIncomeStatements * sourceIds.Length * 4) return [];
        var itemsByStatement = items.GroupBy(x => x.FinancialStatementId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<CashFlowItemObservation>)g.Select(x => new CashFlowItemObservation(x.SourceItemId, x.LineItemId, x.Value, x.SourceTitle)).ToArray());
        return selected.Select(x => new CashFlowIncomeStatementObservation(x.Header,
            itemsByStatement.GetValueOrDefault(x.Row.Id) ?? [])).ToArray();
    }

    public Task<bool> ConsolidatedExistsAsync(string providerName, string externalCompanyId,
        CancellationToken cancellationToken = default) => db.FinancialStatements.AsNoTracking()
        .AnyAsync(x => x.ProviderName == providerName && x.ExternalCompanyId == externalCompanyId &&
                       x.StatementType == "CashFlow" && x.IsComposing && PeriodTypes.Contains(x.PeriodType), cancellationToken);

    private static CashFlowStatementHeader MapHeader(NormalizedFinancialStatementRow row)
    {
        var metadata = EfCoreFinancialStatementTableRepository.StatementMetadata.Parse(row.WarningsJson);
        return new(row.Id, row.ExternalStatementId, row.PeriodStart, row.PeriodEnd, row.PeriodType,
            row.IsAudited, row.IsRepresented, row.IsComposing, row.PublishedAt,
            metadata.JalaliAnnouncementDate, metadata.JalaliFiscalYearEnd, metadata.AssumedScale, metadata.SourceIsComposing);
    }
}

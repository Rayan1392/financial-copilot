using System.Text;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class EfCoreFundEquityPositionRepository(FinancialProviderDbContext dbContext) : IFundEquityPositionRepository
{
    public async Task<FundEquityPositionPage> QueryPositionsAsync(FundEquityPositionQuery query, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var source = dbContext.FundEquityPositionSnapshots.AsNoTracking().Where(row => row.FundId == query.FundId);
        if (query.PeriodEndDate is not null) source = source.Where(row => row.PeriodEndDate == query.PeriodEndDate);
        if (query.PeriodContext is not null) source = source.Where(row => row.PeriodContext == query.PeriodContext);
        if (query.PositionState is not null) source = source.Where(row => row.PositionState == query.PositionState);
        if (query.SecurityType is not null) source = source.Where(row => row.SecurityType == query.SecurityType);
        if (query.ResolutionStatus is not null) source = source.Where(row => row.ResolutionStatus == query.ResolutionStatus);
        if (query.MinimumWeightOfTotalAssetsPercentage is not null) source = source.Where(row => row.WeightOfTotalAssetsPercentage >= query.MinimumWeightOfTotalAssetsPercentage);
        source = ApplyCursor(source, query.Cursor);
        var rows = await source.OrderBy(row => row.NormalizedSecurityName).ThenBy(row => row.SourceLogicalRow).ThenBy(row => row.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(ToDomain).ToArray();
        return new(items, hasMore ? EncodeCursor(items[^1].NormalizedSecurityName, items[^1].Id) : null, hasMore);
    }

    public async Task<FundEquityActivityPage> QueryActivitiesAsync(FundEquityActivityQuery query, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var source = dbContext.FundEquityPeriodActivities.AsNoTracking().Where(row => row.FundId == query.FundId);
        if (query.PeriodEndDate is not null) source = source.Where(row => row.PeriodEndDate == query.PeriodEndDate);
        if (query.PeriodContext is not null) source = source.Where(row => row.PeriodContext == query.PeriodContext);
        if (query.ActivityClassification is not null) source = source.Where(row => row.ActivityClassification == query.ActivityClassification);
        if (query.SecurityType is not null) source = source.Where(row => row.SecurityType == query.SecurityType);
        if (query.ResolutionStatus is not null)
        {
            var positionResolution = query.ResolutionStatus;
            source = source.Where(row => dbContext.FundEquityPositionSnapshots.Any(position => position.ReportId == row.ReportId && position.SourceLogicalRow == row.SourceLogicalRow && position.ResolutionStatus == positionResolution));
        }
        source = ApplyActivityCursor(source, query.Cursor);
        var rows = await source.OrderBy(row => row.NormalizedSecurityName).ThenBy(row => row.SourceLogicalRow).ThenBy(row => row.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(ToDomain).ToArray();
        return new(items, hasMore ? EncodeCursor(items[^1].NormalizedSecurityName, items[^1].Id) : null, hasMore);
    }

    public async Task<(IReadOnlyList<CompanyFundHolding> Items, string? NextCursor, bool HasMore)> QueryCompanyHoldingsAsync(CompanyFundHoldingsQuery query, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var source = from position in dbContext.FundEquityPositionSnapshots.AsNoTracking()
                     join report in dbContext.FundPortfolioReports.AsNoTracking() on position.ReportId equals report.Id
                     where position.ExternalCompanyId == query.ExternalCompanyId
                     select new { position, report.ImportedAtUtc };
        if (query.PeriodEndDate is not null) source = source.Where(row => row.position.PeriodEndDate == query.PeriodEndDate);
        var cursor = DecodeCursor(query.Cursor);
        if (cursor is not null) source = source.Where(row => (row.position.FundId.ToString() + row.position.ReportId.ToString()).CompareTo(cursor.Value.Name) > 0);
        var rows = await source.OrderBy(row => row.position.FundId).ThenBy(row => row.position.ReportId).ThenBy(row => row.position.PositionState).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(row => new CompanyFundHolding(row.position.FundId, row.position.ReportId, row.position.PeriodEndDate, row.position.PeriodContext, row.position.PositionState, row.position.SecurityType, row.position.RawSecurityName, row.position.Quantity, row.position.MarketOrNetSaleValue, row.position.WeightOfTotalAssetsPercentage, row.position.ResolutionStatus, row.position.SourceRevision, row.ImportedAtUtc, row.position.SourceEvidenceJson)).ToArray();
        var nextCursor = hasMore ? EncodeCursor($"{items[^1].FundId:N}{items[^1].ReportId:N}", items[^1].ReportId) : null;
        return (items, nextCursor, hasMore);
    }

    private static IQueryable<FundEquityPositionSnapshotRow> ApplyCursor(IQueryable<FundEquityPositionSnapshotRow> source, string? token)
    {
        var cursor = DecodeCursor(token);
        return cursor is null ? source : source.Where(row => row.NormalizedSecurityName.CompareTo(cursor.Value.Name) > 0 || (row.NormalizedSecurityName == cursor.Value.Name && row.Id.CompareTo(cursor.Value.Id) > 0));
    }

    private static IQueryable<FundEquityPeriodActivityRow> ApplyActivityCursor(IQueryable<FundEquityPeriodActivityRow> source, string? token)
    {
        var cursor = DecodeCursor(token);
        return cursor is null ? source : source.Where(row => row.NormalizedSecurityName.CompareTo(cursor.Value.Name) > 0 || (row.NormalizedSecurityName == cursor.Value.Name && row.Id.CompareTo(cursor.Value.Id) > 0));
    }

    private static FundEquityPositionSnapshot ToDomain(FundEquityPositionSnapshotRow row) => new(row.Id, row.ReportId, row.FundId, row.PeriodContext, row.PeriodEndDate, row.PositionState, row.SecurityType, row.ExternalCompanyId, row.TradingInstrumentId, row.RawSecurityName, row.NormalizedSecurityName, row.Quantity, row.UnitMarketPrice, row.CostAmount, row.MarketOrNetSaleValue, row.WeightOfTotalAssetsPercentage, row.ResolutionStatus, row.SourceLogicalRow, row.SourceSheetId, row.SourceAddress, row.SourceRevision, row.ImportedAtUtc, row.ParserProfileVersion, row.MonetaryUnit, row.PercentageScale, row.SourceEvidenceJson);
    private static FundEquityPeriodActivity ToDomain(FundEquityPeriodActivityRow row) => new(row.Id, row.ReportId, row.FundId, row.PeriodContext, row.PeriodEndDate, row.SecurityType, row.ExternalCompanyId, row.TradingInstrumentId, row.RawSecurityName, row.NormalizedSecurityName, row.PurchasedQuantity, row.PurchaseCostAmount, row.SoldQuantity, row.SaleProceedsAmount, row.ActivityClassification, row.QuantityReconciliationDifference, row.ReconciliationStatus, row.KnownCorporateActionAdjustment, row.SourceLogicalRow, row.SourceSheetId, row.SourceAddress, row.SourceRevision, row.ImportedAtUtc, row.ParserProfileVersion, row.MonetaryUnit, row.SourceEvidenceJson);

    private static string EncodeCursor(string name, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{name}\u001f{id:N}"));
    private static (string Name, Guid Id)? DecodeCursor(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('\u001f');
            return parts.Length == 2 && Guid.TryParse(parts[1], out var id) ? (parts[0], id) : null;
        }
        catch (FormatException) { return null; }
    }
}

public sealed class GetFundEquityPositionsUseCase(IFundEquityPositionRepository repository) : IGetFundEquityPositionsUseCase
{
    public Task<FundEquityPositionPage> ExecuteAsync(FundEquityPositionQuery query, CancellationToken cancellationToken) => repository.QueryPositionsAsync(query, cancellationToken);
}

public sealed class GetFundEquityActivityUseCase(IFundEquityPositionRepository repository) : IGetFundEquityActivityUseCase
{
    public Task<FundEquityActivityPage> ExecuteAsync(FundEquityActivityQuery query, CancellationToken cancellationToken) => repository.QueryActivitiesAsync(query, cancellationToken);
}

public sealed class GetCompanyFundHoldingsUseCase(IFundEquityPositionRepository repository) : IGetCompanyFundHoldingsUseCase
{
    public Task<(IReadOnlyList<CompanyFundHolding> Items, string? NextCursor, bool HasMore)> ExecuteAsync(CompanyFundHoldingsQuery query, CancellationToken cancellationToken) => repository.QueryCompanyHoldingsAsync(query, cancellationToken);
}

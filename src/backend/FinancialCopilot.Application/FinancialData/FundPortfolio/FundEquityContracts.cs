using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundEquityPositionQuery(
    Guid FundId,
    DateOnly? PeriodEndDate = null,
    FundWorkbookPeriodContext? PeriodContext = null,
    FundPositionState? PositionState = null,
    FundEquitySecurityType? SecurityType = null,
    FundSecurityResolutionStatus? ResolutionStatus = null,
    decimal? MinimumWeightOfTotalAssetsPercentage = null,
    string? Cursor = null,
    int PageSize = 50);

public sealed record FundEquityActivityQuery(
    Guid FundId,
    DateOnly? PeriodEndDate = null,
    FundWorkbookPeriodContext? PeriodContext = null,
    FundEquityActivityClassification? ActivityClassification = null,
    FundEquitySecurityType? SecurityType = null,
    FundSecurityResolutionStatus? ResolutionStatus = null,
    string? Cursor = null,
    int PageSize = 50);

public sealed record FundEquityPositionPage(
    IReadOnlyList<FundEquityPositionSnapshot> Items,
    string? NextCursor,
    bool HasMore);

public sealed record FundEquityActivityPage(
    IReadOnlyList<FundEquityPeriodActivity> Items,
    string? NextCursor,
    bool HasMore);

public sealed record CompanyFundHoldingsQuery(
    string ExternalCompanyId,
    DateOnly? PeriodEndDate = null,
    string? Cursor = null,
    int PageSize = 50);

public sealed record CompanyFundHolding(
    Guid FundId,
    Guid ReportId,
    DateOnly? PeriodEndDate,
    FundWorkbookPeriodContext PeriodContext,
    FundPositionState PositionState,
    FundEquitySecurityType SecurityType,
    string RawSecurityName,
    decimal? Quantity,
    decimal? MarketOrNetSaleValue,
    decimal? WeightOfTotalAssetsPercentage,
    FundSecurityResolutionStatus ResolutionStatus,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    string SourceEvidenceJson);

public interface IFundPortfolioEquitySectionNormalizer : IFundPortfolioSectionNormalizer
{
}

public interface IFundEquityPositionRepository
{
    Task<FundEquityPositionPage> QueryPositionsAsync(FundEquityPositionQuery query, CancellationToken cancellationToken);
    Task<FundEquityActivityPage> QueryActivitiesAsync(FundEquityActivityQuery query, CancellationToken cancellationToken);
    Task<(IReadOnlyList<CompanyFundHolding> Items, string? NextCursor, bool HasMore)> QueryCompanyHoldingsAsync(CompanyFundHoldingsQuery query, CancellationToken cancellationToken);
}

public interface IGetFundEquityPositionsUseCase
{
    Task<FundEquityPositionPage> ExecuteAsync(FundEquityPositionQuery query, CancellationToken cancellationToken);
}

public interface IGetFundEquityActivityUseCase
{
    Task<FundEquityActivityPage> ExecuteAsync(FundEquityActivityQuery query, CancellationToken cancellationToken);
}

public interface IGetCompanyFundHoldingsUseCase
{
    Task<(IReadOnlyList<CompanyFundHolding> Items, string? NextCursor, bool HasMore)> ExecuteAsync(CompanyFundHoldingsQuery query, CancellationToken cancellationToken);
}

public interface IFundEquityNormalizationTelemetry
{
    void Record(Guid reportId, int rowCount, int resolvedCount, int unresolvedCount, int newPositionCount, int fullExitCount, int reconciliationMismatchCount, TimeSpan duration);
}

public interface IFundEquityCorporateActionAdjustmentProvider
{
    Task<decimal?> GetKnownQuantityAdjustmentAsync(Guid reportId, FundWorkbookPeriodContext periodContext, string normalizedSecurityName, CancellationToken cancellationToken);
}

public static class FundEquityActivityPolicy
{
    public static decimal? CalculateQuantityDifference(decimal? beginningQuantity, decimal? purchasedQuantity, decimal? soldQuantity, decimal? endingQuantity, decimal? knownCorporateActionAdjustment = null) =>
        beginningQuantity.HasValue && purchasedQuantity.HasValue && soldQuantity.HasValue && endingQuantity.HasValue
            ? endingQuantity.Value - (beginningQuantity.Value + purchasedQuantity.Value - soldQuantity.Value + (knownCorporateActionAdjustment ?? 0m))
            : null;

    public static FundEquityReconciliationStatus Reconcile(decimal? beginningQuantity, decimal? purchasedQuantity, decimal? soldQuantity, decimal? endingQuantity, decimal? knownCorporateActionAdjustment = null)
    {
        if (!beginningQuantity.HasValue && !endingQuantity.HasValue) return FundEquityReconciliationStatus.NotApplicable;
        if (!beginningQuantity.HasValue || !endingQuantity.HasValue || !purchasedQuantity.HasValue || !soldQuantity.HasValue)
            return FundEquityReconciliationStatus.Unknown;
        var expected = beginningQuantity.Value + purchasedQuantity.Value - soldQuantity.Value + (knownCorporateActionAdjustment ?? 0m);
        return expected == endingQuantity.Value ? FundEquityReconciliationStatus.Reconciled : FundEquityReconciliationStatus.Unreconciled;
    }

    public static FundEquityActivityClassification Classify(decimal? beginningQuantity, decimal? purchasedQuantity, decimal? soldQuantity, decimal? endingQuantity, FundEquityReconciliationStatus reconciliation)
    {
        if (reconciliation == FundEquityReconciliationStatus.Unreconciled) return FundEquityActivityClassification.Unreconciled;
        if (endingQuantity is > 0 && (beginningQuantity is null or 0)) return FundEquityActivityClassification.NewPosition;
        if (beginningQuantity is > 0 && endingQuantity is 0) return FundEquityActivityClassification.FullExit;
        if (beginningQuantity.HasValue && endingQuantity.HasValue)
            return endingQuantity > beginningQuantity ? FundEquityActivityClassification.Increased : endingQuantity < beginningQuantity ? FundEquityActivityClassification.Reduced : FundEquityActivityClassification.Unchanged;
        return FundEquityActivityClassification.Unknown;
    }
}

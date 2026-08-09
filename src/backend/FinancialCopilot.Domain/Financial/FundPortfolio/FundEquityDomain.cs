namespace FinancialCopilot.Domain.Financial.FundPortfolio;

public enum FundPositionState
{
    Beginning,
    Ending
}

public enum FundEquitySecurityType
{
    OrdinaryEquity,
    PreemptiveRight,
    InvestmentFundUnit,
    Unresolved
}

public enum FundSecurityResolutionStatus
{
    Resolved,
    Ambiguous,
    Unresolved
}

public enum FundEquityActivityClassification
{
    Unknown,
    NewPosition,
    FullExit,
    Increased,
    Reduced,
    Unchanged,
    Unreconciled
}

public enum FundEquityReconciliationStatus
{
    NotApplicable,
    Reconciled,
    Unreconciled,
    Unknown
}

public sealed record FundEquityPositionSnapshot(
    Guid Id,
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    DateOnly? PeriodEndDate,
    FundPositionState PositionState,
    FundEquitySecurityType SecurityType,
    string? ExternalCompanyId,
    Guid? TradingInstrumentId,
    string RawSecurityName,
    string NormalizedSecurityName,
    decimal? Quantity,
    decimal? UnitMarketPrice,
    decimal? CostAmount,
    decimal? MarketOrNetSaleValue,
    decimal? WeightOfTotalAssetsPercentage,
    FundSecurityResolutionStatus ResolutionStatus,
    int SourceLogicalRow,
    Guid SourceSheetId,
    string? SourceAddress,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    string ParserProfileVersion,
    string MonetaryUnit,
    string PercentageScale,
    string SourceEvidenceJson);

public sealed record FundEquityPeriodActivity(
    Guid Id,
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    DateOnly? PeriodEndDate,
    FundEquitySecurityType SecurityType,
    string? ExternalCompanyId,
    Guid? TradingInstrumentId,
    string RawSecurityName,
    string NormalizedSecurityName,
    decimal? PurchasedQuantity,
    decimal? PurchaseCostAmount,
    decimal? SoldQuantity,
    decimal? SaleProceedsAmount,
    FundEquityActivityClassification ActivityClassification,
    decimal? QuantityReconciliationDifference,
    FundEquityReconciliationStatus ReconciliationStatus,
    decimal? KnownCorporateActionAdjustment,
    int SourceLogicalRow,
    Guid SourceSheetId,
    string? SourceAddress,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    string ParserProfileVersion,
    string MonetaryUnit,
    string SourceEvidenceJson);

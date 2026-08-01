namespace FinancialCopilot.Domain.Financial.FundPortfolio;

public enum FundAssetClass
{
    EquityAndRights,
    CommodityCertificates,
    BankDeposits,
    Derivatives,
    CashAndOther,
    Unknown
}

public enum FundNonEquityResolutionStatus
{
    NotApplicable,
    Resolved,
    Ambiguous,
    Unresolved
}

public enum FundCommodityType
{
    GoldBullion,
    CopperCathode,
    Rebar,
    OtherCommodity,
    Unknown
}

public enum FundDerivativeType
{
    ProtectivePut,
    ExchangeTradedOption,
    Unknown
}

public enum FundOptionType
{
    Put,
    Call,
    Unknown
}

public enum FundPositionSide
{
    Long,
    Short,
    Unknown
}

public enum FundNonEquityReconciliationStatus
{
    NotApplicable,
    Reconciled,
    Unreconciled,
    UnknownInputs,
    SummaryUnavailableDueToSourceError
}

public enum FundHedgeCoverageStatus
{
    Covered,
    PartiallyCovered,
    OverCovered,
    NoMatchingHolding,
    UnknownInputs,
    NotApplicable
}

public sealed record FundAssetAllocationSnapshot(
    Guid Id,
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    DateOnly? PeriodEndDate,
    FundAssetClass AssetClass,
    string RawAssetClassLabel,
    string NormalizedAssetClassCode,
    decimal? CostAmount,
    decimal? MarketOrNetSaleValue,
    decimal? WeightOfTotalAssetsPercentage,
    bool IsSectionTotal,
    bool HasSourceFormulaError,
    int SourceLogicalRow,
    Guid SourceSheetId,
    string? SourceAddress,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    string ParserProfileVersion,
    string MonetaryUnit,
    string PercentageScale,
    string SourceEvidenceJson);

public sealed record FundCommodityCertificatePosition(
    Guid Id,
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    DateOnly? PeriodEndDate,
    FundCommodityType CommodityType,
    string? CommodityCode,
    string? ExtractedInstrumentSymbol,
    Guid? TradingInstrumentId,
    string RawInstrumentName,
    string NormalizedInstrumentName,
    decimal? BeginningQuantity,
    decimal? BeginningCostAmount,
    decimal? BeginningMarketValue,
    decimal? PurchasedQuantity,
    decimal? PurchaseCostAmount,
    decimal? SoldQuantity,
    decimal? SaleProceedsAmount,
    decimal? EndingQuantity,
    decimal? EndingUnitPrice,
    decimal? EndingCostAmount,
    decimal? EndingMarketValue,
    decimal? WeightOfTotalAssetsPercentage,
    decimal? QuantityReconciliationDifference,
    FundNonEquityReconciliationStatus ReconciliationStatus,
    FundNonEquityResolutionStatus ResolutionStatus,
    bool IsSectionTotal,
    int SourceLogicalRow,
    Guid SourceSheetId,
    string? SourceAddress,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    string ParserProfileVersion,
    string SourceEvidenceJson);

public sealed record FundBankDepositPosition(
    Guid Id,
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    DateOnly? PeriodEndDate,
    string? BankCode,
    string RawBankName,
    string NormalizedBankName,
    decimal? BeginningBalance,
    decimal? IncreaseAmount,
    decimal? DecreaseAmount,
    decimal? EndingBalance,
    decimal? WeightOfTotalAssetsPercentage,
    decimal? BalanceReconciliationDifference,
    FundNonEquityReconciliationStatus ReconciliationStatus,
    FundNonEquityResolutionStatus ResolutionStatus,
    bool IsSectionTotal,
    int SourceLogicalRow,
    Guid SourceSheetId,
    string? SourceAddress,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    string ParserProfileVersion,
    string SourceEvidenceJson);

public sealed record FundDerivativePosition(
    Guid Id,
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    DateOnly? PeriodEndDate,
    FundDerivativeType DerivativeType,
    FundOptionType OptionType,
    FundPositionSide PositionSide,
    Guid? TradingInstrumentId,
    string? UnderlyingExternalCompanyId,
    Guid? UnderlyingTradingInstrumentId,
    string RawInstrumentName,
    string NormalizedInstrumentName,
    string? RawUnderlyingName,
    decimal? ContractQuantity,
    decimal? ContractMultiplier,
    decimal? UnderlyingCoverageQuantity,
    decimal? StrikePrice,
    string? ExpiryOrExerciseJalali,
    DateOnly? ExpiryOrExerciseDate,
    decimal? EffectiveReturnPercentage,
    decimal? CostAmount,
    decimal? MarketValue,
    decimal? WeightOfTotalAssetsPercentage,
    FundNonEquityResolutionStatus ResolutionStatus,
    FundHedgeCoverageStatus HedgeCoverageStatus,
    string? HedgeCoverageCalculationVersion,
    string? HedgeCoverageEvidenceJson,
    int SourceLogicalRow,
    Guid SourceSheetId,
    string? SourceAddress,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    string ParserProfileVersion,
    string SourceEvidenceJson);

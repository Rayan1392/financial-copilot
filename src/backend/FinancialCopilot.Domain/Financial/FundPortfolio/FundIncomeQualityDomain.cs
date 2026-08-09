namespace FinancialCopilot.Domain.Financial.FundPortfolio;

public enum FundIncomeCategory
{
    EquityDividend,
    EquityUnrealized,
    EquityRealized,
    CommodityUnrealized,
    CommodityRealized,
    DepositInterest,
    OtherIncome,
    Unknown
}

public enum FundIncomeResolutionStatus
{
    Resolved,
    Unresolved,
    Ambiguous,
    NotApplicable
}

public enum FundIncomeReconciliationStatus
{
    Reconciled,
    Unreconciled,
    Unavailable,
    UnknownInputs
}

public enum FundPortfolioValuationQualityStatus
{
    Unknown,
    High,
    Moderate,
    Limited,
    InsufficientEvidence
}

public sealed record FundInvestmentIncomeSummary(
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    FundIncomeCategory IncomeCategory,
    decimal? Amount,
    decimal? SourcePercentageOfTotalIncome,
    decimal? CalculatedPercentageOfTotalIncome,
    decimal? PercentageOfTotalAssets,
    decimal? CumulativeAmount,
    bool HasSourceFormulaError,
    FundIncomeReconciliationStatus ReconciliationStatus,
    string SourceEvidenceJson,
    string CalculationVersion);

public sealed record FundSecurityIncomeAttribution(
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    string RawSecurityName,
    string? ExternalCompanyId,
    Guid? TradingInstrumentId,
    decimal? DividendIncome,
    decimal? UnrealizedPriceChangeIncome,
    decimal? RealizedSaleIncome,
    decimal? TotalIncome,
    FundIncomeResolutionStatus ResolutionStatus,
    FundIncomeReconciliationStatus ReconciliationStatus,
    string SourceEvidenceJson);

public sealed record FundDividendIncomeDetail(
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    string RawSecurityName,
    string? ExternalCompanyId,
    DateOnly? MeetingDate,
    string? MeetingDateJalali,
    decimal? EntitledQuantity,
    decimal? DividendPerShare,
    decimal? GrossDividendIncome,
    decimal? DiscountCost,
    decimal? NetDividendIncome,
    FundIncomeResolutionStatus ResolutionStatus,
    string SourceEvidenceJson);

public sealed record FundCommodityIncomeDetail(
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    string RawInstrumentName,
    decimal? UnrealizedIncome,
    decimal? RealizedIncome,
    decimal? TotalIncome,
    FundIncomeResolutionStatus ResolutionStatus,
    string SourceEvidenceJson);

public sealed record FundDepositIncomeDetail(
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    string RawBankName,
    decimal? GrossIncome,
    decimal? DiscountCost,
    decimal? NetIncome,
    FundIncomeResolutionStatus ResolutionStatus,
    string SourceEvidenceJson);

public sealed record FundValuationAdjustment(
    Guid ReportId,
    Guid FundId,
    FundWorkbookPeriodContext PeriodContext,
    string RawSecurityName,
    Guid? TradingInstrumentId,
    decimal? Quantity,
    decimal? ClosingPrice,
    decimal? AdjustedPrice,
    decimal? SourceAdjustmentPercentage,
    decimal? CalculatedAdjustmentPercentage,
    decimal? AdjustedValue,
    string? Reason,
    FundIncomeResolutionStatus ResolutionStatus,
    bool IsMaterial,
    string SourceEvidenceJson);

public sealed record FundPortfolioValuationQualitySnapshot(
    Guid ReportId,
    Guid FundId,
    int AdjustedSecurityCount,
    decimal? AdjustedValueAmount,
    decimal? AdjustedValueExposurePercentage,
    int MaterialReconciliationIssueCount,
    FundPortfolioValuationQualityStatus QualityStatus,
    decimal? QualityScore,
    string CalculationVersion,
    string EvidenceJson);

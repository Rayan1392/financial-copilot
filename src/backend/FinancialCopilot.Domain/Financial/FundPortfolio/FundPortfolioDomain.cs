namespace FinancialCopilot.Domain.Financial.FundPortfolio;

/// <summary>Canonical identity for an investment fund, distinct from a followed market symbol.</summary>
public sealed class InvestmentFund
{
    private InvestmentFund() { }

    public InvestmentFund(
        Guid id,
        string fundName,
        string normalizedFundName,
        string providerName,
        string? externalFundId = null,
        string? fundSymbol = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Fund id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(fundName)) throw new ArgumentException("Fund name is required.", nameof(fundName));
        if (string.IsNullOrWhiteSpace(normalizedFundName)) throw new ArgumentException("Normalized fund name is required.", nameof(normalizedFundName));
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("Provider name is required.", nameof(providerName));

        Id = id;
        FundName = fundName.Trim();
        NormalizedFundName = normalizedFundName.Trim();
        ProviderName = providerName.Trim();
        ExternalFundId = externalFundId;
        FundSymbol = fundSymbol;
        IsActive = true;
        CreatedAtUtc = UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string? ExternalFundId { get; private set; }
    public string FundName { get; private set; } = string.Empty;
    public string NormalizedFundName { get; private set; } = string.Empty;
    public string? FundSymbol { get; private set; }
    public string? RegistrationNumber { get; private set; }
    public string? ManagerName { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
}

public enum FundPortfolioParseStatus
{
    Queued,
    Parsing,
    PartiallyParsed,
    Parsed,
    NeedsReview,
    Failed,
    Superseded
}

public enum FundWorkbookLogicalSheetType
{
    Unclassified,
    ReportCover,
    FormulaOrControlSheetIgnored,
    AssetAllocationSummary,
    EquityPortfolioCurrent,
    EquityPortfolioComparative,
    DerivativePositions,
    CommodityCertificatePositions,
    BankDepositPositions,
    ValuationAdjustments,
    InvestmentIncomeSummary,
    EquityIncomeSummary,
    DividendIncomeDetail,
    EquityUnrealizedIncomeDetail,
    EquityRealizedIncomeDetail,
    CommodityIncomeSummary,
    CommodityUnrealizedIncomeDetail,
    CommodityRealizedIncomeDetail,
    DepositIncomeSummary,
    DepositIncomeDetail,
    OtherIncomeDetail
}

public enum FundExtractionIssueSeverity
{
    Info,
    Warning,
    Error,
    Fatal
}

public enum FundWorkbookPeriodContext
{
    CurrentPeriod,
    FiscalYearToDate,
    PriorComparablePeriod,
    UnknownPeriodContext
}

public enum FundPortfolioReportType
{
    MonthlyPortfolio
}

public enum FundResolutionStatus
{
    Resolved,
    Created,
    Ambiguous,
    NeedsReview
}

public sealed record FundPortfolioReportPeriod(
    string? PeriodEndJalali,
    DateOnly? PeriodEndDate,
    string? PeriodStartJalali = null,
    DateOnly? PeriodStartDate = null,
    string? FiscalYearStartJalali = null,
    string? FiscalYearEndJalali = null);

public sealed class FundPortfolioReport
{
    private FundPortfolioReport() { }

    public FundPortfolioReport(
        Guid id,
        Guid fundId,
        string providerName,
        FundPortfolioReportPeriod period,
        string originalFileName,
        string fileSha256,
        string rawStorageKey,
        string parserProfileVersion,
        int sourceRevision = 1)
    {
        if (id == Guid.Empty || fundId == Guid.Empty) throw new ArgumentException("Report and fund ids are required.");
        if (period is null) throw new ArgumentNullException(nameof(period));
        if (string.IsNullOrWhiteSpace(fileSha256)) throw new ArgumentException("File hash is required.", nameof(fileSha256));
        Id = id; FundId = fundId; ProviderName = providerName; Period = period;
        OriginalFileName = originalFileName; FileSha256 = fileSha256; RawStorageKey = rawStorageKey;
        ParserProfileVersion = parserProfileVersion; SourceRevision = sourceRevision;
        ReportType = FundPortfolioReportType.MonthlyPortfolio;
        ParseStatus = FundPortfolioParseStatus.Queued;
        ImportedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid FundId { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public string? ExternalReportId { get; private set; }
    public FundPortfolioReportType ReportType { get; private set; }
    public FundPortfolioReportPeriod Period { get; private set; } = new(null, null);
    public string OriginalFileName { get; private set; } = string.Empty;
    public string FileSha256 { get; private set; } = string.Empty;
    public string RawStorageKey { get; private set; } = string.Empty;
    public string ParserProfileVersion { get; private set; } = string.Empty;
    public FundPortfolioParseStatus ParseStatus { get; private set; }
    public int SourceRevision { get; private set; }
    public DateTimeOffset ImportedAtUtc { get; private set; }
    public Guid? SupersedesReportId { get; private set; }
}

public sealed class FundPortfolioReportSheet
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public string OriginalSheetName { get; init; } = string.Empty;
    public string NormalizedSheetName { get; init; } = string.Empty;
    public FundWorkbookLogicalSheetType LogicalSheetType { get; init; }
    public int SheetIndex { get; init; }
    public string? UsedRange { get; init; }
    public decimal ClassificationConfidence { get; init; }
    public string? HeaderFingerprint { get; init; }
    public string ParserProfileVersion { get; init; } = string.Empty;
}

public sealed record FundPortfolioExtractionIssue
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public Guid? SheetId { get; init; }
    public FundExtractionIssueSeverity Severity { get; init; }
    public string IssueCode { get; init; } = string.Empty;
    public string? SourceAddress { get; init; }
    public string? RawValue { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ParserProfileVersion { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <remarks>A followed symbol/user portfolio is not an investment fund or its disclosed holdings.</remarks>
public static class FundPortfolioOwnership
{
    public const string Capability = "FundPortfolio";
    public const string NormalizedRowsOwnedBy = "Features 102-104";
}

using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundPortfolioWorkbookParseRequest(
    Guid ReportId,
    Guid FundId,
    string ProviderName,
    string OriginalFileName,
    string FileSha256,
    string ParserProfileVersion,
    Stream Workbook,
    FundPortfolioReportPeriod? KnownPeriod = null);

public sealed record FundWorkbookCellEvidence(
    string SheetName,
    int SheetIndex,
    string SourceAddress,
    string? RawValue,
    string? NormalizedValue,
    string? FormulaText,
    string? HeaderPath,
    string? PeriodContext,
    string ParserProfileVersion);

public sealed record FundWorkbookSheetEnvelope(
    Guid SheetId,
    string OriginalSheetName,
    string NormalizedSheetName,
    FundWorkbookLogicalSheetType LogicalSheetType,
    int SheetIndex,
    string? UsedRange,
    decimal ClassificationConfidence,
    string HeaderFingerprint,
    string ClassifierVersion,
    IReadOnlyList<FundWorkbookCellEvidence> Cells,
    IReadOnlyList<FundPortfolioExtractionIssue> Issues);

public sealed record FundPortfolioWorkbookEnvelope(
    Guid ReportId,
    Guid FundId,
    string ProviderName,
    string OriginalFileName,
    string FileSha256,
    string ParserProfileVersion,
    FundPortfolioReportPeriod Period,
    IReadOnlyList<FundWorkbookSheetEnvelope> Sheets,
    IReadOnlyList<FundPortfolioExtractionIssue> Issues)
{
    public string? ExtractedFundName { get; init; }
    public string? ReportTitle { get; init; }
    public string? CorrelationId { get; init; }

    public FundPortfolioParseStatus Status =>
        Issues.Any(issue => issue.Severity is FundExtractionIssueSeverity.Fatal)
            ? FundPortfolioParseStatus.Failed
            : Issues.Any(issue => issue.Severity is FundExtractionIssueSeverity.Error)
                ? FundPortfolioParseStatus.PartiallyParsed
                : FundPortfolioParseStatus.Parsed;
}

public interface IFundPortfolioWorkbookParser
{
    Task<FundPortfolioWorkbookEnvelope> ParseAsync(
        FundPortfolioWorkbookParseRequest request,
        CancellationToken cancellationToken);
}

public interface IFundPortfolioValueNormalizer
{
    string NormalizeText(string? value);
    bool TryParseDecimal(string? value, out decimal result);
    bool IsExcelError(string? value);
}

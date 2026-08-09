using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record CreateOrResolveInvestmentFundRequest(
    string ProviderName,
    string FundName,
    string? ExternalFundId = null,
    string? FundSymbol = null,
    bool AllowCreate = true);

public sealed record CreateOrResolveInvestmentFundResult(
    FundResolutionStatus Status,
    InvestmentFund? Fund,
    IReadOnlyList<InvestmentFund> Candidates,
    string? Message = null);

public interface ICreateOrResolveInvestmentFundUseCase
{
    Task<CreateOrResolveInvestmentFundResult> ExecuteAsync(
        CreateOrResolveInvestmentFundRequest request,
        CancellationToken cancellationToken);
}

public sealed record IngestFundPortfolioWorkbookRequest(
    string ProviderName,
    string FundName,
    string OriginalFileName,
    string ContentType,
    Stream Workbook,
    string? ExternalFundId = null,
    string? FundSymbol = null,
    FundPortfolioReportPeriod? KnownPeriod = null,
    string? CorrelationId = null,
    string? SourceObjectId = null);

public sealed record IngestFundPortfolioWorkbookResult(
    Guid ReportId,
    FundResolutionStatus FundResolutionStatus,
    FundPortfolioParseStatus ParseStatus,
    bool IsDuplicate,
    int SourceRevision,
    string FileSha256,
    string? Message = null);

public interface IIngestFundPortfolioWorkbookUseCase
{
    Task<IngestFundPortfolioWorkbookResult> ExecuteAsync(
        IngestFundPortfolioWorkbookRequest request,
        CancellationToken cancellationToken);
}

public sealed record FundPortfolioReportStatusResult(
    Guid ReportId,
    Guid FundId,
    FundPortfolioParseStatus ParseStatus,
    int SourceRevision,
    string ProviderName,
    string FileSha256,
    DateTimeOffset ImportedAtUtc,
    string? CorrelationId = null,
    string? SourceObjectId = null);

public sealed record FundPortfolioReportIssueResult(
    Guid Id,
    Guid ReportId,
    Guid? SheetId,
    FundExtractionIssueSeverity Severity,
    string IssueCode,
    string? SourceAddress,
    string? RawValue,
    string Message,
    DateTimeOffset CreatedAtUtc);
public sealed record FundPortfolioReportIssuePage(IReadOnlyList<FundPortfolioReportIssueResult> Items, int Page, int PageSize, int TotalCount);

public interface IGetFundPortfolioReportStatusUseCase
{
    Task<FundPortfolioReportStatusResult?> ExecuteAsync(Guid reportId, CancellationToken cancellationToken);
}

public interface IGetFundPortfolioReportIssuesUseCase
{
    Task<FundPortfolioReportIssuePage> ExecuteAsync(Guid reportId, int page = 1, int pageSize = 100, FundExtractionIssueSeverity? severity = null, string? issueCode = null, CancellationToken cancellationToken = default);
}

public sealed record FundPortfolioReprocessWork(Guid ReportId, Guid FundId, string ProviderName, string OriginalFileName, string RawStorageKey, string FileSha256, FundPortfolioReportPeriod Period);
public sealed record ReprocessFundPortfolioReportRequest(Guid ReportId, string ParserProfileVersion);
public interface IFundPortfolioRawWorkbookReader
{
    Task<Stream> OpenAsync(string storageKey, CancellationToken cancellationToken);
}
public interface IFundPortfolioReportReprocessRepository
{
    Task<FundPortfolioReprocessWork?> GetReprocessWorkAsync(Guid reportId, CancellationToken cancellationToken);
    Task ReplaceParsedEvidenceAsync(FundPortfolioWorkbookEnvelope envelope, string parserProfileVersion, CancellationToken cancellationToken);
}
public interface IReprocessFundPortfolioReportUseCase
{
    Task<FundPortfolioParseStatus?> ExecuteAsync(ReprocessFundPortfolioReportRequest request, CancellationToken cancellationToken);
}

public interface IInvestmentFundRepository
{
    Task<IReadOnlyList<InvestmentFund>> FindCandidatesAsync(string providerName, string normalizedFundName, string? externalFundId, CancellationToken cancellationToken);
    Task<InvestmentFund> AddAsync(InvestmentFund fund, CancellationToken cancellationToken);
}

public sealed record FundPortfolioStoredFile(string StorageKey, long SizeBytes, string ContentType, string Sha256);

public interface IFundPortfolioRawWorkbookStore
{
    Task<FundPortfolioStoredFile> StoreAsync(Guid reportId, string originalFileName, string contentType, Stream content, string sha256, CancellationToken cancellationToken);
}

public interface IFundPortfolioReportRepository
{
    Task<FundPortfolioReportStatusResult?> FindStatusAsync(Guid reportId, CancellationToken cancellationToken);
    Task<FundPortfolioReportIssuePage> FindIssuesAsync(Guid reportId, int page, int pageSize, FundExtractionIssueSeverity? severity, string? issueCode, CancellationToken cancellationToken);
    Task<(Guid ReportId, int SourceRevision)?> FindByHashAsync(string providerName, string fileSha256, CancellationToken cancellationToken);
    Task<int> GetNextRevisionAsync(Guid fundId, string providerName, DateOnly? periodEndDate, CancellationToken cancellationToken);
    Task<bool> SaveParsedReportAsync(InvestmentFund fund, IngestFundPortfolioWorkbookRequest request, FundPortfolioStoredFile storedFile, FundPortfolioWorkbookEnvelope envelope, int sourceRevision, Guid? supersedesReportId, CancellationToken cancellationToken);
    Task<Guid?> FindLatestReportIdAsync(Guid fundId, string providerName, DateOnly? periodEndDate, CancellationToken cancellationToken);
}

public interface IFundPortfolioSectionNormalizer
{
    Task NormalizeAsync(FundPortfolioWorkbookEnvelope envelope, CancellationToken cancellationToken);
}

public sealed record FundPortfolioIngestionTelemetry(
    Guid CorrelationId,
    FundResolutionStatus FundResolutionStatus,
    string FileSha256Prefix,
    string ParserProfileVersion,
    int SheetCount,
    int UnclassifiedSheetCount,
    int IssueCount,
    int ErrorCount,
    FundPortfolioParseStatus FinalStatus,
    TimeSpan Duration,
    int FormulaErrorCount = 0,
    int DateFailureCount = 0,
    int PartialParseCount = 0);

public interface IFundPortfolioIngestionTelemetrySink
{
    void Record(FundPortfolioIngestionTelemetry telemetry);
}

public static class FundPortfolioFundNamePolicy
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Fund name is required.", nameof(value));
        var chars = value.Trim().Normalize(System.Text.NormalizationForm.FormKC)
            .Where(c => !char.GetUnicodeCategory(c).Equals(System.Globalization.UnicodeCategory.Format)).ToArray();
        return string.Join(' ', new string(chars).Replace('\u200c', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

public static class FundPortfolioReportIdentityPolicy
{
    public static string BuildImportIdentity(string providerName, string fileSha256) => $"{providerName.Trim().ToUpperInvariant()}:{fileSha256.Trim().ToUpperInvariant()}";
    public static string BuildRevisionIdentity(Guid fundId, string providerName, DateOnly? periodEndDate, FundPortfolioReportType reportType, int sourceRevision) =>
        $"{fundId:N}:{providerName.Trim().ToUpperInvariant()}:{periodEndDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}:{reportType}:{sourceRevision}";
}

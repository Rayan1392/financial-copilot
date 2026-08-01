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
    FundPortfolioReportPeriod? KnownPeriod = null);

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
    DateTimeOffset ImportedAtUtc);

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

public interface IGetFundPortfolioReportStatusUseCase
{
    Task<FundPortfolioReportStatusResult?> ExecuteAsync(Guid reportId, CancellationToken cancellationToken);
}

public interface IGetFundPortfolioReportIssuesUseCase
{
    Task<IReadOnlyList<FundPortfolioReportIssueResult>> ExecuteAsync(Guid reportId, CancellationToken cancellationToken);
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
    Task<IReadOnlyList<FundPortfolioReportIssueResult>> FindIssuesAsync(Guid reportId, CancellationToken cancellationToken);
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
    TimeSpan Duration);

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

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundPortfolioSourceQuery(
    string ProviderName,
    DateTimeOffset? ModifiedAfterUtc = null,
    int MaximumItems = 100,
    string? ContinuationToken = null);

public sealed record FundPortfolioReportSourceDescriptor(
    string ProviderName,
    string StableSourceObjectId,
    string OriginalFileName,
    DateOnly? ObservedPeriodEnd,
    string? ObservedFundName,
    DateTimeOffset? LastModifiedUtc,
    string? Checksum,
    string DownloadToken);

public sealed record FundPortfolioSourcePage(
    IReadOnlyList<FundPortfolioReportSourceDescriptor> Items,
    string? ContinuationToken);

public sealed record FundPortfolioSourceDownload(
    Stream Content,
    string ContentType,
    long Length,
    string? Checksum);

public interface IFundPortfolioReportSource
{
    string ProviderName { get; }
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    Task<FundPortfolioSourcePage> DiscoverAsync(FundPortfolioSourceQuery query, CancellationToken cancellationToken);
    Task<FundPortfolioSourceDownload> DownloadAsync(FundPortfolioReportSourceDescriptor descriptor, CancellationToken cancellationToken);
}

public sealed record ManualFundPortfolioUpload(
    string OriginalFileName,
    string ContentType,
    byte[] Content,
    string? FundName = null,
    DateOnly? PeriodEnd = null,
    string? Checksum = null);

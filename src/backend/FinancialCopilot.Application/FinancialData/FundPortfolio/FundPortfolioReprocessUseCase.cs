using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed class ReprocessFundPortfolioReportUseCase(
    IFundPortfolioReportReprocessRepository reports,
    IFundPortfolioRawWorkbookReader rawFiles,
    IFundPortfolioWorkbookParser parser,
    IFundPortfolioMappingReviewRepository reviews,
    IEnumerable<IFundPortfolioSectionNormalizer> sectionNormalizers) : IReprocessFundPortfolioReportUseCase
{
    public async Task<FundPortfolioParseStatus?> ExecuteAsync(ReprocessFundPortfolioReportRequest request, CancellationToken cancellationToken)
    {
        var work = await reports.GetReprocessWorkAsync(request.ReportId, cancellationToken);
        if (work is null) return null;
        await using var workbook = await rawFiles.OpenAsync(work.RawStorageKey, cancellationToken);
        var envelope = await parser.ParseAsync(new(work.ReportId, work.FundId, work.ProviderName, work.OriginalFileName, work.FileSha256, request.ParserProfileVersion, workbook, work.Period), cancellationToken);
        await reports.ReplaceParsedEvidenceAsync(envelope, request.ParserProfileVersion, cancellationToken);
        foreach (var normalizer in sectionNormalizers)
            await normalizer.NormalizeAsync(envelope, cancellationToken);
        await reviews.CreateFromReportIssuesAsync(work.ReportId, cancellationToken);
        return envelope.Status;
    }
}

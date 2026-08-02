using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class EfCoreFundComparablePeriodReportReader(
    FinancialProviderDbContext dbContext) : IFundComparablePeriodReportReader
{
    public async Task<FundComparableReportCandidate?> GetAsync(
        Guid reportId,
        CancellationToken cancellationToken) =>
        await dbContext.FundPortfolioReports.AsNoTracking()
            .Where(report => report.Id == reportId)
            .Select(report => ToCandidate(report))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<FundComparableReportCandidate>> ListComparableCandidatesAsync(
        FundComparableReportIdentity identity,
        CancellationToken cancellationToken) =>
        await dbContext.FundPortfolioReports.AsNoTracking()
            .Where(report => report.FundId == identity.FundId &&
                report.ProviderName == identity.ProviderName &&
                report.ReportType == identity.ReportType)
            .Select(report => ToCandidate(report))
            .ToArrayAsync(cancellationToken);

    private static FundComparableReportCandidate ToCandidate(FundPortfolioReportRow report) =>
        new(
            report.Id,
            report.FundId,
            report.ProviderName,
            report.ReportType,
            report.PeriodEndDate,
            report.ParseStatus,
            report.SourceRevision,
            report.ImportedAtUtc,
            report.SupersedesReportId);
}

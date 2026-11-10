using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Explains why a company has no monthly data. A failed or slow read only loses the explanation, so it is logged
/// and reported as <see cref="MonthlyReportCoverageKind.Unknown"/> and the turn continues.
/// </summary>
internal sealed class NoavaranMonthlyReportCoverageReader(
    ICanonicalMonthlyCompanyResolver companies,
    FinancialIngestionDbContext dbContext,
    INoavaranMonthlyReportTypeResolver reportTypes,
    ILogger<NoavaranMonthlyReportCoverageReader> logger) : IMonthlyReportCoverageReader
{
    private const string ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName;
    private const string Route = "monthly_activity_trend";
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    public async Task<MonthlyReportCoverage> ReadAsync(string symbol, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        try
        {
            var coverage = await ReadCoreAsync(symbol, timeout.Token);
            logger.LogInformation(
                "Monthly report coverage read. Route={Route} CompanySymbol={CompanySymbol} Coverage={Coverage}",
                Route, symbol, coverage.Kind);
            return coverage;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Monthly report coverage read failed. Route={Route} CompanySymbol={CompanySymbol} Outcome={Outcome}",
                Route, symbol, "coverage_read_failed");
            return new MonthlyReportCoverage(MonthlyReportCoverageKind.Unknown);
        }
    }

    private async Task<MonthlyReportCoverage> ReadCoreAsync(string symbol, CancellationToken cancellationToken)
    {
        var resolution = await companies.ResolveAsync(symbol, cancellationToken);
        switch (resolution.Status)
        {
            case CompanyResolutionStatus.Ambiguous:
                return new MonthlyReportCoverage(MonthlyReportCoverageKind.CompanyAmbiguous);
            case CompanyResolutionStatus.UnsupportedProviderCompany:
                return new MonthlyReportCoverage(MonthlyReportCoverageKind.OutsideScope);
            case CompanyResolutionStatus.Resolved when resolution.Company is not null:
                break;
            default:
                return new MonthlyReportCoverage(MonthlyReportCoverageKind.Unknown);
        }

        var row = await dbContext.Companies.AsNoTracking()
            .FirstOrDefaultAsync(company => company.Id == resolution.Company.Id, cancellationToken);
        if (row is null)
            return new MonthlyReportCoverage(MonthlyReportCoverageKind.Unknown);
        if (!NoavaranCompanyScope.IsEligible(row, ProviderName))
            return new MonthlyReportCoverage(MonthlyReportCoverageKind.OutsideScope);

        return reportTypes.Resolve(row.ReportingType).Outcome switch
        {
            NoavaranMonthlyReportRouteOutcome.ProductSales or NoavaranMonthlyReportRouteOutcome.ServiceSales =>
                new MonthlyReportCoverage(MonthlyReportCoverageKind.HasRoute),
            NoavaranMonthlyReportRouteOutcome.Unsupported when NoavaranReportingTypeCategories.For(row.ReportingType) is { } label =>
                new MonthlyReportCoverage(MonthlyReportCoverageKind.NoRoute, label.Fa, label.En),
            _ => new MonthlyReportCoverage(MonthlyReportCoverageKind.Unknown)
        };
    }
}

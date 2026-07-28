using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// One-time historical fill for spec 075. Reads already-persisted Noavaran single-month
/// ProductSales reports and reuses <see cref="ICompanyProductRevenueMixCalculator"/> so the
/// historical path and the live ingestion path stay identical.
/// </summary>
public sealed class ProductRevenueMixBackfillService(
    FinancialIngestionDbContext dbContext,
    ICompanyProductRevenueMixCalculator calculator,
    TimeProvider timeProvider,
    ILogger<ProductRevenueMixBackfillService> logger) : IProductRevenueMixBackfillService
{
    private const string ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName;
    private static readonly PersianCalendar Calendar = new();
    private static readonly CompanyMetadata EmptyMetadata = new(null, null, null);

    public async Task<ProductRevenueMixBackfillResult> RunAsync(
        ProductRevenueMixBackfillRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();

        var reports = await dbContext.MonthlyReports
            .AsNoTracking()
            .Where(r => r.ProviderName == ProviderName
                     && r.ReportType == "ProductSales"
                     && (r.OutputType == null || r.OutputType == 0))
            .Select(r => new CandidateReport(
                r.Id,
                r.ExternalCompanyId,
                r.PeriodStart,
                r.WarningsJson))
            .ToListAsync(cancellationToken);

        if (reports.Count == 0)
        {
            return BuildResult(
                "NoCandidates",
                request.RequestedBy,
                companiesConsidered: 0,
                companyMonthsDiscovered: 0,
                companyMonthsProcessed: 0,
                companyMonthsSkippedNoSalesLineItems: 0,
                startedAt);
        }

        var companyLookup = await dbContext.Companies
            .AsNoTracking()
            .Where(c => c.ProviderName == ProviderName)
            .Select(c => new { c.ExternalCompanyId, c.Name, c.CompanySymbol })
            .ToDictionaryAsync(
                c => c.ExternalCompanyId,
                c => new CompanyMetadata(c.CompanySymbol, c.Name, null),
                StringComparer.Ordinal,
                cancellationToken);

        var reportIds = reports.Select(r => r.Id).ToArray();
        var reportIdsWithSalesLineItems = (await dbContext.MonthlyReportLineItems
                .AsNoTracking()
                .Where(li => reportIds.Contains(li.MonthlyReportId) && li.SalesAmount != null)
                .Select(li => li.MonthlyReportId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var discovered = reports
            .GroupBy(r => new { r.ExternalCompanyId, r.PeriodStart })
            .Select(group =>
            {
                var firstWithMetadata = group.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.WarningsJson)) ?? group.First();
                var evidenceMetadata = ParseMetadata(firstWithMetadata.WarningsJson);
                companyLookup.TryGetValue(group.Key.ExternalCompanyId, out var companyMetadata);
                var effectiveMetadata = new CompanyMetadata(
                    companyMetadata?.CompanySymbol ?? evidenceMetadata.CompanySymbol,
                    companyMetadata?.CompanyName ?? evidenceMetadata.CompanyName,
                    evidenceMetadata.FiscalEndDate);
                return new CandidateCompanyMonth(
                    group.Key.ExternalCompanyId,
                    group.Key.PeriodStart,
                    group.Select(r => r.Id).ToArray(),
                    effectiveMetadata);
            })
            .OrderBy(x => x.ExternalCompanyId, StringComparer.Ordinal)
            .ThenBy(x => x.PeriodStart)
            .ToArray();

        var processed = 0;
        var skippedNoSales = 0;

        foreach (var candidate in discovered)
        {
            if (!candidate.ReportIds.Any(reportIdsWithSalesLineItems.Contains))
            {
                skippedNoSales++;
                continue;
            }

            var month = ToShamsiMonth(candidate.PeriodStart);
            await calculator.RecalculateAsync(
                candidate.ExternalCompanyId,
                month.Year,
                (byte)month.Month,
                candidate.Metadata.CompanySymbol,
                candidate.Metadata.CompanyName,
                candidate.Metadata.FiscalEndDate,
                cancellationToken);
            processed++;
        }

        logger.LogInformation(
            "Product revenue mix backfill completed for {Companies} companies across {CompanyMonths} company-months. " +
            "Processed={Processed}, SkippedNoSalesLineItems={Skipped}, RequestedBy={RequestedBy}.",
            discovered.Select(x => x.ExternalCompanyId).Distinct(StringComparer.Ordinal).Count(),
            discovered.Length,
            processed,
            skippedNoSales,
            request.RequestedBy);

        return BuildResult(
            "Completed",
            request.RequestedBy,
            companiesConsidered: discovered.Select(x => x.ExternalCompanyId).Distinct(StringComparer.Ordinal).Count(),
            companyMonthsDiscovered: discovered.Length,
            companyMonthsProcessed: processed,
            companyMonthsSkippedNoSalesLineItems: skippedNoSales,
            startedAt);
    }

    private ProductRevenueMixBackfillResult BuildResult(
        string outcome,
        string requestedBy,
        int companiesConsidered,
        int companyMonthsDiscovered,
        int companyMonthsProcessed,
        int companyMonthsSkippedNoSalesLineItems,
        DateTimeOffset startedAt)
    {
        var finishedAt = timeProvider.GetUtcNow();
        return new ProductRevenueMixBackfillResult(
            outcome,
            requestedBy,
            companiesConsidered,
            companyMonthsDiscovered,
            companyMonthsProcessed,
            companyMonthsSkippedNoSalesLineItems,
            (finishedAt - startedAt).ToString("g", CultureInfo.InvariantCulture));
    }

    private static ShamsiMonth ToShamsiMonth(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return new ShamsiMonth(Calendar.GetYear(dateTime), Calendar.GetMonth(dateTime));
    }

    private static CompanyMetadata ParseMetadata(string? warningsJson)
    {
        if (string.IsNullOrWhiteSpace(warningsJson))
        {
            return EmptyMetadata;
        }

        try
        {
            using var document = JsonDocument.Parse(warningsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                return EmptyMetadata;
            }

            var first = document.RootElement[0];
            return new CompanyMetadata(
                TryGetString(first, "BourseSymbol"),
                TryGetString(first, "CompanyTitle"),
                TryGetString(first, "JalaliFiscalYearEnd"));
        }
        catch (JsonException)
        {
            return EmptyMetadata;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record CandidateReport(
        Guid Id,
        string ExternalCompanyId,
        DateOnly PeriodStart,
        string WarningsJson);

    private sealed record CandidateCompanyMonth(
        string ExternalCompanyId,
        DateOnly PeriodStart,
        Guid[] ReportIds,
        CompanyMetadata Metadata);

    private sealed record CompanyMetadata(
        string? CompanySymbol,
        string? CompanyName,
        string? FiscalEndDate);
}

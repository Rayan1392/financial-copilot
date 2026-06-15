using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesMonthlyReportNormalizer(
    FinancialIngestionDbContext dbContext,
    ICompanyResolverService companyResolver,
    ILogger<CyclicalWavesMonthlyReportNormalizer> logger) : IFinancialPayloadNormalizer
{
    public string ProviderName => "CyclicalWaves";

    public ProviderDataset Dataset => ProviderDataset.MonthlyProductionSales;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var response = JsonSerializer.Deserialize<CyclicalWavesTickerDetailResponse>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves ticker detail payload is invalid.");

        if (!response.Success)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves ticker detail response indicates failure.");
        }

        var data = response.Data;
        var linkage = await CyclicalWavesCompanyLinkageResolver.ResolveAsync(
            dbContext,
            data.Ticker,
            data.Enticker,
            cancellationToken);

        // Resolve Companies.Id via the normalized symbol — sets CompanyId FK on each row (spec 067).
        var resolvedCompany = await companyResolver.ResolveBySymbolAsync(data.Ticker, cancellationToken);
        if (resolvedCompany is null)
        {
            logger.LogWarning(
                "[CyclicalWaves] CompanyId unresolved for ticker={Ticker} enticker={EnTicker}",
                data.Ticker,
                data.Enticker);
        }

        var asOf = payload.ReceivedAt;
        var warnings = Warnings(linkage is null, data.Ticker);
        var vendorMonthDate = ParseVendorDate(data.LastMonthSaleDate);

        var months = new[]
        {
            (
                ReportId: $"{data.Id}:M0",
                Period: CyclicalWavesRelativePeriodResolver.ResolveMonth(asOf, CyclicalWavesRelativePeriodResolver.MonthOffset.M0),
                Sale: data.LastMonthSale,
                VendorPeriodDate: vendorMonthDate,
                IsM0: true,
                AvgSale: data.Average12MonthSale
            ),
            (
                ReportId: $"{data.Id}:M1",
                Period: CyclicalWavesRelativePeriodResolver.ResolveMonth(asOf, CyclicalWavesRelativePeriodResolver.MonthOffset.M1),
                Sale: data.PenultimateMonthSale,
                VendorPeriodDate: (DateOnly?)null,
                IsM0: false,
                AvgSale: (decimal?)null
            ),
            (
                ReportId: $"{data.Id}:M12",
                Period: CyclicalWavesRelativePeriodResolver.ResolveMonth(asOf, CyclicalWavesRelativePeriodResolver.MonthOffset.M12),
                Sale: data.LastYearSameMonthSale,
                VendorPeriodDate: (DateOnly?)null,
                IsM0: false,
                AvgSale: (decimal?)null
            )
        };

        foreach (var m in months)
        {
            var report = await dbContext.MonthlyReports.SingleOrDefaultAsync(
                row => row.ProviderName == ProviderName && row.ExternalReportId == m.ReportId,
                cancellationToken);

            if (report is null)
            {
                report = new NormalizedMonthlyReportRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = ProviderName,
                    ExternalReportId = m.ReportId
                };
                dbContext.MonthlyReports.Add(report);
            }

            report.ExternalCompanyId = linkage?.ExternalCompanyId ?? data.Id;
            report.CompanyId = resolvedCompany?.Id;
            report.PeriodStart = m.Period.Start;
            report.PeriodEnd = m.Period.End;
            report.SourcePayloadChecksum = payload.Checksum;
            report.LastSynchronizedAt = payload.ReceivedAt;
            report.WarningsJson = warnings;
            report.VendorPeriodDate = m.VendorPeriodDate;

            await dbContext.SaveChangesAsync(cancellationToken);

            var lineItem = await dbContext.MonthlyReportLineItems.SingleOrDefaultAsync(
                row => row.MonthlyReportId == report.Id && row.ProductCode == "REVENUE",
                cancellationToken);

            if (lineItem is null)
            {
                lineItem = new NormalizedMonthlyReportLineItemRow
                {
                    Id = Guid.NewGuid(),
                    MonthlyReportId = report.Id,
                    ProductCode = "REVENUE"
                };
                dbContext.MonthlyReportLineItems.Add(lineItem);
            }

            lineItem.SalesAmount = m.Sale;

            // Pre-computed 12-month rolling average of monthly revenue, supplied by CyclicalWaves.
            // Only meaningful for the most-recent period (M0); null for M1/M12.
            if (m.IsM0)
            {
                var avgLineItem = await dbContext.MonthlyReportLineItems.SingleOrDefaultAsync(
                    row => row.MonthlyReportId == report.Id && row.ProductCode == "AVG_12M",
                    cancellationToken);

                if (avgLineItem is null)
                {
                    avgLineItem = new NormalizedMonthlyReportLineItemRow
                    {
                        Id = Guid.NewGuid(),
                        MonthlyReportId = report.Id,
                        ProductCode = "AVG_12M"
                    };
                    dbContext.MonthlyReportLineItems.Add(avgLineItem);
                }

                avgLineItem.SalesAmount = m.AvgSale;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new NormalizationOutcome(months.Length, linkage?.ExternalCompanyId);
    }

    private static string Warnings(bool missingLinkage, string? ticker)
    {
        object[] warnings = missingLinkage
            ?
            [
                new
                {
                    Code = nameof(FinancialDataWarningCode.StaleData),
                    Message = "Monthly period dates are estimated from the request timestamp using Gregorian calendar approximations."
                },
                new
                {
                    Code = nameof(FinancialDataWarningCode.MissingData),
                    Message = $"CyclicalWaves ticker '{ticker}' could not be linked to an existing NADPCO company catalog row."
                }
            ]
            :
            [
                new
                {
                    Code = nameof(FinancialDataWarningCode.StaleData),
                    Message = "Monthly period dates are estimated from the request timestamp using Gregorian calendar approximations."
                }
            ];

        return JsonSerializer.Serialize(warnings, JsonOptions);
    }

    private static DateOnly? ParseVendorDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesMonthlyReportNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => "CyclicalWaves";

    public ProviderDataset Dataset => ProviderDataset.MonthlyProductionSales;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
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
        var asOf = payload.ReceivedAt;
        var staleWarnings = StaleDataWarnings();

        var months = new[]
        {
            (
                ReportId: $"{data.Id}:M0",
                Period: CyclicalWavesRelativePeriodResolver.ResolveMonth(asOf, CyclicalWavesRelativePeriodResolver.MonthOffset.M0),
                Sale: data.LastMonthSale
            ),
            (
                ReportId: $"{data.Id}:M1",
                Period: CyclicalWavesRelativePeriodResolver.ResolveMonth(asOf, CyclicalWavesRelativePeriodResolver.MonthOffset.M1),
                Sale: data.PenultimateMonthSale
            ),
            (
                ReportId: $"{data.Id}:M12",
                Period: CyclicalWavesRelativePeriodResolver.ResolveMonth(asOf, CyclicalWavesRelativePeriodResolver.MonthOffset.M12),
                Sale: data.LastYearSameMonthSale
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

            report.ExternalCompanyId = data.Id;
            report.PeriodStart = m.Period.Start;
            report.PeriodEnd = m.Period.End;
            report.SourcePayloadChecksum = payload.Checksum;
            report.LastSynchronizedAt = payload.ReceivedAt;
            report.WarningsJson = staleWarnings;

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
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return months.Length;
    }

    private static string StaleDataWarnings() =>
        JsonSerializer.Serialize(
            new[] { new { Code = nameof(FinancialDataWarningCode.StaleData), Message = "Monthly period dates are estimated from the request timestamp using Gregorian calendar approximations." } },
            JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

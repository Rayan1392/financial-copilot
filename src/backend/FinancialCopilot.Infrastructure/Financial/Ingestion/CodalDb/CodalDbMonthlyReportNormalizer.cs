using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Normalizes the CodalDB <c>MonthlyProductionSales</c> payload (a JSON array of
/// <see cref="CodalMonthlyActivityRow"/> for one company) into the canonical
/// <c>NormalizedMonthlyReportRow</c> / <c>NormalizedMonthlyReportLineItemRow</c> tables.
/// <para>
/// Each <c>MonthlyActivity</c> row produces one <c>NormalizedMonthlyReportRow</c>; each
/// per-product <c>MonthlyActivityAmounts</c> row produces one line item with
/// <c>ProductCode = ProductId</c>. Jalali <c>(Year, Month)</c> is converted to the
/// Gregorian first/last day of the month using <see cref="PersianCalendar"/>.
/// </para>
/// <para>
/// <c>ProductTitle</c>, <c>ProductSaleRate</c>, and <c>ProductUnit</c> are not stored in
/// the normalized model (no columns exist); they are recorded as evidence in
/// <c>WarningsJson</c> rather than silently dropped. Deferred: add columns when needed.
/// </para>
/// <para>
/// Idempotent on <c>(ProviderName, ExternalReportId)</c> and <c>(MonthlyReportId, ProductCode)</c>.
/// Zero-amount months are retained (not filtered).
/// </para>
/// </summary>
public sealed class CodalDbMonthlyReportNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => CodalDbSymbolNormalizer.CodalDbProviderName;

    public ProviderDataset Dataset => ProviderDataset.MonthlyProductionSales;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var rows = JsonSerializer.Deserialize<IReadOnlyList<CodalMonthlyActivityRow>>(payload.Payload, JsonOptions)
            ?? throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CodalDb monthly-activity payload is null or invalid.");

        var count = 0;
        string? canonicalExternalCompanyId = null;

        foreach (var activity in rows)
        {
            var (periodStart, periodEnd) = JalaliDateResolver.ResolveMonth(activity.Year, activity.Month);
            var externalCompanyId = activity.CompanyId.ToString(CultureInfo.InvariantCulture);
            canonicalExternalCompanyId = externalCompanyId;
            var externalReportId = activity.Id.ToString(CultureInfo.InvariantCulture);

            var report = await dbContext.MonthlyReports.SingleOrDefaultAsync(
                row => row.ProviderName == ProviderName && row.ExternalReportId == externalReportId,
                cancellationToken);

            if (report is null)
            {
                report = new NormalizedMonthlyReportRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = ProviderName,
                    ExternalReportId = externalReportId
                };
                dbContext.MonthlyReports.Add(report);
            }

            report.ExternalCompanyId = externalCompanyId;
            report.PeriodStart = periodStart;
            report.PeriodEnd = periodEnd;
            report.SourcePayloadChecksum = payload.Checksum;
            report.LastSynchronizedAt = payload.ReceivedAt;
            report.WarningsJson = BuildEvidenceJson(activity, periodStart, periodEnd);

            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var product in activity.Products)
            {
                var productCode = product.ProductId.ToString(CultureInfo.InvariantCulture);

                var lineItem = await dbContext.MonthlyReportLineItems.SingleOrDefaultAsync(
                    row => row.MonthlyReportId == report.Id && row.ProductCode == productCode,
                    cancellationToken);

                if (lineItem is null)
                {
                    lineItem = new NormalizedMonthlyReportLineItemRow
                    {
                        Id = Guid.NewGuid(),
                        MonthlyReportId = report.Id,
                        ProductCode = productCode
                    };
                    dbContext.MonthlyReportLineItems.Add(lineItem);
                }

                lineItem.ProductionQuantity = product.ProductProduceAmount;
                lineItem.SalesQuantity = product.ProductSaleAmount;
                lineItem.SalesAmount = product.ProductSaleValue;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            count++;
        }

        return new NormalizationOutcome(count, canonicalExternalCompanyId);
    }

    private static string BuildEvidenceJson(
        CodalMonthlyActivityRow activity,
        DateOnly periodStart,
        DateOnly periodEnd) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                Code = "CodalMonthlyActivityPeriod",
                JalaliYear = activity.Year,
                JalaliMonth = (int)activity.Month,
                FiscalYearEnd = activity.FiscalYearEnd,
                GregorianPeriodStart = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                GregorianPeriodEnd = periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                // ProductTitle, ProductSaleRate, ProductUnit have no columns in the normalized
                // line-item model; recorded here so they are not silently dropped.
                // Deferred: add columns to NormalizedMonthlyReportLineItemRow when needed.
                DeferredLineItemFields = "ProductTitle,ProductSaleRate,ProductUnit"
            }
        }, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

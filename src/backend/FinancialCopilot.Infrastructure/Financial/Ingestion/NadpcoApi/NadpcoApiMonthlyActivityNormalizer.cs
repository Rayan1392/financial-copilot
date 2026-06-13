using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoApiMonthlyActivityNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => NadpcoApiCompanyNormalizer.NadpcoApiProviderName;

    public ProviderDataset Dataset => ProviderDataset.MonthlyProductionSales;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var (productSalesSlots, serviceSalesJson) = DeserializeEnvelope(payload.Payload);

        var items = new List<NadpcoApiMonthlyActivityItem>();
        foreach (var (json, outputTypeHint) in productSalesSlots)
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                items.AddRange(ReadProductSales(json, outputTypeHint));
            }
        }

        items.AddRange(ReadServiceSales(serviceSalesJson ?? "[]"));

        var groupedReports = items
            .GroupBy(item => new
            {
                item.SourceKind,
                item.ExternalCompanyId,
                item.ExternalReportId,
                item.JalaliYear,
                item.JalaliMonth
            })
            .ToArray();

        foreach (var group in groupedReports)
        {
            var first = group.First();
            var (periodStart, periodEnd) = JalaliDateResolver.ResolveMonth(first.JalaliYear, first.JalaliMonth);

            var report = await dbContext.MonthlyReports.SingleOrDefaultAsync(
                row => row.ProviderName == ProviderName && row.ExternalReportId == first.ExternalReportId,
                cancellationToken);

            if (report is null)
            {
                report = new NormalizedMonthlyReportRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = ProviderName,
                    ExternalReportId = first.ExternalReportId
                };
                dbContext.MonthlyReports.Add(report);
            }

            report.ExternalCompanyId = first.ExternalCompanyId;
            report.OutputType = first.OutputType;
            report.PeriodStart = periodStart;
            report.PeriodEnd = periodEnd;
            report.ReportType = first.SourceKind;
            report.SourcePayloadChecksum = payload.Checksum;
            report.LastSynchronizedAt = payload.ReceivedAt;
            report.WarningsJson = BuildEvidenceJson(group.ToArray(), periodStart, periodEnd);

            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var item in group)
            {
                var lineItem = await dbContext.MonthlyReportLineItems.SingleOrDefaultAsync(
                    row => row.MonthlyReportId == report.Id && row.ProductCode == item.LineItemCode,
                    cancellationToken);

                if (lineItem is null)
                {
                    lineItem = new NormalizedMonthlyReportLineItemRow
                    {
                        Id = Guid.NewGuid(),
                        MonthlyReportId = report.Id,
                        ProductCode = item.LineItemCode
                    };
                    dbContext.MonthlyReportLineItems.Add(lineItem);
                }

                lineItem.ProductionQuantity = item.ProductionQuantity;
                lineItem.SalesQuantity = item.SalesQuantity;
                lineItem.SalesAmount = item.SalesAmount;
                lineItem.Title = item.Title;
                lineItem.Unit = item.Unit;
                lineItem.SalesRate = item.SalesRate;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return groupedReports.Length;
    }

    // Deserializes the envelope payload. Tries the new 6-field shape (spec 059) first; falls back to
    // the legacy 2-field shape for payloads stored before the spec-059 migration. Legacy ProductSales
    // content is returned as a single slot with a null output-type hint (backward compat: OutputType
    // will be taken from the record itself, or remain null for truly old payloads).
    private static (IReadOnlyList<(string? Json, int? OutputTypeHint)> ProductSlots, string? ServiceSalesJson)
        DeserializeEnvelope(string rawPayload)
    {
        var envelope = JsonSerializer.Deserialize<NadpcoMonthlyActivityEnvelope>(rawPayload, JsonOptions);
        if (envelope is not null &&
            (envelope.ProductSalesType0 ?? envelope.ProductSalesType1 ?? envelope.ProductSalesType2 ??
             envelope.ProductSalesType3 ?? envelope.ProductSalesType4) is not null)
        {
            var slots = new (string?, int?)[]
            {
                (envelope.ProductSalesType0, 0),
                (envelope.ProductSalesType1, 1),
                (envelope.ProductSalesType2, 2),
                (envelope.ProductSalesType3, 3),
                (envelope.ProductSalesType4, 4),
            };
            return (slots, envelope.ServiceSales);
        }

        // Legacy envelope: fall back to the old 2-field shape.
        var legacy = JsonSerializer.Deserialize<NadpcoMonthlyActivityLegacyEnvelope>(rawPayload, JsonOptions);
        if (legacy is null)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "NADPCO monthly-activity envelope is invalid.");
        }

        return ([(legacy.ProductSales, null)], legacy.ServiceSales);
    }

    private static IReadOnlyList<NadpcoApiMonthlyActivityItem> ReadProductSales(string json, int? outputTypeHint)
    {
        IReadOnlyList<NadpcoApiProductSalesRecord> records;
        try
        {
            records = JsonSerializer.Deserialize<IReadOnlyList<NadpcoApiProductSalesRecord>>(json, JsonOptions) ??
                throw new JsonException("Payload was null.");
        }
        catch (JsonException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "NADPCO product-sales monthly-activity payload is invalid.",
                exception);
        }

        // Live v2 shape (verified 2026-06-10): one record per company with the per-product facts
        // (month, year, quantities, rate, value) nested under "productSales". Legacy flat records
        // (no nested list) are treated as a single item. Company identity fields are merged from
        // the parent when the nested item does not carry them.
        return records.SelectMany(record =>
        {
            var items = record.ProductSales is { Count: > 0 }
                ? record.ProductSales
                : [record];
            return items.Select((item, index) => BuildProductItem(record, item, index, outputTypeHint));
        }).ToArray();
    }

    private static NadpcoApiMonthlyActivityItem BuildProductItem(
        NadpcoApiProductSalesRecord parent,
        NadpcoApiProductSalesRecord item,
        int index,
        int? outputTypeHint = null)
    {
        var companyId = RequireCompanyId(item.GetCompanyId() ?? parent.GetCompanyId(), "product-sales");
        var year = RequireYear(item.Year ?? parent.Year, "product-sales");
        var month = RequireMonth(item.Month ?? parent.Month, "product-sales");
        var title = item.GetProductTitle() ?? parent.GetProductTitle();
        var unit = item.GetProductUnit() ?? parent.GetProductUnit();
        var categoryId = item.CategoryID ?? parent.CategoryID;
        var category = item.CategoryTitle ?? parent.CategoryTitle ??
            categoryId?.ToString(CultureInfo.InvariantCulture);
        // Record-level outputType takes precedence; fall back to the envelope-slot hint (which is
        // authoritative for the new multi-type envelope) so legacy payloads still normalize.
        var outputType = item.GetOutputType() ?? parent.GetOutputType() ?? outputTypeHint;
        var vendorCode = item.GetProductCode();
        var lineItemCode = BuildLineItemCode("PRODUCT", vendorCode, title, category, unit, index);
        var externalReportId = BuildExternalReportId(
            "ProductSales",
            item.GetActivityId() ?? parent.GetActivityId(),
            companyId,
            year,
            month,
            outputType,
            categoryId);

        return new NadpcoApiMonthlyActivityItem(
            "ProductSales",
            companyId.ToString(CultureInfo.InvariantCulture),
            externalReportId,
            year,
            month,
            lineItemCode,
            title,
            unit,
            item.GetProductionQuantity(),
            item.GetSalesQuantity(),
            item.GetSalesRate(),
            item.GetSalesValue(),
            outputType,
            item.OutputTypeTitle ?? parent.OutputTypeTitle,
            categoryId,
            item.CategoryTitle ?? parent.CategoryTitle,
            item.GetBourseSymbol() ?? parent.GetBourseSymbol(),
            item.GetCompanyTitle() ?? parent.GetCompanyTitle(),
            item.IndustryID ?? parent.IndustryID,
            item.IndustryTitle ?? parent.IndustryTitle,
            item.GetTseCode() ?? parent.GetTseCode(),
            item.FiscalYearEnd ?? parent.FiscalYearEnd,
            item.JalaliFiscalYearEnd ?? parent.JalaliFiscalYearEnd,
            item.PublishDate ?? parent.PublishDate,
            item.JalaliPublishDate ?? parent.JalaliPublishDate,
            VendorLineItemId: vendorCode,
            MissingVendorLineItemId: string.IsNullOrWhiteSpace(vendorCode));
    }

    private static IReadOnlyList<NadpcoApiMonthlyActivityItem> ReadServiceSales(string json)
    {
        IReadOnlyList<NadpcoApiServiceSalesRecord> records;
        try
        {
            records = JsonSerializer.Deserialize<IReadOnlyList<NadpcoApiServiceSalesRecord>>(json, JsonOptions) ??
                throw new JsonException("Payload was null.");
        }
        catch (JsonException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "NADPCO service-sales monthly-activity payload is invalid.",
                exception);
        }

        return records.Select((record, index) =>
        {
            var companyId = RequireCompanyId(record.GetCompanyId(), "service-sales");
            var year = RequireYear(record.Year, "service-sales");
            var month = RequireMonth(record.Month, "service-sales");
            var title = record.GetServiceTitle();
            var unit = record.GetServiceUnit();
            var category = record.CategoryTitle ?? record.CategoryID?.ToString(CultureInfo.InvariantCulture);
            var vendorCode = record.GetServiceCode();
            var lineItemCode = BuildLineItemCode("SERVICE", vendorCode, title, category, unit, index);
            var externalReportId = BuildExternalReportId(
                "ServiceSales",
                record.GetActivityId(),
                companyId,
                year,
                month,
                OutputType: null,
                record.CategoryID);

            return new NadpcoApiMonthlyActivityItem(
                "ServiceSales",
                companyId.ToString(CultureInfo.InvariantCulture),
                externalReportId,
                year,
                month,
                lineItemCode,
                title,
                unit,
                ProductionQuantity: null,
                record.GetSalesQuantity(),
                record.GetSalesRate(),
                record.GetSalesValue(),
                OutputType: null,
                OutputTypeTitle: null,
                record.CategoryID,
                record.CategoryTitle,
                record.GetBourseSymbol(),
                record.ComTitle,
                record.IndustryID,
                record.IndustryTitle,
                record.GetTseCode(),
                record.FiscalYearEnd,
                record.JalaliFiscalYearEnd,
                record.PublishDate,
                record.JalaliPublishDate,
                VendorLineItemId: vendorCode,
                MissingVendorLineItemId: string.IsNullOrWhiteSpace(vendorCode));
        }).ToArray();
    }

    private static string BuildExternalReportId(
        string sourceKind,
        long? activityId,
        int companyId,
        int year,
        byte month,
        int? OutputType,
        int? categoryId)
    {
        var outputPart = OutputType?.ToString(CultureInfo.InvariantCulture) ?? "none";

        if (activityId is not null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{sourceKind}:{activityId.Value}:output-{outputPart}");
        }

        var categoryPart = categoryId?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sourceKind}:{companyId}:{year:D4}-{month:D2}:output-{outputPart}:category-{categoryPart}");
    }

    private static string BuildLineItemCode(
        string prefix,
        string? vendorCode,
        string? title,
        string? category,
        string? unit,
        int index)
    {
        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            return $"{prefix}:{vendorCode.Trim()}";
        }

        var naturalKey = string.Join("|", [title, category, unit, index.ToString(CultureInfo.InvariantCulture)]);
        return $"{prefix}:NATURAL:{HashShort(naturalKey)}";
    }

    private static int RequireCompanyId(int? value, string sourceKind) =>
        value is > 0
            ? value.Value
            : throw InvalidRequiredField(sourceKind, "company id");

    private static int RequireYear(int? value, string sourceKind) =>
        value is > 0
            ? value.Value
            : throw InvalidRequiredField(sourceKind, "year");

    private static byte RequireMonth(byte? value, string sourceKind) =>
        value is >= 1 and <= 12
            ? value.Value
            : throw InvalidRequiredField(sourceKind, "month");

    private static FinancialProviderException InvalidRequiredField(string sourceKind, string field) =>
        new(
            FinancialProviderErrorCode.InvalidResponse,
            $"NADPCO monthly {sourceKind} payload is missing a valid {field}.");

    private static string BuildEvidenceJson(
        IReadOnlyCollection<NadpcoApiMonthlyActivityItem> items,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var first = items.First();
        return JsonSerializer.Serialize(new[]
        {
            new
            {
                Code = "NadpcoApiMonthlyActivity",
                first.SourceKind,
                first.ExternalCompanyId,
                first.JalaliYear,
                JalaliMonth = (int)first.JalaliMonth,
                GregorianPeriodStart = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                GregorianPeriodEnd = periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                first.BourseSymbol,
                first.CompanyTitle,
                first.IndustryID,
                first.IndustryTitle,
                first.TseCode,
                first.FiscalYearEnd,
                first.JalaliFiscalYearEnd,
                first.PublishDate,
                first.JalaliPublishDate,
                SchemaAudit = "No migration required: service sales map to SalesQuantity/SalesAmount; product or service title, unit, rate, output type, category, and publication fields are preserved as evidence.",
                LineItems = items.Select(item => new
                {
                    item.LineItemCode,
                    item.VendorLineItemId,
                    item.MissingVendorLineItemId,
                    item.Title,
                    item.Unit,
                    item.SalesRate,
                    item.OutputType,
                    item.OutputTypeTitle,
                    item.CategoryID,
                    item.CategoryTitle,
                    NaturalKeyNote = item.MissingVendorLineItemId
                        ? "Line item code is a deterministic natural key, not a fabricated vendor product/service id."
                        : null
                }).ToArray()
            }
        }, JsonOptions);
    }

    private static string HashShort(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];

    private sealed record NadpcoApiMonthlyActivityItem(
        string SourceKind,
        string ExternalCompanyId,
        string ExternalReportId,
        int JalaliYear,
        byte JalaliMonth,
        string LineItemCode,
        string? Title,
        string? Unit,
        decimal? ProductionQuantity,
        decimal? SalesQuantity,
        decimal? SalesRate,
        decimal? SalesAmount,
        int? OutputType,
        string? OutputTypeTitle,
        int? CategoryID,
        string? CategoryTitle,
        string? BourseSymbol,
        string? CompanyTitle,
        int? IndustryID,
        string? IndustryTitle,
        string? TseCode,
        string? FiscalYearEnd,
        string? JalaliFiscalYearEnd,
        string? PublishDate,
        string? JalaliPublishDate,
        string? VendorLineItemId,
        bool MissingVendorLineItemId);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

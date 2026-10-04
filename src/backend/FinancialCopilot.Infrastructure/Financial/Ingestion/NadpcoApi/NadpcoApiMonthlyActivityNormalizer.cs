using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoApiMonthlyActivityNormalizer(
    FinancialIngestionDbContext dbContext,
    ICompanyProductRevenueMixCalculator revenueMixCalculator,
    ICompanyMonthlyActivityTrendSnapshotCalculator trendSnapshotCalculator,
    IRecalculateMonthlySalesQualityRankingUseCase monthlySalesQualityRankingUseCase,
    ILogger<NadpcoApiMonthlyActivityNormalizer> logger) : IFinancialPayloadNormalizer
{
    public string ProviderName => NadpcoApiCompanyNormalizer.NadpcoApiProviderName;

    public ProviderDataset Dataset => ProviderDataset.MonthlyProductionSales;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
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
            var normalizedItems = CollapseDuplicateLineItems(group).ToArray();
            var first = normalizedItems[0];
            var (periodStart, periodEnd) = JalaliDateResolver.ResolveMonth(first.JalaliYear, first.JalaliMonth);

            var logicalReportKey = BuildLogicalReportKey(
                first.ExternalCompanyId, periodStart, periodEnd, first.SourceKind, first.OutputType);
            var revisionFingerprint = payload.Checksum;
            var existingCandidate = await dbContext.MonthlyReports.SingleOrDefaultAsync(
                row => row.ProviderName == ProviderName &&
                       row.ExternalReportId == first.ExternalReportId &&
                       row.RevisionFingerprint == revisionFingerprint,
                cancellationToken);

            // The same accepted revision may be replayed by a scheduled sync.  A replay is a
            // no-op, including its line items: this keeps row ids/provenance stable and prevents
            // multiplicity from growing on each fetch.
            if (existingCandidate is not null)
            {
                continue;
            }

            var report = new NormalizedMonthlyReportRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalReportId = first.ExternalReportId,
                ExternalCompanyId = first.ExternalCompanyId,
                OutputType = first.OutputType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                ReportType = first.SourceKind,
                LogicalReportKey = logicalReportKey,
                RevisionFingerprint = revisionFingerprint,
                SourcePayloadChecksum = payload.Checksum,
                LastSynchronizedAt = payload.ReceivedAt,
                ProviderPublishedAtUtc = first.ProviderPublishedAtUtc,
                PublishedAt = first.ProviderPublishedAtUtc is { } published
                    ? DateOnly.FromDateTime(published.UtcDateTime)
                    : null,
                WarningsJson = BuildEvidenceJson(normalizedItems, periodStart, periodEnd),
                IsAccepted = true,
                RevisionStatus = "Accepted"
            };

            var current = await dbContext.MonthlyReports
                .Where(row => row.LogicalReportKey == logicalReportKey && row.IsAccepted)
                .OrderByDescending(row => row.ProviderPublishedAtUtc)
                .ThenByDescending(row => row.RevisionFingerprint)
                .FirstOrDefaultAsync(cancellationToken);

            if (current is not null)
            {
                var comparison = CompareRevisionEvidence(report, current);
                if (comparison < 0)
                {
                    report.IsAccepted = false;
                    report.RevisionStatus = "RejectedOlder";
                }
                else if (comparison == 0)
                {
                    // Equal or missing provider evidence is not enough to infer a correction.
                    // Keep the current pointer and retain the candidate for review; receipt time
                    // and checksum ordering must never turn an unknown revision into last-write-wins.
                    report.IsAccepted = false;
                    report.RevisionStatus = "Pending";
                }
                else
                {
                    current.IsAccepted = false;
                    current.RevisionStatus = "Superseded";
                }
            }

            dbContext.MonthlyReports.Add(report);

            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var item in normalizedItems)
            {
                var lineItem = new NormalizedMonthlyReportLineItemRow
                {
                    Id = Guid.NewGuid(),
                    MonthlyReportId = report.Id,
                    ProductCode = item.LineItemCode,
                    SourceRowKey = item.SourceRowKey,
                    SourceRowFingerprint = item.SourceRowFingerprint ?? string.Empty,
                    SourceMultiplicity = item.SourceMultiplicity,
                    ProviderProductCode = item.ProviderProductCode,
                    ProviderProductId = item.ProviderProductId,
                    SourcePayloadChecksum = payload.Checksum,
                    ProductionQuantity = item.ProductionQuantity,
                    SalesQuantity = item.SalesQuantity,
                    SalesAmount = item.SalesAmount,
                    Title = item.Title,
                    Unit = item.Unit,
                    SalesRate = item.SalesRate
                };
                dbContext.MonthlyReportLineItems.Add(lineItem);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // ServiceSales is a monthly source equivalent for trend purposes. Group by company/month
        // so a historical payload containing both sources recalculates once; the calculator applies
        // ProductSales-over-ServiceSales precedence from persisted rows.
        var monthlyTrendGroups = groupedReports
            .Where(g =>
                (g.Key.SourceKind == "ProductSales" && g.First().OutputType is null or 0) ||
                g.Key.SourceKind == "ServiceSales")
            .GroupBy(g => new
            {
                g.Key.ExternalCompanyId,
                g.Key.JalaliYear,
                g.Key.JalaliMonth
            })
            .Select(g => g.First())
            .ToArray();

        foreach (var group in monthlyTrendGroups)
        {
            var first = group.First();
            await revenueMixCalculator.RecalculateAsync(
                first.ExternalCompanyId,
                first.JalaliYear,
                first.JalaliMonth,
                first.BourseSymbol,
                first.CompanyTitle,
                first.JalaliFiscalYearEnd,
                cancellationToken);
        }

        // Recalculate the trend snapshot for every monthly trend source, including ServiceSales-only
        // periods (spec 076). The calculator applies ProductSales-over-ServiceSales precedence.
        foreach (var group in monthlyTrendGroups)
        {
            var first = group.First();
            await trendSnapshotCalculator.RecalculateAsync(
                first.ExternalCompanyId,
                first.JalaliYear,
                first.JalaliMonth,
                first.BourseSymbol,
                first.CompanyTitle,
                first.JalaliFiscalYearEnd,
                cancellationToken);
        }

        var affectedPeriods = monthlyTrendGroups
            .Select(group => new { group.Key.JalaliYear, group.Key.JalaliMonth })
            .Distinct()
            .ToArray();

        foreach (var period in affectedPeriods)
        {
            try
            {
                await monthlySalesQualityRankingUseCase.ExecuteAsync(
                    new RecalculateMonthlySalesQualityRankingRequest(period.JalaliYear, period.JalaliMonth),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Monthly sales quality ranking recalculation failed for {ReportYear}/{ReportMonth:00} after NADPCO monthly activity ingestion.",
                    period.JalaliYear,
                    period.JalaliMonth);
            }
        }

        // ExternalCompanyId varies per report group (one company per request); use the first.
        var canonicalId = groupedReports.Length > 0 ? groupedReports[0].Key.ExternalCompanyId : null;
        return new NormalizationOutcome(groupedReports.Length, canonicalId);
    }

    private static IReadOnlyList<NadpcoApiMonthlyActivityItem> CollapseDuplicateLineItems(
        IEnumerable<NadpcoApiMonthlyActivityItem> items)
    {
        var materialized = items
            .Select(item => item with { SourceRowFingerprint = BuildSourceRowFingerprint(item) })
            .ToArray();

        // A provider row id is authoritative for duplicate detection.  Without one, preserve
        // every occurrence: identical economic rows may be legitimate multiplicity.  Their
        // canonical ordinal is report-local evidence only and is never a cross-period ProductKey.
        var withProviderIdentity = materialized
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceRowKey))
            .GroupBy(item => item.SourceRowKey!, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.SourceRowFingerprint, StringComparer.Ordinal).First() with
            {
                SourceMultiplicity = group.Count()
            })
            .ToArray();
        var withoutProviderIdentity = materialized
            .Where(item => string.IsNullOrWhiteSpace(item.SourceRowKey))
            .GroupBy(item => item.SourceRowFingerprint, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.LineItemCode, StringComparer.Ordinal).First() with
            {
                SourceMultiplicity = group.Count()
            })
            .OrderBy(item => item.SourceRowFingerprint, StringComparer.Ordinal)
            .ToArray();

        var result = withProviderIdentity.Concat(withoutProviderIdentity).ToArray();
        var occurrenceByFingerprint = new Dictionary<string, int>(StringComparer.Ordinal);
        return result.Select(item =>
        {
            var fingerprint = item.SourceRowFingerprint ?? string.Empty;
            occurrenceByFingerprint.TryGetValue(fingerprint, out var occurrence);
            occurrence++;
            occurrenceByFingerprint[fingerprint] = occurrence;
            return item with
            {
                SourceMultiplicity = Math.Max(1, item.SourceMultiplicity),
                SourceRowKey = string.IsNullOrWhiteSpace(item.SourceRowKey)
                    ? $"fingerprint:{fingerprint}:occurrence:{occurrence}"
                    : item.SourceRowKey
            };
        }).OrderBy(item => item.SourceRowFingerprint, StringComparer.Ordinal).ThenBy(item => item.SourceRowKey, StringComparer.Ordinal).ToArray();
    }

    private static string BuildLogicalReportKey(string companyId, DateOnly start, DateOnly end, string reportType, int? outputType) =>
        string.Create(CultureInfo.InvariantCulture, $"{NadpcoApiCompanyNormalizer.NadpcoApiProviderName}|{companyId}|{start:yyyy-MM-dd}|{end:yyyy-MM-dd}|{reportType}|{outputType?.ToString() ?? "null"}");

    private static int CompareRevisionEvidence(NormalizedMonthlyReportRow incoming, NormalizedMonthlyReportRow current)
    {
        if (incoming.ProviderPublishedAtUtc is { } incomingPublished && current.ProviderPublishedAtUtc is { } currentPublished)
            return incomingPublished.CompareTo(currentPublished);
        if (incoming.ProviderPublishedAtUtc is not null && current.ProviderPublishedAtUtc is null) return 1;
        if (incoming.ProviderPublishedAtUtc is null && current.ProviderPublishedAtUtc is not null) return -1;
        return 0;
    }

    private static string BuildSourceRowFingerprint(NadpcoApiMonthlyActivityItem item) =>
        HashShort(string.Join("|", [
            item.SourceKind, item.ExternalCompanyId, item.LineItemCode, item.ProviderLineItemId,
            item.Title, item.Unit,
            item.ProductionQuantity?.ToString(CultureInfo.InvariantCulture), item.SalesQuantity?.ToString(CultureInfo.InvariantCulture),
            item.SalesAmount?.ToString(CultureInfo.InvariantCulture), item.SalesRate?.ToString(CultureInfo.InvariantCulture)]));

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
        var category = item.CategoryTitle ?? parent.CategoryTitle;
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
            outputType);

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
            MissingVendorLineItemId: string.IsNullOrWhiteSpace(vendorCode),
            PublishDateTime: item.PublishDateTime ?? parent.PublishDateTime,
            SourceRowKey: item.GetSourceRowId(),
            ProviderProductId: item.GetProductId(),
            ProviderProductCode: item.ProductCode);
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

        var titleSensitiveCodes = records
            .Where(record => !string.IsNullOrWhiteSpace(record.GetServiceCode()))
            .GroupBy(record => record.GetServiceCode()!.Trim(), StringComparer.Ordinal)
            .Where(group => group
                .Select(record => NormalizeIdentityText(record.GetServiceTitle()))
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        return records.Select((record, index) =>
        {
            var companyId = RequireCompanyId(record.GetCompanyId(), "service-sales");
            var year = RequireYear(record.Year, "service-sales");
            var month = RequireMonth(record.Month, "service-sales");
            var title = record.GetServiceTitle();
            var unit = record.GetServiceUnit();
            var category = record.CategoryTitle;
            var vendorCode = record.GetServiceCode();
            var lineItemCode = BuildLineItemCode(
                "SERVICE",
                vendorCode,
                title,
                category,
                unit,
                index,
                includeTitleForVendorCode: vendorCode is not null && titleSensitiveCodes.Contains(vendorCode.Trim()),
                instrumentCode: record.GetTseCode());
            var externalReportId = BuildExternalReportId(
                "ServiceSales",
                // ServiceSales uses one canonical company-month report identity; provider activity
                // IDs are line/evidence data and must not split the logical monthly report.
                activityId: null,
                companyId,
                year,
                month,
                OutputType: null);

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
                // The ServiceSales endpoint does not expose a normalized service quantity in its
                // live contract. Keep it null rather than treating a monetary amount as quantity.
                SalesQuantity: null,
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
                record.PublishDate ?? record.PublishDateTime,
                record.JalaliPublishDate,
                VendorLineItemId: vendorCode,
                MissingVendorLineItemId: string.IsNullOrWhiteSpace(vendorCode),
                PublishDateTime: record.PublishDateTime,
                RevenueFromBeginning: record.RevenueFromBeginning,
                RevenueEndOfLastPeriod: record.RevenueEndOfLastPeriod,
                SourceRowKey: record.GetSourceRowId(),
                ProviderProductCode: vendorCode);
        }).ToArray();
    }

    private static string BuildExternalReportId(
        string sourceKind,
        long? activityId,
        int companyId,
        int year,
        byte month,
        int? OutputType)
    {
        var outputPart = OutputType?.ToString(CultureInfo.InvariantCulture) ?? "none";

        if (activityId is not null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{sourceKind}:{activityId.Value}:output-{outputPart}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sourceKind}:{companyId}:{year:D4}-{month:D2}:output-{outputPart}");
    }

    private static string BuildLineItemCode(
        string prefix,
        string? vendorCode,
        string? title,
        string? category,
        string? unit,
        int index,
        bool includeTitleForVendorCode = false,
        string? instrumentCode = null)
    {
        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            var normalizedCode = vendorCode.Trim();
            return includeTitleForVendorCode
                ? $"{prefix}:{normalizedCode}:TITLE:{NormalizeIdentityText(title)}"
                : $"{prefix}:{normalizedCode}";
        }

        var naturalKey = string.Join(
            "|",
            [instrumentCode, NormalizeIdentityText(title), NormalizeIdentityText(unit),
                NormalizeIdentityText(category)]);
        return $"{prefix}:NATURAL:{HashShort(naturalKey)}";
    }

    private static string NormalizeIdentityText(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
                SchemaAudit = "No migration required: ServiceSales revenueDuringThePeriod maps to SalesAmount; service quantities remain null when the endpoint does not provide them, and cumulative revenue stays evidence-only.",
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
                        : null,
                    item.PublishDateTime,
                    item.RevenueFromBeginning,
                    item.RevenueEndOfLastPeriod
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
        bool MissingVendorLineItemId,
        string? PublishDateTime = null,
        decimal? RevenueFromBeginning = null,
        decimal? RevenueEndOfLastPeriod = null,
        string? SourceRowKey = null,
        string? SourceRowFingerprint = null,
        int SourceMultiplicity = 1,
        long? ProviderProductId = null,
        string? ProviderProductCode = null)
    {
        public string? ProviderLineItemId => SourceRowKey;
        public DateTimeOffset? ProviderPublishedAtUtc =>
            DateTimeOffset.TryParse(PublishDateTime ?? PublishDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
                ? value.ToUniversalTime()
                : null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class EfCoreFinancialStatementTableRepository(
    FinancialIngestionDbContext dbContext,
    IOptions<NadpcoApiProviderOptions> providerOptions) : IFinancialStatementTableRepository
{
    private readonly string _providerName = providerOptions.Value.ProviderName;

    public async Task<FinancialStatementTableSource?> FindLatestStatementAsync(
        FinancialStatementTableSelection selection,
        CancellationToken ct = default)
    {
        var rows = await dbContext.FinancialStatements
            .AsNoTracking()
            .Where(row => row.ProviderName == selection.ProviderName &&
                          row.ExternalCompanyId == selection.ExternalCompanyId &&
                          row.StatementType == selection.StatementType.ToString() &&
                          row.IsComposing == selection.IsComposing)
            .Where(row => !selection.PeriodMonths.HasValue ||
                          row.PeriodType == PeriodTypeFromMonths(selection.PeriodMonths.Value))
            .Where(row => !selection.IsAudited.HasValue || row.IsAudited == selection.IsAudited.Value)
            .Where(row => !selection.IsRepresented.HasValue || row.IsRepresented == selection.IsRepresented.Value)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return null;

        var company = await dbContext.Companies
            .AsNoTracking()
            .Where(row => row.ExternalCompanyId == selection.ExternalCompanyId)
            .OrderByDescending(row => row.ProviderName == _providerName)
            .ThenByDescending(row => row.LastSynchronizedAt)
            .FirstOrDefaultAsync(ct);

        var selected = rows
            .Select(row => new { Row = row, Metadata = StatementMetadata.Parse(row.WarningsJson) })
            .OrderByDescending(item => item.Metadata.AnnouncementDate ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.Row.PeriodEnd)
            .ThenByDescending(item => ResolvePeriodMonths(item.Row.PeriodType))
            .ThenByDescending(item => item.Row.ExternalStatementId, StringComparer.Ordinal)
            .ThenByDescending(item => item.Row.Id)
            .First();

        var unit = await dbContext.FinancialStatementLineItems
            .AsNoTracking()
            .Where(item => item.FinancialStatementId == selected.Row.Id &&
                           item.SourceItemCatalogId != null)
            .Join(
                dbContext.FinancialStatementSourceItems.AsNoTracking(),
                item => item.SourceItemCatalogId,
                catalog => catalog.Id,
                (_, catalog) => catalog.Unit)
            .FirstOrDefaultAsync(unit => unit != null && unit != string.Empty, ct);

        return new FinancialStatementTableSource(
            selected.Row.Id,
            selected.Row.ExternalStatementId,
            selected.Row.ProviderName,
            selected.Row.ExternalCompanyId,
            FirstNonEmpty(selection.CompanySymbol, company?.CompanySymbol, company?.TseSymbol, company?.Ticker, selection.ExternalCompanyId),
            FirstNonEmpty(selection.CompanyName, company?.Name),
            selection.StatementType,
            selected.Row.PeriodType,
            ResolvePeriodMonths(selected.Row.PeriodType),
            selected.Row.PeriodStart,
            selected.Row.PeriodEnd,
            selected.Metadata.AnnouncementDate,
            selected.Metadata.JalaliPeriodEnd,
            selected.Metadata.JalaliFiscalYearEnd,
            selected.Metadata.JalaliAnnouncementDate,
            selected.Row.IsAudited,
            selected.Row.IsRepresented,
            selected.Row.IsComposing,
            unit);
    }

    public async Task<IReadOnlyList<FinancialStatementTableLineItem>> GetStatementLineItemsAsync(
        Guid statementId,
        CancellationToken ct = default)
    {
        var rows = await dbContext.FinancialStatementLineItems
            .AsNoTracking()
            .Where(item => item.FinancialStatementId == statementId)
            .GroupJoin(
                dbContext.FinancialStatementSourceItems.AsNoTracking(),
                item => item.SourceItemCatalogId,
                catalog => catalog.Id,
                (item, catalogs) => new { Item = item, Catalog = catalogs.FirstOrDefault() })
            .OrderBy(row => row.Catalog == null ? int.MaxValue : row.Catalog.SourceItemId)
            .ThenBy(row => row.Item.MetricCode)
            .ThenBy(row => row.Item.Id)
            .ToListAsync(ct);

        return rows.Select((row, index) =>
        {
            var title = row.Catalog?.TitleFa ?? row.Catalog?.TitleEn ?? row.Item.MetricCode;
            return new FinancialStatementTableLineItem(
                index + 1,
                row.Catalog?.SourceItemId,
                row.Catalog?.TitleFa,
                row.Catalog?.TitleEn,
                row.Item.MetricCode,
                row.Item.Value,
                FormatAmount(row.Item.Value),
                row.Catalog?.Unit,
                ClassifySide(row.Item.MetricCode, title));
        }).ToList();
    }

    private static FinancialStatementTableSide ClassifySide(string? metricCode, string? title)
    {
        if (metricCode is not null)
        {
            if (metricCode.Contains("ASSET", StringComparison.OrdinalIgnoreCase))
                return FinancialStatementTableSide.Assets;

            if (metricCode.Contains("LIABIL", StringComparison.OrdinalIgnoreCase) ||
                metricCode.Contains("EQUITY", StringComparison.OrdinalIgnoreCase))
            {
                return FinancialStatementTableSide.LiabilitiesAndEquity;
            }
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var normalized = title.Replace('\u200c', ' ');
            if (normalized.Contains("دارایی", StringComparison.OrdinalIgnoreCase))
                return FinancialStatementTableSide.Assets;

            if (normalized.Contains("بدهی", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("حقوق", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("سرمایه", StringComparison.OrdinalIgnoreCase))
            {
                return FinancialStatementTableSide.LiabilitiesAndEquity;
            }
        }

        return FinancialStatementTableSide.Unclassified;
    }

    private static string? PeriodTypeFromMonths(int months) => months switch
    {
        3 => "ThreeMonths",
        6 => "SixMonths",
        9 => "NineMonths",
        12 => "TwelveMonths",
        _ => null
    };

    private static int ResolvePeriodMonths(string periodType) => periodType switch
    {
        "ThreeMonths" => 3,
        "SixMonths" => 6,
        "NineMonths" => 9,
        "TwelveMonths" => 12,
        _ => 0
    };

    private static string? FormatAmount(decimal? value) =>
        value.HasValue ? value.Value.ToString("#,##0.###", CultureInfo.InvariantCulture) : null;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed record StatementMetadata(
        string? JalaliPeriodEnd,
        string? JalaliFiscalYearEnd,
        string? JalaliAnnouncementDate,
        DateTimeOffset? AnnouncementDate)
    {
        public static StatementMetadata Parse(string? warningsJson)
        {
            if (string.IsNullOrWhiteSpace(warningsJson))
                return new StatementMetadata(null, null, null, null);

            try
            {
                using var document = JsonDocument.Parse(warningsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return new StatementMetadata(null, null, null, null);

                string? jalaliPeriodEnd = null;
                string? jalaliFiscalYearEnd = null;
                string? jalaliAnnouncementDate = null;
                DateTimeOffset? announcementDate = null;

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("code", out var code) &&
                        item.TryGetProperty("evidence", out var evidence))
                    {
                        switch (code.GetString())
                        {
                            case "JalaliPeriodEnd":
                                jalaliPeriodEnd = evidence.GetString();
                                continue;
                            case "JalaliFiscalYearEnd":
                                jalaliFiscalYearEnd = evidence.GetString();
                                continue;
                            case "JalaliAnouncementDate":
                            case "JalaliAnnouncementDate":
                                jalaliAnnouncementDate = evidence.GetString();
                                continue;
                            case "AnouncementDate":
                            case "AnnouncementDate":
                                if (evidence.ValueKind == JsonValueKind.String &&
                                    DateTimeOffset.TryParse(evidence.GetString(), out var parsedEvidence))
                                {
                                    announcementDate = parsedEvidence;
                                }

                                continue;
                        }
                    }

                    jalaliPeriodEnd ??= TryGetString(item, "jalaliPeriodEnd", "JalaliPeriodEnd");
                    jalaliFiscalYearEnd ??= TryGetString(item, "jalaliFiscalYearEnd", "JalaliFiscalYearEnd");
                    jalaliAnnouncementDate ??= TryGetString(item, "jalaliAnouncementDate", "JalaliAnouncementDate", "jalaliAnnouncementDate", "JalaliAnnouncementDate");

                    var announcementText = TryGetString(item, "anouncementDate", "AnouncementDate", "announcementDate", "AnnouncementDate");
                    if (announcementDate is null &&
                        DateTimeOffset.TryParse(announcementText, out var parsed))
                    {
                        announcementDate = parsed;
                    }
                }

                return new StatementMetadata(
                    jalaliPeriodEnd,
                    jalaliFiscalYearEnd,
                    jalaliAnnouncementDate,
                    announcementDate);
            }
            catch (JsonException)
            {
                return new StatementMetadata(null, null, null, null);
            }
        }

        private static string? TryGetString(JsonElement item, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (item.TryGetProperty(propertyName, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }

            return null;
        }
    }
}

internal sealed class FinancialStatementTableQueryUseCase(
    ICompanyResolverService companyResolver,
    IFinancialStatementTableRepository repository,
    IFinancialStatementTableRenderer renderer,
    IOptions<NadpcoApiProviderOptions> providerOptions,
    TimeProvider timeProvider) : IFinancialStatementTableQueryUseCase
{
    public async Task<FinancialStatementTableResult?> ExecuteAsync(
        FinancialStatementTableQuery query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.CompanyQuery) || query.StatementType is null)
            return null;

        var company = await companyResolver.ResolveBySymbolAsync(query.CompanyQuery, ct);
        if (company is null)
            return null;

        var providerName = providerOptions.Value.ProviderName;
        var selection = new FinancialStatementTableSelection(
            company.ExternalCompanyId,
            query.StatementType.Value,
            providerName,
            query.PeriodMonths,
            query.IsAudited,
            query.IsRepresented,
            query.IsComposing ?? false,
            FirstNonEmpty(company.CompanySymbol, company.TseSymbol, company.Ticker, query.CompanyQuery),
            null);

        var source = await repository.FindLatestStatementAsync(selection, ct);
        if (source is null)
            return null;

        var lineItems = await repository.GetStatementLineItemsAsync(source.StatementId, ct);
        var warnings = new List<string>();
        if (lineItems.Count == 0)
            warnings.Add("هیچ ردیف مالی برای صورت انتخاب شده در پایگاه داده وجود ندارد.");

        var balanceRows = source.StatementType == FinancialStatementType.BalanceSheet
            ? BuildBalanceSheetRows(lineItems, warnings)
            : [];

        var result = new FinancialStatementTableResult(
            source,
            lineItems,
            balanceRows,
            warnings,
            RenderedAnswer: null,
            timeProvider.GetUtcNow());

        return result with { RenderedAnswer = renderer.Render(result) };
    }

    private static IReadOnlyList<BalanceSheetTableRow> BuildBalanceSheetRows(
        IReadOnlyList<FinancialStatementTableLineItem> lineItems,
        List<string> warnings)
    {
        var assets = lineItems
            .Where(item => item.Side == FinancialStatementTableSide.Assets)
            .ToList();
        var liabilities = lineItems
            .Where(item => item.Side == FinancialStatementTableSide.LiabilitiesAndEquity)
            .ToList();
        var unclassified = lineItems
            .Where(item => item.Side == FinancialStatementTableSide.Unclassified)
            .ToList();

        if (unclassified.Count > 0)
        {
            warnings.Add($"تعداد {unclassified.Count} ردیف ترازنامه طبقه بندی نشده است و در انتهای سمت بدهی/حقوق مالکانه نمایش داده شد.");
            liabilities.AddRange(unclassified);
        }

        var count = Math.Max(assets.Count, liabilities.Count);
        return Enumerable.Range(0, count)
            .Select(index => new BalanceSheetTableRow(
                index < assets.Count ? assets[index] : null,
                index < liabilities.Count ? liabilities[index] : null))
            .ToList();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

internal sealed class FinancialStatementTableRenderer : IFinancialStatementTableRenderer
{
    public string Render(FinancialStatementTableResult result)
    {
        var sb = new StringBuilder();
        var source = result.Source;
        var company = string.IsNullOrWhiteSpace(source.CompanyName)
            ? source.CompanySymbol
            : $"{source.CompanyName} ({source.CompanySymbol})";

        sb.AppendLine($"### {StatementTypeLabel(source.StatementType)} {company}");
        sb.AppendLine($"دوره: {source.PeriodMonths} ماهه منتهی به {source.JalaliPeriodEnd ?? source.PeriodEnd.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(source.JalaliFiscalYearEnd))
            sb.AppendLine($"سال مالی منتهی به: {source.JalaliFiscalYearEnd}");
        if (!string.IsNullOrWhiteSpace(source.JalaliAnnouncementDate))
            sb.AppendLine($"تاریخ انتشار: {source.JalaliAnnouncementDate}");
        sb.AppendLine($"منبع: {source.ProviderName}");
        sb.AppendLine($"نوع گزارش: {(source.IsComposing ? "تلفیقی" : "غیرتلفیقی")}، {(source.IsAudited ? "حسابرسی شده" : "حسابرسی نشده")}، {(source.IsRepresented ? "تجدید ارائه شده" : "اصلی")}");
        if (!string.IsNullOrWhiteSpace(source.Unit))
            sb.AppendLine($"واحد: {source.Unit}");
        sb.AppendLine();

        if (source.StatementType == FinancialStatementType.BalanceSheet)
            AppendBalanceSheet(sb, result.BalanceSheetRows);
        else
            AppendOneSidedTable(sb, result.LineItems);

        if (result.Warnings.Count > 0)
        {
            sb.AppendLine();
            foreach (var warning in result.Warnings)
                sb.AppendLine($"هشدار: {warning}");
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendOneSidedTable(StringBuilder sb, IReadOnlyList<FinancialStatementTableLineItem> items)
    {
        sb.AppendLine("| ردیف | شرح | مبلغ | شناسه آیتم منبع |");
        sb.AppendLine("|---:|---|---:|---|");
        foreach (var item in items)
        {
            sb.AppendLine($"| {item.RowNumber} | {Escape(item.TitleFa ?? item.TitleEn ?? item.MetricCode ?? "-")} | {item.FormattedValue ?? "-"} | {item.SourceItemId?.ToString(CultureInfo.InvariantCulture) ?? item.MetricCode ?? "-"} |");
        }
    }

    private static void AppendBalanceSheet(StringBuilder sb, IReadOnlyList<BalanceSheetTableRow> rows)
    {
        sb.AppendLine("| دارایی‌ها | مبلغ | بدهی‌ها و حقوق مالکانه | مبلغ |");
        sb.AppendLine("|---|---:|---|---:|");
        foreach (var row in rows)
        {
            sb.AppendLine($"| {Title(row.Asset)} | {Amount(row.Asset)} | {Title(row.LiabilityOrEquity)} | {Amount(row.LiabilityOrEquity)} |");
        }
    }

    private static string Title(FinancialStatementTableLineItem? item) =>
        item is null ? string.Empty : Escape(item.TitleFa ?? item.TitleEn ?? item.MetricCode ?? "-");

    private static string Amount(FinancialStatementTableLineItem? item) =>
        item?.FormattedValue ?? string.Empty;

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string StatementTypeLabel(FinancialStatementType type) => type switch
    {
        FinancialStatementType.IncomeStatement => "صورت سود و زیان",
        FinancialStatementType.BalanceSheet => "ترازنامه",
        FinancialStatementType.CashFlow => "صورت جریان وجوه نقد",
        _ => type.ToString()
    };
}

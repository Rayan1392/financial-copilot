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
    IOptions<NadpcoApiProviderOptions> providerOptions) : IFinancialStatementTableRepository, IQuarterlyFinancialReportsRepository
{
    private readonly string _providerName = providerOptions.Value.ProviderName;

    public async Task<IReadOnlyList<QuarterlyReportObservation>> GetQuarterlyIncomeStatementReportsAsync(
        QuarterlyFinancialReportSelection selection,
        CancellationToken ct = default)
    {
        var approved = QuarterlyFinancialReportsNormalizer.RequiredMappings;
        var ids = approved.Select(item => item.SourceItemId).ToArray();
        var catalog = await dbContext.FinancialStatementSourceItems.AsNoTracking()
            .Where(item => item.ProviderName == "NoavaranCurrentApi" &&
                           item.StatementType == "IncomeStatement" && ids.Contains(item.SourceItemId))
            .ToListAsync(ct);
        // The source catalog's primary key is a database surrogate. It is generated when a
        // provider item is first ingested, so it can differ across environments and for items
        // introduced after the original catalog. Resolve identity by the provider tuple above
        // (provider, statement type, source item id), then carry the actual catalog id through.
        var catalogIds = catalog.ToDictionary(item => item.Id);
        var statementsQuery = dbContext.FinancialStatements.AsNoTracking()
            .Where(item => item.ProviderName == "NoavaranCurrentApi" &&
                           item.ExternalCompanyId == selection.ExternalCompanyId &&
                           item.StatementType == "IncomeStatement" &&
                           (item.PeriodType == "ThreeMonths" || item.PeriodType == "SixMonths" ||
                            item.PeriodType == "NineMonths" || item.PeriodType == "TwelveMonths"));
        if (selection.IsComposing.HasValue) statementsQuery = statementsQuery.Where(item => item.IsComposing == selection.IsComposing.Value);
        if (selection.IsAudited.HasValue) statementsQuery = statementsQuery.Where(item => item.IsAudited == selection.IsAudited.Value);
        if (selection.IsRepresented.HasValue) statementsQuery = statementsQuery.Where(item => item.IsRepresented == selection.IsRepresented.Value);
        // Legacy rows can have a separate-statement flag while their source evidence
        // identifies a consolidated statement. Exclude them before limiting periods.
        var orderedQuery = statementsQuery.OrderByDescending(item => item.PeriodEnd)
            .ThenByDescending(item => item.LastSynchronizedAt);
        List<NormalizedFinancialStatementRow> statements;
        if (selection.EndingPeriod is { } endingPeriod)
        {
            var comparisonWindowEnd = endingPeriod.AddMonths(12);
            var visible = await orderedQuery.Where(item => item.PeriodEnd <= endingPeriod).Take(48).ToListAsync(ct);
            var references = await orderedQuery.Where(item => item.PeriodEnd > endingPeriod &&
                    item.PeriodEnd <= comparisonWindowEnd).Take(48).ToListAsync(ct);
            statements = visible.Concat(references).DistinctBy(item => item.Id).ToList();
        }
        else
        {
            statements = await orderedQuery.Take(48).ToListAsync(ct);
        }
        statements = statements
            .Where(item => StatementMetadata.Parse(item.WarningsJson).SourceIsComposing is not { } sourceScope ||
                           sourceScope == item.IsComposing)
            .ToList();
        if (statements.Count == 0) return [];
        var statementIds = statements.Select(item => item.Id).ToArray();
        var catalogItemIds = catalogIds.Keys.ToArray();
        var lines = await dbContext.FinancialStatementLineItems.AsNoTracking()
            .Where(item => statementIds.Contains(item.FinancialStatementId) &&
                           item.SourceItemCatalogId.HasValue && catalogItemIds.Contains(item.SourceItemCatalogId.Value))
            .ToListAsync(ct);
        var linesByStatement = lines.GroupBy(item => item.FinancialStatementId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return statements.Select(statement =>
        {
            var items = linesByStatement.GetValueOrDefault(statement.Id, []);
            var observations = items.Select(line =>
            {
                var source = catalogIds[line.SourceItemCatalogId!.Value];
                return new QuarterlySourceObservation(line.Id, source.Id, source.SourceItemId,
                    source.TitleFa, source.Unit, line.Value);
            }).ToArray();
            var metadata = StatementMetadata.Parse(statement.WarningsJson);
            return new QuarterlyReportObservation(statement.Id, statement.ExternalStatementId,
                statement.ProviderName, statement.ExternalCompanyId, statement.StatementType,
                ResolvePeriodMonths(statement.PeriodType), statement.PeriodStart, statement.PeriodEnd,
                statement.IsAudited, statement.IsRepresented, statement.IsComposing,
                statement.SourcePayloadChecksum, statement.LastSynchronizedAt,
                metadata.JalaliAnnouncementDate ?? metadata.AnnouncementDate?.ToString("O", CultureInfo.InvariantCulture),
                metadata.JalaliFiscalYearEnd, observations, metadata.AssumedScale, metadata.SourceAmountUnit,
                metadata.CurrencyCode, metadata.AccountingBasis, metadata.ReportRevisionLineage,
                statement.PeriodType);
        }).ToArray();
    }

    public Task<IReadOnlyList<QuarterlyReportObservation>> GetIncomeStatementReportsAsync(
        QuarterlyFinancialReportSelection selection,
        CancellationToken cancellationToken = default) =>
        GetQuarterlyIncomeStatementReportsAsync(selection, cancellationToken);

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

        var company = await dbContext.Companies
            .AsNoTracking()
            .Where(row => row.ExternalCompanyId == selection.ExternalCompanyId)
            .OrderByDescending(row => row.ProviderName == _providerName)
            .ThenByDescending(row => row.LastSynchronizedAt)
            .FirstOrDefaultAsync(ct);

        var selected = rows
            .Select(row => new { Row = row, Metadata = StatementMetadata.Parse(row.WarningsJson) })
            .Where(item => item.Metadata.SourceIsComposing is not { } sourceScope ||
                           sourceScope == item.Row.IsComposing)
            .OrderByDescending(item => item.Metadata.AnnouncementDate ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.Row.PeriodEnd)
            .ThenByDescending(item => ResolvePeriodMonths(item.Row.PeriodType))
            .ThenByDescending(item => item.Row.ExternalStatementId, StringComparer.Ordinal)
            .ThenByDescending(item => item.Row.Id)
            .FirstOrDefault();
        if (selected is null)
            return null;

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
        var statementType = await dbContext.FinancialStatements
            .AsNoTracking()
            .Where(statement => statement.Id == statementId)
            .Select(statement => statement.StatementType)
            .FirstOrDefaultAsync(ct);

        var rows = await dbContext.FinancialStatementLineItems
            .AsNoTracking()
            .Where(item => item.FinancialStatementId == statementId)
            .GroupJoin(
                dbContext.FinancialStatementSourceItems.AsNoTracking(),
                item => item.SourceItemCatalogId,
                catalog => catalog.Id,
                (item, catalogs) => new { Item = item, Catalog = catalogs.FirstOrDefault() })
            .ToListAsync(ct);

        var orderedRows = rows
            .OrderBy(row => row.Catalog == null
                ? InferSourceItemId(statementType, row.Item.MetricCode) ?? int.MaxValue
                : row.Catalog.SourceItemId)
            .ThenBy(row => row.Item.MetricCode)
            .ThenBy(row => row.Item.Id)
            .ToList();

        return orderedRows.Select((row, index) =>
        {
            var titleFa = FirstNonEmpty(row.Catalog?.TitleFa, PersianTitleFromMetricCode(row.Item.MetricCode));
            var sourceItemId = row.Catalog?.SourceItemId ?? InferSourceItemId(statementType, row.Item.MetricCode);
            var title = FirstNonEmpty(titleFa, row.Catalog?.TitleEn, row.Item.MetricCode);
            return new FinancialStatementTableLineItem(
                index + 1,
                sourceItemId,
                titleFa,
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

    private static int? InferSourceItemId(string? statementType, string? metricCode)
    {
        if (string.IsNullOrWhiteSpace(statementType) || string.IsNullOrWhiteSpace(metricCode))
            return null;

        var map = statementType switch
        {
            nameof(FinancialStatementType.IncomeStatement) => NadpcoApiStatementItemMaps.IncomeItemIdToMetricCode,
            nameof(FinancialStatementType.BalanceSheet) => NadpcoApiStatementItemMaps.BalanceSheetItemIdToMetricCode,
            nameof(FinancialStatementType.CashFlow) => NadpcoApiStatementItemMaps.CashFlowItemIdToMetricCode,
            _ => null
        };

        if (map is null)
            return null;

        foreach (var pair in map)
        {
            if (string.Equals(pair.Value, metricCode, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        }

        return null;
    }

    private static string? PersianTitleFromMetricCode(string? metricCode)
    {
        if (string.IsNullOrWhiteSpace(metricCode))
            return null;

        return metricCode.ToUpperInvariant() switch
        {
            "REVENUE" => "درآمدهای عملیاتی",
            "TOTAL_REVENUE" => "جمع درآمدها",
            "GROSS_PROFIT" => "سود ناخالص",
            "OPERATING_PROFIT" => "سود عملیاتی",
            "NET_PROFIT" => "سود خالص",
            "EPS" => "سود هر سهم",
            "EPS_CONSOLIDATED" => "سود هر سهم تلفیقی",
            "FINANCE_COSTS" => "هزینه‌های مالی",
            "INCOME_TAX" => "مالیات بر درآمد",
            "TOTAL_EQUITY" => "جمع حقوق مالکانه",
            "CAPITAL" => "سرمایه",
            "OPERATING_CASH_FLOW" => "جریان نقد عملیاتی",
            _ => null
        };
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
        DateTimeOffset? AnnouncementDate,
        string? AssumedScale,
        string? SourceAmountUnit,
        string? CurrencyCode,
        string? AccountingBasis,
        string? ReportRevisionLineage,
        bool? SourceIsComposing)
    {
        public static StatementMetadata Parse(string? warningsJson)
        {
            if (string.IsNullOrWhiteSpace(warningsJson))
                return new StatementMetadata(null, null, null, null, null, null, null, null, null, null);

            try
            {
                using var document = JsonDocument.Parse(warningsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return new StatementMetadata(null, null, null, null, null, null, null, null, null, null);

                string? jalaliPeriodEnd = null;
                string? jalaliFiscalYearEnd = null;
                string? jalaliAnnouncementDate = null;
                DateTimeOffset? announcementDate = null;
                string? assumedScale = null;
                string? sourceAmountUnit = null;
                string? currencyCode = null;
                string? accountingBasis = null;
                string? reportRevisionLineage = null;
                bool? sourceIsComposing = null;

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("code", out var sourceCode) &&
                        sourceCode.ValueKind == JsonValueKind.String &&
                        sourceCode.GetString() == "NadpcoApiStatementSelection" &&
                        item.TryGetProperty("isComposing", out var sourceScope) &&
                        sourceScope.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        sourceIsComposing = sourceScope.GetBoolean();
                    }
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
                    assumedScale ??= TryGetString(item, "assumedScale", "AssumedScale");
                    sourceAmountUnit ??= TryGetString(item, "sourceAmountUnit", "SourceAmountUnit");
                    currencyCode ??= TryGetString(item, "currencyCode", "CurrencyCode");
                    accountingBasis ??= TryGetString(item, "accountingBasis", "AccountingBasis");
                    reportRevisionLineage ??= TryGetString(item, "reportRevisionLineage", "ReportRevisionLineage", "revisionLineage", "RevisionLineage");

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
                    announcementDate,
                    assumedScale,
                    sourceAmountUnit,
                    currencyCode,
                    accountingBasis,
                    reportRevisionLineage,
                    sourceIsComposing);
            }
            catch (JsonException)
            {
                return new StatementMetadata(null, null, null, null, null, null, null, null, null, null);
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
    IQuarterlyFinancialReportsQueryUseCase quarterlyReportsUseCase,
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

        if (query.UseQuarterlyReportTable && query.StatementType == FinancialStatementType.IncomeStatement)
        {
            var quarterlyResult = await quarterlyReportsUseCase.ExecuteAsync(
                new QuarterlyFinancialReportTableQuery(
                    string.Empty,
                    query.CompanyQuery,
                    query.QuarterCount, query.EndingPeriod, query.IsComposing, query.IsAudited, query.IsRepresented), ct);
            return new FinancialStatementTableResult(null, [], [], [],
                RenderQuarterly(quarterlyResult), timeProvider.GetUtcNow(), quarterlyResult);
        }

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

    internal static string RenderQuarterly(QuarterlyFinancialReportTableResult result)
    {
        if (result.Status == "Unavailable")
            return $"گزارش فصلی برای {result.CompanySymbol} در دسترس نیست. وضعیت: {result.Reason ?? result.Status}";

        var culture = CultureInfo.GetCultureInfo("fa-IR");
        var builder = new StringBuilder();
        var symbol = result.CompanySymbol.Trim();
        var companyName = result.CompanyName?.Trim();
        var title = string.IsNullOrWhiteSpace(companyName) ||
                    string.Equals(companyName, symbol, StringComparison.OrdinalIgnoreCase)
            ? symbol
            : $"{companyName} ({symbol})";
        builder.AppendLine($"### جدول گزارش فصلی {title}");
        if (result.Status == "Partial") builder.AppendLine($"پوشش ناقص: {result.Reason}");
        builder.AppendLine("واحد مبالغ: میلیون ریال؛ EPS مقایسه‌ای: ریال به‌ازای هر سهم؛ مبنای سهام: هزار سهم");
        builder.AppendLine();
        builder.Append("| شرح | ");
        builder.Append(string.Join(" | ", result.PeriodsNewestFirst.Select(period => FormatQuarterHeader(result, period))));
        builder.AppendLine(" | تغییر فصل‌به‌فصل | ");
        builder.Append("|---| ");
        builder.Append(string.Join(" | ", result.PeriodsNewestFirst.Select(_ => "---:")));
        builder.AppendLine(" | ---: | ");
        foreach (var row in result.Rows)
        {
            builder.Append($"| {row.LabelFa} | ");
            builder.Append(string.Join(" | ", result.PeriodsNewestFirst.Select(period => FormatCell(row.Cells[period.Label], culture))));
            var latest = result.PeriodsNewestFirst.FirstOrDefault();
            var comparison = latest is null ? null : result.Comparisons.FirstOrDefault(item => item.RowId == row.RowId && item.TargetPeriod == latest);
            builder.AppendLine($" | {(comparison?.RelativeChangePercent is decimal change ? change.ToString("#,##0.##", culture) + "%" : comparison?.AbsoluteChange?.ToString("#,##0.##", culture) ?? "-")} | ");
        }
        foreach (var period in result.PeriodsNewestFirst)
        {
            var eps = result.Rows.FirstOrDefault(row => row.RowId == "source_160")?.Cells.GetValueOrDefault(period.Label);
            var evidence = eps?.ComparableEvidence;
            if (evidence is null) continue;
            var reference = evidence.ReferenceExternalReportId is null
                ? "گزارش مرجع نامشخص"
                : $"FY{evidence.ReferenceFiscalYear} Q{evidence.ReferenceQuarter}، گزارش {evidence.ReferenceExternalReportId}";
            var profit = evidence.StandaloneAttributableProfitMillionRials is decimal numerator
                ? numerator.ToString("#,##0.##", culture)
                : "ناموجود";
            var shares = evidence.ShareBasisThousand is decimal denominator
                ? denominator.ToString("#,##0.##", culture)
                : "ناموجود";
            var value = eps?.Value is decimal epsValue ? epsValue.ToString("#,##0.##", culture) : "ناموجود";
            var raw = evidence.RawCurrentValue is decimal rawCurrent
                ? $"؛ EPS تجمعی منبع {rawCurrent.ToString("#,##0.##", culture)}" +
                  (evidence.RawPreviousValue is decimal rawPrevious ? $" منهای {rawPrevious.ToString("#,##0.##", culture)}" : "")
                : "";
            var numeratorSource = evidence.AttributableProfitSourceItemId is int sourceId
                ? $" (قلم منبع {sourceId})"
                : "";
            builder.AppendLine($"مبنای EPS مقایسه‌ای: {period.Label}: سود قابل انتساب{numeratorSource} {profit} میلیون ریال ÷ {shares} هزار سهم؛ {reference}؛ مقدار مشتق‌شده {value} ریال با گردکردن به نزدیک‌ترین ریال و نیم‌به‌بالا؛ سهام گزارش‌شده پایان دوره است و میانگین وزنی محسوب نمی‌شود{raw}.");
        }
        return builder.ToString().TrimEnd();
    }

    internal static string FormatQuarterHeader(QuarterlyFinancialReportTableResult result, QuarterlyFiscalPeriod period)
    {
        // Match the current cumulative report (3M/6M/9M/12M), never the predecessor
        // used to derive the quarter's financial values.
        var selectedReports = result.SelectedReports ?? [];
        var selected = selectedReports.FirstOrDefault(report =>
                report.PeriodEnd == period.End && QuarterForPeriodType(report.PeriodType) == period.Quarter)
            ?? selectedReports.FirstOrDefault(report =>
                report.PeriodEnd == period.End && QuarterForPeriodType(report.PeriodType) is null);
        if (selected is null || selected.PeriodEnd == default)
            return FiscalQuarterFallback(period.FiscalYear, period.Quarter);

        var date = selected.PeriodEnd.ToDateTime(TimeOnly.MinValue);
        var calendar = new PersianCalendar();
        if (date < calendar.MinSupportedDateTime || date > calendar.MaxSupportedDateTime)
            return FiscalQuarterFallback(period.FiscalYear, period.Quarter);
        string[] months = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
        var day = ToPersianDigits(calendar.GetDayOfMonth(date).ToString(CultureInfo.InvariantCulture));
        var year = ToPersianDigits(calendar.GetYear(date).ToString(CultureInfo.InvariantCulture));
        var fiscalQuarter = QuarterForPeriodType(selected.PeriodType) ?? period.Quarter;
        var quarterName = QuarterName(fiscalQuarter);
        return quarterName is null
            ? $"سال مالی {ToPersianDigits(period.FiscalYear.ToString(CultureInfo.InvariantCulture))}، فصل {ToPersianDigits(fiscalQuarter.ToString(CultureInfo.InvariantCulture))}"
            : $"{quarterName}<br>منتهی به<br>{day} {months[calendar.GetMonth(date) - 1]} {year}";
    }

    private static int? QuarterForPeriodType(string? periodType) => periodType switch
    {
        "ThreeMonths" => 1,
        "SixMonths" => 2,
        "NineMonths" => 3,
        "TwelveMonths" => 4,
        _ => null
    };

    private static string? QuarterName(int quarter) => quarter switch
    {
        1 => "فصل اول",
        2 => "فصل دوم",
        3 => "فصل سوم",
        4 => "فصل چهارم",
        _ => null
    };

    private static string FiscalQuarterFallback(int fiscalYear, int quarter) =>
        $"سال مالی {ToPersianDigits(fiscalYear.ToString(CultureInfo.InvariantCulture))}، فصل {ToPersianDigits(quarter.ToString(CultureInfo.InvariantCulture))}";

    private static string ToPersianDigits(string value) =>
        string.Concat(value.Select(character => character is >= '0' and <= '9'
            ? (char)('۰' + character - '0')
            : character));

    private static string FormatCell(QuarterlyFinancialCell cell, CultureInfo culture)
    {
        if (cell.State != QuarterlyCellState.Available || !cell.Value.HasValue) return "—";
        if (cell.Value.Value == 0 && cell.RenderZeroAsDash) return "-";
        var formatted = cell.Value.Value.ToString("#,##0.##", culture);
        return cell.Value.Value < 0 ? $"({formatted.TrimStart('-')})" : formatted;
    }
}

internal sealed class FinancialStatementTableRenderer : IFinancialStatementTableRenderer
{
    public string Render(FinancialStatementTableResult result)
    {
        if (result.QuarterlyResult is not null)
            return result.RenderedAnswer ?? string.Empty;
        if (result.Source is null)
            throw new InvalidOperationException("A persisted source is required for a non-quarterly statement table.");
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
        sb.AppendLine("| ردیف | شرح | مبلغ |");
        sb.AppendLine("|---:|---|---:|");
        foreach (var item in items)
        {
            sb.AppendLine($"| {item.RowNumber} | {Escape(item.TitleFa ?? item.TitleEn ?? item.MetricCode ?? "-")} | {item.FormattedValue ?? "-"} |");
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

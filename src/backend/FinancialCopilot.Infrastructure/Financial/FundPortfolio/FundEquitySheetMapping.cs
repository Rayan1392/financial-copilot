using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed record FundEquityMappedRow(
    int SourceLogicalRow,
    string RawSecurityName,
    string NormalizedSecurityName,
    FundEquitySecurityType SecurityType,
    decimal? BeginningQuantity,
    decimal? BeginningCostAmount,
    decimal? BeginningMarketOrNetSaleValue,
    decimal? PurchasedQuantity,
    decimal? PurchaseCostAmount,
    decimal? SoldQuantity,
    decimal? SaleProceedsAmount,
    decimal? EndingQuantity,
    decimal? EndingUnitMarketPrice,
    decimal? EndingCostAmount,
    decimal? EndingMarketOrNetSaleValue,
    decimal? WeightOfTotalAssetsPercentage,
    string? SourceAddress,
    string SourceEvidenceJson,
    bool IsTotalRow,
    FundWorkbookPeriodContext PeriodContext);

public static class FundEquitySheetMapping
{
    public const string MappingVersion = "iran-fund-equity-header-path-v1";
    private enum ColumnKind
    {
        Security,
        BeginningQuantity,
        BeginningCost,
        BeginningValue,
        PurchaseQuantity,
        PurchaseCost,
        SaleQuantity,
        SaleProceeds,
        EndingQuantity,
        EndingPrice,
        EndingCost,
        EndingValue,
        Weight
    }

    public static IReadOnlyList<FundEquityMappedRow> Parse(
        FundWorkbookSheetEnvelope sheet,
        FundWorkbookPeriodContext periodContext,
        IFundPortfolioValueNormalizer valueNormalizer)
    {
        var cells = sheet.Cells.Where(cell => TryGetRowNumber(cell.SourceAddress, out _)).ToArray();
        var rows = cells.GroupBy(cell => GetRowNumber(cell.SourceAddress)).OrderBy(group => group.Key).ToArray();
        var header = FindHeader(rows, valueNormalizer);
        if (header is null) return [];

        var headers = BuildColumnHeaders(rows, header.Value.Row, valueNormalizer);
        var securityColumn = headers.FirstOrDefault(x => x.Value == ColumnKind.Security).Key;
        if (securityColumn == 0) securityColumn = headers.Keys.OrderBy(x => x).FirstOrDefault();
        if (securityColumn == 0) return [];

        var result = new List<FundEquityMappedRow>();
        foreach (var row in rows.Where(group => group.Key > header.Value.Row))
        {
            var byColumn = row.ToDictionary(cell => GetColumnNumber(cell.SourceAddress));
            if (!byColumn.TryGetValue(securityColumn, out var securityCell) || string.IsNullOrWhiteSpace(securityCell.RawValue)) continue;
            var rawName = securityCell.RawValue.Trim();
            var normalizedName = NormalizeSecurityName(valueNormalizer.NormalizeText(rawName));
            var isTotal = IsTotalRow(normalizedName);
            var values = headers.ToDictionary(pair => pair.Key, pair => ParseDecimal(byColumn.GetValueOrDefault(pair.Key)?.RawValue, valueNormalizer));
            var period = DetectPeriodContext(rows, header.Value.Row, periodContext, valueNormalizer);
            result.Add(new FundEquityMappedRow(
                row.Key,
                rawName,
                normalizedName,
                DetectSecurityType(normalizedName),
                Value(values, headers, ColumnKind.BeginningQuantity),
                Value(values, headers, ColumnKind.BeginningCost),
                Value(values, headers, ColumnKind.BeginningValue),
                Value(values, headers, ColumnKind.PurchaseQuantity),
                Value(values, headers, ColumnKind.PurchaseCost),
                Value(values, headers, ColumnKind.SaleQuantity),
                Value(values, headers, ColumnKind.SaleProceeds),
                Value(values, headers, ColumnKind.EndingQuantity),
                Value(values, headers, ColumnKind.EndingPrice),
                Value(values, headers, ColumnKind.EndingCost),
                Value(values, headers, ColumnKind.EndingValue),
                Value(values, headers, ColumnKind.Weight),
                securityCell.SourceAddress,
                JsonSerializer.Serialize(new
                {
                    mappingVersion = MappingVersion,
                    sheet = sheet.OriginalSheetName,
                    row = row.Key,
                    cells = row.ToDictionary(cell => cell.SourceAddress, cell => cell.RawValue)
                }),
                isTotal,
                period));
        }
        return result;
    }

    private static (int Row, string Text)? FindHeader(IGrouping<int, FundWorkbookCellEvidence>[] rows, IFundPortfolioValueNormalizer normalizer)
    {
        foreach (var row in rows.Take(20))
        {
            var text = string.Join(' ', row.Select(cell => NormalizeHeader(normalizer.NormalizeText(cell.RawValue))));
            if (ContainsAny(text, "security", "company", "symbol", "quantity", "qty", "نام", "شرکت", "نماد", "تعداد", "سهام"))
                return (row.Key, text);
        }
        return null;
    }

    private static Dictionary<int, ColumnKind> BuildColumnHeaders(
        IGrouping<int, FundWorkbookCellEvidence>[] rows,
        int headerRow,
        IFundPortfolioValueNormalizer normalizer)
    {
        var headers = new Dictionary<int, ColumnKind>();
        var headerRows = rows.Where(row => row.Key <= headerRow).ToArray();
        foreach (var column in headerRows.SelectMany(row => row).Select(cell => GetColumnNumber(cell.SourceAddress)).Distinct())
        {
            var text = string.Join(' ', headerRows.SelectMany(row => row).Where(cell => GetColumnNumber(cell.SourceAddress) == column)
                .Select(cell => NormalizeHeader(normalizer.NormalizeText(cell.RawValue))));
            var kind = ClassifyColumn(text);
            if (kind is not null) headers[column] = kind.Value;
        }
        return headers;
    }

    private static ColumnKind? ClassifyColumn(string text)
    {
        if (ContainsAny(text, "security", "company", "symbol", "نام شرکت", "نام سهم", "نام نماد", "نماد", "سهام")) return ColumnKind.Security;
        var beginning = ContainsAny(text, "beginning", "opening", "اول دوره", "ابتدای دوره", "اول دوره");
        var purchase = ContainsAny(text, "purchase", "purchased", "خرید", "خريد");
        var sale = ContainsAny(text, "sale", "sold", "فروش", "فروش");
        var ending = ContainsAny(text, "ending", "closing", "پایان دوره", "پايان دوره", "انتهای دوره", "انتهاي دوره");
        var quantity = ContainsAny(text, "quantity", "qty", "تعداد", "مقدار");
        var cost = ContainsAny(text, "cost", "بهای تمام", "بهاي تمام", "بها", "ارزش خرید", "ارزش خريد");
        var value = ContainsAny(text, "market value", "net sale", "market/net", "value", "ارزش روز", "ارزش خالص فروش", "ارزش فروش");
        var price = ContainsAny(text, "price", "قیمت", "قيمت");
        var weight = ContainsAny(text, "weight", "percentage", "percent", "درصد", "درصد از دارایی", "درصد از دارايي");
        if (weight) return ColumnKind.Weight;
        if (beginning && quantity) return ColumnKind.BeginningQuantity;
        if (beginning && cost) return ColumnKind.BeginningCost;
        if (beginning && value) return ColumnKind.BeginningValue;
        if (purchase && quantity) return ColumnKind.PurchaseQuantity;
        if (purchase && (cost || value)) return ColumnKind.PurchaseCost;
        if (sale && quantity) return ColumnKind.SaleQuantity;
        if (sale && (cost || value)) return ColumnKind.SaleProceeds;
        if (ending && quantity) return ColumnKind.EndingQuantity;
        if (ending && price) return ColumnKind.EndingPrice;
        if (ending && cost) return ColumnKind.EndingCost;
        if (ending && value) return ColumnKind.EndingValue;
        return null;
    }

    private static FundWorkbookPeriodContext DetectPeriodContext(
        IGrouping<int, FundWorkbookCellEvidence>[] rows,
        int headerRow,
        FundWorkbookPeriodContext defaultContext,
        IFundPortfolioValueNormalizer normalizer)
    {
        var headerText = string.Join(' ', rows.Where(row => row.Key <= headerRow).SelectMany(row => row).Select(cell => NormalizeHeader(normalizer.NormalizeText(cell.RawValue))));
        return ContainsAny(headerText, "fiscal year to date", "year to date", "از ابتدای سال", "از ابتداي سال", "دوره مالی")
            ? FundWorkbookPeriodContext.FiscalYearToDate
            : defaultContext;
    }

    private static decimal? Value(IReadOnlyDictionary<int, decimal?> values, IReadOnlyDictionary<int, ColumnKind> headers, ColumnKind kind) =>
        values.FirstOrDefault(pair => headers.TryGetValue(pair.Key, out var mapped) && mapped == kind).Value;

    private static decimal? ParseDecimal(string? raw, IFundPortfolioValueNormalizer normalizer)
    {
        if (string.IsNullOrWhiteSpace(raw) || normalizer.IsExcelError(raw)) return null;
        return normalizer.TryParseDecimal(raw, out var value) ? value : null;
    }

    public static string NormalizeSecurityName(string value)
    {
        var normalized = value.Normalize().Replace('_', ' ').Replace('،', ',');
        normalized = string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        foreach (var suffix in new[] { " شرکت", " سهامی عام", " سهامي عام", " co", " company" })
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^suffix.Length].Trim();
        return normalized.Trim('(', ')', '[', ']', '،', ',');
    }

    public static FundEquitySecurityType DetectSecurityType(string normalizedName) =>
        ContainsAny(normalizedName, "preemptive", "right", "حق تقدم", "حق‌تقدم", "حق تقدم")
            ? FundEquitySecurityType.PreemptiveRight
            : ContainsAny(normalizedName, "fund unit", "investment fund", "واحد صندوق", "واحد سرمایه گذاری", "واحد سرمايه گذاري")
                ? FundEquitySecurityType.InvestmentFundUnit
                : FundEquitySecurityType.OrdinaryEquity;

    public static bool IsTotalRow(string normalizedName) =>
        ContainsAny(normalizedName, "total", "subtotal", "sum", "جمع", "جمع کل", "جمع كل", "مجموع", "دارایی های صندوق", "دارايي هاي صندوق");

    private static string NormalizeHeader(string? value) => (value ?? string.Empty).ToLowerInvariant();
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static decimal? GetValue(this IReadOnlyDictionary<int, decimal?> values, int key) => values.TryGetValue(key, out var value) ? value : null;
    private static bool TryGetRowNumber(string address, out int row) { row = GetRowNumber(address); return row > 0; }
    private static int GetRowNumber(string address)
    {
        var digits = new string(address.SkipWhile(char.IsLetter).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
    private static int GetColumnNumber(string address)
    {
        var letters = new string(address.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var value = 0;
        foreach (var character in letters) value = value * 26 + character - 'A' + 1;
        return value;
    }
}

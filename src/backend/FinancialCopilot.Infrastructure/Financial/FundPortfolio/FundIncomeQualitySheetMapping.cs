using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

internal enum IncomeColumn
{
    Name, Amount, SourceIncomePercentage, AssetPercentage, CumulativeAmount, Dividend, Unrealized, Realized, Total,
    MeetingDate, Quantity, Dps, Gross, Discount, Net, ClosingPrice, AdjustedPrice, AdjustmentPercentage, AdjustedValue, Reason
}

public sealed record IncomeSummaryMappedRow(int Row, FundIncomeCategory Category, string RawCategory, decimal? Amount, decimal? SourceIncomePercentage,
    decimal? AssetPercentage, decimal? CumulativeAmount, bool HasFormulaError, bool IsSourceTotal, FundWorkbookPeriodContext PeriodContext, string SourceAddress, string Evidence);

public sealed record SecurityIncomeMappedRow(int Row, string RawName, decimal? Dividend, decimal? Unrealized, decimal? Realized, decimal? Total,
    FundWorkbookPeriodContext PeriodContext, string SourceAddress, string Evidence);

public sealed record DividendMappedRow(int Row, string RawName, string? JalaliDate, DateOnly? Date, decimal? Quantity, decimal? Dps, decimal? Gross,
    decimal? Discount, decimal? Net, FundWorkbookPeriodContext PeriodContext, string SourceAddress, string Evidence);

public sealed record CommodityIncomeMappedRow(int Row, string RawName, decimal? Unrealized, decimal? Realized, decimal? Total,
    FundWorkbookPeriodContext PeriodContext, string SourceAddress, string Evidence);

public sealed record DepositIncomeMappedRow(int Row, string RawName, decimal? Gross, decimal? Discount, decimal? Net,
    FundWorkbookPeriodContext PeriodContext, string SourceAddress, string Evidence);

public sealed record ValuationAdjustmentMappedRow(int Row, string RawName, decimal? Quantity, decimal? ClosingPrice, decimal? AdjustedPrice,
    decimal? SourcePercentage, decimal? AdjustedValue, string? Reason, FundWorkbookPeriodContext PeriodContext, string SourceAddress, string Evidence);

public static partial class FundIncomeQualitySheetMapping
{
    public const string MappingVersion = "fund-income-quality-mapping-v1";

    public static IReadOnlyList<IncomeSummaryMappedRow> ParseIncomeSummaries(FundWorkbookSheetEnvelope sheet, IFundPortfolioValueNormalizer normalizer)
    {
        var result = new List<IncomeSummaryMappedRow>();
        foreach (var row in DataRows(sheet, normalizer, out var headers))
        {
            var name = ReadText(row, headers, IncomeColumn.Name, normalizer);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var isSourceTotal = IsTotal(name, normalizer);
            var amount = ReadDecimal(row, headers, IncomeColumn.Amount, normalizer, out var amountError);
            var sourcePercentage = ReadDecimal(row, headers, IncomeColumn.SourceIncomePercentage, normalizer, out var sourcePercentageError, true);
            var category = isSourceTotal ? FundIncomeCategory.Unknown : MapCategory(name, sheet.LogicalSheetType, normalizer);
            result.Add(new(row.Key, category, name, amount, sourcePercentage,
                ReadDecimal(row, headers, IncomeColumn.AssetPercentage, normalizer, out _, true),
                ReadDecimal(row, headers, IncomeColumn.CumulativeAmount, normalizer, out _),
                amountError || sourcePercentageError, isSourceTotal, InferPeriod(row, sheet), FirstAddress(row), Evidence(sheet, row, new { mapping = MappingVersion, category })));
        }
        return result;
    }

    public static IReadOnlyList<SecurityIncomeMappedRow> ParseSecurityIncome(FundWorkbookSheetEnvelope sheet, IFundPortfolioValueNormalizer normalizer)
    {
        var result = new List<SecurityIncomeMappedRow>();
        foreach (var row in DataRows(sheet, normalizer, out var headers))
        {
            var name = ReadText(row, headers, IncomeColumn.Name, normalizer);
            if (string.IsNullOrWhiteSpace(name) || IsTotal(name, normalizer)) continue;
            var dividend = ReadDecimal(row, headers, IncomeColumn.Dividend, normalizer, out _);
            var unrealized = ReadDecimal(row, headers, IncomeColumn.Unrealized, normalizer, out _);
            var realized = ReadDecimal(row, headers, IncomeColumn.Realized, normalizer, out _);
            var total = ReadDecimal(row, headers, IncomeColumn.Total, normalizer, out _);
            if (dividend is null && unrealized is null && realized is null && total is null) continue;
            result.Add(new(row.Key, name, dividend, unrealized, realized, total, InferPeriod(row, sheet), FirstAddress(row), Evidence(sheet, row, new { mapping = MappingVersion })));
        }
        return result;
    }

    public static IReadOnlyList<DividendMappedRow> ParseDividends(FundWorkbookSheetEnvelope sheet, IFundPortfolioValueNormalizer normalizer)
    {
        var result = new List<DividendMappedRow>();
        foreach (var row in DataRows(sheet, normalizer, out var headers))
        {
            var name = ReadText(row, headers, IncomeColumn.Name, normalizer);
            if (string.IsNullOrWhiteSpace(name) || IsTotal(name, normalizer)) continue;
            var rawDate = ReadText(row, headers, IncomeColumn.MeetingDate, normalizer);
            TryParseJalaliDate(rawDate, out var date);
            result.Add(new(row.Key, name, rawDate, date == default ? null : date,
                ReadDecimal(row, headers, IncomeColumn.Quantity, normalizer, out _), ReadDecimal(row, headers, IncomeColumn.Dps, normalizer, out _),
                ReadDecimal(row, headers, IncomeColumn.Gross, normalizer, out _), ReadDecimal(row, headers, IncomeColumn.Discount, normalizer, out _),
                ReadDecimal(row, headers, IncomeColumn.Net, normalizer, out _), InferPeriod(row, sheet), FirstAddress(row), Evidence(sheet, row, new { mapping = MappingVersion })));
        }
        return result;
    }

    public static IReadOnlyList<CommodityIncomeMappedRow> ParseCommodityIncome(FundWorkbookSheetEnvelope sheet, IFundPortfolioValueNormalizer normalizer)
    {
        var result = new List<CommodityIncomeMappedRow>();
        foreach (var row in DataRows(sheet, normalizer, out var headers))
        {
            var name = ReadText(row, headers, IncomeColumn.Name, normalizer);
            if (string.IsNullOrWhiteSpace(name) || IsTotal(name, normalizer)) continue;
            var unrealized = ReadDecimal(row, headers, IncomeColumn.Unrealized, normalizer, out _);
            var realized = ReadDecimal(row, headers, IncomeColumn.Realized, normalizer, out _);
            var total = ReadDecimal(row, headers, IncomeColumn.Total, normalizer, out _);
            if (unrealized is null && realized is null && total is null) continue;
            result.Add(new(row.Key, name, unrealized, realized, total, InferPeriod(row, sheet), FirstAddress(row), Evidence(sheet, row, new { mapping = MappingVersion })));
        }
        return result;
    }

    public static IReadOnlyList<DepositIncomeMappedRow> ParseDepositIncome(FundWorkbookSheetEnvelope sheet, IFundPortfolioValueNormalizer normalizer)
    {
        var result = new List<DepositIncomeMappedRow>();
        foreach (var row in DataRows(sheet, normalizer, out var headers))
        {
            var name = ReadText(row, headers, IncomeColumn.Name, normalizer);
            if (string.IsNullOrWhiteSpace(name) || IsTotal(name, normalizer)) continue;
            var gross = ReadDecimal(row, headers, IncomeColumn.Gross, normalizer, out _);
            var discount = ReadDecimal(row, headers, IncomeColumn.Discount, normalizer, out _);
            var net = ReadDecimal(row, headers, IncomeColumn.Net, normalizer, out _);
            if (gross is null && discount is null && net is null) continue;
            result.Add(new(row.Key, name, gross, discount, net, InferPeriod(row, sheet), FirstAddress(row), Evidence(sheet, row, new { mapping = MappingVersion })));
        }
        return result;
    }

    public static IReadOnlyList<ValuationAdjustmentMappedRow> ParseValuationAdjustments(FundWorkbookSheetEnvelope sheet, IFundPortfolioValueNormalizer normalizer)
    {
        var result = new List<ValuationAdjustmentMappedRow>();
        foreach (var row in DataRows(sheet, normalizer, out var headers))
        {
            var name = ReadText(row, headers, IncomeColumn.Name, normalizer);
            if (string.IsNullOrWhiteSpace(name) || IsTotal(name, normalizer)) continue;
            result.Add(new(row.Key, name, ReadDecimal(row, headers, IncomeColumn.Quantity, normalizer, out _), ReadDecimal(row, headers, IncomeColumn.ClosingPrice, normalizer, out _),
                ReadDecimal(row, headers, IncomeColumn.AdjustedPrice, normalizer, out _), ReadDecimal(row, headers, IncomeColumn.AdjustmentPercentage, normalizer, out _, true),
                ReadDecimal(row, headers, IncomeColumn.AdjustedValue, normalizer, out _), ReadText(row, headers, IncomeColumn.Reason, normalizer), InferPeriod(row, sheet), FirstAddress(row), Evidence(sheet, row, new { mapping = MappingVersion })));
        }
        return result;
    }

    public static FundIncomeCategory MapCategory(string raw, FundWorkbookLogicalSheetType sheetType, IFundPortfolioValueNormalizer normalizer)
    {
        var value = normalizer.NormalizeText(raw).ToLowerInvariant();
        if (Contains(value, "dividend", "سود", "سود")) return FundIncomeCategory.EquityDividend;
        if (Contains(value, "unrealized", "price change", "تغییر قیمت", "تغییر قیمت")) return FundIncomeCategory.EquityUnrealized;
        if (Contains(value, "realized", "sale", "فروش", "فروش")) return FundIncomeCategory.EquityRealized;
        if (sheetType == FundWorkbookLogicalSheetType.EquityIncomeSummary || Contains(value, "equity", "سهام", "سهام")) return FundIncomeCategory.EquityRealized;
        if (sheetType is FundWorkbookLogicalSheetType.CommodityIncomeSummary or FundWorkbookLogicalSheetType.CommodityUnrealizedIncomeDetail or FundWorkbookLogicalSheetType.CommodityRealizedIncomeDetail || Contains(value, "commodity", "گواهی", "گواهی")) return Contains(value, "change", "unrealized", "تغییر", "تغییر") ? FundIncomeCategory.CommodityUnrealized : FundIncomeCategory.CommodityRealized;
        if (sheetType is FundWorkbookLogicalSheetType.DepositIncomeSummary or FundWorkbookLogicalSheetType.DepositIncomeDetail || Contains(value, "deposit", "سپرده", "سپرده")) return FundIncomeCategory.DepositInterest;
        if (Contains(value, "other", "سایر", "سایر")) return FundIncomeCategory.OtherIncome;
        return FundIncomeCategory.Unknown;
    }

    public static bool TryParseJalaliDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var match = JalaliDateRegex().Match(raw.Trim().Replace('-', '/').Replace('.', '/'));
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, out var year) || !int.TryParse(match.Groups[2].Value, out var month) || !int.TryParse(match.Groups[3].Value, out var day)) return false;
        try { date = DateOnly.FromDateTime(new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0)); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static IEnumerable<KeyValuePair<int, Dictionary<string, FundWorkbookCellEvidence>>> DataRows(FundWorkbookSheetEnvelope sheet, IFundPortfolioValueNormalizer normalizer, out Dictionary<IncomeColumn, string> headers)
    {
        var rows = sheet.Cells.GroupBy(x => RowNumber(x.SourceAddress)).OrderBy(x => x.Key).Select(x => new KeyValuePair<int, Dictionary<string, FundWorkbookCellEvidence>>(x.Key, x.ToDictionary(x => ColumnName(x.SourceAddress), StringComparer.OrdinalIgnoreCase))).ToList();
        var selectedHeaders = new Dictionary<IncomeColumn, string>();
        var headerRow = rows.FirstOrDefault(row => row.Value.Values.Count(cell => ClassifyHeader(normalizer.NormalizeText(cell.RawValue)) is not null) >= 2);
        if (headerRow.Value is not null)
            foreach (var cell in headerRow.Value) { var column = ClassifyHeader(normalizer.NormalizeText(cell.Value.RawValue)); if (column is not null) selectedHeaders[column.Value] = cell.Key; }
        headers = selectedHeaders;
        return rows.Where(row => row.Key != headerRow.Key && selectedHeaders.Count > 0);
    }

    private static IncomeColumn? ClassifyHeader(string value)
    {
        var text = value.ToLowerInvariant();
        if (Contains(text, "security", "symbol", "stock", "company", "bank", "commodity", "category", "نام", "نام", "شرح", "شرح")) return IncomeColumn.Name;
        if (Contains(text, "cumulative", "fiscal", "year to date", "تجمیعی", "تجمیعی")) return IncomeColumn.CumulativeAmount;
        if (Contains(text, "meeting date", "dividend date", "تاریخ مجمع", "تاریخ مجمع")) return IncomeColumn.MeetingDate;
        if (Contains(text, "closing price", "close", "قیمت پایانی", "قیمت پایانی")) return IncomeColumn.ClosingPrice;
        if (Contains(text, "adjusted price", "قیمت تعدیل", "قیمت تعدیل")) return IncomeColumn.AdjustedPrice;
        if (Contains(text, "adjustment percentage", "adjustment %", "درصد تعدیل", "درصد تعدیل")) return IncomeColumn.AdjustmentPercentage;
        if (Contains(text, "adjusted value", "ارزش تعدیل", "ارزش تعدیل")) return IncomeColumn.AdjustedValue;
        if (Contains(text, "reason", "علت", "علت")) return IncomeColumn.Reason;
        if (Contains(text, "dividend per share", "dps", "سود هر سهم", "سود هر سهم")) return IncomeColumn.Dps;
        if (Contains(text, "entitled", "quantity", "shares", "تعداد", "تعداد")) return IncomeColumn.Quantity;
        if (Contains(text, "gross", "ناخالص", "ناخالص")) return IncomeColumn.Gross;
        if (Contains(text, "discount", "تخفیف", "تخفیف")) return IncomeColumn.Discount;
        if (Contains(text, "net", "خالص", "خالص")) return IncomeColumn.Net;
        if (Contains(text, "unrealized", "price change", "تغییر قیمت", "تغییر قیمت")) return IncomeColumn.Unrealized;
        if (Contains(text, "realized", "sale income", "فروش", "فروش")) return IncomeColumn.Realized;
        if (Contains(text, "dividend", "سود سهام", "سود سهام")) return IncomeColumn.Dividend;
        if (Contains(text, "total", "جمع", "جمع")) return IncomeColumn.Total;
        if (Contains(text, "income %", "income percentage", "درصد درآمد", "درصد درآمد")) return IncomeColumn.SourceIncomePercentage;
        if (Contains(text, "asset %", "asset percentage", "درصد دارایی", "درصد دارایی")) return IncomeColumn.AssetPercentage;
        if (Contains(text, "income", "amount", "مبلغ", "درآمد", "مبلغ", "درآمد")) return IncomeColumn.Amount;
        if (Contains(text, "asset %", "asset percentage", "درصد دارایی", "درصد دارایی")) return IncomeColumn.AssetPercentage;
        return null;
    }

    private static string? ReadText(Dictionary<string, FundWorkbookCellEvidence> row, Dictionary<IncomeColumn, string> headers, IncomeColumn column, IFundPortfolioValueNormalizer normalizer) => headers.TryGetValue(column, out var key) && row.TryGetValue(key, out var cell) ? normalizer.NormalizeText(cell.RawValue) : null;
    private static string? ReadText(KeyValuePair<int, Dictionary<string, FundWorkbookCellEvidence>> row, Dictionary<IncomeColumn, string> headers, IncomeColumn column, IFundPortfolioValueNormalizer normalizer) => ReadText(row.Value, headers, column, normalizer);
    private static decimal? ReadDecimal(Dictionary<string, FundWorkbookCellEvidence> row, Dictionary<IncomeColumn, string> headers, IncomeColumn column, IFundPortfolioValueNormalizer normalizer, out bool formulaError, bool percentage = false)
    {
        formulaError = false; if (!headers.TryGetValue(column, out var key) || !row.TryGetValue(key, out var cell) || string.IsNullOrWhiteSpace(cell.RawValue)) return null;
        if (normalizer.IsExcelError(cell.RawValue)) { formulaError = true; return null; }
        if (!normalizer.TryParseDecimal(cell.RawValue, out var value)) return null;
        var disclosedAsPercent = normalizer.NormalizeText(cell.RawValue).Contains('%', StringComparison.Ordinal);
        return percentage && disclosedAsPercent ? value * 100m : value;
    }

    private static decimal? ReadDecimal(KeyValuePair<int, Dictionary<string, FundWorkbookCellEvidence>> row, Dictionary<IncomeColumn, string> headers, IncomeColumn column, IFundPortfolioValueNormalizer normalizer, out bool formulaError, bool percentage = false) => ReadDecimal(row.Value, headers, column, normalizer, out formulaError, percentage);

    private static FundWorkbookPeriodContext InferPeriod(Dictionary<string, FundWorkbookCellEvidence> row, FundWorkbookSheetEnvelope sheet)
    {
        var text = string.Join(' ', row.Values.Select(x => $"{x.RawValue} {x.HeaderPath} {x.PeriodContext}")).ToLowerInvariant();
        return Contains(text, "cumulative", "fiscal", "year to date", "تجمیعی", "تجمیعی", "ytd") ? FundWorkbookPeriodContext.FiscalYearToDate : FundWorkbookPeriodContext.CurrentPeriod;
    }

    private static bool IsTotal(string value, IFundPortfolioValueNormalizer normalizer) => Contains(normalizer.NormalizeText(value), "total", "جمع", "جمع", "sum");
    private static FundWorkbookPeriodContext InferPeriod(KeyValuePair<int, Dictionary<string, FundWorkbookCellEvidence>> row, FundWorkbookSheetEnvelope sheet) => InferPeriod(row.Value, sheet);
    private static bool Contains(string value, params string[] values) => values.Any(value.Contains);
    private static int RowNumber(string address) { var match = Regex.Match(address, @"\d+$"); return match.Success && int.TryParse(match.Value, out var row) ? row : 0; }
    private static string ColumnName(string address) => Regex.Match(address, "^[A-Za-z]+").Value;
    private static string FirstAddress(Dictionary<string, FundWorkbookCellEvidence> row) => row.Values.FirstOrDefault()?.SourceAddress ?? string.Empty;
    private static string FirstAddress(KeyValuePair<int, Dictionary<string, FundWorkbookCellEvidence>> row) => FirstAddress(row.Value);
    private static string Evidence(FundWorkbookSheetEnvelope sheet, Dictionary<string, FundWorkbookCellEvidence> row, object extra) => JsonSerializer.Serialize(new { sheet = sheet.OriginalSheetName, row = row.ToDictionary(x => x.Key, x => x.Value.RawValue), extra });
    private static string Evidence(FundWorkbookSheetEnvelope sheet, KeyValuePair<int, Dictionary<string, FundWorkbookCellEvidence>> row, object extra) => Evidence(sheet, row.Value, extra);

    [GeneratedRegex(@"(\d{4})[/-](\d{1,2})[/-](\d{1,2})")]
    private static partial Regex JalaliDateRegex();
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed record FundAssetAllocationMappedRow(
    int SourceLogicalRow,
    string RawLabel,
    FundAssetClass AssetClass,
    decimal? CostAmount,
    decimal? MarketOrNetSaleValue,
    decimal? WeightOfTotalAssetsPercentage,
    bool IsSectionTotal,
    bool HasSourceFormulaError,
    string? SourceAddress,
    string SourceEvidenceJson,
    FundWorkbookPeriodContext PeriodContext);

public sealed record FundBankDepositMappedRow(
    int SourceLogicalRow,
    string RawBankName,
    string NormalizedBankName,
    string? BankCode,
    decimal? BeginningBalance,
    decimal? IncreaseAmount,
    decimal? DecreaseAmount,
    decimal? EndingBalance,
    decimal? WeightOfTotalAssetsPercentage,
    bool IsSectionTotal,
    string? SourceAddress,
    string SourceEvidenceJson,
    FundWorkbookPeriodContext PeriodContext);

public sealed record FundDerivativeMappedRow(
    int SourceLogicalRow,
    FundDerivativeType DerivativeType,
    FundOptionType OptionType,
    FundPositionSide PositionSide,
    string RawInstrumentName,
    string NormalizedInstrumentName,
    string? RawUnderlyingName,
    decimal? ContractQuantity,
    decimal? ContractMultiplier,
    decimal? UnderlyingCoverageQuantity,
    decimal? StrikePrice,
    string? ExpiryOrExerciseJalali,
    DateOnly? ExpiryOrExerciseDate,
    bool HasImpossibleDate,
    decimal? EffectiveReturnPercentage,
    decimal? CostAmount,
    decimal? MarketValue,
    decimal? WeightOfTotalAssetsPercentage,
    string? SourceAddress,
    string SourceEvidenceJson,
    FundWorkbookPeriodContext PeriodContext);

public static partial class FundNonEquitySheetMapping
{
    public const string AssetAllocationMappingVersion = "iran-fund-asset-allocation-header-v1";
    public const string CommodityMappingVersion = "iran-fund-commodity-certificate-header-v1";
    public const string BankDepositMappingVersion = "iran-fund-bank-deposit-header-v1";
    public const string DerivativeMappingVersion = "iran-fund-derivative-block-header-v1";

    private enum AllocationColumn { Label, Cost, Value, Weight }
    private enum DepositColumn { Bank, Beginning, Increase, Decrease, Ending, Weight }
    private enum DerivativeColumn { Instrument, Underlying, Quantity, Multiplier, Coverage, Strike, Date, Return, Side, Cost, Value, Weight }

    public static IReadOnlyList<FundAssetAllocationMappedRow> ParseAssetAllocation(
        FundWorkbookSheetEnvelope sheet,
        IFundPortfolioValueNormalizer normalizer)
    {
        var rows = Rows(sheet);
        var headerRow = FindHeaderRow(rows, normalizer, "دارایی", "سرمایه گذاری", "asset", "بهای تمام", "ارزش روز", "درصد", "cost", "value", "weight");
        if (headerRow == 0) return [];
        var headers = BuildHeaders(rows, headerRow, normalizer, ClassifyAllocationColumn);
        var labelColumn = headers.FirstOrDefault(x => x.Value == AllocationColumn.Label).Key;
        if (labelColumn == 0) labelColumn = headers.Keys.OrderBy(x => x).FirstOrDefault();
        if (labelColumn == 0) return [];
        var result = new List<FundAssetAllocationMappedRow>();
        foreach (var row in rows.Where(x => x.Key > headerRow))
        {
            var cells = ByColumn(row);
            if (!cells.TryGetValue(labelColumn, out var labelCell) || string.IsNullOrWhiteSpace(labelCell.RawValue)) continue;
            var rawLabel = labelCell.RawValue.Trim();
            var normalized = normalizer.NormalizeText(rawLabel);
            var sourceCells = row.ToDictionary(x => x.SourceAddress, x => x.RawValue);
            result.Add(new(
                row.Key,
                rawLabel,
                MapAssetClass(normalized),
                Read(headers, cells, AllocationColumn.Cost, normalizer),
                Read(headers, cells, AllocationColumn.Value, normalizer),
                Read(headers, cells, AllocationColumn.Weight, normalizer, percentagePoints: true),
                IsTotal(normalized),
                row.Any(x => normalizer.IsExcelError(x.RawValue)),
                labelCell.SourceAddress,
                Evidence(AssetAllocationMappingVersion, sheet, row.Key, sourceCells),
                DetectPeriodContext(row, FundWorkbookPeriodContext.CurrentPeriod)));
        }
        return result;
    }

    public static IReadOnlyList<FundEquityMappedRow> ParseCommodityCertificates(
        FundWorkbookSheetEnvelope sheet,
        IFundPortfolioValueNormalizer normalizer)
    {
        var mapped = FundEquitySheetMapping.Parse(sheet, FundWorkbookPeriodContext.CurrentPeriod, normalizer);
        return mapped.Select(row => row with
        {
            SourceEvidenceJson = JsonSerializer.Serialize(new
            {
                mappingVersion = CommodityMappingVersion,
                source = JsonDocument.Parse(row.SourceEvidenceJson).RootElement
            })
        }).ToArray();
    }

    public static IReadOnlyList<FundBankDepositMappedRow> ParseBankDeposits(
        FundWorkbookSheetEnvelope sheet,
        IFundPortfolioValueNormalizer normalizer)
    {
        var rows = Rows(sheet);
        var headerRow = FindHeaderRow(rows, normalizer, "بانک", "سپرده", "ابتدای دوره", "افزایش", "کاهش", "پایان دوره", "bank", "deposit", "beginning", "increase", "decrease", "ending", "weight");
        if (headerRow == 0) return [];
        var headers = BuildHeaders(rows, headerRow, normalizer, ClassifyDepositColumn);
        var bankColumn = headers.FirstOrDefault(x => x.Value == DepositColumn.Bank).Key;
        if (bankColumn == 0) bankColumn = headers.Keys.OrderBy(x => x).FirstOrDefault();
        if (bankColumn == 0) return [];
        var result = new List<FundBankDepositMappedRow>();
        foreach (var row in rows.Where(x => x.Key > headerRow))
        {
            var cells = ByColumn(row);
            if (!cells.TryGetValue(bankColumn, out var bankCell) || string.IsNullOrWhiteSpace(bankCell.RawValue)) continue;
            var rawBank = bankCell.RawValue.Trim();
            var normalized = NormalizeBankName(normalizer.NormalizeText(rawBank));
            result.Add(new(
                row.Key,
                rawBank,
                normalized,
                FundBankCatalog.Resolve(normalized),
                Read(headers, cells, DepositColumn.Beginning, normalizer),
                Read(headers, cells, DepositColumn.Increase, normalizer),
                Read(headers, cells, DepositColumn.Decrease, normalizer),
                Read(headers, cells, DepositColumn.Ending, normalizer),
                Read(headers, cells, DepositColumn.Weight, normalizer, percentagePoints: true),
                IsTotal(normalized),
                bankCell.SourceAddress,
                Evidence(BankDepositMappingVersion, sheet, row.Key, row.ToDictionary(x => x.SourceAddress, x => x.RawValue)),
                DetectPeriodContext(row, FundWorkbookPeriodContext.CurrentPeriod)));
        }
        return result;
    }

    public static IReadOnlyList<FundDerivativeMappedRow> ParseDerivatives(
        FundWorkbookSheetEnvelope sheet,
        IFundPortfolioValueNormalizer normalizer)
    {
        var rows = Rows(sheet);
        var result = new List<FundDerivativeMappedRow>();
        FundDerivativeType currentSection = FundDerivativeType.Unknown;
        Dictionary<int, DerivativeColumn>? headers = null;
        var instrumentColumn = 0;
        foreach (var row in rows)
        {
            var rowText = normalizer.NormalizeText(string.Join(' ', row.Select(x => x.RawValue))).ToLowerInvariant();
            if (ContainsAny(rowText, "اختیار فروش تبعی", "اوراق تبعی", "protective put")) currentSection = FundDerivativeType.ProtectivePut;
            else if (ContainsAny(rowText, "موقعیت اختیار", "اختیار معامله", "ordinary option", "option position")) currentSection = FundDerivativeType.ExchangeTradedOption;

            if (row.Count() == 1 && currentSection != FundDerivativeType.Unknown)
            {
                headers = null;
                instrumentColumn = 0;
                continue;
            }

            var candidateHeaders = BuildSingleRowHeaders(row, normalizer, ClassifyDerivativeColumn);
            if (candidateHeaders.Values.Contains(DerivativeColumn.Instrument) && candidateHeaders.Count >= 2)
            {
                headers = candidateHeaders;
                instrumentColumn = headers.First(x => x.Value == DerivativeColumn.Instrument).Key;
                continue;
            }
            if (headers is null || instrumentColumn == 0) continue;
            var cells = ByColumn(row);
            if (!cells.TryGetValue(instrumentColumn, out var instrumentCell) || string.IsNullOrWhiteSpace(instrumentCell.RawValue)) continue;
            var rawName = instrumentCell.RawValue.Trim();
            var normalizedName = FundEquitySheetMapping.NormalizeSecurityName(normalizer.NormalizeText(rawName));
            if (IsTotal(normalizedName)) continue;
            var rawUnderlying = ReadText(headers, cells, DerivativeColumn.Underlying);
            var parsedName = ParseDerivativeName(normalizedName, rawUnderlying, normalizer);
            var rawDate = ReadText(headers, cells, DerivativeColumn.Date) ?? parsedName.JalaliDate;
            var hasDate = !string.IsNullOrWhiteSpace(rawDate);
            var validDate = TryParseJalaliDate(rawDate, out var gregorianDate);
            var section = currentSection == FundDerivativeType.Unknown
                ? parsedName.IsProtective ? FundDerivativeType.ProtectivePut : FundDerivativeType.ExchangeTradedOption
                : currentSection;
            var quantity = Read(headers, cells, DerivativeColumn.Quantity, normalizer);
            var disclosedCoverage = Read(headers, cells, DerivativeColumn.Coverage, normalizer);
            result.Add(new(
                row.Key,
                section,
                parsedName.OptionType,
                ParseSide(ReadText(headers, cells, DerivativeColumn.Side), normalizer),
                rawName,
                normalizedName,
                rawUnderlying ?? parsedName.Underlying,
                quantity,
                null,
                disclosedCoverage,
                Read(headers, cells, DerivativeColumn.Strike, normalizer) ?? parsedName.Strike,
                rawDate,
                validDate ? gregorianDate : null,
                hasDate && !validDate,
                Read(headers, cells, DerivativeColumn.Return, normalizer, percentagePoints: true),
                Read(headers, cells, DerivativeColumn.Cost, normalizer),
                Read(headers, cells, DerivativeColumn.Value, normalizer),
                Read(headers, cells, DerivativeColumn.Weight, normalizer, percentagePoints: true),
                instrumentCell.SourceAddress,
                Evidence(DerivativeMappingVersion, sheet, row.Key, row.ToDictionary(x => x.SourceAddress, x => x.RawValue)),
                DetectPeriodContext(row, FundWorkbookPeriodContext.CurrentPeriod)));
        }
        return result;
    }

    public static FundAssetClass MapAssetClass(string normalizedLabel) =>
        ContainsAny(normalizedLabel, "سهام", "حق تقدم", "equity", "stock") ? FundAssetClass.EquityAndRights :
        ContainsAny(normalizedLabel, "گواهی سپرده", "کالا", "طلا", "commodity") ? FundAssetClass.CommodityCertificates :
        ContainsAny(normalizedLabel, "سپرده بانکی", "سپرده بانک", "bank deposit") ? FundAssetClass.BankDeposits :
        ContainsAny(normalizedLabel, "مشتقه", "اختیار", "derivative", "option") ? FundAssetClass.Derivatives :
        ContainsAny(normalizedLabel, "نقد", "سایر", "cash", "other") ? FundAssetClass.CashAndOther : FundAssetClass.Unknown;

    public static string? ExtractInstrumentSymbol(string rawName)
    {
        var match = InstrumentSymbolRegex().Match(rawName);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public static bool TryParseJalaliDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var normalized = raw.Trim().Replace('-', '/').Replace('.', '/');
        var match = JalaliDateRegex().Match(normalized);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var year) || !int.TryParse(match.Groups[2].Value, out var month) || !int.TryParse(match.Groups[3].Value, out var day)) return false;
        try
        {
            var value = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
            date = DateOnly.FromDateTime(value);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static DerivativeNameParts ParseDerivativeName(string value, string? disclosedUnderlying, IFundPortfolioValueNormalizer normalizer)
    {
        var optionType = ContainsAny(value, "اختیار فروش", "put") ? FundOptionType.Put : ContainsAny(value, "اختیار خرید", "call") ? FundOptionType.Call : FundOptionType.Unknown;
        var isProtective = ContainsAny(value, "تبعی", "حمایتی", "protective");
        var strikeMatch = StrikeRegex().Match(value);
        decimal? strike = strikeMatch.Success && normalizer.TryParseDecimal(strikeMatch.Groups[1].Value, out var strikeValue) ? strikeValue : null;
        var dateMatch = JalaliDateRegex().Match(value);
        var underlying = disclosedUnderlying;
        if (string.IsNullOrWhiteSpace(underlying))
        {
            var underlyingMatch = UnderlyingRegex().Match(value);
            if (underlyingMatch.Success) underlying = underlyingMatch.Groups[1].Value.Trim();
        }
        return new(optionType, isProtective, underlying, strike, dateMatch.Success ? dateMatch.Value : null);
    }

    private static FundPositionSide ParseSide(string? value, IFundPortfolioValueNormalizer normalizer)
    {
        var normalized = normalizer.NormalizeText(value).ToLowerInvariant();
        return ContainsAny(normalized, "خرید", "دارنده", "long") ? FundPositionSide.Long :
            ContainsAny(normalized, "فروشنده", "موقعیت فروش", "short") ? FundPositionSide.Short : FundPositionSide.Unknown;
    }

    private static string NormalizeBankName(string value)
    {
        var normalized = value.Trim();
        foreach (var suffix in new[] { " شعبه مرکزی", " شعبه مرکزي", " حساب جاری", " حساب سپرده", " سپرده" })
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^suffix.Length].Trim();
        return normalized;
    }

    private static AllocationColumn? ClassifyAllocationColumn(string text)
    {
        if (ContainsAny(text, "نوع دارایی", "عنوان", "شرح", "asset class", "asset")) return AllocationColumn.Label;
        if (ContainsAny(text, "درصد", "weight", "percent")) return AllocationColumn.Weight;
        if (ContainsAny(text, "بهای تمام", "بهاي تمام", "cost")) return AllocationColumn.Cost;
        if (ContainsAny(text, "ارزش روز", "خالص ارزش فروش", "net sale", "market value", "value")) return AllocationColumn.Value;
        return null;
    }

    private static DepositColumn? ClassifyDepositColumn(string text)
    {
        if (ContainsAny(text, "نام بانک", "بانک", "bank")) return DepositColumn.Bank;
        if (ContainsAny(text, "درصد", "weight", "percent")) return DepositColumn.Weight;
        if (ContainsAny(text, "ابتدای دوره", "اول دوره", "beginning", "opening")) return DepositColumn.Beginning;
        if (ContainsAny(text, "افزایش", "واریز", "increase")) return DepositColumn.Increase;
        if (ContainsAny(text, "کاهش", "برداشت", "decrease")) return DepositColumn.Decrease;
        if (ContainsAny(text, "پایان دوره", "انتهای دوره", "ending", "closing")) return DepositColumn.Ending;
        return null;
    }

    private static DerivativeColumn? ClassifyDerivativeColumn(string text)
    {
        if (ContainsAny(text, "نام قرارداد", "نام اختیار", "نماد", "contract", "instrument")) return DerivativeColumn.Instrument;
        if (ContainsAny(text, "دارایی پایه", "دارايي پايه", "underlying")) return DerivativeColumn.Underlying;
        if (ContainsAny(text, "ضریب", "ضريب", "multiplier")) return DerivativeColumn.Multiplier;
        if (ContainsAny(text, "پوشش", "تعداد سهام پایه", "coverage")) return DerivativeColumn.Coverage;
        if (ContainsAny(text, "تعداد", "quantity", "contracts")) return DerivativeColumn.Quantity;
        if (ContainsAny(text, "قیمت اعمال", "قيمت اعمال", "strike")) return DerivativeColumn.Strike;
        if (ContainsAny(text, "تاریخ اعمال", "تاريخ اعمال", "سررسید", "سررسيد", "expiry", "exercise date")) return DerivativeColumn.Date;
        if (ContainsAny(text, "بازده", "return")) return DerivativeColumn.Return;
        if (ContainsAny(text, "موقعیت", "موقعيت", "side")) return DerivativeColumn.Side;
        if (ContainsAny(text, "بهای تمام", "بهاي تمام", "cost")) return DerivativeColumn.Cost;
        if (ContainsAny(text, "ارزش روز", "market value", "value")) return DerivativeColumn.Value;
        if (ContainsAny(text, "درصد", "weight", "percent")) return DerivativeColumn.Weight;
        return null;
    }

    private static IGrouping<int, FundWorkbookCellEvidence>[] Rows(FundWorkbookSheetEnvelope sheet) =>
        sheet.Cells.Where(x => CellAddress.TryGetRow(x.SourceAddress, out _)).GroupBy(x => CellAddress.Row(x.SourceAddress)).OrderBy(x => x.Key).ToArray();

    private static int FindHeaderRow(IGrouping<int, FundWorkbookCellEvidence>[] rows, IFundPortfolioValueNormalizer normalizer, params string[] terms) =>
        rows.Take(25).Where(row => terms.Count(term => normalizer.NormalizeText(string.Join(' ', row.Select(x => x.RawValue))).Contains(term, StringComparison.OrdinalIgnoreCase)) >= 2).Select(row => row.Key).LastOrDefault();

    private static Dictionary<int, T> BuildHeaders<T>(IGrouping<int, FundWorkbookCellEvidence>[] rows, int headerRow, IFundPortfolioValueNormalizer normalizer, Func<string, T?> classifier) where T : struct
    {
        var result = new Dictionary<int, T>();
        foreach (var column in rows.Where(x => x.Key <= headerRow).SelectMany(x => x).Select(x => CellAddress.Column(x.SourceAddress)).Distinct())
        {
            var text = normalizer.NormalizeText(string.Join(' ', rows.Where(x => x.Key <= headerRow).SelectMany(x => x).Where(x => CellAddress.Column(x.SourceAddress) == column).Select(x => x.RawValue))).ToLowerInvariant();
            var kind = classifier(text); if (kind.HasValue) result[column] = kind.Value;
        }
        return result;
    }

    private static Dictionary<int, T> BuildSingleRowHeaders<T>(IGrouping<int, FundWorkbookCellEvidence> row, IFundPortfolioValueNormalizer normalizer, Func<string, T?> classifier) where T : struct
    {
        var result = new Dictionary<int, T>();
        foreach (var cell in row)
        {
            var kind = classifier(normalizer.NormalizeText(cell.RawValue).ToLowerInvariant());
            if (kind.HasValue) result[CellAddress.Column(cell.SourceAddress)] = kind.Value;
        }
        return result;
    }

    private static Dictionary<int, FundWorkbookCellEvidence> ByColumn(IGrouping<int, FundWorkbookCellEvidence> row) => row.GroupBy(x => CellAddress.Column(x.SourceAddress)).ToDictionary(x => x.Key, x => x.First());
    private static decimal? Read<T>(IReadOnlyDictionary<int, T> headers, IReadOnlyDictionary<int, FundWorkbookCellEvidence> cells, T kind, IFundPortfolioValueNormalizer normalizer, bool percentagePoints = false) where T : struct, Enum
    {
        var column = headers.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, kind)).Key;
        if (column == 0 || !cells.TryGetValue(column, out var cell) || string.IsNullOrWhiteSpace(cell.RawValue) || normalizer.IsExcelError(cell.RawValue)) return null;
        if (!normalizer.TryParseDecimal(cell.RawValue, out var value)) return null;
        return percentagePoints && cell.RawValue.Contains('%') ? value * 100m : value;
    }
    private static string? ReadText<T>(IReadOnlyDictionary<int, T> headers, IReadOnlyDictionary<int, FundWorkbookCellEvidence> cells, T kind) where T : struct, Enum
    {
        var column = headers.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, kind)).Key;
        return column != 0 && cells.TryGetValue(column, out var cell) && !string.IsNullOrWhiteSpace(cell.RawValue) ? cell.RawValue.Trim() : null;
    }
    private static FundWorkbookPeriodContext DetectPeriodContext(IGrouping<int, FundWorkbookCellEvidence> row, FundWorkbookPeriodContext fallback) =>
        row.Select(x => x.PeriodContext).FirstOrDefault(value => Enum.TryParse<FundWorkbookPeriodContext>(value, true, out _)) is { } value && Enum.TryParse<FundWorkbookPeriodContext>(value, true, out var parsed) ? parsed : fallback;
    private static string Evidence(string version, FundWorkbookSheetEnvelope sheet, int row, IReadOnlyDictionary<string, string?> cells) => JsonSerializer.Serialize(new { mappingVersion = version, sheet = sheet.OriginalSheetName, sourceLogicalRow = row, cells });
    private static bool IsTotal(string value) => ContainsAny(value, "جمع", "مجموع", "کل", "total", "subtotal");
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private sealed record DerivativeNameParts(FundOptionType OptionType, bool IsProtective, string? Underlying, decimal? Strike, string? JalaliDate);

    [GeneratedRegex(@"(?:نماد|symbol|code)\s*[:：-]?\s*([\p{L}\p{N}_-]{2,32})", RegexOptions.IgnoreCase)]
    private static partial Regex InstrumentSymbolRegex();
    [GeneratedRegex(@"(1[34]\d{2})[/.-](\d{1,2})[/.-](\d{1,2})")]
    private static partial Regex JalaliDateRegex();
    [GeneratedRegex(@"(?:قیمت\s*اعمال|strike)\s*[:：-]?\s*([\d۰-۹٠-٩,.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex StrikeRegex();
    [GeneratedRegex(@"(?:دارایی\s*پایه|دارايي\s*پايه|underlying)\s*[:：-]?\s*([^،,;|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UnderlyingRegex();

    private static class CellAddress
    {
        public static bool TryGetRow(string address, out int row) { row = Row(address); return row > 0; }
        public static int Row(string address) => int.TryParse(new string(address.SkipWhile(char.IsLetter).ToArray()), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        public static int Column(string address)
        {
            var value = 0;
            foreach (var character in address.TakeWhile(char.IsLetter).Select(char.ToUpperInvariant)) value = value * 26 + character - 'A' + 1;
            return value;
        }
    }
}

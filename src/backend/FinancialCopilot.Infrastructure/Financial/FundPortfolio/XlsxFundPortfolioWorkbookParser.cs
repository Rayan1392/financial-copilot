using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed record FundPortfolioWorkbookLimits(
    long MaxFileBytes = 50 * 1024 * 1024,
    long MaxUncompressedBytes = 250 * 1024 * 1024,
    int MaxSheets = 50,
    int MaxRowsPerSheet = 20_000,
    int MaxColumnsPerSheet = 200,
    int MaxCellTextLength = 10_000,
    int MaxParsedCells = 1_000_000);

public sealed class XlsxFundPortfolioWorkbookParser(
    IFundPortfolioValueNormalizer normalizer,
    FundPortfolioWorkbookLimits? limits = null) : IFundPortfolioWorkbookParser
{
    public const string ProfileVersion = "iran-fund-portfolio-workbook-v1";
    public const string ClassifierVersion = "iran-fund-portfolio-sheet-classifier-v1";
    private readonly FundPortfolioWorkbookLimits limits = limits ?? new();

    public async Task<FundPortfolioWorkbookEnvelope> ParseAsync(
        FundPortfolioWorkbookParseRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(request.OriginalFileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .xlsx workbooks are supported.");
        if (request.Workbook.CanSeek && request.Workbook.Length > limits.MaxFileBytes)
            throw new InvalidDataException("Workbook exceeds the configured size limit.");

        using var input = new MemoryStream();
        await request.Workbook.CopyToAsync(input, cancellationToken);
        if (input.Length > limits.MaxFileBytes) throw new InvalidDataException("Workbook exceeds the configured size limit.");
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var uncompressed = archive.Entries.Sum(entry => entry.Length);
        if (uncompressed > limits.MaxUncompressedBytes || (input.Length > 0 && uncompressed / (double)input.Length > 100))
            throw new InvalidDataException("Workbook compression or expanded size exceeds the configured limit.");
        if (archive.Entries.Any(entry => entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.Contains("embeddings/", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.Contains("externalLinks/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Macros, embedded objects, and external links are not accepted.");

        var sharedStrings = ReadSharedStrings(archive);
        var sheetRefs = ReadSheets(archive);
        if (sheetRefs.Count > limits.MaxSheets) throw new InvalidDataException("Workbook exceeds the sheet limit.");
        var allIssues = new List<FundPortfolioExtractionIssue>();
        var sheets = new List<FundWorkbookSheetEnvelope>();
        var parsedCells = 0;
        foreach (var sheetRef in sheetRefs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheetIssues = new List<FundPortfolioExtractionIssue>();
            var cells = ReadCells(archive, sheetRef.Path, sharedStrings, sheetRef.Name, sheetRef.Index, request.ParserProfileVersion, request.ReportId, sheetIssues, ref parsedCells);
            var normalizedName = normalizer.NormalizeText(sheetRef.Name);
            var classification = Classify(normalizedName, cells);
            var sheetId = DeterministicGuid(request.ReportId, $"sheet:{sheetRef.Index}:{sheetRef.Name}");
            foreach (var issue in sheetIssues) allIssues.Add(issue with { SheetId = sheetId });
            sheets.Add(new FundWorkbookSheetEnvelope(sheetId, sheetRef.Name, normalizedName, classification.Type,
                sheetRef.Index, FindUsedRange(cells), classification.Confidence, classification.Fingerprint,
                ClassifierVersion, cells, sheetIssues.Select(issue => issue with { SheetId = sheetId }).ToArray()));
        }

        var metadata = ExtractMetadata(sheets, request.ReportId, request.ParserProfileVersion, allIssues);
        var period = request.KnownPeriod ?? new FundPortfolioReportPeriod(
            metadata.PeriodEndJalali, metadata.PeriodEndDate, PeriodEndText: metadata.PeriodEndText);
        var duplicateTypes = sheets.Where(sheet => sheet.LogicalSheetType is not FundWorkbookLogicalSheetType.Unclassified and not FundWorkbookLogicalSheetType.FormulaOrControlSheetIgnored)
            .GroupBy(sheet => sheet.LogicalSheetType).Where(group => group.Count() > 1);
        foreach (var duplicate in duplicateTypes)
            allIssues.Add(Issue(request.ReportId, FundExtractionIssueSeverity.Warning, "DUPLICATE_LOGICAL_SHEET_TYPE", null, null,
                $"Logical sheet type '{duplicate.Key}' occurs more than once and requires review.", request.ParserProfileVersion));
        if (sheets.Count == 0) allIssues.Add(Issue(request.ReportId, FundExtractionIssueSeverity.Fatal, "NO_SHEETS", null, null, "Workbook has no readable sheets.", request.ParserProfileVersion));
        var cover = sheets.FirstOrDefault(sheet => sheet.LogicalSheetType == FundWorkbookLogicalSheetType.ReportCover);
        var coverValues = cover?.Cells.Select(cell => cell.NormalizedValue).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
        var title = coverValues.FirstOrDefault(value => value is not null && value.Contains("وضعیت", StringComparison.OrdinalIgnoreCase));
        var fundName = coverValues.FirstOrDefault(value => value is not null && value != title && !JalaliDateRegex.IsMatch(value));
        return new FundPortfolioWorkbookEnvelope(request.ReportId, request.FundId, request.ProviderName,
            request.OriginalFileName, request.FileSha256, request.ParserProfileVersion, period, sheets, allIssues)
        { ExtractedFundName = metadata.FundName, ReportTitle = metadata.ReportTitle };
    }

    private List<FundWorkbookCellEvidence> ReadCells(ZipArchive archive, string path, IReadOnlyList<string> sharedStrings,
        string sheetName, int sheetIndex, string profile, Guid reportId, List<FundPortfolioExtractionIssue> issues, ref int parsedCells)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Sheet '{sheetName}' is missing.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        var rows = document.Descendants(SpreadsheetNamespace + "row").ToArray();
        if (rows.Length > limits.MaxRowsPerSheet) throw new InvalidDataException($"Sheet '{sheetName}' exceeds the row limit.");
        var cells = new List<FundWorkbookCellEvidence>();
        foreach (var row in rows)
        {
            var rowCells = row.Elements(SpreadsheetNamespace + "c").ToArray();
            if (rowCells.Length > limits.MaxColumnsPerSheet)
                throw new InvalidDataException($"Sheet '{sheetName}' exceeds the column limit.");
            foreach (var cell in rowCells)
            {
            if (++parsedCells > limits.MaxParsedCells) throw new InvalidDataException("Workbook exceeds the parsed-cell limit.");
            var address = (string?)cell.Attribute("r") ?? string.Empty;
            var raw = ReadCellValue(cell, sharedStrings);
            if (raw?.Length > limits.MaxCellTextLength) throw new InvalidDataException("Cell text exceeds the configured limit.");
            var formula = cell.Element(SpreadsheetNamespace + "f")?.Value;
            if (normalizer.IsExcelError(raw))
                issues.Add(Issue(reportId, FundExtractionIssueSeverity.Error, "EXCEL_ERROR_VALUE", address, raw,
                    "Excel error token was retained as an issue and was not converted to zero.", profile));
                cells.Add(new FundWorkbookCellEvidence(sheetName, sheetIndex, address, raw, normalizer.NormalizeText(raw), formula,
                    null, null, profile));
            }
        }
        return cells;
    }

    private (FundWorkbookLogicalSheetType Type, decimal Confidence, string Fingerprint) Classify(string name, IReadOnlyList<FundWorkbookCellEvidence> cells)
    {
        if (name.Contains("\u062A\u06CC\u062A\u0631", StringComparison.OrdinalIgnoreCase))
            return (FundWorkbookLogicalSheetType.ReportCover, 0.9m, Fingerprint(name));
        var text = normalizer.NormalizeText(name + " " + string.Join(' ', cells.Take(12).Select(cell => cell.NormalizedValue)));
        var mappings = new (string[] Keys, FundWorkbookLogicalSheetType Type)[]
        {
            (new[]{"تیترا", "تیتر", "cover"}, FundWorkbookLogicalSheetType.ReportCover),
            (new[]{"سرمایه گذاری ها", "سرمایه گذاریها"}, FundWorkbookLogicalSheetType.AssetAllocationSummary),
            (new[]{"سهام 2", "سهام۲"}, FundWorkbookLogicalSheetType.EquityPortfolioComparative),
            (new[]{"سهام"}, FundWorkbookLogicalSheetType.EquityPortfolioCurrent),
            (new[]{"اوراق مشتقه", "مشتقه"}, FundWorkbookLogicalSheetType.DerivativePositions),
            (new[]{"گواهی سپرده کالایی", "در گواهی سپرده"}, FundWorkbookLogicalSheetType.CommodityCertificatePositions),
            (new[]{"سپرده بانکی", "سپرده بانک"}, FundWorkbookLogicalSheetType.BankDepositPositions),
            (new[]{"تعدیل قیمت"}, FundWorkbookLogicalSheetType.ValuationAdjustments),
            (new[]{"درآمدها"}, FundWorkbookLogicalSheetType.InvestmentIncomeSummary),
            (new[]{"سرمایه گذاری در سهام"}, FundWorkbookLogicalSheetType.EquityIncomeSummary),
            (new[]{"درآمد سود سهام"}, FundWorkbookLogicalSheetType.DividendIncomeDetail),
            (new[]{"تغییر قیمت سهام"}, FundWorkbookLogicalSheetType.EquityUnrealizedIncomeDetail),
            (new[]{"فروش سهام"}, FundWorkbookLogicalSheetType.EquityRealizedIncomeDetail),
            (new[]{"درآمد گواهی سپرده کالایی"}, FundWorkbookLogicalSheetType.CommodityIncomeSummary),
            (new[]{"تغییر قیمت گواهی سپرده"}, FundWorkbookLogicalSheetType.CommodityUnrealizedIncomeDetail),
            (new[]{"فروش گواهی سپرده"}, FundWorkbookLogicalSheetType.CommodityRealizedIncomeDetail),
            (new[]{"درآمد سپرده بانکی"}, FundWorkbookLogicalSheetType.DepositIncomeSummary),
            (new[]{"سپرده بانکی 2", "سپرده بانکی ۲"}, FundWorkbookLogicalSheetType.DepositIncomeDetail),
            (new[]{"سایر درآمدها"}, FundWorkbookLogicalSheetType.OtherIncomeDetail),
            (new[]{"0", "کنترل", "فرمول"}, FundWorkbookLogicalSheetType.FormulaOrControlSheetIgnored)
        };
        var match = mappings.FirstOrDefault(mapping => mapping.Keys.Any(key => text.Contains(key, StringComparison.OrdinalIgnoreCase)));
        return (match.Type == default && !text.Contains("0", StringComparison.Ordinal) ? FundWorkbookLogicalSheetType.Unclassified : match.Type,
            match.Type == default ? 0.05m : 0.9m, Fingerprint(text));
    }

    private WorkbookMetadata ExtractMetadata(
        IReadOnlyList<FundWorkbookSheetEnvelope> sheets,
        Guid reportId,
        string profile,
        List<FundPortfolioExtractionIssue> issues)
    {
        var candidates = new List<MetadataCandidate>();
        foreach (var sheet in sheets)
        {
            var isCover = sheet.LogicalSheetType == FundWorkbookLogicalSheetType.ReportCover;
            var cells = sheet.Cells.Where(cell => isCover || GetRowNumber(cell.SourceAddress) <= 12).ToArray();
            foreach (var cell in cells)
            {
                if (string.IsNullOrWhiteSpace(cell.RawValue)) continue;

                var normalized = MatchingText(cell.RawValue);
                AddInlineCandidate(candidates, sheet, cell, MetadataKind.FundName, FundNameLabels, normalized);
                AddInlineCandidate(candidates, sheet, cell, MetadataKind.ReportTitle, ReportTitleLabels, normalized);
                AddInlineCandidate(candidates, sheet, cell, MetadataKind.PeriodEnd, PeriodEndLabels, normalized);

                var dateMatch = JalaliDateRegex.Match(NormalizeDateText(cell.RawValue));
                if (!dateMatch.Success) continue;
                if (JalaliDateResolver.TryResolveDate(dateMatch.Value, out var date))
                {
                    candidates.Add(new MetadataCandidate(MetadataKind.PeriodEnd, cell.RawValue.Trim(),
                        dateMatch.Value, sheet, cell, date));
                }
                else
                {
                    issues.Add(Issue(reportId, FundExtractionIssueSeverity.Error, "INVALID_JALALI_DATE",
                        cell.SourceAddress, cell.RawValue,
                        "Jalali date could not be converted by the governed date converter.", profile)
                        with { SheetId = sheet.SheetId });
                    candidates.Add(new MetadataCandidate(MetadataKind.PeriodEnd, cell.RawValue.Trim(),
                        dateMatch.Value, sheet, cell, null));
                }
            }
        }

        var fund = Reconcile(candidates, MetadataKind.FundName, reportId, profile, issues);
        var title = Reconcile(candidates, MetadataKind.ReportTitle, reportId, profile, issues);
        var period = Reconcile(candidates, MetadataKind.PeriodEnd, reportId, profile, issues);
        return new WorkbookMetadata(fund?.Value, title?.Value, period?.ComparisonValue, period?.Date, period?.Value);
    }

    private void AddInlineCandidate(
        List<MetadataCandidate> candidates,
        FundWorkbookSheetEnvelope sheet,
        FundWorkbookCellEvidence cell,
        MetadataKind kind,
        IReadOnlyList<string> labels,
        string normalizedCell)
    {
        if (!labels.Any(label => normalizedCell.Contains(label, StringComparison.OrdinalIgnoreCase))) return;

        var inlineValue = ValueAfterSeparator(cell.RawValue);
        if (string.IsNullOrWhiteSpace(inlineValue))
        {
            var position = GetCellPosition(cell.SourceAddress);
            inlineValue = sheet.Cells
                .Where(candidate => GetRowNumber(candidate.SourceAddress) == position.Row &&
                    GetColumnNumber(candidate.SourceAddress) > position.Column)
                .OrderBy(candidate => GetColumnNumber(candidate.SourceAddress))
                .Select(candidate => candidate.RawValue)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
                    !labels.Any(label => string.Equals(MatchingText(value).Trim(), label,
                        StringComparison.OrdinalIgnoreCase)));
        }

        if (string.IsNullOrWhiteSpace(inlineValue)) return;
        var value = inlineValue.Trim();
        if (kind == MetadataKind.PeriodEnd)
        {
            var dateMatch = JalaliDateRegex.Match(NormalizeDateText(value));
            if (!dateMatch.Success || !JalaliDateResolver.TryResolveDate(dateMatch.Value, out var date)) return;
            candidates.Add(new MetadataCandidate(kind, value, dateMatch.Value, sheet, cell, date));
            return;
        }

        candidates.Add(new MetadataCandidate(kind, value, MatchingText(value), sheet, cell, null));
    }

    private MetadataCandidate? Reconcile(
        IReadOnlyList<MetadataCandidate> candidates,
        MetadataKind kind,
        Guid reportId,
        string profile,
        List<FundPortfolioExtractionIssue> issues)
    {
        var matching = candidates.Where(candidate => candidate.Kind == kind).ToArray();
        if (matching.Length == 0) return null;

        var groups = matching.GroupBy(candidate => candidate.ComparisonValue, StringComparer.OrdinalIgnoreCase).ToArray();
        var selected = SelectPreferred(matching);
        if (groups.Length > 1)
        {
            var evidence = string.Join(" | ", matching.Select(candidate =>
                $"{candidate.Sheet.OriginalSheetName}!{candidate.Cell.SourceAddress}={candidate.Value}"));
            issues.Add(Issue(reportId, FundExtractionIssueSeverity.Warning, "HEADER_METADATA_CONFLICT",
                selected.Cell.SourceAddress, evidence,
                $"Conflicting {kind} evidence was found across the cover and repeated sheet headers.", profile)
                with { SheetId = selected.Sheet.SheetId });
        }

        return selected;
    }

    private static MetadataCandidate SelectPreferred(IReadOnlyList<MetadataCandidate> candidates) =>
        candidates.OrderByDescending(candidate => candidate.Sheet.LogicalSheetType == FundWorkbookLogicalSheetType.ReportCover)
            .ThenBy(candidate => candidate.Sheet.SheetIndex)
            .ThenBy(candidate => candidate.Cell.SourceAddress, StringComparer.Ordinal)
            .First();

    private string MatchingText(string? value)
    {
        var normalized = normalizer.NormalizeText(value);
        if (normalized.Any(character => character > 255)) return normalized;
        try
        {
            var repaired = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(normalized));
            return repaired.Contains('\uFFFD', StringComparison.Ordinal) ? normalized : repaired;
        }
        catch (EncoderFallbackException)
        {
            return normalized;
        }
    }

    private string NormalizeDateText(string? value)
    {
        var normalized = normalizer.NormalizeText(value);
        return string.Concat(normalized.Select(character => character switch
        {
            >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
            >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
            _ => character
        }));
    }

    private static string? ValueAfterSeparator(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var separator = value.IndexOfAny([':', '\uFF1A']);
        return separator < 0 || separator == value.Length - 1 ? null : value[(separator + 1)..].Trim();
    }

    private static int GetRowNumber(string address) => GetCellPosition(address).Row;
    private static int GetColumnNumber(string address) => GetCellPosition(address).Column;

    private static (int Row, int Column) GetCellPosition(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return (int.MaxValue, int.MaxValue);
        var match = Regex.Match(address, "^([A-Za-z]+)([0-9]+)$");
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var row)) return (int.MaxValue, int.MaxValue);
        var column = 0;
        foreach (var character in match.Groups[1].Value.ToUpperInvariant()) column = column * 26 + character - 'A' + 1;
        return (row, column);
    }

    private sealed record WorkbookMetadata(string? FundName, string? ReportTitle, string? PeriodEndJalali, DateOnly? PeriodEndDate, string? PeriodEndText);
    private sealed record MetadataCandidate(MetadataKind Kind, string Value, string ComparisonValue, FundWorkbookSheetEnvelope Sheet, FundWorkbookCellEvidence Cell, DateOnly? Date);
    private enum MetadataKind { FundName, ReportTitle, PeriodEnd }

    private static readonly string[] FundNameLabels = ["\u0646\u0627\u0645 \u0635\u0646\u062f\u0648\u0642", "\u0646\u0627\u0645 \u0634\u0631\u06a9\u062a", "\u0646\u0627\u0645 \u0635\u0646\u062f\u0648\u0642 \u0633\u0631\u0645\u0627\u06cc\u0647\u200c\u06af\u0630\u0627\u0631\u06cc"];
    private static readonly string[] ReportTitleLabels = ["\u0639\u0646\u0648\u0627\u0646 \u06af\u0632\u0627\u0631\u0634", "\u0645\u0648\u0636\u0648\u0639", "\u062a\u06cc\u062a\u0631", "\u06af\u0632\u0627\u0631\u0634"];
    private static readonly string[] PeriodEndLabels = ["\u0645\u0646\u062a\u0647\u06cc \u0628\u0647", "\u062f\u0648\u0631\u0647 \u0645\u0646\u062a\u0647\u06cc \u0628\u0647", "\u062a\u0627\u0631\u06cc\u062e \u06af\u0632\u0627\u0631\u0634", "\u062f\u0648\u0631\u0647 \u0645\u0627\u0644\u06cc", "\u062a\u0627\u0631\u06cc\u062e", "\u062f\u0648\u0631\u0647"];

    private static string? ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var value = cell.Element(SpreadsheetNamespace + "v")?.Value;
        if (cell.Attribute("t")?.Value == "s" && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count) return sharedStrings[index];
        if (cell.Attribute("t")?.Value == "inlineStr") return cell.Element(SpreadsheetNamespace + "is")?.Value;
        return value;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open(); var document = XDocument.Load(stream);
        return document.Descendants(SpreadsheetNamespace + "si").Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value))).ToList();
    }

    private static List<(string Name, string Path, int Index)> ReadSheets(ZipArchive archive)
    {
        using var workbook = (archive.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("Workbook metadata is missing.")).Open();
        var document = XDocument.Load(workbook);
        using var relationships = (archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("Workbook relationships are missing.")).Open();
        var relDocument = XDocument.Load(relationships);
        var rels = relDocument.Descendants(PackageRelationshipNamespace + "Relationship").ToDictionary(x => (string)x.Attribute("Id")!, x => "xl/" + ((string)x.Attribute("Target")!).TrimStart('/'));
        return document.Descendants(SpreadsheetNamespace + "sheet").Select((sheet, index) => ((string)sheet.Attribute("name")!, rels[(string)sheet.Attribute(RelationshipNamespace + "id")!], index)).ToList();
    }

    private static string? FindUsedRange(IReadOnlyList<FundWorkbookCellEvidence> cells) => cells.Count == 0 ? null : $"{cells.First().SourceAddress}:{cells.Last().SourceAddress}";
    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    private static Guid DeterministicGuid(Guid seed, string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(seed + value))[..16]);
    private static FundPortfolioExtractionIssue Issue(Guid reportId, FundExtractionIssueSeverity severity, string code, string? address, string? raw, string message, string profile) => new()
    { Id = Guid.NewGuid(), ReportId = reportId, Severity = severity, IssueCode = code, SourceAddress = address, RawValue = raw, Message = message, ParserProfileVersion = profile, CreatedAtUtc = DateTimeOffset.UtcNow };

    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly System.Text.RegularExpressions.Regex JalaliDateRegex = new(@"1[34]\d{2}[-/]\d{1,2}[-/]\d{1,2}", System.Text.RegularExpressions.RegexOptions.Compiled);
}

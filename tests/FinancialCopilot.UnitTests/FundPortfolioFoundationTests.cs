using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FinancialCopilot.UnitTests;

public sealed class FundPortfolioFoundationTests
{
    [Fact]
    public void ValueNormalizer_NormalizesPersianDigitsArabicLettersAndBidiMarks()
    {
        var normalizer = new FundPortfolioValueNormalizer();

        Assert.Equal("1234567890 ی ک", normalizer.NormalizeText("۱۲۳۴۵۶۷۸۹۰ ي ك\u200c"));
    }

    [Theory]
    [InlineData("#NAME?")]
    [InlineData("#REF!")]
    [InlineData("#N/A")]
    [InlineData("#DIV/0!")]
    public void ValueNormalizer_NeverTreatsExcelErrorsAsNumericZero(string token)
    {
        var normalizer = new FundPortfolioValueNormalizer();

        Assert.True(normalizer.IsExcelError(token));
        Assert.False(normalizer.TryParseDecimal(token, out _));
    }

    [Theory]
    [InlineData("(1,250)", -1250)]
    [InlineData("۱۲٫۵٪", 0.125)]
    [InlineData("1,250 ریال", 1250)]
    public void ValueNormalizer_ParsesDisplayValuesWithoutFormulaEvaluation(string value, decimal expected)
    {
        var normalizer = new FundPortfolioValueNormalizer();

        Assert.True(normalizer.TryParseDecimal(value, out var result));
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task WorkbookParser_RetainsUnknownAndControlSheetsAndReportsFormulaErrors()
    {
        await using var workbook = CreateWorkbook();
        var parser = new XlsxFundPortfolioWorkbookParser(new FundPortfolioValueNormalizer());
        var result = await parser.ParseAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "TestProvider", "sample.xlsx", "ABC", "iran-fund-portfolio-workbook-v1", workbook), CancellationToken.None);

        Assert.Equal(4, result.Sheets.Count);
        Assert.Equal(FundWorkbookLogicalSheetType.FormulaOrControlSheetIgnored, result.Sheets[0].LogicalSheetType);
        Assert.Equal(FundWorkbookLogicalSheetType.EquityPortfolioCurrent, result.Sheets[1].LogicalSheetType);
        Assert.Equal(FundWorkbookLogicalSheetType.Unclassified, result.Sheets[2].LogicalSheetType);
        Assert.Equal(FundWorkbookLogicalSheetType.EquityPortfolioCurrent, result.Sheets[3].LogicalSheetType);
        Assert.Contains(result.Issues, issue => issue.IssueCode == "EXCEL_ERROR_VALUE" && issue.RawValue == "#REF!");
        Assert.Contains(result.Issues, issue => issue.IssueCode == "DUPLICATE_LOGICAL_SHEET_TYPE");
        Assert.DoesNotContain(result.Sheets[1].Cells, cell => cell.NormalizedValue == "0" && cell.RawValue == "#REF!");
    }

    [Fact]
    public async Task WorkbookParser_ExtractsCoverMetadataAndPreservesPeriodText()
    {
        await using var workbook = CreateMetadataWorkbook("صندوق آلفا", "صندوق آلفا", "1403/03/29");
        var parser = new XlsxFundPortfolioWorkbookParser(new FundPortfolioValueNormalizer());

        var result = await parser.ParseAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "TestProvider", "metadata.xlsx", "ABC", "iran-fund-portfolio-workbook-v1", workbook), CancellationToken.None);

        Assert.Equal("صندوق آلفا", result.ExtractedFundName);
        Assert.Equal("گزارش پرتفوی", result.ReportTitle);
        Assert.Equal("1403/03/29", result.Period.PeriodEndJalali);
        Assert.Equal("1403/03/29", result.Period.PeriodEndText);
        Assert.Equal(DateOnly.FromDateTime(new PersianCalendar().ToDateTime(1403, 3, 29, 0, 0, 0, 0)), result.Period.PeriodEndDate);
        Assert.DoesNotContain(result.Issues, issue => issue.IssueCode == "HEADER_METADATA_CONFLICT");
    }

    [Fact]
    public async Task WorkbookParser_ReconcilesRepeatedHeadersAndFlagsConflicts()
    {
        await using var workbook = CreateMetadataWorkbook("صندوق آلفا", "صندوق بتا", "1403/03/29");
        var parser = new XlsxFundPortfolioWorkbookParser(new FundPortfolioValueNormalizer());

        var result = await parser.ParseAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "TestProvider", "conflict.xlsx", "ABC", "iran-fund-portfolio-workbook-v1", workbook), CancellationToken.None);

        Assert.Equal("صندوق آلفا", result.ExtractedFundName);
        Assert.Contains(result.Issues, issue => issue.IssueCode == "HEADER_METADATA_CONFLICT" && issue.Message.Contains("FundName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkbookParser_PreservesInvalidPeriodTextAndReportsConversionFailure()
    {
        await using var workbook = CreateMetadataWorkbook("صندوق آلفا", "صندوق آلفا", "1403/13/01");
        var parser = new XlsxFundPortfolioWorkbookParser(new FundPortfolioValueNormalizer());

        var result = await parser.ParseAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), "TestProvider", "invalid-period.xlsx", "ABC", "iran-fund-portfolio-workbook-v1", workbook), CancellationToken.None);

        Assert.Equal("1403/13/01", result.Period.PeriodEndJalali);
        Assert.Equal("1403/13/01", result.Period.PeriodEndText);
        Assert.Null(result.Period.PeriodEndDate);
        Assert.Contains(result.Issues, issue => issue.IssueCode == "INVALID_JALALI_DATE");
    }

    private static MemoryStream CreateMetadataWorkbook(string coverFundName, string headerFundName, string periodEnd)
    {
        const string fundLabel = "\u0646\u0627\u0645 \u0635\u0646\u062f\u0648\u0642";
        const string titleLabel = "\u0639\u0646\u0648\u0627\u0646 \u06af\u0632\u0627\u0631\u0634";
        const string periodLabel = "\u062f\u0648\u0631\u0647 \u0645\u0646\u062a\u0647\u06cc \u0628\u0647";
        const string coverName = "\u062a\u06cc\u062a\u0631";
        const string holdingsName = "\u0633\u0647\u0627\u0645";
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "xl/workbook.xml", $"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"{coverName}\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"{holdingsName}\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
            Write(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
            Write(archive, "xl/worksheets/sheet1.xml", MetadataSheetXml(fundLabel, titleLabel, periodLabel, coverFundName, periodEnd, includeTitle: true));
            Write(archive, "xl/worksheets/sheet2.xml", MetadataSheetXml(fundLabel, titleLabel, periodLabel, headerFundName, periodEnd, includeTitle: false));
        }
        stream.Position = 0;
        return stream;
    }

    private static string MetadataSheetXml(string fundLabel, string titleLabel, string periodLabel, string fundName, string periodEnd, bool includeTitle) =>
        $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
        $"<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>{fundLabel}</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>{fundName}</t></is></c></row>" +
        (includeTitle ? $"<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>{titleLabel}</t></is></c><c r=\"B2\" t=\"inlineStr\"><is><t>گزارش پرتفوی</t></is></c></row>" : string.Empty) +
        $"<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>{periodLabel}</t></is></c><c r=\"B3\" t=\"inlineStr\"><is><t>{periodEnd}</t></is></c></row></sheetData></worksheet>";

    private static MemoryStream CreateWorkbook()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="0" sheetId="1" r:id="rId1"/><sheet name="سهام" sheetId="2" r:id="rId2"/><sheet name="Future Section" sheetId="3" r:id="rId3"/><sheet name="سهام (2)" sheetId="4" r:id="rId4"/></sheets></workbook>
                """);
            Write(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Target="worksheets/sheet3.xml"/><Relationship Id="rId4" Target="worksheets/sheet4.xml"/></Relationships>
                """);
            Write(archive, "xl/worksheets/sheet1.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>control</t></is></c></row></sheetData></worksheet>""");
            Write(archive, "xl/worksheets/sheet2.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>سهام</t></is></c><c r="B1"><f>BAD()</f><v>#REF!</v></c></row></sheetData></worksheet>""");
            Write(archive, "xl/worksheets/sheet3.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>future</t></is></c></row></sheetData></worksheet>""");
            Write(archive, "xl/worksheets/sheet4.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>سهام</t></is></c></row></sheetData></worksheet>""");
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}

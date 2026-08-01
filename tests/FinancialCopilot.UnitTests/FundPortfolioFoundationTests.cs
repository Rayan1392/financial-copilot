using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
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

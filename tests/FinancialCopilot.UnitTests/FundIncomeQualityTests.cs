using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class FundIncomeQualityTests
{
    [Fact]
    public void IncomeSummaryMapping_PreservesFormulaErrorAndPeriodContext()
    {
        var rows = FundIncomeQualitySheetMapping.ParseIncomeSummaries(Sheet(FundWorkbookLogicalSheetType.InvestmentIncomeSummary,
            ("A1", "Category"), ("B1", "Amount"), ("C1", "Income %"), ("D1", "Cumulative Amount"),
            ("A2", "Equity Dividend"), ("B2", "100"), ("C2", "50%"), ("D2", "300"),
            ("A3", "Other Income"), ("B3", "#NAME?"), ("C3", "#NAME?"), ("D3", "#NAME?")), new FundPortfolioValueNormalizer());

        Assert.Equal(2, rows.Count);
        Assert.Equal(FundIncomeCategory.EquityDividend, rows[0].Category);
        Assert.Equal(50m, rows[0].SourceIncomePercentage);
        Assert.Equal(FundWorkbookPeriodContext.FiscalYearToDate, rows[0].PeriodContext);
        Assert.True(rows[1].HasFormulaError);
        Assert.Null(rows[1].Amount);
    }

    [Fact]
    public void DividendMapping_ComputesNetOnlyFromValidGrossAndDiscount()
    {
        var row = Assert.Single(FundIncomeQualitySheetMapping.ParseDividends(Sheet(FundWorkbookLogicalSheetType.DividendIncomeDetail,
            ("A1", "Security"), ("B1", "Meeting Date"), ("C1", "Quantity"), ("D1", "DPS"), ("E1", "Gross"), ("F1", "Discount"),
            ("A2", "FOOLAD"), ("B2", "1403/06/31"), ("C2", "100"), ("D2", "3"), ("E2", "300"), ("F2", "20")), new FundPortfolioValueNormalizer()));

        Assert.Equal(new DateOnly(2024, 9, 21), row.Date);
        Assert.Equal(300m, row.Gross);
        Assert.Equal(20m, row.Discount);
    }

    [Fact]
    public void ValuationQuality_UsesPortfolioQualityLanguageAndValidExposureOnly()
    {
        Assert.Equal(10m, FundIncomeQualityMethodology.CalculateAdjustmentPercentage(100, 110));
        Assert.Equal(10m, FundIncomeQualityMethodology.CalculateShare(100, 1000));
        Assert.Equal(FundPortfolioValuationQualityStatus.Limited,
            FundIncomeQualityMethodology.ClassifyValuationQuality(true, 0, 1, 2, false));
        Assert.Equal(FundPortfolioValuationQualityStatus.InsufficientEvidence,
            FundIncomeQualityMethodology.ClassifyValuationQuality(false, 0, 0, null, false));
    }

    [Fact]
    public async Task Normalizer_PersistsIncomeQualityAndReplacesRowsIdempotently()
    {
        await using var providerConnection = new SqliteConnection("Data Source=:memory:");
        await providerConnection.OpenAsync();
        await using var providerDb = new FinancialProviderDbContext(new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(providerConnection).Options);
        await providerDb.Database.EnsureCreatedAsync();
        var reportId = Guid.NewGuid(); var fundId = Guid.NewGuid(); var importedAt = DateTimeOffset.UtcNow;
        providerDb.FundPortfolioReports.Add(new FundPortfolioReportRow { Id = reportId, FundId = fundId, ProviderName = "Test", ReportType = FundPortfolioReportType.MonthlyPortfolio, PeriodEndDate = new DateOnly(2024, 9, 21), OriginalFileName = "income.xlsx", FileSha256 = "income-104", RawStorageKey = "income-104", RawFileSizeBytes = 1, RawMimeType = "application/octet-stream", ParserProfileVersion = "v1", ParseStatus = FundPortfolioParseStatus.Parsed, SourceRevision = 1, ImportedAtUtc = importedAt });
        await providerDb.SaveChangesAsync();

        await using var catalogConnection = new SqliteConnection("Data Source=:memory:");
        await catalogConnection.OpenAsync();
        await using var catalogDb = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseSqlite(catalogConnection).Options);
        await catalogDb.Database.EnsureCreatedAsync();
        var envelope = new FundPortfolioWorkbookEnvelope(reportId, fundId, "Test", "income.xlsx", "income-104", "v1", new("1403/06/31", new DateOnly(2024, 9, 21)),
        [Sheet(FundWorkbookLogicalSheetType.InvestmentIncomeSummary, ("A1", "Category"), ("B1", "Amount"), ("C1", "Income %"), ("A2", "Equity Dividend"), ("B2", "100"), ("C2", "#NAME?"), ("A3", "Equity Unrealized"), ("B3", "100"), ("C3", "#NAME?")),
         Sheet(FundWorkbookLogicalSheetType.ValuationAdjustments, ("A1", "Security"), ("B1", "Closing Price"), ("C1", "Adjusted Price"), ("D1", "Adjustment %"), ("E1", "Reason"), ("A2", "UNKNOWN"), ("B2", "100"), ("C2", "110"), ("D2", "10%"), ("E2", "Fund valuation policy"))], []);

        var normalizer = new FundIncomeQualitySectionNormalizer(providerDb, catalogDb, new FundPortfolioValueNormalizer(), NullLogger<FundIncomeQualitySectionNormalizer>.Instance);
        await normalizer.NormalizeAsync(envelope, CancellationToken.None);
        await normalizer.NormalizeAsync(envelope, CancellationToken.None);

        Assert.Equal(2, await providerDb.FundInvestmentIncomeSummaries.CountAsync());
        Assert.Equal(50m, (await providerDb.FundInvestmentIncomeSummaries.OrderBy(x => x.Id).FirstAsync()).CalculatedPercentageOfTotalIncome);
        Assert.Single(await providerDb.FundValuationAdjustments.ToListAsync());
        Assert.Equal(FundPortfolioValuationQualityStatus.InsufficientEvidence, (await providerDb.FundPortfolioValuationQualitySnapshots.SingleAsync()).QualityStatus);
        Assert.Contains(await providerDb.FundPortfolioExtractionIssues.Select(x => x.IssueCode).ToListAsync(), code => code == "INCOME_SOURCE_FORMULA_ERROR");
    }

    private static FundWorkbookSheetEnvelope Sheet(FundWorkbookLogicalSheetType type, params (string Address, string Value)[] values) =>
        new(Guid.NewGuid(), type.ToString(), type.ToString(), type, 0, null, 0.99m, "income-test", "test", values.Select(x => new FundWorkbookCellEvidence(type.ToString(), 0, x.Address, x.Value, x.Value, null, x.Address.StartsWith("D", StringComparison.Ordinal) ? "Fiscal" : null, x.Address.StartsWith("D", StringComparison.Ordinal) ? "FiscalYearToDate" : "test", "test")).ToArray(), []);
}

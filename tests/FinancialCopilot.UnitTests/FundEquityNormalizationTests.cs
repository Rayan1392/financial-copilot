using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class FundEquityNormalizationTests
{
    [Fact]
    public void HeaderPathMapping_SeparatesBeginningPurchasesSalesAndEndingValues()
    {
        var sheet = CreateSheet(
            ("A1", "Security"), ("B1", "Beginning Quantity"), ("C1", "Purchase Quantity"), ("D1", "Sale Quantity"),
            ("E1", "Ending Quantity"), ("F1", "Ending Price"), ("G1", "Ending Value"), ("H1", "Weight"),
            ("A2", "ABC"), ("B2", "0"), ("C2", "100"), ("D2", "0"), ("E2", "100"), ("F2", "125"), ("G2", "12500"), ("H2", "2.5"));

        var rows = FundEquitySheetMapping.Parse(sheet, FundWorkbookPeriodContext.CurrentPeriod, new FundPortfolioValueNormalizer());
        var row = Assert.Single(rows);

        Assert.Equal(100, row.PurchasedQuantity);
        Assert.Equal(0, row.BeginningQuantity);
        Assert.Equal(100, row.EndingQuantity);
        Assert.Equal(125, row.EndingUnitMarketPrice);
        Assert.Equal(2.5m, row.WeightOfTotalAssetsPercentage);
        Assert.Equal(FundEquitySecurityType.OrdinaryEquity, row.SecurityType);
    }

    [Fact]
    public void ComparativeSheetMapping_UsesFiscalContextFromHeaderPath()
    {
        var sheet = CreateSheetWithType(FundWorkbookLogicalSheetType.EquityPortfolioComparative,
            ("A1", "Security"), ("B1", "Fiscal Year To Date Beginning Quantity"), ("C1", "Purchase Quantity"), ("D1", "Sale Quantity"), ("E1", "Ending Quantity"),
            ("A2", "ABC"), ("B2", "10"), ("C2", "0"), ("D2", "0"), ("E2", "10"));

        var row = Assert.Single(FundEquitySheetMapping.Parse(sheet, FundWorkbookPeriodContext.PriorComparablePeriod, new FundPortfolioValueNormalizer()));
        Assert.Equal(FundWorkbookPeriodContext.FiscalYearToDate, row.PeriodContext);
    }

    [Fact]
    public void FullExitMapping_PreservesDisclosedSaleQuantity()
    {
        var row = Assert.Single(FundEquitySheetMapping.Parse(CreateSheet(
            ("A1", "Security"), ("B1", "Beginning Quantity"), ("C1", "Purchase Quantity"), ("D1", "Sale Quantity"), ("E1", "Ending Quantity"),
            ("A2", "EXIT"), ("B2", "100"), ("C2", "0"), ("D2", "100"), ("E2", "0")), FundWorkbookPeriodContext.CurrentPeriod, new FundPortfolioValueNormalizer()));

        Assert.Equal(100, row.SoldQuantity);
        Assert.Equal(FundEquityActivityClassification.FullExit, FundEquityActivityPolicy.Classify(row.BeginningQuantity, row.PurchasedQuantity, row.SoldQuantity, row.EndingQuantity, FundEquityActivityPolicy.Reconcile(row.BeginningQuantity, row.PurchasedQuantity, row.SoldQuantity, row.EndingQuantity)));
    }

    [Fact]
    public void TotalRows_AreRetainedForReconciliationButNotSecurityActivityRows()
    {
        var rows = FundEquitySheetMapping.Parse(CreateSheet(
            ("A1", "Security"), ("B1", "Ending Quantity"), ("C1", "Ending Value"),
            ("A2", "ABC"), ("B2", "10"), ("C2", "100"),
            ("A3", "Total"), ("B3", "10"), ("C3", "100")), FundWorkbookPeriodContext.CurrentPeriod, new FundPortfolioValueNormalizer());

        Assert.Equal(2, rows.Count);
        Assert.False(rows[0].IsTotalRow);
        Assert.True(rows[1].IsTotalRow);
        Assert.Equal(100, rows[1].EndingMarketOrNetSaleValue);
    }

    [Theory]
    [InlineData("حق تقدم ABC", FundEquitySecurityType.PreemptiveRight)]
    [InlineData("واحد صندوق سرمایه گذاری", FundEquitySecurityType.InvestmentFundUnit)]
    public void SecurityTypePolicy_DistinguishesGovernedInstrumentKinds(string name, FundEquitySecurityType expected)
    {
        Assert.Equal(expected, FundEquitySheetMapping.DetectSecurityType(name));
    }

    [Fact]
    public void ActivityPolicy_ClassifiesNewExitAndMismatchWithoutReplacingSourceValues()
    {
        Assert.Equal(FundEquityActivityClassification.NewPosition, FundEquityActivityPolicy.Classify(0, 100, 0, 100, FundEquityReconciliationStatus.Reconciled));
        Assert.Equal(FundEquityActivityClassification.FullExit, FundEquityActivityPolicy.Classify(100, 0, 100, 0, FundEquityReconciliationStatus.Reconciled));
        Assert.Equal(FundEquityActivityClassification.Unreconciled, FundEquityActivityPolicy.Classify(100, 10, 0, 120, FundEquityReconciliationStatus.Unreconciled));
        Assert.Equal(10, FundEquityActivityPolicy.CalculateQuantityDifference(100, 10, 0, 120));
    }

    [Fact]
    public void NumericMapping_PreservesBlankZeroAndExcelErrorAsDistinctSourceStates()
    {
        var rows = FundEquitySheetMapping.Parse(CreateSheet(
            ("A1", "Security"), ("B1", "Beginning Quantity"), ("C1", "Purchase Quantity"), ("D1", "Sale Quantity"), ("E1", "Ending Quantity"),
            ("A2", "ABC"), ("B2", ""), ("C2", "0"), ("D2", "#REF!"), ("E2", "1")), FundWorkbookPeriodContext.CurrentPeriod, new FundPortfolioValueNormalizer());
        var row = Assert.Single(rows);

        Assert.Null(row.BeginningQuantity);
        Assert.Equal(0, row.PurchasedQuantity);
        Assert.Null(row.SoldQuantity);
        Assert.Equal(1, row.EndingQuantity);
        Assert.Contains("#REF!", row.SourceEvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Normalizer_PersistsResolutionReconciliationAndIsIdempotent()
    {
        await using var providerConnection = new SqliteConnection("Data Source=:memory:");
        await providerConnection.OpenAsync();
        var providerOptions = new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(providerConnection).Options;
        await using var providerDb = new FinancialProviderDbContext(providerOptions);
        await providerDb.Database.EnsureCreatedAsync();
        var reportId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        providerDb.FundPortfolioReports.Add(new FundPortfolioReportRow
        {
            Id = reportId, FundId = fundId, ProviderName = "Test", ReportType = FundPortfolioReportType.MonthlyPortfolio,
            PeriodEndDate = new DateOnly(2024, 6, 18), OriginalFileName = "equity.xlsx", FileSha256 = "hash",
            RawStorageKey = "fund-portfolio/equity.xlsx", RawFileSizeBytes = 1, RawMimeType = "application/octet-stream",
            ParserProfileVersion = "v1", ParseStatus = FundPortfolioParseStatus.Parsed, SourceRevision = 1, ImportedAtUtc = DateTimeOffset.UtcNow
        });
        await providerDb.SaveChangesAsync();

        await using var catalogConnection = new SqliteConnection("Data Source=:memory:");
        await catalogConnection.OpenAsync();
        var catalogOptions = new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseSqlite(catalogConnection).Options;
        await using var catalogDb = new FinancialIngestionDbContext(catalogOptions);
        await catalogDb.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid();
        catalogDb.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId, ProviderName = "Catalog", ExternalCompanyId = "company-abc", Name = "ABC", CompanySymbol = "ABC",
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        catalogDb.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = Guid.NewGuid(), ProviderName = "TSE", ExternalInstrumentId = Guid.NewGuid(), InstrumentCode = 1,
            InstrumentIsin = "IRABC", Symbol = "ABC", Name = "ABC", MarketCode = "TSE", InstrumentKind = "Equity",
            NormalizedCompanyId = companyId, IsActive = true, SourceChangedAt = DateTimeOffset.UtcNow, LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        await catalogDb.SaveChangesAsync();

        var normalizer = new FundEquitySectionNormalizer(providerDb, catalogDb, new FundPortfolioValueNormalizer(), new NoopEquityTelemetry(), NullLogger<FundEquitySectionNormalizer>.Instance, new NoKnownFundEquityCorporateActionAdjustmentProvider());
        var envelope = new FundPortfolioWorkbookEnvelope(reportId, fundId, "Test", "equity.xlsx", "hash", "v1", new("1403/03/29", new DateOnly(2024, 6, 18)), [CreateSheet(
            ("A1", "Security"), ("B1", "Beginning Quantity"), ("C1", "Purchase Quantity"), ("D1", "Sale Quantity"), ("E1", "Ending Quantity"),
            ("A2", "ABC"), ("B2", "100"), ("C2", "10"), ("D2", "0"), ("E2", "120"),
            ("A3", "NEGATIVE"), ("B3", "-1"), ("C3", "0"), ("D3", "0"), ("E3", "0"),
            ("A4", "UNKNOWN"), ("B4", "0"), ("C4", "0"), ("D4", "0"), ("E4", "0"))], []);

        await normalizer.NormalizeAsync(envelope, CancellationToken.None);
        await normalizer.NormalizeAsync(envelope, CancellationToken.None);

        Assert.Equal(4, await providerDb.FundEquityPositionSnapshots.CountAsync());
        Assert.Equal(2, await providerDb.FundEquityPeriodActivities.CountAsync());
        var activity = await providerDb.FundEquityPeriodActivities.SingleAsync(x => x.RawSecurityName == "ABC");
        Assert.Equal("company-abc", activity.ExternalCompanyId);
        Assert.Equal(FundEquityReconciliationStatus.Unreconciled, activity.ReconciliationStatus);
        Assert.Equal(FundEquityActivityClassification.Unreconciled, activity.ActivityClassification);
        Assert.Equal(1, await providerDb.FundPortfolioExtractionIssues.CountAsync(x => x.IssueCode == "EQUITY_QUANTITY_RECONCILIATION_MISMATCH"));
        Assert.Equal(1, await providerDb.FundPortfolioExtractionIssues.CountAsync(x => x.IssueCode == "NEGATIVE_EQUITY_QUANTITY"));
        var reviewRepository = new EfCoreFundPortfolioMappingReviewRepository(providerDb);
        Assert.Equal(3, await reviewRepository.CreateFromReportIssuesAsync(reportId, CancellationToken.None));
        Assert.Contains(await reviewRepository.ListAsync(FundPortfolioMappingReviewStatus.Pending, CancellationToken.None), review => review.MappingType == FundPortfolioMappingReviewType.UnresolvedSecurity);

        var repository = new EfCoreFundEquityPositionRepository(providerDb);
        var firstPage = await repository.QueryPositionsAsync(new(fundId, PageSize: 3), CancellationToken.None);
        var secondPage = await repository.QueryPositionsAsync(new(fundId, Cursor: firstPage.NextCursor, PageSize: 3), CancellationToken.None);
        Assert.Equal(3, firstPage.Items.Count);
        Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasMore);

        var companyHoldings = await repository.QueryCompanyHoldingsAsync(new("company-abc", PageSize: 1), CancellationToken.None);
        Assert.Single(companyHoldings.Items);
        Assert.Equal(reportId, companyHoldings.Items[0].ReportId);
        Assert.Equal(new DateOnly(2024, 6, 18), companyHoldings.Items[0].PeriodEndDate);
    }

    private static FundWorkbookSheetEnvelope CreateSheet(params (string Address, string Value)[] values) => CreateSheetWithType(FundWorkbookLogicalSheetType.EquityPortfolioCurrent, values);

    private static FundWorkbookSheetEnvelope CreateSheetWithType(FundWorkbookLogicalSheetType type, params (string Address, string Value)[] values) =>
        new(Guid.NewGuid(), "سهام", "سهام", type, 0, "A1:H20", 0.95m, "fixture", "v1",
            values.Select(value => new FundWorkbookCellEvidence("سهام", 0, value.Address, value.Value, value.Value, null, null, null, "v1")).ToArray(), []);

    private sealed class NoopEquityTelemetry : IFundEquityNormalizationTelemetry
    {
        public void Record(Guid reportId, int rowCount, int resolvedCount, int unresolvedCount, int newPositionCount, int fullExitCount, int reconciliationMismatchCount, TimeSpan duration) { }
    }
}

using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class FundNonEquityNormalizationTests
{
    [Fact]
    public void AssetAllocationMapping_GovernsClassesAndPreservesFormulaErrorAndTotal()
    {
        var rows = FundNonEquitySheetMapping.ParseAssetAllocation(Sheet(FundWorkbookLogicalSheetType.AssetAllocationSummary,
            ("A1", "Asset Class"), ("B1", "Cost"), ("C1", "Market Value"), ("D1", "Weight"),
            ("A2", "سپرده بانکی"), ("B2", "100"), ("C2", "110"), ("D2", "10%"),
            ("A3", "اوراق مشتقه"), ("B3", "#REF!"), ("C3", "0"), ("D3", "0"),
            ("A4", "جمع کل"), ("B4", "100"), ("C4", "110"), ("D4", "10")), new FundPortfolioValueNormalizer());

        Assert.Equal(3, rows.Count);
        Assert.Equal(FundAssetClass.BankDeposits, rows[0].AssetClass);
        Assert.Equal(10m, rows[0].WeightOfTotalAssetsPercentage);
        Assert.True(rows[1].HasSourceFormulaError);
        Assert.Null(rows[1].CostAmount);
        Assert.True(rows[2].IsSectionTotal);
    }

    [Fact]
    public void CommodityMapping_ParsesMovementAndGenericCommodityCatalog()
    {
        var row = Assert.Single(FundNonEquitySheetMapping.ParseCommodityCertificates(Sheet(FundWorkbookLogicalSheetType.CommodityCertificatePositions,
            ("A1", "Security"), ("B1", "Beginning Quantity"), ("C1", "Purchase Quantity"), ("D1", "Sale Quantity"),
            ("E1", "Ending Quantity"), ("F1", "Ending Price"), ("G1", "Ending Value"), ("H1", "Weight"),
            ("A2", "گواهی شمش طلا نماد: GOLD01"), ("B2", "10"), ("C2", "2"), ("D2", "1"),
            ("E2", "11"), ("F2", "100"), ("G2", "1100"), ("H2", "2.5")), new FundPortfolioValueNormalizer()));

        Assert.Equal("GOLD01", FundNonEquitySheetMapping.ExtractInstrumentSymbol(row.RawSecurityName));
        Assert.Equal((FundCommodityType.GoldBullion, "GOLD_BULLION"), FundCommodityCatalog.Resolve(row.NormalizedSecurityName));
        Assert.Equal(FundNonEquityReconciliationStatus.Reconciled,
            FundNonEquityReconciliationPolicy.ReconcileMovement(row.BeginningQuantity, row.PurchasedQuantity, row.SoldQuantity, row.EndingQuantity, 0.0001m));
        Assert.Contains(FundNonEquitySheetMapping.CommodityMappingVersion, row.SourceEvidenceJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gold bullion certificate", FundCommodityType.GoldBullion, "GOLD_BULLION")]
    [InlineData("copper cathode certificate", FundCommodityType.CopperCathode, "COPPER_CATHODE")]
    [InlineData("rebar certificate", FundCommodityType.Rebar, "REBAR")]
    [InlineData("future commodity certificate", FundCommodityType.OtherCommodity, "OTHER_COMMODITY")]
    public void CommodityCatalog_UsesGovernedExtensibleMappings(string name, FundCommodityType expectedType, string expectedCode)
    {
        Assert.Equal((expectedType, expectedCode), FundCommodityCatalog.Resolve(name));
    }

    [Fact]
    public void BankDepositMapping_UsesGovernedAliasAndPreservesMovementEquation()
    {
        var row = Assert.Single(FundNonEquitySheetMapping.ParseBankDeposits(Sheet(FundWorkbookLogicalSheetType.BankDepositPositions,
            ("A1", "Bank"), ("B1", "Beginning"), ("C1", "Increase"), ("D1", "Decrease"), ("E1", "Ending"), ("F1", "Weight"),
            ("A2", "بانک ملت"), ("B2", "100"), ("C2", "20"), ("D2", "10"), ("E2", "110"), ("F2", "5")), new FundPortfolioValueNormalizer()));

        Assert.Equal("BANK_MELLAT", row.BankCode);
        Assert.Equal(0m, FundNonEquityReconciliationPolicy.MovementDifference(row.BeginningBalance, row.IncreaseAmount, row.DecreaseAmount, row.EndingBalance));
    }

    [Fact]
    public void DerivativeMapping_SeparatesProtectiveAndOrdinaryBlocksAndConvertsJalaliDate()
    {
        var rows = FundNonEquitySheetMapping.ParseDerivatives(Sheet(FundWorkbookLogicalSheetType.DerivativePositions,
            ("A1", "اختیار فروش تبعی"),
            ("A2", "نام قرارداد"), ("B2", "دارایی پایه"), ("C2", "تعداد"), ("D2", "تعداد سهام پایه"), ("E2", "قیمت اعمال"), ("F2", "تاریخ اعمال"), ("G2", "بازده موثر"),
            ("A3", "اختیار فروش تبعی فولاد"), ("B3", "فولاد"), ("C3", "1"), ("D3", "100"), ("E3", "5000"), ("F3", "1403/06/31"), ("G3", "12%"),
            ("A5", "موقعیت اختیار معامله"),
            ("A6", "نام قرارداد"), ("B6", "دارایی پایه"), ("C6", "تعداد"), ("D6", "قیمت اعمال"), ("E6", "سررسید"), ("F6", "موقعیت"),
            ("A7", "اختیار خرید فولاد"), ("B7", "فولاد"), ("C7", "2"), ("D7", "6000"), ("E7", "1403/07/30"), ("F7", "خرید")), new FundPortfolioValueNormalizer());

        Assert.Equal(2, rows.Count);
        Assert.Equal(FundDerivativeType.ProtectivePut, rows[0].DerivativeType);
        Assert.Equal(FundOptionType.Put, rows[0].OptionType);
        Assert.Equal(100m, rows[0].UnderlyingCoverageQuantity);
        Assert.NotNull(rows[0].ExpiryOrExerciseDate);
        Assert.Equal(12m, rows[0].EffectiveReturnPercentage);
        Assert.Null(rows[0].ContractMultiplier);
        Assert.Equal(FundDerivativeType.ExchangeTradedOption, rows[1].DerivativeType);
        Assert.Equal(FundOptionType.Call, rows[1].OptionType);
        Assert.Equal(FundPositionSide.Long, rows[1].PositionSide);
    }

    [Theory]
    [InlineData(100, 100, FundHedgeCoverageStatus.Covered)]
    [InlineData(50, 100, FundHedgeCoverageStatus.PartiallyCovered)]
    [InlineData(150, 100, FundHedgeCoverageStatus.OverCovered)]
    public void HedgeCoveragePolicy_IsEvidenceBased(decimal covered, decimal holding, FundHedgeCoverageStatus expected) =>
        Assert.Equal(expected, FundHedgeCoveragePolicy.Classify(FundDerivativeType.ProtectivePut, covered, holding));

    [Fact]
    public void JalaliDateMapping_PreservesInvalidDisclosedDate()
    {
        Assert.False(FundNonEquitySheetMapping.TryParseJalaliDate("1403/13/40", out _));
        Assert.True(FundNonEquitySheetMapping.TryParseJalaliDate("1403/06/31", out var date));
        Assert.Equal(new DateOnly(2024, 9, 21), date);
    }

    [Fact]
    public async Task Normalizer_PersistsDistinctAssetsCoverageReviewsAndIsIdempotent()
    {
        await using var providerConnection = new SqliteConnection("Data Source=:memory:");
        await providerConnection.OpenAsync();
        await using var providerDb = new FinancialProviderDbContext(new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(providerConnection).Options);
        await providerDb.Database.EnsureCreatedAsync();
        var reportId = Guid.NewGuid(); var fundId = Guid.NewGuid(); var importedAt = DateTimeOffset.UtcNow;
        providerDb.FundPortfolioReports.Add(new FundPortfolioReportRow
        {
            Id = reportId, FundId = fundId, ProviderName = "Test", ReportType = FundPortfolioReportType.MonthlyPortfolio,
            PeriodEndDate = new DateOnly(2024, 9, 21), OriginalFileName = "non-equity.xlsx", FileSha256 = "hash-103",
            RawStorageKey = "fund-portfolio/non-equity.xlsx", RawFileSizeBytes = 1, RawMimeType = "application/octet-stream",
            ParserProfileVersion = "v1", ParseStatus = FundPortfolioParseStatus.Parsed, SourceRevision = 2, ImportedAtUtc = importedAt
        });
        providerDb.FundEquityPositionSnapshots.Add(new FundEquityPositionSnapshotRow
        {
            Id = Guid.NewGuid(), ReportId = reportId, FundId = fundId, PeriodContext = FundWorkbookPeriodContext.CurrentPeriod,
            PeriodEndDate = new DateOnly(2024, 9, 21), PositionState = FundPositionState.Ending, SecurityType = FundEquitySecurityType.OrdinaryEquity,
            ExternalCompanyId = "company-foolad", RawSecurityName = "فولاد", NormalizedSecurityName = "فولاد", Quantity = 100,
            ResolutionStatus = FundSecurityResolutionStatus.Resolved, SourceLogicalRow = 2, SourceSheetId = Guid.NewGuid(), SourceRevision = 2,
            ImportedAtUtc = importedAt, ParserProfileVersion = "v1", SourceEvidenceJson = "{}"
        });
        await providerDb.SaveChangesAsync();

        await using var catalogConnection = new SqliteConnection("Data Source=:memory:");
        await catalogConnection.OpenAsync();
        await using var catalogDb = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseSqlite(catalogConnection).Options);
        await catalogDb.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid();
        catalogDb.Companies.Add(new NormalizedCompanyRow { Id = companyId, ProviderName = "Catalog", ExternalCompanyId = "company-foolad", Name = "فولاد", CompanySymbol = "فولاد", LastSynchronizedAt = importedAt });
        catalogDb.TradingInstruments.AddRange(
            Instrument("GOLD01", "گواهی شمش طلا نماد: GOLD01", "Commodity", null),
            Instrument("PUTFOOLAD", "اختیار فروش تبعی فولاد", "Option", companyId));
        await catalogDb.SaveChangesAsync();

        var envelope = new FundPortfolioWorkbookEnvelope(reportId, fundId, "Test", "non-equity.xlsx", "hash-103", "v1", new("1403/06/31", new DateOnly(2024, 9, 21)),
        [
            Sheet(FundWorkbookLogicalSheetType.AssetAllocationSummary,
                ("A1", "Asset Class"), ("B1", "Cost"), ("C1", "Market Value"), ("D1", "Weight"),
                ("A2", "گواهی سپرده کالایی"), ("B2", "1000"), ("C2", "1100"), ("D2", "2.5"),
                ("A3", "سپرده بانکی"), ("B3", "100"), ("C3", "110"), ("D3", "5"),
                ("A4", "اوراق مشتقه"), ("B4", "10"), ("C4", "20"), ("D4", "0.2")),
            Sheet(FundWorkbookLogicalSheetType.CommodityCertificatePositions,
                ("A1", "Security"), ("B1", "Beginning Quantity"), ("C1", "Purchase Quantity"), ("D1", "Sale Quantity"), ("E1", "Ending Quantity"), ("F1", "Ending Value"), ("G1", "Weight"),
                ("A2", "گواهی شمش طلا نماد: GOLD01"), ("B2", "10"), ("C2", "2"), ("D2", "1"), ("E2", "11"), ("F2", "1100"), ("G2", "2.5")),
            Sheet(FundWorkbookLogicalSheetType.BankDepositPositions,
                ("A1", "Bank"), ("B1", "Beginning"), ("C1", "Increase"), ("D1", "Decrease"), ("E1", "Ending"), ("F1", "Weight"),
                ("A2", "بانک ملت"), ("B2", "100"), ("C2", "20"), ("D2", "10"), ("E2", "115"), ("F2", "5"),
                ("A3", "بانک ناشناخته"), ("B3", "0"), ("C3", "0"), ("D3", "0"), ("E3", "0"), ("F3", "0")),
            Sheet(FundWorkbookLogicalSheetType.DerivativePositions,
                ("A1", "اختیار فروش تبعی"),
                ("A2", "نام قرارداد"), ("B2", "دارایی پایه"), ("C2", "تعداد"), ("D2", "تعداد سهام پایه"), ("E2", "قیمت اعمال"), ("F2", "تاریخ اعمال"), ("G2", "ارزش روز"), ("H2", "درصد"),
                ("A3", "اختیار فروش تبعی فولاد"), ("B3", "فولاد"), ("C3", "1"), ("D3", "100"), ("E3", "5000"), ("F3", "1403/06/31"), ("G3", "20"), ("H3", "0.2"))
        ], []);

        var normalizer = new FundNonEquitySectionNormalizer(providerDb, catalogDb, new FundPortfolioValueNormalizer(),
            Options.Create(new FundNonEquityNormalizationOptions()), new NoopTelemetry(), NullLogger<FundNonEquitySectionNormalizer>.Instance);
        await normalizer.NormalizeAsync(envelope, CancellationToken.None);
        await normalizer.NormalizeAsync(envelope, CancellationToken.None);

        Assert.Equal(3, await providerDb.FundAssetAllocationSnapshots.CountAsync());
        Assert.Single(await providerDb.FundCommodityCertificatePositions.ToListAsync());
        Assert.Equal(2, await providerDb.FundBankDepositPositions.CountAsync());
        var derivative = Assert.Single(await providerDb.FundDerivativePositions.ToListAsync());
        Assert.Equal(FundDerivativeType.ProtectivePut, derivative.DerivativeType);
        Assert.Equal("company-foolad", derivative.UnderlyingExternalCompanyId);
        Assert.Equal(FundHedgeCoverageStatus.Covered, derivative.HedgeCoverageStatus);
        Assert.Equal(FundHedgeCoveragePolicy.CalculationVersion, derivative.HedgeCoverageCalculationVersion);
        Assert.Equal(1, await providerDb.FundPortfolioExtractionIssues.CountAsync(x => x.IssueCode == "BANK_DEPOSIT_RECONCILIATION_MISMATCH"));
        Assert.Equal(1, await providerDb.FundPortfolioExtractionIssues.CountAsync(x => x.IssueCode == "UNRESOLVED_BANK"));
        Assert.Equal(1, await providerDb.FundPortfolioExtractionIssues.CountAsync(x => x.IssueCode == "NON_EQUITY_SUMMARY_DETAIL_MISMATCH"));

        var reviewRepository = new EfCoreFundPortfolioMappingReviewRepository(providerDb);
        var createdReviews = await reviewRepository.CreateFromReportIssuesAsync(reportId, CancellationToken.None);
        Assert.True(createdReviews >= 3);
        var repository = new EfCoreFundNonEquityAssetRepository(providerDb);
        Assert.Equal(1, await repository.CountUnresolvedAsync(reportId, CancellationToken.None));
        Assert.Single(await repository.QueryDerivativesAsync(new(ReportId: reportId), CancellationToken.None));
    }

    private static TradingInstrumentRow Instrument(string symbol, string name, string kind, Guid? companyId) => new()
    {
        Id = Guid.NewGuid(), ProviderName = "Catalog", ExternalInstrumentId = Guid.NewGuid(), InstrumentCode = Random.Shared.NextInt64(1, long.MaxValue),
        InstrumentIsin = $"IR{symbol}", Symbol = symbol, Name = name, MarketCode = "TSE", InstrumentKind = kind,
        NormalizedCompanyId = companyId, IsActive = true, SourceChangedAt = DateTimeOffset.UtcNow, LastSynchronizedAt = DateTimeOffset.UtcNow
    };

    private static FundWorkbookSheetEnvelope Sheet(FundWorkbookLogicalSheetType type, params (string Address, string Value)[] values) =>
        new(Guid.NewGuid(), type.ToString(), type.ToString(), type, 0, "A1:Z50", 0.95m, "fixture", "v1",
            values.Select(x => new FundWorkbookCellEvidence(type.ToString(), 0, x.Address, x.Value, x.Value, null, null, null, "v1")).ToArray(), []);

    private sealed class NoopTelemetry : IFundNonEquityNormalizationTelemetry
    {
        public void Record(Guid reportId, int allocationCount, int commodityCount, int depositCount, int derivativeCount, int unresolvedCount,
            int depositEquationFailureCount, int totalDifferenceCount, int resolvedUnderlyingCount, int coverageAvailableCount, TimeSpan duration) { }
    }
}

using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlyProductTrend136Tests
{
    [Fact]
    public void Rate_UsesMillionRialToTomanFormula()
    {
        var result = MonthlyProductTrendCalculator.CalculateRate(68_209_284m, 109_949m);

        Assert.Equal(MonthlyProductTrendRateStatus.ValidRate, result.Status);
        Assert.NotNull(result.Rate);
        Assert.InRange(result.Rate!.Value, 62_037_202.70m, 62_037_202.71m);
    }

    [Fact]
    public void Rate_StatusesNeverUseProviderRateOrFloatingPointSentinels()
    {
        Assert.Equal((0m, MonthlyProductTrendRateStatus.ValidZeroRate),
            MonthlyProductTrendCalculator.CalculateRate(0m, 10m));
        Assert.Equal(MonthlyProductTrendRateStatus.MissingQuantity,
            MonthlyProductTrendCalculator.CalculateRate(10m, null).Status);
        Assert.Equal(MonthlyProductTrendRateStatus.ZeroQuantity,
            MonthlyProductTrendCalculator.CalculateRate(10m, 0m).Status);
        Assert.Equal(MonthlyProductTrendRateStatus.MissingValue,
            MonthlyProductTrendCalculator.CalculateRate(null, 10m).Status);
        Assert.Equal(MonthlyProductTrendRateStatus.InvalidNegativeInput,
            MonthlyProductTrendCalculator.CalculateRate(-1m, 10m).Status);
    }

    [Theory]
    [InlineData("روند فروش آهن اسفنجی کچاد", "آهن اسفنجی", "کچاد")]
    [InlineData("نرخ فروش آهن اسفنجی کچاد", "آهن اسفنجی", "کچاد")]
    public void Routing_ExtractsValidatedProductSlot(string query, string product, string company)
    {
        Assert.True(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(query));
        var parsed = MonthlyProductTrendIntentRules.BuildQuery(query);
        Assert.Equal(product, parsed.ProductText);
        Assert.Equal(company, parsed.CompanyText);
    }

    [Theory]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u06af\u0646\u062f\u0644\u0647 \u06a9\u06af\u0644", "\u06af\u0646\u062f\u0644\u0647", "\u06a9\u06af\u0644")]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0622\u0647\u0646 \u0627\u0633\u0641\u0646\u062c\u06cc \u06a9\u0686\u0627\u062f", "\u0622\u0647\u0646 \u0627\u0633\u0641\u0646\u062c\u06cc", "\u06a9\u0686\u0627\u062f")]
    public void Routing_ExactUnicodePersianQueriesSelectProductSlot(
        string query, string product, string company)
    {
        Assert.True(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(query));
        Assert.False(MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(query));

        var parsed = MonthlyProductTrendIntentRules.BuildQuery(query);
        Assert.Equal(product, parsed.ProductText);
        Assert.Equal(company, parsed.CompanyText);
    }

    [Theory]
    [InlineData("\u062f\u0631 12 \u0645\u0627\u0647\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("\u062f\u0631 12 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("\u0637\u06cc 12 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("12 \u0645\u0627\u0647 \u06af\u0630\u0634\u062a\u0647")]
    [InlineData("\u062f\u0631 \u06f1\u06f2 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("\u062f\u0631 \u06f1\u06f2 \u0645\u0627\u0647\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("\u0637\u06cc \u06f1\u06f2 \u0645\u0627\u0647 \u06af\u0630\u0634\u062a\u0647")]
    [InlineData("\u062f\u0648\u0627\u0632\u062f\u0647 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("\u06cc\u06a9 \u0633\u0627\u0644 \u0627\u062e\u06cc\u0631")]
    public void Routing_NormalizesExplicitDefaultRecentWindow(string window)
    {
        var baseline = "\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u06af\u0646\u062f\u0644\u0647 \u06a9\u06af\u0644";
        var withWindow = $"{baseline} {window}";

        Assert.Equal(baseline, MonthlyProductTrendIntentRules.NormalizeQuery(withWindow));
        Assert.True(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(withWindow));
        Assert.Equal(MonthlyProductTrendIntentRules.BuildQuery(baseline), MonthlyProductTrendIntentRules.BuildQuery(withWindow));
    }

    [Theory]
    [InlineData("\u0686\u0637\u0648\u0631 \u0628\u0648\u062f\u0647\u061f")]
    [InlineData("\u0686\u06af\u0648\u0646\u0647 \u0628\u0648\u062f\u0647\u061f")]
    [InlineData("\u0686\u0647 \u0631\u0648\u0646\u062f\u06cc \u062f\u0627\u0634\u062a\u0647\u061f")]
    [InlineData("\u0686\u0647 \u0648\u0636\u0639\u06cc\u062a\u06cc \u062f\u0627\u0634\u062a\u0647\u061f")]
    public void Routing_RemovesConversationalSuffixWithoutChangingSlots(string suffix)
    {
        var baseline = "\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0622\u0647\u0646 \u0627\u0633\u0641\u0646\u062c\u06cc \u06a9\u0686\u0627\u062f";
        var withSuffix = $"{baseline} {suffix}";

        Assert.Equal(baseline, MonthlyProductTrendIntentRules.NormalizeQuery(withSuffix));
        Assert.Equal(MonthlyProductTrendIntentRules.BuildQuery(baseline), MonthlyProductTrendIntentRules.BuildQuery(withSuffix));
    }

    [Theory]
    [InlineData("\u062f\u0631 6 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("\u062f\u0631 \u06f3 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("\u062f\u0631 18 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631")]
    [InlineData("\u0633\u0627\u0644 1404")]
    [InlineData("\u0627\u0632 \u0641\u0631\u0648\u0631\u062f\u06cc\u0646 \u062a\u0627 \u0634\u0647\u0631\u06cc\u0648\u0631")]
    public async Task Routing_DifferentWindowsRemainTypedUnsupported(string window)
    {
        var baseline = "\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u06af\u0646\u062f\u0644\u0647 \u06a9\u06af\u0644";
        var query = MonthlyProductTrendIntentRules.BuildQuery($"{baseline} {window}");
        var result = await ResolveParsedAsync(
            query, "\u06a9\u06af\u0644", "4",
            Observation("\u06af\u0646\u062f\u0644\u0647", "4", productKey: "4-product"));

        Assert.NotEqual(baseline, MonthlyProductTrendIntentRules.NormalizeQuery($"{baseline} {window}"));
        Assert.Contains(window, MonthlyProductTrendIntentRules.NormalizeQuery($"{baseline} {window}"));
        Assert.Equal("\u06af\u0646\u062f\u0644\u0647", query.ProductText);
        Assert.Equal("\u06a9\u06af\u0644", query.CompanyText);
        Assert.True(query.UnsupportedTimeWindow);
        Assert.Equal(MonthlyProductTrendResolutionState.NotFound, result.ResolutionState);
        Assert.Equal("unsupported_time_window", result.BlockingReason);
    }

    [Theory]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u06af\u0646\u062f\u0644\u0647 \u06a9\u06af\u0644", "\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u06af\u0646\u062f\u0644\u0647 \u06a9\u06af\u0644 \u062f\u0631 \u06f1\u06f2 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631", "\u06af\u0646\u062f\u0644\u0647", "\u06a9\u06af\u0644", "4")]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0622\u0647\u0646 \u0627\u0633\u0641\u0646\u062c\u06cc \u06a9\u0686\u0627\u062f", "\u0637\u06cc 12 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631 \u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0622\u0647\u0646 \u0627\u0633\u0641\u0646\u062c\u06cc \u06a9\u0686\u0627\u062f \u0686\u0637\u0648\u0631 \u0628\u0648\u062f\u0647\u061f", "\u0622\u0647\u0646 \u0627\u0633\u0641\u0646\u062c\u06cc", "\u06a9\u0686\u0627\u062f", "3")]
    public async Task Routing_DefaultWindowQueriesReturnIdenticalFeature136Result(
        string baselineText, string explicitWindowText, string productTitle, string companyText, string companyId)
    {
        var baselineQuery = MonthlyProductTrendIntentRules.BuildQuery(baselineText);
        var explicitQuery = MonthlyProductTrendIntentRules.BuildQuery(explicitWindowText);
        var observation = Observation(productTitle, companyId, productKey: $"{companyId}-product");
        var baseline = await ResolveParsedAsync(baselineQuery, companyText, companyId, observation);
        var explicitWindow = await ResolveParsedAsync(explicitQuery, companyText, companyId, observation);

        Assert.True(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(baselineText));
        Assert.True(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(explicitWindowText));
        Assert.Equal(baselineQuery, explicitQuery);
        Assert.Equal(baseline.ResolutionState, explicitWindow.ResolutionState);
        Assert.Equal(baseline.ExternalCompanyId, explicitWindow.ExternalCompanyId);
        Assert.Equal(baseline.CompanyName, explicitWindow.CompanyName);
        Assert.Equal(baseline.CompanySymbol, explicitWindow.CompanySymbol);
        Assert.Equal(baseline.ProductKey, explicitWindow.ProductKey);
        Assert.Equal(baseline.ProviderProductCode, explicitWindow.ProviderProductCode);
        Assert.Equal(baseline.ProviderProductId, explicitWindow.ProviderProductId);
        Assert.Equal(baseline.ProductTitle, explicitWindow.ProductTitle);
        Assert.Equal(baseline.Message, explicitWindow.Message);
        Assert.Equal(baseline.Candidates.Count, explicitWindow.Candidates.Count);
        Assert.Equal(baseline.Candidates.Select(candidate =>
                (candidate.ProductKey, candidate.DisplayTitle, candidate.Unit, candidate.ProviderProductCode, candidate.ProviderProductId)),
            explicitWindow.Candidates.Select(candidate =>
                (candidate.ProductKey, candidate.DisplayTitle, candidate.Unit, candidate.ProviderProductCode, candidate.ProviderProductId)));
        Assert.Equal(12, explicitWindow.Points.Count);
        for (var index = 0; index < baseline.Points.Count; index++)
        {
            var expected = baseline.Points[index];
            var actual = explicitWindow.Points[index];
            Assert.Equal(expected.Period, actual.Period);
            Assert.Equal(expected.FiscalLabel, actual.FiscalLabel);
            Assert.Equal(expected.ProductKey, actual.ProductKey);
            Assert.Equal(expected.ProductTitle, actual.ProductTitle);
            Assert.Equal(expected.ProductUnit, actual.ProductUnit);
            Assert.Equal(expected.ProductionQuantity, actual.ProductionQuantity);
            Assert.Equal(expected.SaleQuantity, actual.SaleQuantity);
            Assert.Equal(expected.SalesValueMillionRial, actual.SalesValueMillionRial);
            Assert.Equal(expected.SalesValueBillionToman, actual.SalesValueBillionToman);
            Assert.Equal(expected.CalculatedSaleRateToman, actual.CalculatedSaleRateToman);
            Assert.Equal(expected.RateStatus, actual.RateStatus);
            Assert.Equal(expected.IsGap, actual.IsGap);
            Assert.Equal(expected.Evidence, actual.Evidence);
        }
    }

    [Theory]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u06a9\u06af\u0644")]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 \u06a9\u0686\u0627\u062f")]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u06a9\u06af\u0644 \u062f\u0631 6 \u0645\u0627\u0647 \u0627\u062e\u06cc\u0631")]
    public void Routing_CompanyOnlyQueriesStayOutOfProductUseCase(string query)
    {
        Assert.False(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(query));
        Assert.True(MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(query));
    }

    [Fact]
    public void Routing_PreservesRevenueMixAndComparisonPrecedence()
    {
        Assert.False(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery("سهم آهن اسفنجی از فروش کچاد"));
        Assert.False(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery("تغییر فروش آهن اسفنجی کچاد بین دو ماه"));
    }

    [Fact]
    public async Task Normalizer_PreservesSameCodeEconomicRowsAndExactReplayIsIdempotent()
    {
        await using var db = CreateDb();
        var json = """
            [{"activityID":9001,"com_ID":3,"year":1405,"month":3,"outputType":0,
              "productCode":"P-1","productTitle":"آهن A","productUnit":"ton","salesQuantity":10,"salesValue":100,
              "sourceRowId":"row-a"},
             {"activityID":9001,"com_ID":3,"year":1405,"month":3,"outputType":0,
              "productCode":"P-1","productTitle":"آهن B","productUnit":"ton","salesQuantity":20,"salesValue":200,
              "sourceRowId":"row-b"}]
            """;
        var payload = Payload("same-code", "2026-06-01T10:00:00Z", json);

        var normalizer = CreateNormalizer(db);
        await normalizer.NormalizeAsync(payload, CancellationToken.None);
        await normalizer.NormalizeAsync(payload, CancellationToken.None);

        Assert.Single(await db.MonthlyReports.ToListAsync());
        var rows = await db.MonthlyReportLineItems.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(300m, rows.Sum(row => row.SalesAmount));
        Assert.Equal(2, rows.Select(row => row.SourceRowKey).Distinct().Count());
    }

    [Fact]
    public async Task Normalizer_ReorderedReplayDoesNotChangeEconomicFacts()
    {
        await using var db = CreateDb();
        var firstJson = """
            [{"activityID":9002,"com_ID":3,"year":1405,"month":3,"outputType":0,"productCode":"P-1","productTitle":"A","productUnit":"ton","salesQuantity":10,"salesValue":100},
             {"activityID":9002,"com_ID":3,"year":1405,"month":3,"outputType":0,"productCode":"P-1","productTitle":"B","productUnit":"ton","salesQuantity":20,"salesValue":200}]
            """;
        var secondJson = """
            [{"activityID":9002,"com_ID":3,"year":1405,"month":3,"outputType":0,"productCode":"P-1","productTitle":"B","productUnit":"ton","salesQuantity":20,"salesValue":200},
             {"activityID":9002,"com_ID":3,"year":1405,"month":3,"outputType":0,"productCode":"P-1","productTitle":"A","productUnit":"ton","salesQuantity":10,"salesValue":100}]
            """;
        var first = Payload("reordered", "2026-06-01T10:00:00Z", firstJson);
        var second = Payload("reordered", "2026-06-01T10:00:00Z", secondJson);

        var normalizer = CreateNormalizer(db);
        await normalizer.NormalizeAsync(first, CancellationToken.None);
        var before = (await db.MonthlyReportLineItems.OrderBy(row => row.SourceRowFingerprint).Select(row => new { row.SourceRowFingerprint, row.SalesAmount }).ToListAsync());
        await normalizer.NormalizeAsync(second, CancellationToken.None);
        var after = await db.MonthlyReportLineItems.OrderBy(row => row.SourceRowFingerprint).Select(row => new { row.SourceRowFingerprint, row.SalesAmount }).ToListAsync();

        Assert.Equal(before.Select(row => row.SourceRowFingerprint), after.Select(row => row.SourceRowFingerprint));
        Assert.Equal(before.Select(row => row.SalesAmount), after.Select(row => row.SalesAmount));
    }

    [Fact]
    public async Task Normalizer_LaterSameIdCorrectionBecomesAcceptedAndOlderLateRevisionCannotReplaceIt()
    {
        await using var db = CreateDb();
        var normalizer = CreateNormalizer(db);
        await normalizer.NormalizeAsync(Payload("revision-1", "2026-06-01T10:00:00Z", ReportJson(10, 100, "2026-06-01T10:00:00Z")), CancellationToken.None);
        await normalizer.NormalizeAsync(Payload("revision-2", "2026-06-02T10:00:00Z", ReportJson(20, 200, "2026-06-02T10:00:00Z")), CancellationToken.None);
        await normalizer.NormalizeAsync(Payload("revision-3", "2026-05-30T10:00:00Z", ReportJson(5, 50, "2026-05-30T10:00:00Z")), CancellationToken.None);

        var reports = await db.MonthlyReports.OrderByDescending(row => row.ProviderPublishedAtUtc).ToListAsync();
        Assert.Equal(3, reports.Count);
        Assert.Equal("Accepted", Assert.Single(reports, row => row.IsAccepted).RevisionStatus);
        var acceptedId = Assert.Single(reports, row => row.IsAccepted).RevisionFingerprint;
        Assert.Equal("revision-2", acceptedId);
        Assert.Equal(2, reports.Count(row => !row.IsAccepted));
    }

    [Fact]
    public async Task ComparisonRepository_UsesOnlyProductSalesOutputTypeZeroAcceptedRows()
    {
        await using var db = CreateDb();
        var start = DateOnly.FromDateTime(new PersianCalendar().ToDateTime(1405, 3, 1, 0, 0, 0, 0));
        var end = DateOnly.FromDateTime(new PersianCalendar().ToDateTime(1405, 3, 31, 0, 0, 0, 0));
        db.MonthlyReports.AddRange(
            Report("accepted", "ProductSales", 0, true, start, end),
            Report("other-output", "ProductSales", 1, true, start, end),
            Report("null-output", "ProductSales", null, true, start, end),
            Report("service", "ServiceSales", null, true, start, end),
            Report("rejected", "ProductSales", 0, false, start, end));
        await db.SaveChangesAsync();
        var accepted = db.MonthlyReports.Single(row => row.ExternalReportId == "accepted");
        db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
        {
            Id = Guid.NewGuid(), MonthlyReportId = accepted.Id, ProductCode = "P", Title = "A", Unit = "ton",
            SalesQuantity = 1, SalesAmount = 1, SourceRowFingerprint = "row", SourcePayloadChecksum = "accepted"
        });
        await db.SaveChangesAsync();

        var repository = new EfCoreMonthlyProductComparisonRepository(db);
        var periods = await repository.GetAvailablePeriodsAsync("3");
        Assert.Single(periods);
        Assert.NotNull(await repository.GetPeriodAsync("3", periods[0]));
    }

    [Fact]
    public async Task ProductFollowUpRead_UsesNewestNonNullAcceptedLineItemPeriodAtOrBeforeCompanyPeriod()
    {
        var db = CreateDb();
        var calendar = new PersianCalendar();
        DateOnly Start(int month) => DateOnly.FromDateTime(calendar.ToDateTime(1405, month, 1, 0, 0, 0, 0));
        DateOnly End(int month) => DateOnly.FromDateTime(calendar.ToDateTime(1405, month, calendar.GetDaysInMonth(1405, month), 0, 0, 0, 0));
        var usable = Report("usable", "ProductSales", 0, true, Start(1), End(1));
        var nullOnly = Report("null-only", "ProductSales", 0, true, Start(2), End(2));
        var headerOnly = Report("header-only", "ProductSales", 0, true, Start(3), End(3));
        var future = Report("future", "ProductSales", 0, true, Start(4), End(4));
        db.MonthlyReports.AddRange(usable, nullOnly, headerOnly, future);
        db.MonthlyReportLineItems.AddRange(
            Line(usable.Id, "usable", 0m),
            Line(nullOnly.Id, "null-only", null),
            Line(future.Id, "future", 100m));
        await db.SaveChangesAsync();

        var read = await new EfCoreMonthlyProductComparisonRepository(db)
            .GetProductFollowUpReadAsync("3", new JalaliPeriod(1405, 3));

        Assert.Equal(new JalaliPeriod(1405, 1), read.AnchorPeriod);
        Assert.Equal("usable", Assert.Single(read.AnchorObservations).ProductCode);
        Assert.DoesNotContain(read.AnchorObservations, row => row.ProductCode == "future");
        Assert.DoesNotContain(read.AnchorObservations, row => row.ProductCode == "header-only");
    }

    [Fact]
    public async Task Resolver_KchadExactTitleWinsOverLongerPartialTitle()
    {
        var result = await ResolveAsync(
            "کچاد",
            "فولاد",
            Observation("فولاد", productKey: "kchad-steel"),
            Observation("فولاد فروش صادراتی", productKey: "kchad-steel-export"));

        Assert.Equal(MonthlyProductTrendResolutionState.Resolved, result.ResolutionState);
        Assert.Equal("فولاد", result.ProductTitle);
        Assert.Equal("kchad-steel", result.ProductKey);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task Resolver_KchadLongerExactTitleResolvesIndependently()
    {
        var result = await ResolveAsync(
            "کچاد",
            "فولاد فروش صادراتی",
            Observation("فولاد", productKey: "kchad-steel"),
            Observation("فولاد فروش صادراتی", productKey: "kchad-steel-export"));

        Assert.Equal(MonthlyProductTrendResolutionState.Resolved, result.ResolutionState);
        Assert.Equal("فولاد فروش صادراتی", result.ProductTitle);
        Assert.Equal("kchad-steel-export", result.ProductKey);
    }

    [Fact]
    public async Task Resolver_PartialOnlyMultipleMatchesRemainAmbiguous()
    {
        var result = await ResolveAsync(
            "کچاد",
            "فول",
            Observation("فولاد", productKey: "kchad-steel"),
            Observation("فولاد فروش صادراتی", productKey: "kchad-steel-export"));

        Assert.Equal(MonthlyProductTrendResolutionState.Ambiguous, result.ResolutionState);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public async Task Resolver_MultipleExactProductKeysRemainAmbiguous()
    {
        var result = await ResolveAsync(
            "کچاد",
            "گندله",
            Observation("گندله", productKey: "kchad-pellet-a"),
            Observation("گندله", productKey: "kchad-pellet-b"));

        Assert.Equal(MonthlyProductTrendResolutionState.Ambiguous, result.ResolutionState);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public async Task Resolver_RemainsCompanyScoped()
    {
        var companies = new Dictionary<string, ResolvedCompany>(StringComparer.Ordinal)
        {
            ["کچاد"] = Company("3"),
            ["کگل"] = Company("4")
        };
        var repository = new StubProductComparisonRepository(new Dictionary<string, ProductSalesObservation[]>
        {
            ["3"] = [Observation("فولاد", "3", productKey: "kchad-steel")],
            ["4"] = [Observation("گندله", "4", productKey: "kگل-pellet")]
        });
        var useCase = new MonthlyProductTrendQueryUseCase(
            new StubCompanyResolver(companies),
            repository);

        var result = await useCase.ExecuteAsync(new MonthlyProductTrendQuery("کگل", "فولاد"));

        Assert.Equal(MonthlyProductTrendResolutionState.NotFound, result.ResolutionState);
        Assert.Equal("product_not_found", result.BlockingReason);
        Assert.Equal(["4"], repository.RequestedCompanyIds);
    }

    [Fact]
    public async Task Resolver_KglGenericExactTitleAmbiguityRemains()
    {
        var result = await ResolveAsync(
            "کگل",
            "گندله",
            Observation("گندله", "4", productKey: "kgl-pellet-a"),
            Observation("گندله", "4", productKey: "kgl-pellet-b"));

        Assert.Equal(MonthlyProductTrendResolutionState.Ambiguous, result.ResolutionState);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public async Task Resolver_UsesCanonicalProductKeyWithoutSecondTitleResolution()
    {
        var companyId = Guid.NewGuid();
        var canonicalProduct = new CanonicalQueryProduct(
            companyId,
            "4",
            "kgl-pellet-a",
            "گندله",
            "تن",
            "PELLET-A",
            101,
            "feature-128-test");
        var useCase = new MonthlyProductTrendQueryUseCase(
            new StubCompanyResolver(new Dictionary<string, ResolvedCompany>()),
            new StubProductComparisonRepository(new Dictionary<string, ProductSalesObservation[]>
            {
                ["4"] =
                [
                    Observation("گندله", "4", productKey: "kgl-pellet-a", providerProductCode: "PELLET-A", providerProductId: 101),
                    Observation("گندله", "4", productKey: "kgl-pellet-b", providerProductCode: "PELLET-B", providerProductId: 102)
                ]
            }));

        var result = await useCase.ExecuteAsync(new MonthlyProductTrendQuery(
            "کگل",
            "گندله",
            CanonicalCompany: new CanonicalQueryEntity(companyId, "کگل", "کگل", "Company", "feature-128-test", "4"),
            CanonicalProduct: canonicalProduct));

        Assert.Equal(MonthlyProductTrendResolutionState.Resolved, result.ResolutionState);
        Assert.Equal("kgl-pellet-a", result.ProductKey);
        Assert.Equal("PELLET-A", result.ProviderProductCode);
        Assert.Equal(101, result.ProviderProductId);
    }

    private static async Task<MonthlyProductTrendResult> ResolveAsync(
        string companyText,
        string productText,
        params ProductSalesObservation[] observations)
    {
        var useCase = new MonthlyProductTrendQueryUseCase(
            new StubCompanyResolver(new Dictionary<string, ResolvedCompany>(StringComparer.Ordinal)
            {
                [companyText] = Company(companyText == "کگل" ? "4" : "3")
            }),
            new StubProductComparisonRepository(new Dictionary<string, ProductSalesObservation[]>
            {
                [companyText == "کگل" ? "4" : "3"] = observations
            }));

        return await useCase.ExecuteAsync(new MonthlyProductTrendQuery(companyText, productText));
    }

    private static async Task<MonthlyProductTrendResult> ResolveParsedAsync(
        MonthlyProductTrendQuery query,
        string companyText,
        string companyId,
        params ProductSalesObservation[] observations)
    {
        var useCase = new MonthlyProductTrendQueryUseCase(
            new StubCompanyResolver(new Dictionary<string, ResolvedCompany>(StringComparer.Ordinal)
            {
                [companyText] = Company(companyId)
            }),
            new StubProductComparisonRepository(new Dictionary<string, ProductSalesObservation[]>
            {
                [companyId] = observations
            }));

        return await useCase.ExecuteAsync(query);
    }

    private static ResolvedCompany Company(string externalCompanyId) => new(
        Guid.NewGuid(), externalCompanyId, null, null, null, null, null,
        externalCompanyId == "3" ? "کچاد" : "کگل", null);

    private static ProductSalesObservation Observation(
        string title,
        string externalCompanyId = "3",
        string? productKey = null,
        string? providerProductCode = null,
        long? providerProductId = null) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        externalCompanyId,
        new JalaliPeriod(1405, 6),
        ProviderSources.NoavaranCurrentApiName,
        "report",
        new DateOnly(2026, 8, 23),
        new DateOnly(2026, 9, 22),
        null,
        title,
        "تن",
        10m,
        10m,
        null,
        100m,
        0,
        providerProductCode,
        providerProductId,
        productKey);

    private sealed class StubCompanyResolver(IReadOnlyDictionary<string, ResolvedCompany> companies) : ICompanyResolverService
    {
        public Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult(companies.TryGetValue(symbol, out var company) ? company : null);
    }

    private sealed class StubProductComparisonRepository(
        IReadOnlyDictionary<string, ProductSalesObservation[]> observationsByCompany) : IMonthlyProductComparisonReadRepository
    {
        private readonly List<string> requestedCompanyIds = [];
        public IReadOnlyList<string> RequestedCompanyIds => requestedCompanyIds;

        public Task<IReadOnlyList<JalaliPeriod>> GetAvailablePeriodsAsync(string externalCompanyId, CancellationToken ct = default)
        {
            requestedCompanyIds.Add(externalCompanyId);
            return Task.FromResult<IReadOnlyList<JalaliPeriod>>([new JalaliPeriod(1405, 6)]);
        }

        public Task<MonthlyProductComparisonPeriod?> GetPeriodAsync(string externalCompanyId, JalaliPeriod period, CancellationToken ct = default) =>
            Task.FromResult<MonthlyProductComparisonPeriod?>(new MonthlyProductComparisonPeriod(
                period,
                observationsByCompany.TryGetValue(externalCompanyId, out var observations) ? observations : [],
                []));
    }

    private static FinancialIngestionDbContext CreateDb() => new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static NadpcoApiMonthlyActivityNormalizer CreateNormalizer(FinancialIngestionDbContext db) =>
        new(db, new NoOpRevenueMix(), new NoOpTrend(), new NoOpRanking(), NullLogger<NadpcoApiMonthlyActivityNormalizer>.Instance);

    private static ProviderRawPayload Payload(string checksum, string publishDate, string productJson) =>
        new(Guid.NewGuid(), ProviderSources.NoavaranCurrentApiName, ProviderDataset.MonthlyProductionSales,
            "monthly", "3", JsonSerializer.Serialize(new NadpcoMonthlyActivityEnvelope(productJson, null, null, null, null, "[]")), checksum,
            DateTimeOffset.Parse(publishDate, CultureInfo.InvariantCulture));

    private static string ReportJson(decimal quantity, decimal value, string publishDateTime = "2026-06-01T10:00:00Z") =>
        $"[{{\"activityID\":9003,\"com_ID\":3,\"year\":1405,\"month\":3,\"outputType\":0,\"publishDateTime\":\"{publishDateTime}\",\"productCode\":\"P-1\",\"productTitle\":\"A\",\"productUnit\":\"ton\",\"salesQuantity\":{quantity.ToString(CultureInfo.InvariantCulture)},\"salesValue\":{value.ToString(CultureInfo.InvariantCulture)}}}]";

    private static NormalizedMonthlyReportRow Report(string id, string type, int? output, bool accepted, DateOnly start, DateOnly end) => new()
    {
        Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName, ExternalCompanyId = "3", ExternalReportId = id,
        PeriodStart = start, PeriodEnd = end, ReportType = type, OutputType = output, IsAccepted = accepted,
        LogicalReportKey = $"{id}", RevisionFingerprint = id, RevisionStatus = accepted ? "Accepted" : "RejectedOlder", SourcePayloadChecksum = id,
        LastSynchronizedAt = DateTimeOffset.UtcNow
    };

    private static NormalizedMonthlyReportLineItemRow Line(Guid reportId, string key, decimal? amount) => new()
    {
        Id = Guid.NewGuid(), MonthlyReportId = reportId, ProductCode = key, Title = key, Unit = "ton",
        SalesAmount = amount, SourceRowFingerprint = key, SourcePayloadChecksum = key
    };

    private sealed class NoOpRevenueMix : ICompanyProductRevenueMixCalculator
    {
        public Task RecalculateAsync(string externalCompanyId, int jalaliYear, byte jalaliMonth, string? bourseSymbol, string? companyTitle, string? fiscalEndDate, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class NoOpTrend : ICompanyMonthlyActivityTrendSnapshotCalculator
    {
        public Task RecalculateAsync(string externalCompanyId, int jalaliYear, byte jalaliMonth, string? bourseSymbol, string? companyName, string? fiscalEndDate, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecalculateRangeAsync(string externalCompanyId, int fromYear, int fromMonth, int toYear, int toMonth, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class NoOpRanking : IRecalculateMonthlySalesQualityRankingUseCase
    {
        public Task<RecalculateMonthlySalesQualityRankingResult> ExecuteAsync(RecalculateMonthlySalesQualityRankingRequest request, CancellationToken ct = default) =>
            Task.FromResult(new RecalculateMonthlySalesQualityRankingResult(request.ReportYear ?? 0, request.ReportMonth ?? 0, 0, 0, DateTimeOffset.UtcNow));
    }
}

using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.AI.Orchestration;
using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.UnitTests;

public sealed class QuarterlyFinancialReports144Tests
{
    [Theory]
    [InlineData("جدول گزارشات فصلی چخزر", "چخزر")]
    [InlineData("جدول گزارشات فصلی کگل", "کگل")]
    [InlineData("جدول گزارش‌های فصلی چخزر", "چخزر")]
    [InlineData("گزارش فصلی چخزر", "چخزر")]
    [InlineData("صورت سود و زیان فصلی کگل", "کگل")]
    public void BuildQuarterlyQuery_RemovesPersianReportCuesWithoutLeakingPluralSuffix(
        string message,
        string expectedCompany)
    {
        Assert.True(FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(message));
        var query = FinancialStatementTableIntentRules.BuildQuarterlyQuery(message);
        Assert.Equal(expectedCompany, query.CompanyQuery);
        Assert.Equal(FinancialStatementType.IncomeStatement, query.StatementType);
        Assert.True(query.UseQuarterlyReportTable);
    }

    [Fact]
    public void Normalize_UsesAdjacentYtdDifferencesAndPreservesZeroMissingAndProvenance()
    {
        var start = new DateOnly(2025, 3, 21);
        var q1 = Report(start, 3, new Dictionary<int, decimal?>
        {
            [15] = 7_365_921m, [1] = -4_751_954m, [139] = 2_000_000m,
            [160] = 10_155m, [153] = 200_000m, [12] = 0m, [278] = 0m
        });
        var q2 = Report(start, 6, new Dictionary<int, decimal?>
        {
            [15] = 16_754_501m, [1] = -10_000_000m, [139] = 5_000_000m,
            [160] = 24_967m, [153] = 200_000m, [12] = 0m, [278] = 0m
        });

        var result = QuarterlyFinancialReportsNormalizer.Normalize("چخزر", null, "issuer-1", [q1, q2], 2, null,
            DateTimeOffset.Parse("2026-10-12T00:00:00Z"));

        Assert.Equal("Available", result.Status);
        Assert.Equal(16, result.Rows.Count);
        Assert.Equal(new int?[] { 15, 1, 139, null, 5, 39, 140, 12, 40, 142, 334, 279, 278, 143, 160, 153 },
            result.Rows.Select(row => row.SourceItemId));
        Assert.Equal("فروش", result.Rows[0].LabelFa);
        Assert.Equal("مارجین ناخالص", result.Rows[3].LabelFa);
        Assert.Equal(153, result.Rows[15].SourceItemId);
        Assert.Equal(new[] { 2, 1 }, result.PeriodsNewestFirst.Select(period => period.Quarter));
        Assert.Equal(9_388_580m, Cell(result, 15, 2).Value);
        Assert.Equal(-5_248_046m, Cell(result, 1, 2).Value);
        Assert.Equal(14_812m, Cell(result, 160, 2).Value);
        Assert.Equal("rial_per_share", Cell(result, 160, 2).Unit);
        Assert.Equal(0, Cell(result, 160, 2).ValueScale);
        Assert.Equal(200_000m, Cell(result, 153, 2).Value);
        Assert.Equal("thousand_shares", Cell(result, 153, 2).Unit);
        Assert.Equal(0m, Cell(result, 12, 2).Value);
        Assert.Equal(QuarterlyCellState.Available, Cell(result, 12, 2).State);
        Assert.True(Cell(result, 12, 2).RenderZeroAsDash);
        Assert.Equal(0m, Cell(result, 278, 2).Value);
        Assert.Equal(QuarterlyCellState.Available, Cell(result, 278, 2).State);
        Assert.True(Cell(result, 278, 2).RenderZeroAsDash);
        Assert.Equal(2_000_000m / 7_365_921m * 100m, Cell(result, null, 1, "gross_margin").Value);
        Assert.Equal(2, Cell(result, 15, 2).SourceReportIds.Count);
        Assert.Equal(1, result.Comparisons.Count(item => item.RowId == "source_15" && item.RelativeChangePercent.HasValue));
    }

    [Fact]
    public void Normalize_DoesNotBridgeMissingPredecessorAndPreservesNegativeZeroDifference()
    {
        var start = new DateOnly(2025, 3, 21);
        var report = Report(start, 6, new Dictionary<int, decimal?> { [15] = 0m, [12] = 0m, [153] = 0m });
        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1", [report], 2, null,
            DateTimeOffset.UtcNow);

        Assert.Equal(QuarterlyCellState.MissingPredecessor, Cell(result, 15, 2).State);
        Assert.Null(Cell(result, 15, 2).Value);
        Assert.Equal(QuarterlyCellState.MissingPredecessor, Cell(result, 12, 2).State);
        Assert.Equal(0m, Cell(result, 153, 2).Value);
        Assert.Equal(QuarterlyCellState.Available, Cell(result, 153, 2).State);
    }

    [Fact]
    public void Normalize_SelectsAllFourReportPriorityLevels()
    {
        var start = new DateOnly(2025, 3, 21);
        var report = Report(start, 3, new Dictionary<int, decimal?> { [15] = 10m });
        var auditedNotRestated = report with { ReportId = Guid.NewGuid(), ExternalReportId = "b", IsAudited = true };
        var unauditedRestated = report with { ReportId = Guid.NewGuid(), ExternalReportId = "c", IsRepresented = true,
            Items = [report.Items[0] with { Value = 30m }] };
        var auditedRestated = report with { ReportId = Guid.NewGuid(), ExternalReportId = "d", IsAudited = true,
            IsRepresented = true, Items = [report.Items[0] with { Value = 40m }] };
        var unauditedNotRestated = report with { Items = [report.Items[0] with { Value = 20m }] };

        var prioritySets = new[]
        {
            (Reports: new[] { unauditedNotRestated, auditedNotRestated, unauditedRestated, auditedRestated }, Expected: 40m),
            (Reports: new[] { unauditedNotRestated, auditedNotRestated, unauditedRestated }, Expected: 10m),
            (Reports: new[] { unauditedNotRestated, unauditedRestated }, Expected: 30m),
            (Reports: new[] { unauditedNotRestated }, Expected: 20m)
        };
        foreach (var (reports, expected) in prioritySets)
        {
            var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1", reports, 1, null,
                DateTimeOffset.UtcNow);
            Assert.Equal(expected, Cell(result, 15, 1).Value);
        }
    }

    [Fact]
    public void Normalize_AuditedNotRestatedOutranksUnauditedRestated()
    {
        var start = new DateOnly(2025, 3, 21);
        var unauditedRestated = Report(start, 3, new Dictionary<int, decimal?> { [15] = 30m }) with { IsRepresented = true };
        var auditedNotRestated = Report(start, 3, new Dictionary<int, decimal?> { [15] = 20m }) with { IsAudited = true };

        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1",
            [unauditedRestated, auditedNotRestated], 1, null, DateTimeOffset.UtcNow);

        Assert.Equal(20m, Cell(result, 15, 1).Value);
    }

    [Fact]
    public void Normalize_ConflictingTopPriorityVersionsRemainUnavailable()
    {
        var start = new DateOnly(2025, 3, 21);
        var report = Report(start, 3, new Dictionary<int, decimal?> { [15] = 10m }) with { AnnouncementDate = "2025-06-01" };
        var duplicate = report with { ReportId = Guid.NewGuid(), SourcePayloadChecksum = "other",
            Items = [report.Items[0] with { Value = 11m }] };

        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1", [report, duplicate], 1, null,
            DateTimeOffset.UtcNow);

        Assert.Equal("Unavailable", result.Status);
        Assert.Contains("conflicting_report_revision", result.Reason);
        Assert.Equal(QuarterlyFinancialReportOutcome.SourceObservationsUnavailable, result.Outcome);
    }

    [Fact]
    public void Normalize_IsolatesProviderCompanyStatementAndReportScope()
    {
        var start = new DateOnly(2025, 3, 21);
        var valid = Report(start, 3, new Dictionary<int, decimal?> { [15] = 10m });
        var foreignProvider = valid with
        {
            ReportId = Guid.NewGuid(), ProviderName = "OtherProvider",
            Items = [valid.Items[0] with { Value = 999m }]
        };
        var foreignCompany = valid with
        {
            ReportId = Guid.NewGuid(), ExternalCompanyId = "other-company",
            Items = [valid.Items[0] with { Value = 888m }]
        };
        var wrongType = valid with { ReportId = Guid.NewGuid(), StatementType = "BalanceSheet" };

        var result = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [valid, foreignProvider, foreignCompany, wrongType], 1, null, DateTimeOffset.UtcNow);

        Assert.Equal(10m, Cell(result, 15, 1).Value);
        Assert.Equal("Available", result.Status);
        var consolidatedOnly = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [valid with { IsComposing = true }], 1, null, DateTimeOffset.UtcNow);
        Assert.True(consolidatedOnly.IsComposing);
        var nextPeriodConsolidated = valid with
        {
            ReportId = Guid.NewGuid(), ExternalReportId = "consolidated-q2", PeriodMonths = 6,
            PeriodEnd = DateOnly.FromDateTime(new PersianCalendar().AddMonths(start.ToDateTime(TimeOnly.MinValue), 6).AddDays(-1)),
            IsComposing = true
        };
        var scopeConflict = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [valid, nextPeriodConsolidated], 2, null, DateTimeOffset.UtcNow);
        Assert.Equal("Unavailable", scopeConflict.Status);
        Assert.Contains("conflicting_report_scope", scopeConflict.Reason);
    }

    [Fact]
    public void ApprovedMappings_ContainExactlyTheFifteenApprovedSourceIdentities()
    {
        Assert.Equal(new[] { 15, 1, 139, 5, 39, 140, 12, 40, 142, 334, 279, 278, 143, 160, 153 },
            QuarterlyFinancialReportsNormalizer.ApprovedMappings.Select(item => item.SourceItemId));
        Assert.Equal(new[]
        {
            "07c03a70-a1fa-4b9b-861c-937748e2a1dc", "57940eb5-27f8-42b1-8827-d64dc49eab2d",
            "917b2bbb-3859-42f0-9864-2eac3f98a3b3", "c04b4fae-69db-48d9-a63e-c96a932e7b89",
            "17f91e30-ba52-4789-89d0-3ffd3ac61c17", "f2a6e460-176f-4426-a4e7-78d4ffd86ba6",
            "b8342891-ad45-4c4b-a4dd-361bb5f37adb", "e08f0ce7-ab6a-4a24-8fab-efc3f93c140f",
            "1cbc0ee5-21bb-4b9d-b5ba-72c3b84731b3", "31f4454a-78d2-41fb-8caa-b86564423cd6",
            "abdbd529-7e27-43cc-a955-10ae19aea4cb", "cb6c92c3-cff6-40c1-a85a-db9fde4ba791",
            "bd36909d-abf8-44c2-b7db-5e53dd678b53", "2146da26-b28e-4d04-b50c-f49fc9b70e61",
            "e98e6c00-7204-44b1-a279-bbbb68781d3c"
        }, QuarterlyFinancialReportsNormalizer.ApprovedMappings.Select(item => item.CatalogId));
    }

    [Fact]
    public void Normalize_UsesPersianFiscalCalendarForNonEsfandFiscalStart()
    {
        var start = new DateOnly(2025, 5, 22);
        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1",
            [Report(start, 3, new Dictionary<int, decimal?> { [15] = 1m }),
             Report(start, 6, new Dictionary<int, decimal?> { [15] = 4m })],
            2, null, DateTimeOffset.UtcNow);

        Assert.Equal(3m, Cell(result, 15, 2).Value);
        Assert.Equal(1404, result.PeriodsNewestFirst[0].FiscalYear);
    }

    [Fact]
    public void Normalize_MarksUnsupportedFiscalBoundaryUnavailableInsteadOfGuessing()
    {
        var start = new DateOnly(2025, 3, 21);
        var report = Report(start, 3, new Dictionary<int, decimal?> { [15] = 100m }) with
        {
            PeriodEnd = new DateOnly(2025, 6, 20)
        };
        var result = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [report], 1, null, DateTimeOffset.UtcNow);

        var cell = Cell(result, 15, 1);
        Assert.Equal(QuarterlyCellState.UnsupportedFiscalPeriod, cell.State);
        Assert.Null(cell.Value);
        Assert.Contains("boundary", cell.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_DoesNotSubtractAcrossMissingIntermediatePeriod()
    {
        var start = new DateOnly(2025, 3, 21);
        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1",
            [Report(start, 3, new Dictionary<int, decimal?> { [15] = 1m }),
             Report(start, 9, new Dictionary<int, decimal?> { [15] = 12m })],
            3, null, DateTimeOffset.UtcNow);

        Assert.Equal(QuarterlyCellState.MissingPredecessor, Cell(result, 15, 3).State);
        Assert.Null(Cell(result, 15, 3).Value);
    }

    [Fact]
    public void Normalize_GrossMarginIsUnavailableForZeroRevenue()
    {
        var start = new DateOnly(2025, 3, 21);
        var report = Report(start, 3, new Dictionary<int, decimal?> { [15] = 0m, [139] = 1m });
        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1", [report], 1, null,
            DateTimeOffset.UtcNow);

        Assert.Equal(QuarterlyCellState.CalculationUnavailable, Cell(result, null, 1, "gross_margin").State);
        Assert.Null(Cell(result, null, 1, "gross_margin").Value);
    }

    [Fact]
    public void Normalize_GrossMarginIsUnavailableWhenOperandIsMissing()
    {
        var start = new DateOnly(2025, 3, 21);
        var report = Report(start, 3, new Dictionary<int, decimal?> { [15] = 100m });
        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1", [report], 1, null,
            DateTimeOffset.UtcNow);

        Assert.Equal(QuarterlyCellState.CalculationUnavailable, Cell(result, null, 1, "gross_margin").State);
        Assert.Null(Cell(result, null, 1, "gross_margin").Value);
    }

    [Fact]
    public void Normalize_UsesTypedComparisonForZeroBase()
    {
        var start = new DateOnly(2025, 3, 21);
        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1",
            [Report(start, 3, new Dictionary<int, decimal?> { [15] = 0m }),
             Report(start, 6, new Dictionary<int, decimal?> { [15] = 1m })],
            2, null, DateTimeOffset.UtcNow);

        var comparison = Assert.Single(result.Comparisons, item => item.RowId == "source_15");
        Assert.Equal(QuarterlyComparisonState.NonPositiveBase, comparison.State);
        Assert.Null(comparison.RelativeChangePercent);
        Assert.Equal(1m, comparison.AbsoluteChange);
    }

    [Fact]
    public void Normalize_ReportsTypedQuarterlyComparisonCases()
    {
        var start = new DateOnly(2025, 3, 21);
        var scenarios = new[]
        {
            (Prior: 100m, Current: 150m, State: QuarterlyComparisonState.Available, Absolute: 50m, Relative: (decimal?)50m),
            (Prior: -100m, Current: -50m, State: QuarterlyComparisonState.NonPositiveBase, Absolute: 50m, Relative: (decimal?)null),
            (Prior: -100m, Current: 50m, State: QuarterlyComparisonState.SignChanged, Absolute: 150m, Relative: (decimal?)null),
            (Prior: 10m, Current: 0m, State: QuarterlyComparisonState.Available, Absolute: -10m, Relative: (decimal?)-100m)
        };
        foreach (var scenario in scenarios)
        {
            var result = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
                [Report(start, 3, new Dictionary<int, decimal?> { [15] = scenario.Prior }),
                 Report(start, 6, new Dictionary<int, decimal?> { [15] = scenario.Prior + scenario.Current })],
                2, null, DateTimeOffset.UtcNow);

            var comparison = Assert.Single(result.Comparisons, item => item.RowId == "source_15");
            Assert.Equal(scenario.State, comparison.State);
            Assert.Equal(scenario.Absolute, comparison.AbsoluteChange);
            Assert.Equal(scenario.Relative, comparison.RelativeChangePercent);
        }
    }

    [Fact]
    public void Normalize_GrossMarginComparisonSeparatesRelativePercentAndPercentagePoints()
    {
        var start = new DateOnly(2025, 3, 21);
        var result = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [Report(start, 3, new Dictionary<int, decimal?> { [15] = 100m, [139] = 25m }),
             Report(start, 6, new Dictionary<int, decimal?> { [15] = 200m, [139] = 55m })],
            2, null, DateTimeOffset.UtcNow);

        var comparison = Assert.Single(result.Comparisons, item => item.RowId == "gross_margin");
        Assert.Equal(QuarterlyComparisonState.Available, comparison.State);
        Assert.Equal(5m, comparison.AbsoluteChange);
        Assert.Equal(20m, comparison.RelativeChangePercent);
        Assert.Equal(5m, comparison.PercentagePointChange);
    }

    [Fact]
    public void Normalize_AllowsMissingCurrencyAndDifferentRevisionMetadataAcrossPeriods()
    {
        var start = new DateOnly(2025, 3, 21);
        var prior = Report(start, 3, new Dictionary<int, decimal?> { [15] = 100m });
        var priorWithoutCurrency = prior with
        {
            IsAudited = false, IsRepresented = false,
            CurrencyCode = null, AccountingBasis = null, ReportRevisionLineage = null
        };
        var current = Report(start, 6, new Dictionary<int, decimal?> { [15] = 200m }) with
        {
            IsAudited = true, IsRepresented = true,
            CurrencyCode = null, AccountingBasis = null, ReportRevisionLineage = "different-lineage"
        };
        var result = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [priorWithoutCurrency, current], 2, null, DateTimeOffset.UtcNow);

        var q2 = Cell(result, 15, 2);
        Assert.Equal(QuarterlyCellState.Available, q2.State);
        Assert.Equal(100m, q2.Value);
        Assert.Equal("million_rial", q2.Unit);
        Assert.Equal("IRR", q2.CurrencyCode);
        Assert.Equal(6, q2.ValueScale);
        Assert.Equal(2, q2.SourceReportIds.Count);
        Assert.Contains(result.SelectedReports!, report => report.ReportId == priorWithoutCurrency.ReportId && !report.IsAudited && !report.IsRepresented);
        Assert.Contains(result.SelectedReports!, report => report.ReportId == current.ReportId && report.IsAudited && report.IsRepresented);
        Assert.Contains(result.SelectedReports!, report => report.ReportId == priorWithoutCurrency.ReportId && report.CurrencyCode is null);
        Assert.Contains(result.SelectedReports!, report => report.ReportId == current.ReportId && report.ReportRevisionLineage == "different-lineage");
    }

    [Fact]
    public void Normalize_LeavesValidQuartersAvailableWhenAnotherPeriodHasUnsupportedScale()
    {
        var start = new DateOnly(2025, 3, 21);
        var q1 = Report(start, 3, new Dictionary<int, decimal?> { [15] = 10m });
        var q2 = Report(start, 6, new Dictionary<int, decimal?> { [15] = 25m }) with
        {
            AssumedScale = "Rials"
        };
        var result = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [q1, q2], 2, null, DateTimeOffset.UtcNow);

        Assert.Equal(10m, Cell(result, 15, 1).Value);
        Assert.Equal(QuarterlyCellState.Available, Cell(result, 15, 1).State);
        Assert.Null(Cell(result, 15, 2).Value);
        Assert.Equal(QuarterlyCellState.IncompatibleObservation, Cell(result, 15, 2).State);
    }

    [Fact]
    public void Normalize_DeclaresUnitsScaleAndFiscalEvidenceInStructuredOutput()
    {
        var report = Report(new DateOnly(2025, 3, 21), 3,
            new Dictionary<int, decimal?> { [15] = 123.456789m, [160] = -2.5m, [153] = 200_000m });
        report = report with { FiscalYearEvidence = "1404/01/01" };
        var result = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1", [report], 1,
            null, DateTimeOffset.UtcNow);

        var revenue = Cell(result, 15, 1);
        Assert.Equal(123.456789m, revenue.Value);
        Assert.Equal("IRR", revenue.CurrencyCode);
        Assert.Equal(6, revenue.ValueScale);
        Assert.Equal("million_rial", revenue.Unit);
        Assert.Equal("thousand_shares", Cell(result, 153, 1).Unit);
        Assert.Equal(3, Cell(result, 153, 1).ValueScale);
        Assert.Equal("1404/01/01", Assert.Single(result.SelectedReports!).FiscalYearEvidence);
    }

    [Fact]
    public void Normalize_MissingAndUnavailableComparisonStatesRemainDistinct()
    {
        var start = new DateOnly(2025, 3, 21);
        var missing = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [Report(start, 3, new Dictionary<int, decimal?> { [15] = 100m }),
             Report(start, 6, new Dictionary<int, decimal?> { [15] = 200m })], 2, null, DateTimeOffset.UtcNow);
        Assert.Equal(QuarterlyComparisonState.MissingCurrent,
            Assert.Single(missing.Comparisons, item => item.RowId == "source_1").State);

        var unavailable = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [Report(start, 3, new Dictionary<int, decimal?> { [15] = 0m, [139] = 1m }),
             Report(start, 6, new Dictionary<int, decimal?> { [15] = 0m, [139] = 2m })], 2, null, DateTimeOffset.UtcNow);
        Assert.Equal(QuarterlyComparisonState.Unavailable,
            Assert.Single(unavailable.Comparisons, item => item.RowId == "gross_margin").State);
    }

    [Theory]
    [InlineData("گزارش فصلی چخزر", 5)]
    [InlineData("عملکرد ۵ فصل اخیر فولاد", 5)]
    [InlineData("جدول سود و زیان ۸ فصل اخیر فولاد", 8)]
    public void QuarterlyTableIntent_RecognizesQuarterlyQueriesAndDefaultOrRequestedCount(string query, int count)
    {
        Assert.True(FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(query));
        Assert.Equal(count, FinancialStatementTableIntentRules.ResolveQuarterCount(query));
    }

    [Theory]
    [InlineData("Analyze Foolad quarterly earnings", false)]
    [InlineData("Show the last 4 quarterly income statements for Foolad", true)]
    [InlineData("quarterly financial reports table for Foolad", true)]
    public void QuarterlyTableIntent_DistinguishesTablesFromAnalysis(string query, bool expected)
    {
        Assert.Equal(expected, FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(query));
    }

    [Fact]
    public void ResultJsonRoundTrip_PreservesAvailableZeroAndMissingAsDistinctStates()
    {
        var start = new DateOnly(2025, 3, 21);
        var report = Report(start, 3, new Dictionary<int, decimal?> { [15] = 0m, [12] = 0m });
        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1", [report], 1, null,
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(result);
        var restored = JsonSerializer.Deserialize<QuarterlyFinancialReportTableResult>(json);

        Assert.NotNull(restored);
        Assert.Equal(0m, Cell(restored!, 15, 1).Value);
        Assert.Equal(QuarterlyCellState.Available, Cell(restored!, 15, 1).State);
        Assert.Null(Cell(restored!, 1, 1).Value);
        Assert.Equal(QuarterlyCellState.MissingObservation, Cell(restored!, 1, 1).State);
    }

    [Fact]
    public void QuarterlyTableIntent_ParsesJalaliEndPeriod()
    {
        var actual = FinancialStatementTableIntentRules.ResolveEndingPeriod("گزارش فصلی چخزر تا ۱۴۰۴/۰۳/۳۱");
        Assert.Equal(new DateOnly(2025, 6, 21), actual);
    }

    private static QuarterlyFinancialCell Cell(QuarterlyFinancialReportTableResult result, int? sourceId,
        int quarter, string? rowId = null)
    {
        var period = Assert.Single(result.PeriodsNewestFirst, item => item.Quarter == quarter);
        var row = result.Rows.Single(item => rowId is not null ? item.RowId == rowId : item.SourceItemId == sourceId);
        return row.Cells[period.Label];
    }

    private static QuarterlyReportObservation Report(DateOnly start, int months, IDictionary<int, decimal?> values)
    {
        var reportId = Guid.NewGuid();
        var end = DateOnly.FromDateTime(new PersianCalendar().AddMonths(start.ToDateTime(TimeOnly.MinValue), months).AddDays(-1));
        var items = values.Select(pair => new QuarterlySourceObservation(Guid.NewGuid(),
            QuarterlyFinancialReportsNormalizer.ApprovedMappings.Single(item => item.SourceItemId == pair.Key).CatalogId is { } id
                ? Guid.Parse(id) : Guid.Empty,
            pair.Key, pair.Key == 12 ? "هزینه‌های مالی" : pair.Key == 278 ? "سود عملیات متوقف‌شده" : null,
            pair.Key == 153 ? "thousand_shares" : "million_rial", pair.Value)).ToArray();
        return new QuarterlyReportObservation(reportId, $"report-{reportId}", "NoavaranCurrentApi", "issuer-1",
            "IncomeStatement", months, start, end, false, false, false, $"checksum-{reportId}",
            DateTimeOffset.UtcNow, null, null, items, "MillionRials", "N/A", "IRR", "Accrual", "fixture-lineage");
    }
}

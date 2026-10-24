using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.AI.Orchestration;
using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.UnitTests;

public sealed class QuarterlyFinancialReports144Tests
{
    private static readonly Guid BankingIndustryGroupId = Guid.Parse("c96520f9-1d2e-444a-ab46-5bdffd3797d2");

    [Theory]
    [InlineData("جدول گزارشات فصلی چخزر", "چخزر")]
    [InlineData("گزارشات فصلی چخزر؟", "چخزر")]
    [InlineData("گزارشات فصلی چخزر", "چخزر")]
    [InlineData("جدول گزارشات فصلی کگل", "کگل")]
    [InlineData("جدول گزارش‌های فصلی چخزر", "چخزر")]
    [InlineData("گزارشهای فصلی غبهنوش؟", "غبهنوش")]
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
        Assert.Equal(15, result.Rows.Count);
        Assert.Equal(new int?[] { 15, 1, 139, null, 5, 39, 140, 12, 40, 142, 334, 278, 143, 160, 153 },
            result.Rows.Select(row => row.SourceItemId));
        Assert.Equal("فروش", result.Rows[0].LabelFa);
        Assert.Equal("مارجین ناخالص", result.Rows[3].LabelFa);
        Assert.Equal(153, result.Rows[14].SourceItemId);
        Assert.Equal(new[] { 2, 1 }, result.PeriodsNewestFirst.Select(period => period.Quarter));
        Assert.Equal(9_388_580m, Cell(result, 15, 2).Value);
        Assert.Equal(-5_248_046m, Cell(result, 1, 2).Value);
        Assert.Null(Cell(result, 160, 2).Value);
        Assert.Equal(QuarterlyCellState.CalculationUnavailable, Cell(result, 160, 2).State);
        Assert.Equal(24_967m, Cell(result, 160, 2).ComparableEvidence?.RawCurrentValue);
        Assert.Equal(10_155m, Cell(result, 160, 2).ComparableEvidence?.RawPreviousValue);
        Assert.Equal("rial_per_share", Cell(result, 160, 2).Unit);
        Assert.Equal(0, Cell(result, 160, 2).ValueScale);
        Assert.Equal(200_000m, Cell(result, 153, 2).Value);
        Assert.Equal(200_000m, Cell(result, 153, 2).ComparableEvidence?.RawCurrentValue);
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
    public void Normalize_BankingUsesTwentyTwoRowsAndStandaloneOperatingIncome()
    {
        var start = Jalali(1405, 1, 1);
        var q1 = Report(start, 3, new Dictionary<int, decimal?>
        {
            [304] = 1_009_684_387m, [305] = -651_453_193m, [306] = 358_231_194m,
            [26] = 186_404_253m, [7] = -15_582_207m, [307] = 170_822_046m,
            [61] = 2m, [27] = 3m, [194] = 200_000_000m, [40] = 4m, [5] = -5m, [6] = -6m, [12] = -7m,
            [142] = 395_735_576m, [13] = -11_677_566m, [278] = 0m, [143] = 384_058_010m, [153] = 3_382_000_000m
        });
        var q2 = Report(start, 6, new Dictionary<int, decimal?>
        {
            [304] = 2_150_761_533m, [305] = -1_220_160_496m, [306] = 930_601_037m,
            [26] = 264_372_382m, [7] = -34_811_139m, [307] = 229_561_243m,
            [61] = 9_955_260m, [27] = 2_911_690m, [194] = 814_028_450m, [40] = 5m, [5] = -8m, [6] = -9m, [12] = -11m,
            [142] = 1_112_706_518m, [13] = -87_428_386m, [278] = 0m, [143] = 1_025_278_132m, [153] = 3_382_000_000m
        });
        var group = BankingIndustryGroupId;
        var result = QuarterlyFinancialReportsNormalizer.Normalize("وبملت", "بانک ملت", "issuer-1", [q1, q2], 2,
            null, DateTimeOffset.UtcNow, true, group);

        Assert.Equal("BankingIncomeStatement", result.TemplateId);
        Assert.Equal(group, result.ClassificationGroupId);
        Assert.Equal(22, result.Rows.Count);
        Assert.Equal("سود هر سهم بر اساس آخرین سرمایه", result.Rows.Single(row => row.SourceItemId == 160).LabelFa);
        Assert.Equal(new int?[] { 304, 305, 306, null, 26, 7, 307, null, 61, 27, 194, null, 40, 5, 6, 12, 142, 13, 278, 143, 160, 153 },
            result.Rows.Select(row => row.SourceItemId));
        Assert.Equal(new[] { "source_61", "source_27", "source_194", "banking_operating_income", "source_40" },
            result.Rows.Skip(8).Take(5).Select(row => row.RowId));
        Assert.DoesNotContain(result.Rows, row => row.SourceItemId is 140 or 199 or 309);
        Assert.DoesNotContain(result.Rows, row => row.RowId is "source_140" or "source_199" or "source_309");
        Assert.Equal(572_369_843m, result.Rows.Single(row => row.SourceItemId == 306).Cells["1405 Q2"].Value);
        Assert.Equal(-568_707_303m, result.Rows.Single(row => row.SourceItemId == 305).Cells["1405 Q2"].Value);
        Assert.Equal(58_739_197m, result.Rows.Single(row => row.SourceItemId == 307).Cells["1405 Q2"].Value);
        var otherOperatingIncome = result.Rows.Single(row => row.SourceItemId == 194);
        Assert.Equal(614_028_450m, otherOperatingIncome.Cells["1405 Q2"].Value);
        Assert.Equal("million_rial", otherOperatingIncome.Cells["1405 Q2"].Unit);
        Assert.Equal(q2.Items.Single(item => item.SourceItemId == 194).CatalogId, otherOperatingIncome.SourceCatalogId);
        Assert.Equal("خالص سایر درآمدهای غیر مشاع", otherOperatingIncome.SourceCatalogTitleFa);
        var operatingIncome = result.Rows.Single(row => row.RowId == "banking_operating_income");
        var operatingCell = operatingIncome.Cells["1405 Q2"];
        Assert.Null(operatingIncome.SourceItemId);
        Assert.Equal(QuarterlyRowPolicy.DerivedBankingOperatingIncome, operatingIncome.Policy);
        Assert.Equal(1_258_004_435m, operatingCell.Value);
        Assert.Equal("million_rial", operatingCell.Unit);
        Assert.Equal(new decimal?[] { 572_369_843m, 58_739_197m, 614_028_450m, 9_955_258m, 2_911_687m }, operatingCell.OperandValues);
        Assert.Equal(10, operatingCell.SourceLineItemIds.Count);
        Assert.Contains("source_306 + source_307 + source_194 + source_61 + source_27", operatingCell.NormalizationPolicy);
        Assert.Equal(-75_750_820m, result.Rows.Single(row => row.SourceItemId == 13).Cells["1405 Q2"].Value);
        Assert.Equal(0m, result.Rows.Single(row => row.SourceItemId == 278).Cells["1405 Q2"].Value);
        Assert.Equal(641_220_122m, result.Rows.Single(row => row.SourceItemId == 143).Cells["1405 Q2"].Value);
        Assert.Equal(572_369_843m / 1_141_077_146m * 100m, result.Rows.Single(row => row.RowId == "banking_gross_margin").Cells["1405 Q2"].Value);
        Assert.Equal(58_739_197m / 77_968_129m * 100m, result.Rows.Single(row => row.RowId == "banking_fee_margin").Cells["1405 Q2"].Value);
        var expenseComparison = Assert.Single(result.Comparisons, comparison => comparison.RowId == "source_305");
        Assert.Equal(QuarterlyComparisonState.Available, expenseComparison.State);
        Assert.Equal((568_707_303m / 651_453_193m - 1m) * 100m, expenseComparison.RelativeChangePercent);
        Assert.Equal("percent", result.Rows.Single(row => row.RowId == "banking_fee_margin").Unit);
        Assert.All(result.Rows.Where(row => row.Policy == QuarterlyRowPolicy.DerivedBankingMargin), row =>
            Assert.All(row.Cells.Values, cell => Assert.Equal("percent", cell.Unit)));
        var roundTrip = JsonSerializer.Deserialize<QuarterlyFinancialReportTableResult>(JsonSerializer.Serialize(result));
        Assert.Equal("BankingIncomeStatement", roundTrip?.TemplateId);
        Assert.Equal(572_369_843m / 1_141_077_146m * 100m,
            roundTrip?.Rows.Single(row => row.RowId == "banking_gross_margin").Cells["1405 Q2"].Value);
        Assert.Equal(1_258_004_435m,
            roundTrip?.Rows.Single(row => row.RowId == "banking_operating_income").Cells["1405 Q2"].Value);
    }

    [Fact]
    public void Normalize_BankingOperatingIncomeRequiresAllVerifiedStandaloneOperandsAndPreservesSignsAndZero()
    {
        var start = Jalali(1405, 1, 1);
        var complete = Report(start, 3, new Dictionary<int, decimal?>
        {
            [306] = 1m, [307] = 0m, [194] = 10m, [61] = -1m, [27] = 0m
        });
        // Noavaran reports monetary source lines with AmountUnit="N/A"; the report's
        // MillionRials scale is authoritative for the normalized output unit.
        complete = complete with
        {
            Items = complete.Items.Select(item => item.SourceItemId == 194
                ? item with { Unit = "N/A" }
                : item).ToArray()
        };
        var noUnitResult = QuarterlyFinancialReportsNormalizer.Normalize("وبملت", "بانک ملت", "issuer-1",
            [complete], 1, null, DateTimeOffset.UtcNow, true, BankingIndustryGroupId);
        var item194 = noUnitResult.Rows.Single(row => row.SourceItemId == 194).Cells["1405 Q1"];
        Assert.Equal(QuarterlyCellState.Available, item194.State);
        Assert.Equal(10m, item194.Value);
        var noUnitOperatingCell = noUnitResult.Rows.Single(row => row.RowId == "banking_operating_income").Cells["1405 Q1"];
        Assert.Equal(QuarterlyCellState.Available, noUnitOperatingCell.State);
        Assert.Equal(10m, noUnitOperatingCell.Value);
        Assert.Equal(new decimal?[] { 1m, 0m, 10m, -1m, 0m }, noUnitOperatingCell.OperandValues);

        var zeroObservation = Report(start, 3, new Dictionary<int, decimal?>
        {
            [306] = 1m, [307] = 0m, [194] = 0m, [61] = -1m, [27] = 0m
        });
        var zeroResult = QuarterlyFinancialReportsNormalizer.Normalize("وبملت", "بانک ملت", "issuer-1",
            [zeroObservation], 1, null, DateTimeOffset.UtcNow, true, BankingIndustryGroupId);
        var zeroCell = zeroResult.Rows.Single(row => row.RowId == "banking_operating_income").Cells["1405 Q1"];
        Assert.Equal(QuarterlyCellState.Available, zeroCell.State);
        Assert.Equal(0m, zeroCell.Value);

        var missingNullOperand = Report(start, 3, new Dictionary<int, decimal?>
        {
            [306] = 1m, [307] = 0m, [194] = null, [61] = -1m, [27] = 0m
        });
        var missingResult = QuarterlyFinancialReportsNormalizer.Normalize("وبملت", "بانک ملت", "issuer-1",
            [missingNullOperand], 1, null, DateTimeOffset.UtcNow, true, BankingIndustryGroupId);
        var unavailableCell = missingResult.Rows.Single(row => row.RowId == "banking_operating_income").Cells["1405 Q1"];
        Assert.Equal(QuarterlyCellState.CalculationUnavailable, unavailableCell.State);
        Assert.Null(unavailableCell.Value);

        var incompatibleUnit = Report(start, 3, new Dictionary<int, decimal?>
        {
            [306] = 1m, [307] = 0m, [194] = 10m, [61] = -1m, [27] = 0m
        });
        incompatibleUnit = incompatibleUnit with
        {
            Items = incompatibleUnit.Items.Select(item => item.SourceItemId == 194
                ? item with { Unit = "thousand_shares" }
                : item).ToArray()
        };
        var incompatibleResult = QuarterlyFinancialReportsNormalizer.Normalize("وبملت", "بانک ملت", "issuer-1",
            [incompatibleUnit], 1, null, DateTimeOffset.UtcNow, true, BankingIndustryGroupId);
        Assert.Equal(QuarterlyCellState.IncompatibleObservation,
            incompatibleResult.Rows.Single(row => row.SourceItemId == 194).Cells["1405 Q1"].State);
        Assert.Equal(QuarterlyCellState.IncompatibleObservation,
            incompatibleResult.Rows.Single(row => row.RowId == "banking_operating_income").Cells["1405 Q1"].State);

        var missingPredecessor = Report(start, 6, new Dictionary<int, decimal?>
        {
            [306] = 1m, [307] = 0m, [194] = 0m, [61] = -1m, [27] = 0m
        });
        var predecessorResult = QuarterlyFinancialReportsNormalizer.Normalize("وبملت", "بانک ملت", "issuer-1",
            [missingPredecessor], 2, null, DateTimeOffset.UtcNow, true, BankingIndustryGroupId);
        var q2Cell = predecessorResult.Rows.Single(row => row.RowId == "banking_operating_income").Cells["1405 Q2"];
        Assert.Equal(QuarterlyCellState.MissingPredecessor, q2Cell.State);
        Assert.Null(q2Cell.Value);
    }

    [Fact]
    public void Normalize_UsesLatestSelectedQuarterShareBasisForEveryQuarterAndComparableEps()
    {
        var fy1404 = Jalali(1404, 1, 1);
        var fy1405 = Jalali(1405, 1, 1);
        var reports = new[]
        {
            Report(fy1404, 3, new Dictionary<int, decimal?> { [143] = 1_019_350m, [144] = 1_019_350m, [153] = 8_400_000m, [160] = 121m, [317] = 121m }),
            Report(fy1404, 6, new Dictionary<int, decimal?> { [143] = 3_116_624m, [144] = 3_116_624m, [153] = 8_400_000m, [160] = 371m, [317] = 371m }),
            Report(fy1404, 9, new Dictionary<int, decimal?> { [143] = 6_668_740m, [144] = 6_668_740m, [153] = 8_400_000m, [160] = 793m, [317] = 794m }),
            Report(fy1404, 12, new Dictionary<int, decimal?> { [143] = 11_405_105m, [144] = 11_405_105m, [153] = 41_345_696m, [160] = 275m, [317] = 276m }),
            Report(fy1405, 3, new Dictionary<int, decimal?> { [143] = 6_048_613m, [144] = 6_048_613m, [153] = 41_345_696m, [160] = 146m, [317] = 146m }),
            Report(fy1405, 6, new Dictionary<int, decimal?> { [143] = 15_094_332m, [144] = 15_094_332m, [153] = 47_616_343m, [160] = 317m, [317] = 317m })
        };

        var result = QuarterlyFinancialReportsNormalizer.Normalize("قاسم", "قاسم ایران", "issuer-1", reports, 5, null,
            DateTimeOffset.UtcNow);
        QuarterlyFinancialCell CellFor(int id, int year, int quarter) =>
            result.Rows.Single(row => row.SourceItemId == id).Cells[$"{year} Q{quarter}"];
        // Runtime catalog UUIDs are database-generated; their raw units can be N/A even
        // when the provider's official source-item title declares thousand shares.
        var runtimeCatalogReports = reports.Select(report => report with
        {
            Items = report.Items.Select(item => item.SourceItemId is 144 or 153 or 160 or 317
                ? item with
                {
                    CatalogId = Guid.NewGuid(),
                    Unit = "N/A",
                    CatalogTitleFa = item.SourceItemId == 153
                        ? "\u062a\u0639\u062f\u0627\u062f \u0633\u0647\u0627\u0645 \u0634\u0631\u06a9\u062a - \u0628\u0647 \u0647\u0632\u0627\u0631 \u0633\u0647\u0645"
                        : item.CatalogTitleFa
                }
                : item).ToArray()
        }).ToArray();
        var dynamicCatalogResult = QuarterlyFinancialReportsNormalizer.Normalize("issuer", null, "issuer-1",
            runtimeCatalogReports, 5, null, DateTimeOffset.UtcNow);
        Assert.Equal(47_616_343m, dynamicCatalogResult.Rows.Single(row => row.SourceItemId == 153).Cells["1404 Q2"].Value);
        Assert.Equal(44m, dynamicCatalogResult.Rows.Single(row => row.SourceItemId == 160).Cells["1404 Q2"].Value);

        Assert.Equal(47_616_343m, CellFor(153, 1404, 2).Value); // Every selected quarter uses latest selected FY1405 Q2 basis.
        Assert.Equal(44m, CellFor(160, 1404, 2).Value);
        Assert.Equal("سود هر سهم پس از کسر مالیات", result.Rows.Single(row => row.SourceItemId == 160).LabelFa);
        Assert.Equal("تعداد سهام شرکت (هزار سهم)", result.Rows.Single(row => row.SourceItemId == 153).LabelFa);
        Assert.Equal(8_400_000m, CellFor(153, 1404, 2).ComparableEvidence?.RawCurrentValue);
        Assert.Equal(47_616_343m, CellFor(160, 1404, 2).ComparableEvidence?.ShareBasisThousand);
        Assert.Equal("latest_selected_quarter", CellFor(153, 1404, 2).ComparableEvidence?.Method);
        Assert.Equal(47_616_343m, CellFor(153, 1404, 4).Value);
        Assert.Equal(47_616_343m, CellFor(153, 1405, 1).Value);
        Assert.All(result.Rows.Single(row => row.SourceItemId == 153).Cells.Values,
            cell => Assert.Equal(47_616_343m, cell.Value));
        Assert.Equal(4_736_365m, CellFor(143, 1404, 4).Value); // FY1404 Q4 profit remains source-derived.
        Assert.Equal(99m, CellFor(160, 1404, 4).Value);
        Assert.Equal(9_045_719m, CellFor(143, 1405, 2).Value); // FY1405 Q2 profit is unchanged.
        Assert.Equal(190m, CellFor(160, 1405, 2).Value);
        Assert.Contains(result.SelectedReports!, report => report.ExternalReportId ==
            CellFor(160, 1404, 2).ComparableEvidence?.ReferenceExternalReportId);

        var samePeriodOnly = QuarterlyFinancialReportsNormalizer.Normalize("قاسم", "قاسم ایران", "issuer-1", reports,
            3, Jalali(1404, 12, 29), DateTimeOffset.UtcNow);
        Assert.Equal(51m, samePeriodOnly.Rows.Single(row => row.SourceItemId == 160).Cells["1404 Q2"].Value);

        var missingHistoricalShares = reports.Select(report => report.PeriodStart == fy1404 && report.PeriodMonths == 6
            ? report with { Items = report.Items.Select(line => line.SourceItemId == 153 ? line with { Value = null } : line).ToArray() }
            : report).ToArray();
        var historicalSharesUnavailable = QuarterlyFinancialReportsNormalizer.Normalize("issuer", null, "issuer-1",
            missingHistoricalShares, 5, null, DateTimeOffset.UtcNow);
        Assert.Equal(44m, historicalSharesUnavailable.Rows.Single(row => row.SourceItemId == 160).Cells["1404 Q2"].Value);
        Assert.Equal(47_616_343m, historicalSharesUnavailable.Rows.Single(row => row.SourceItemId == 153).Cells["1404 Q2"].Value);

        var missingReferenceShares = reports.Select(report => report.PeriodStart == fy1405 && report.PeriodMonths == 6
            ? report with { Items = report.Items.Select(line => line.SourceItemId == 153 ? line with { Value = null } : line).ToArray() }
            : report).ToArray();
        var unavailableReference = QuarterlyFinancialReportsNormalizer.Normalize("قاسم", "قاسم ایران", "issuer-1",
            missingReferenceShares, 5, null, DateTimeOffset.UtcNow);
        Assert.Null(unavailableReference.Rows.Single(row => row.SourceItemId == 153).Cells["1404 Q2"].Value);
        Assert.Null(unavailableReference.Rows.Single(row => row.SourceItemId == 160).Cells["1404 Q2"].Value);
        Assert.Equal(2_097_274m, unavailableReference.Rows.Single(row => row.SourceItemId == 143).Cells["1404 Q2"].Value);

        var futureQ3Profit = 30_000_000m;
        var futureQ4Profit = 50_000_000m;
        var withNextYearQ4 = reports.Concat([
            Report(fy1405, 9, new Dictionary<int, decimal?> { [143] = futureQ3Profit, [144] = futureQ3Profit, [153] = 50_000_000m, [160] = 600m, [317] = 600m }),
            Report(fy1405, 12, new Dictionary<int, decimal?> { [143] = futureQ4Profit, [144] = futureQ4Profit, [153] = 60_000_000m, [160] = 833m, [317] = 833m })
        ]).ToArray();
        var rebased = QuarterlyFinancialReportsNormalizer.Normalize("قاسم", "قاسم ایران", "issuer-1", withNextYearQ4,
            5, null, DateTimeOffset.UtcNow);
        var oldQ4Eps = rebased.Rows.Single(row => row.SourceItemId == 160).Cells["1404 Q4"];
        Assert.Equal(60_000_000m, rebased.Rows.Single(row => row.SourceItemId == 153).Cells["1404 Q4"].Value);
        Assert.Equal(79m, oldQ4Eps.Value);
        Assert.Equal(4_736_365m, oldQ4Eps.ComparableEvidence?.StandaloneAttributableProfitMillionRials);
        Assert.Equal(60_000_000m, oldQ4Eps.ComparableEvidence?.ShareBasisThousand);
        Assert.Equal(4_736_365m, rebased.Rows.Single(row => row.SourceItemId == 143).Cells["1404 Q4"].Value);

        var invalidCodalCrossCheck = reports.Select(report => report.PeriodStart == fy1404 && report.PeriodMonths == 6
            ? report with { Items = report.Items.Select(line => line.SourceItemId == 317
                ? line with { CatalogId = Guid.Empty }
                : line).ToArray() }
            : report).ToArray();
        var invalidCodal = QuarterlyFinancialReportsNormalizer.Normalize("Ù‚Ø§Ø³Ù…", "Ù‚Ø§Ø³Ù… Ø§ÛŒØ±Ø§Ù†", "issuer-1",
            invalidCodalCrossCheck, 5, null, DateTimeOffset.UtcNow);
        Assert.Null(invalidCodal.Rows.Single(row => row.SourceItemId == 160).Cells["1404 Q2"].Value);
        Assert.Equal(2_097_274m, invalidCodal.Rows.Single(row => row.SourceItemId == 143).Cells["1404 Q2"].Value);

        var missingSameQuarterReference = reports.Where(report => !(report.PeriodStart == fy1405 && report.PeriodMonths == 6))
            .Concat([Report(fy1405, 9, new Dictionary<int, decimal?>
            {
                [143] = 20_000_000m, [144] = 20_000_000m, [153] = 50_000_000m, [160] = 400m, [317] = 400m
            })]).ToArray();
        var fallback = QuarterlyFinancialReportsNormalizer.Normalize("Ù‚Ø§Ø³Ù…", "Ù‚Ø§Ø³Ù… Ø§ÛŒØ±Ø§Ù†", "issuer-1",
            missingSameQuarterReference, 6, null, DateTimeOffset.UtcNow);
        Assert.Equal(50_000_000m, fallback.Rows.Single(row => row.SourceItemId == 153).Cells["1404 Q2"].Value);
        Assert.Equal(42m, fallback.Rows.Single(row => row.SourceItemId == 160).Cells["1404 Q2"].Value);

        // Some Noavaran separate reports carry net profit (143) but omit the duplicate attributable
        // profit line (144). Use 143 only as the separately verified numerator fallback.
        var withoutDuplicateAttributableLine = reports.Select(report => report with
        {
            Items = report.Items.Where(line => line.SourceItemId != 144).ToArray()
        }).ToArray();
        var netProfitFallback = QuarterlyFinancialReportsNormalizer.Normalize("قاسم", "قاسم ایران", "issuer-1",
            withoutDuplicateAttributableLine, 5, null, DateTimeOffset.UtcNow);
        var fallbackEps = netProfitFallback.Rows.Single(row => row.SourceItemId == 160).Cells["1404 Q2"];
        Assert.Equal(44m, fallbackEps.Value);
        Assert.Equal(47_616_343m, netProfitFallback.Rows.Single(row => row.SourceItemId == 153).Cells["1404 Q2"].Value);
        Assert.Equal(143, fallbackEps.ComparableEvidence?.AttributableProfitSourceItemId);
        Assert.Equal(143, fallbackEps.ComparableEvidence?.PreviousAttributableProfitSourceItemId);
        Assert.Equal(2_097_274m, fallbackEps.ComparableEvidence?.StandaloneAttributableProfitMillionRials);
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
        Assert.Null(Cell(result, 153, 2).Value);
        Assert.Equal(QuarterlyCellState.CalculationUnavailable, Cell(result, 153, 2).State);
        Assert.Equal(0m, Cell(result, 153, 2).ComparableEvidence?.RawCurrentValue);
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
        Assert.Equal("Unavailable", consolidatedOnly.Status);
        var nextPeriodConsolidated = valid with
        {
            ReportId = Guid.NewGuid(), ExternalReportId = "consolidated-q2", PeriodMonths = 6,
            PeriodEnd = DateOnly.FromDateTime(new PersianCalendar().AddMonths(start.ToDateTime(TimeOnly.MinValue), 6).AddDays(-1)),
            IsComposing = true
        };
        var scopeConflict = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1",
            [valid, nextPeriodConsolidated], 2, null, DateTimeOffset.UtcNow);
        Assert.Equal(10m, Cell(scopeConflict, 15, 1).Value);
        Assert.False(scopeConflict.IsComposing);
    }

    [Fact]
    public void ApprovedMappings_ContainExactlyTheFourteenApprovedSourceIdentities()
    {
        Assert.Equal(new[] { 15, 1, 139, 5, 39, 140, 12, 40, 142, 334, 278, 143, 160, 153 },
            QuarterlyFinancialReportsNormalizer.ApprovedMappings.Select(item => item.SourceItemId));
        Assert.Equal(new[]
        {
            "07c03a70-a1fa-4b9b-861c-937748e2a1dc", "57940eb5-27f8-42b1-8827-d64dc49eab2d",
            "917b2bbb-3859-42f0-9864-2eac3f98a3b3", "c04b4fae-69db-48d9-a63e-c96a932e7b89",
            "17f91e30-ba52-4789-89d0-3ffd3ac61c17", "f2a6e460-176f-4426-a4e7-78d4ffd86ba6",
            "b8342891-ad45-4c4b-a4dd-361bb5f37adb", "e08f0ce7-ab6a-4a24-8fab-efc3f93c140f",
            "1cbc0ee5-21bb-4b9d-b5ba-72c3b84731b3", "31f4454a-78d2-41fb-8caa-b86564423cd6",
            "cb6c92c3-cff6-40c1-a85a-db9fde4ba791",
            "bd36909d-abf8-44c2-b7db-5e53dd678b53", "2146da26-b28e-4d04-b50c-f49fc9b70e61",
            "e98e6c00-7204-44b1-a279-bbbb68781d3c"
        }, QuarterlyFinancialReportsNormalizer.ApprovedMappings.Select(item => item.CatalogId));
        Assert.Contains(194, QuarterlyFinancialReportsNormalizer.RequiredSourceItemIds);
    }

    [Fact]
    public void Normalize_UsesPersianFiscalCalendarForNonEsfandFiscalStart()
    {
        var start = new DateOnly(2025, 12, 22); // 1404/10/01
        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1",
            [Report(start, 3, new Dictionary<int, decimal?> { [15] = 1m }) with { FiscalYearEvidence = "1405/09/30" },
             Report(start, 6, new Dictionary<int, decimal?> { [15] = 4m }) with { FiscalYearEvidence = "1405/09/30" }],
            2, null, DateTimeOffset.UtcNow);

        Assert.Equal(3m, Cell(result, 15, 2).Value);
        Assert.Equal(1405, result.PeriodsNewestFirst[0].FiscalYear);
        Assert.Equal(start, result.PeriodsNewestFirst[1].Start);
    }

    [Fact]
    public void Normalize_ExcludesTransitionalYearThatOverlapsPrecedingFiscalYearAfterYearEndChange()
    {
        // Mirrors سهرمز: the fiscal year-end moved from 1404/08/30 to 1405/09/30 and the provider
        // labels the one-month 1404/09 transitional statement as twelve months ending 1404/09/30.
        var fy1404 = Jalali(1403, 9, 1);
        var fy1405 = Jalali(1404, 10, 1);
        var transitional = Report(Jalali(1403, 10, 1), 12, Revenue(3_730_732m)) with { FiscalYearEvidence = "1404/09/30" };
        var reports = new[]
        {
            Report(fy1404, 3, Revenue(9_538_662m)) with { FiscalYearEvidence = "1404/08/30" },
            Report(fy1404, 6, Revenue(18_438_567m)) with { FiscalYearEvidence = "1404/08/30" },
            Report(fy1404, 9, Revenue(26_787_795m)) with { FiscalYearEvidence = "1404/08/30" },
            Report(fy1404, 12, Revenue(38_329_011m)) with { FiscalYearEvidence = "1404/08/30" },
            transitional,
            Report(fy1405, 3, Revenue(15_550_339m)) with { FiscalYearEvidence = "1405/09/30" },
            Report(fy1405, 6, Revenue(26_832_551m)) with { FiscalYearEvidence = "1405/09/30" },
            Report(fy1405, 9, Revenue(39_176_731m)) with { FiscalYearEvidence = "1405/09/30" }
        };

        var result = QuarterlyFinancialReportsNormalizer.Normalize("سهرمز", null, "issuer-1", reports, 5, null,
            DateTimeOffset.UtcNow);

        Assert.Equal("Available", result.Status);
        Assert.Equal(new[] { (1405, 3), (1405, 2), (1405, 1), (1404, 4), (1404, 3) },
            result.PeriodsNewestFirst.Select(period => (period.FiscalYear, period.Quarter)));
        var revenue = result.Rows.Single(row => row.SourceItemId == 15);
        Assert.Equal(15_550_339m, revenue.Cells["1405 Q1"].Value);
        Assert.Equal(38_329_011m - 26_787_795m, revenue.Cells["1404 Q4"].Value);
        Assert.DoesNotContain(result.SelectedReports!, report => report.ReportId == transitional.ReportId);
    }

    [Fact]
    public void Normalize_KeepsNewFiscalYearAfterShortenedPrecedingFiscalYear()
    {
        // Mirrors دکوثر: the Esfand fiscal year 1404 was cut short at 1404/09/30 and the new fiscal
        // year ends 1405/09/30. Its nominal span overlaps 1404/01/01-1404/12/29, but no persisted
        // report of the shortened year reaches into it.
        var fy1404 = Jalali(1404, 1, 1);
        var fy1405 = Jalali(1404, 10, 1);
        var reports = new[]
        {
            Report(fy1404, 3, Revenue(10m)) with { FiscalYearEvidence = "1404/12/29" },
            Report(fy1404, 6, Revenue(25m)) with { FiscalYearEvidence = "1404/12/29" },
            Report(fy1404, 9, Revenue(45m)) with { FiscalYearEvidence = "1404/12/29" },
            Report(fy1405, 3, Revenue(30m)) with { FiscalYearEvidence = "1405/09/30" },
            Report(fy1405, 6, Revenue(70m)) with { FiscalYearEvidence = "1405/09/30" },
            Report(fy1405, 9, Revenue(120m)) with { FiscalYearEvidence = "1405/09/30" }
        };

        var result = QuarterlyFinancialReportsNormalizer.Normalize("دکوثر", null, "issuer-1", reports, 5, null,
            DateTimeOffset.UtcNow);

        Assert.Equal(new[] { (1405, 3), (1405, 2), (1405, 1), (1404, 3), (1404, 2) },
            result.PeriodsNewestFirst.Select(period => (period.FiscalYear, period.Quarter)));
        Assert.Equal(Jalali(1405, 6, 31), result.PeriodsNewestFirst[0].End);
        var revenue = result.Rows.Single(row => row.SourceItemId == 15);
        Assert.Equal(50m, revenue.Cells["1405 Q3"].Value);
        Assert.Equal(20m, revenue.Cells["1404 Q3"].Value);
    }

    [Fact]
    public void Normalize_MeasuresTransitionalYearFromTheDayAfterThePrecedingFiscalYear()
    {
        // The Esfand fiscal year 1403 is followed by a nine-month transitional year 1404/01/01-1404/09/30
        // whose provider evidence implies a nominal start of 1403/10/01.
        var fy1403 = Jalali(1403, 1, 1);
        var reports = new[]
        {
            Report(fy1403, 9, Revenue(30m)) with { FiscalYearEvidence = "1403/12/30" },
            Report(fy1403, 12, Revenue(40m)) with { FiscalYearEvidence = "1403/12/30" },
            Report(Jalali(1404, 1, 1), 3, Revenue(12m)) with { FiscalYearEvidence = "1404/12/29" },
            Report(Jalali(1403, 10, 1), 6, Revenue(25m)) with
            {
                PeriodEnd = Jalali(1404, 6, 31), FiscalYearEvidence = "1404/09/30"
            },
            Report(Jalali(1403, 10, 1), 9, Revenue(45m)) with
            {
                PeriodEnd = Jalali(1404, 9, 30), FiscalYearEvidence = "1404/09/30"
            }
        };

        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1", reports, 4, null,
            DateTimeOffset.UtcNow);

        Assert.Equal(new[] { (1404, 3), (1404, 2), (1404, 1), (1403, 4) },
            result.PeriodsNewestFirst.Select(period => (period.FiscalYear, period.Quarter)));
        Assert.Equal(Jalali(1404, 9, 30), result.PeriodsNewestFirst[0].End);
        var revenue = result.Rows.Single(row => row.SourceItemId == 15);
        Assert.Equal(20m, revenue.Cells["1404 Q3"].Value);
        Assert.Equal(13m, revenue.Cells["1404 Q2"].Value);
        Assert.Equal(12m, revenue.Cells["1404 Q1"].Value);
    }

    [Fact]
    public void Normalize_NamesFiscalYearByItsEndYearWhenFiscalEvidenceIsMissing()
    {
        var fy1404 = Jalali(1403, 9, 1);
        var fy1405 = Jalali(1404, 9, 1);
        var reports = new[]
        {
            Report(fy1404, 3, Revenue(1m)) with { FiscalYearEvidence = "1404/08/30" },
            Report(fy1404, 6, Revenue(2m)) with { FiscalYearEvidence = "1404/08/30" },
            Report(fy1404, 9, Revenue(3m)) with { FiscalYearEvidence = "1404/08/30" },
            Report(fy1404, 12, Revenue(4m)) with { FiscalYearEvidence = "1404/08/30" },
            Report(fy1405, 3, Revenue(5m))
        };

        var result = QuarterlyFinancialReportsNormalizer.Normalize("نماد", null, "issuer-1", reports, 5, null,
            DateTimeOffset.UtcNow);

        Assert.Equal(new[] { (1405, 1), (1404, 4), (1404, 3), (1404, 2), (1404, 1) },
            result.PeriodsNewestFirst.Select(period => (period.FiscalYear, period.Quarter)));
        Assert.Equal(5m, result.Rows.Single(row => row.SourceItemId == 15).Cells["1405 Q1"].Value);
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
        report = report with { FiscalYearEvidence = "1404/12/29" };
        var result = QuarterlyFinancialReportsNormalizer.Normalize("Ù†Ù…Ø§Ø¯", null, "issuer-1", [report], 1,
            null, DateTimeOffset.UtcNow);

        var revenue = Cell(result, 15, 1);
        Assert.Equal(QuarterlyCellState.Available, revenue.State);
        Assert.Equal(123.456789m, revenue.Value);
        Assert.Equal("IRR", revenue.CurrencyCode);
        Assert.Equal(6, revenue.ValueScale);
        Assert.Equal("million_rial", revenue.Unit);
        Assert.Equal("thousand_shares", Cell(result, 153, 1).Unit);
        Assert.Equal(3, Cell(result, 153, 1).ValueScale);
        Assert.Equal("1404/12/29", Assert.Single(result.SelectedReports!).FiscalYearEvidence);
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

    private static DateOnly Jalali(int year, int month, int day) =>
        DateOnly.FromDateTime(new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0));

    private static Dictionary<int, decimal?> Revenue(decimal value) => new() { [15] = value };

    private static QuarterlyReportObservation Report(DateOnly start, int months, IDictionary<int, decimal?> values)
    {
        var reportId = Guid.NewGuid();
        var end = DateOnly.FromDateTime(new PersianCalendar().AddMonths(start.ToDateTime(TimeOnly.MinValue), months).AddDays(-1));
        var items = values.Select(pair =>
        {
            var mapping = QuarterlyFinancialReportsNormalizer.RequiredMappings.FirstOrDefault(mapping => mapping.SourceItemId == pair.Key);
            var item = new QuarterlySourceObservation(Guid.NewGuid(),
                string.IsNullOrWhiteSpace(mapping.CatalogId) ? Guid.NewGuid() : Guid.Parse(mapping.CatalogId),
                pair.Key,
                pair.Key == 12 ? "\u0647\u0632\u06cc\u0646\u0647\u200c\u0647\u0627\u06cc \u0645\u0627\u0644\u06cc" :
                    pair.Key == 278 ? "\u0633\u0648\u062f \u0639\u0645\u0644\u06cc\u0627\u062a \u0645\u062a\u0648\u0642\u0641\u200c\u0634\u062f\u0647" :
                    pair.Key == 194 ? "\u062e\u0627\u0644\u0635 \u0633\u0627\u06cc\u0631 \u062f\u0631\u0622\u0645\u062f\u0647\u0627\u06cc \u063a\u06cc\u0631 \u0645\u0634\u0627\u0639" : null,
                pair.Key switch
                {
                    153 => "thousand_shares",
                    160 or 317 => "rial_per_share",
                    _ => "million_rial"
                }, pair.Value);
            return pair.Key == 153
                ? item with { CatalogTitleFa = "\u062a\u0639\u062f\u0627\u062f \u0633\u0647\u0627\u0645 \u0634\u0631\u06a9\u062a - \u0628\u0647 \u0647\u0632\u0627\u0631 \u0633\u0647\u0645" }
                : item;
        }).ToArray();
        return new QuarterlyReportObservation(reportId, $"report-{reportId}", "NoavaranCurrentApi", "issuer-1",
            "IncomeStatement", months, start, end, false, false, false, $"checksum-{reportId}",
            DateTimeOffset.UtcNow, null, null, items, "MillionRials", "N/A", "IRR", "Accrual", "fixture-lineage");
    }
}

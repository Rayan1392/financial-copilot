import { describe, expect, it } from "vitest";
import { createMonthlyTrendChartCardViewModel } from "../monthly-activity-trend-chart-view-model";
import type { MonthlyActivityTrendResult } from "@/lib/chat.functions";

describe("createMonthlyTrendChartCardViewModel", () => {
  it("shows the prior-year total plus the current year-to-date total and prior-year percentage", () => {
    const viewModel = createMonthlyTrendChartCardViewModel({
      companySymbol: "سیسکو",
      latestReportYear: 1405,
      latestReportMonth: 2,
      unitLabelFa: "میلیارد تومان",
      chartPoints: [
        {
          fiscalMonthIndex: 1,
          fiscalMonthNameFa: "فروردین",
          previousFiscalYear: 1404,
          previousFiscalYearSalesAmount: 3_186,
          currentFiscalYear: 1405,
          currentFiscalYearSalesAmount: 2_050,
          average12MonthSalesAmount: 4_902,
          isPreviousYearReported: true,
          isCurrentYearReported: true,
        },
        {
          fiscalMonthIndex: 2,
          fiscalMonthNameFa: "اردیبهشت",
          previousFiscalYear: 1404,
          previousFiscalYearSalesAmount: 2_916,
          currentFiscalYear: 1405,
          currentFiscalYearSalesAmount: 11_697,
          average12MonthSalesAmount: 4_902,
          isPreviousYearReported: true,
          isCurrentYearReported: true,
        },
      ],
      insights: [],
      missingDataPoints: [],
      sourceProviderName: "Noavaran",
      calculatedAtUtc: "2026-07-29T00:00:00Z",
    } satisfies MonthlyActivityTrendResult);

    expect(viewModel.previousYearLegend).toBe("۱۴۰۴: ۶,۱۰۲");
    expect(viewModel.currentYearLegend).toBe("۱۴۰۵: ۱۳,۷۴۷ (۲۲۵.۲۹٪ از ۱۴۰۴)");
    expect(viewModel.averageLegend).toBe("میانگین ۱۲ ماهه");
  });

  it("formats insight percentages with Persian digits, stable RTL signs, and semantic tones", () => {
    const viewModel = createMonthlyTrendChartCardViewModel({
      companySymbol: "گلدیرا",
      latestReportYear: 1405,
      latestReportMonth: 5,
      unitLabelFa: "میلیارد تومان",
      salesAmountYoYGrowthPercent: -1,
      salesVsAverage12MonthPercent: 37.2,
      chartPoints: [],
      insights: [
        { kind: "YoYGrowth", textFa: "فروش ماهانه نسبت به ماه مشابه سال قبل -1.0٪ افت داشته است." },
        {
          kind: "VsAverage12Month",
          textFa: "فروش این ماه نسبت به میانگین 12 ماهه +37.2٪ بالاتر است.",
        },
      ],
      missingDataPoints: [],
      sourceProviderName: "Noavaran",
      calculatedAtUtc: "2026-08-23T00:00:00Z",
    } satisfies MonthlyActivityTrendResult);

    expect(viewModel.explanationLines[0]).toMatchObject({
      valueLabel: "(۱٫۰٪)",
      tone: "negative",
    });
    expect(viewModel.explanationLines[1]).toMatchObject({
      valueLabel: "+۳۷٫۲٪",
      tone: "positive",
    });
    expect(
      viewModel.explanationLines
        .map((line) => `${line.beforeValue}${line.valueLabel ?? ""}${line.afterValue}`)
        .join(" "),
    ).not.toMatch(/[0-9]/);
  });
});

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
});

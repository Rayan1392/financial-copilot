import { describe, expect, it } from "vitest";
import {
  defaultSelectedMonth,
  monthIndexFromTap,
  sameMonthChangePercent,
} from "../monthly-sales-trend-mobile-model";
import {
  barWidthsPercent,
  formatQuarterlyCell,
  isMissingQuarterlyCell,
  periodsNewestFirst,
  shortPeriodLabel,
} from "../quarterly-report-mobile-model";
import type { MonthlyTrendChartPointViewModel } from "@/components/app/monthly-activity-trend-chart-view-model";
import type { QuarterlyFinancialTableRow } from "@/components/app/quarterly-financial-table";

const month = (
  index: number,
  currentYear: number | null,
  previousYear: number | null,
): MonthlyTrendChartPointViewModel => ({
  fiscalMonthIndex: index,
  fiscalMonthLabel: `m${index}`,
  currentYear,
  previousYear,
  average: null,
  currentYearValueLabel: null,
  previousYearValueLabel: null,
  averageValueLabel: null,
  isCurrentYearReported: currentYear !== null,
  isPreviousYearReported: previousYear !== null,
});

describe("monthly sales trend (mobile)", () => {
  it("opens on the latest month with current-year sales, else the latest previous-year month", () => {
    expect(defaultSelectedMonth([month(1, 10, 9), month(2, 11, 8), month(3, null, 7)])).toBe(1);
    expect(defaultSelectedMonth([month(1, null, 9), month(2, null, 8), month(3, null, null)])).toBe(
      1,
    );
    expect(defaultSelectedMonth([])).toBe(0);
  });

  it("computes the same-month change only when both years are reported", () => {
    expect(sameMonthChangePercent(month(1, 120, 100))).toBeCloseTo(20);
    expect(sameMonthChangePercent(month(1, null, 100))).toBeNull();
    expect(sameMonthChangePercent(month(1, 120, null))).toBeNull();
    expect(sameMonthChangePercent(month(1, 120, 0))).toBeNull();
  });

  it("maps a tap to the month column under it and ignores the value axis", () => {
    const plot = { left: 40, right: 4 };
    // 300 px wide chart: plot from 40 to 296 (256 px) split into 12 columns.
    expect(monthIndexFromTap(20, 300, plot, 12)).toBeNull();
    expect(monthIndexFromTap(41, 300, plot, 12)).toBe(0);
    expect(monthIndexFromTap(40 + 256 * (2.5 / 12), 300, plot, 12)).toBe(2);
    expect(monthIndexFromTap(296, 300, plot, 12)).toBe(11);
    expect(monthIndexFromTap(299, 300, plot, 12)).toBeNull();
  });
});

const row = (overrides: Partial<QuarterlyFinancialTableRow> = {}): QuarterlyFinancialTableRow => ({
  label: "فروش",
  sourceItemId: 15,
  values: ["۱۰۰", "—", "-", "۵٫۰٪"],
  grossMargin: false,
  ...overrides,
});

describe("quarterly report (mobile)", () => {
  it("orders periods newest first", () => {
    expect(periodsNewestFirst(5)).toEqual([4, 3, 2, 1, 0]);
    expect(periodsNewestFirst(0)).toEqual([]);
  });

  it("keeps the desktop cell text: em dash = unavailable, short dash = zero, % only on margin rows", () => {
    expect(isMissingQuarterlyCell("—")).toBe(true);
    expect(isMissingQuarterlyCell("-")).toBe(false);
    expect(formatQuarterlyCell(row(), "(۱۲۳)")).toBe("(۱۲۳)");
    expect(formatQuarterlyCell(row({ grossMargin: true }), "۳۴٫۲")).toBe("۳۴٫۲٪");
    expect(formatQuarterlyCell(row({ grossMargin: true }), "—")).toBe("—");
    expect(formatQuarterlyCell(row({ bankingMargin: true, grossMargin: true }), "-")).toBe("-");
    expect(formatQuarterlyCell(row(), undefined)).toBe("—");
  });

  it("scales bars to the largest absolute value and leaves missing values empty", () => {
    expect(barWidthsPercent([50, -100, null, 0])).toEqual([50, 100, 0, 0]);
    expect(barWidthsPercent([null, null])).toEqual([0, 0]);
  });

  it("shortens period labels and falls back to the full label", () => {
    expect(
      shortPeriodLabel(
        {
          label: "x",
          quarterName: "فصل دوم",
          dateLabel: "۳۱ شهریور ۱۴۰۵",
          year: 1405,
          quarter: 2,
          sourceIndex: 0,
        },
        ["فصل دوم", "منتهی به", "۳۱ شهریور ۱۴۰۵"],
      ),
    ).toBe("فصل دوم ۱۴۰۵");
    expect(
      shortPeriodLabel({ label: "x", year: 1404, quarter: 3, sourceIndex: 0 }, [
        "سال مالی ۱۴۰۴",
        "فصل ۳",
      ]),
    ).toBe("فصل ۳ ۱۴۰۴");
    expect(
      shortPeriodLabel({ label: "دوره منتهی به ۳۰ آذر ۱۴۰۴", sourceIndex: 0 }, [
        "دوره منتهی به",
        "۳۰ آذر ۱۴۰۴",
      ]),
    ).toBe("دوره منتهی به ۳۰ آذر ۱۴۰۴");
  });
});
